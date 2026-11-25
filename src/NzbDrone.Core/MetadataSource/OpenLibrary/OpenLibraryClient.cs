using System;
using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using NLog;
using NzbDrone.Common.Http;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Exceptions;
using NzbDrone.Core.MetadataSource.OpenLibrary.Resources;

namespace NzbDrone.Core.MetadataSource.OpenLibrary
{
    public interface IOpenLibraryClient
    {
        OlSearchResponse Search(string query, int limit = 20, int offset = 0);
        OlSearchResponse SearchOnePageWithoutRetry(string query, int limit);
        OlAuthorWorksResponse GetAuthorWorks(string olid, int limit = 100, int offset = 0);
        OlWorkEditionsResponse GetWorkEditions(string olid, int limit = 20, int offset = 0);
        OlWorkResource GetWork(string olid);
        OlWorkResource GetWork(string olid, bool verification);
        OlAuthorResource GetAuthor(string olid);
        OlAuthorResource GetAuthor(string olid, bool confirmNotFound);
        OlAuthorResource GetAuthorProfile(string olid);
        OlEditionResource GetEditionByIsbn(string isbn);
        OlEditionResource GetExactEditionByIsbn(string isbn);
        OlEditionResource GetExactEditionByIsbn(string isbn, bool verification);
        OlEditionResource GetEdition(string olid);
    }

    public class OpenLibraryClient : IOpenLibraryClient
    {
        private const string BaseUrl = "https://openlibrary.org";
        private const string CoverBaseUrl = "https://covers.openlibrary.org";

        private static readonly Uri OpenLibraryOrigin = new Uri(BaseUrl);
        private static readonly Regex IsbnEditionRedirect = new Regex(
            @"\A(?:(?i:https://)[^/\\?#@\s]+)?(?<path>/books/OL[0-9]+M\.json)\z",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        private readonly IHttpClient _httpClient;
        private readonly IConfigService _configService;
        private readonly Logger _logger;
        private readonly IHttpRequestBuilderFactory _requestBuilder;
        private readonly ConcurrentDictionary<string, CircuitState> _circuitState = new ConcurrentDictionary<string, CircuitState>();
        private readonly object _boundedRequestLock = new ();
        private DateTime _boundedUntilUtc;

        private class CircuitState
        {
            public int ConsecutiveFailures;
            public DateTime OpenUntilUtc;
        }

        public OpenLibraryClient(IHttpClient httpClient, IConfigService configService, Logger logger)
        {
            _httpClient = httpClient;
            _configService = configService;
            _logger = logger;
            _requestBuilder = new HttpRequestBuilder(BaseUrl + "/{route}")
                .SetHeader("Accept", "application/json")
                .KeepAlive()
                .CreateFactory();
        }

        public OlSearchResponse Search(string query, int limit = 20, int offset = 0)
        {
            return Search(query, limit, offset, GetRetryBudget(OperationClass.Search), supplemental: false);
        }

        public OlSearchResponse SearchOnePageWithoutRetry(string query, int limit)
        {
            return Search(query, limit, offset: 0, retryBudget: 0, supplemental: true);
        }

        private OlSearchResponse Search(string query, int limit, int offset, int retryBudget, bool supplemental)
        {
            _logger.Debug("OpenLibrary search: {0}", query);

            var requestBuilder = new HttpRequestBuilder(BaseUrl + "/search.json")
                .AddQueryParam("q", query)
                .AddQueryParam("limit", limit.ToString())
                .AddQueryParam("fields", "key,title,author_name,author_key,isbn,cover_i,first_publish_year,number_of_pages_median,language,subject,series,series_with_number,ratings_average,ratings_count,want_to_read_count,currently_reading_count,already_read_count,edition_count")
                .SetHeader("Accept", "application/json");

            if (offset > 0)
            {
                requestBuilder.AddQueryParam("offset", offset.ToString());
            }

            var request = requestBuilder.Build();

            request.RequestTimeout = TimeSpan.FromSeconds(GetTimeoutSeconds(OperationClass.Search));
            request.SuppressHttpError = true;

            if (supplemental)
            {
                request.AllowAutoRedirect = false;
            }

            var response = ExecuteWithRateLimitRetry<OlSearchResponse>("openlibrary.search", request, retryBudget, bounded: supplemental);

            // Author hydration must distinguish a failed request from an empty final page.
            return query.StartsWith("author_key:", StringComparison.Ordinal)
                ? response
                : response ?? new OlSearchResponse { RequestUnavailable = true };
        }

        public OlAuthorWorksResponse GetAuthorWorks(string olid, int limit = 100, int offset = 0)
        {
            _logger.Debug("OpenLibrary get author works: {0} limit={1} offset={2}", olid, limit, offset);

            var cleanKey = StripKeyPrefix(olid);
            var request = _requestBuilder.Create()
                .SetSegment("route", $"authors/{cleanKey}/works.json")
                .AddQueryParam("limit", limit.ToString())
                .AddQueryParam("offset", offset.ToString())
                .Build();

            request.RequestTimeout = TimeSpan.FromSeconds(GetTimeoutSeconds(OperationClass.Work));
            request.SuppressHttpError = true;

            return ExecuteWithRateLimitRetry<OlAuthorWorksResponse>("openlibrary.author-works", request, GetRetryBudget(OperationClass.Work));
        }

        public OlWorkEditionsResponse GetWorkEditions(string olid, int limit = 20, int offset = 0)
        {
            _logger.Debug("OpenLibrary get work editions: {0} limit={1} offset={2}", olid, limit, offset);

            var cleanKey = StripKeyPrefix(olid);
            var request = _requestBuilder.Create()
                .SetSegment("route", $"works/{cleanKey}/editions.json")
                .AddQueryParam("limit", limit.ToString())
                .AddQueryParam("offset", offset.ToString())
                .Build();

            request.RequestTimeout = TimeSpan.FromSeconds(GetTimeoutSeconds(OperationClass.Work));
            request.SuppressHttpError = true;

            var response = ExecuteWithRateLimitRetry<OlWorkEditionsResponse>("openlibrary.work-editions", request, GetRetryBudget(OperationClass.Work));
            return response ?? new OlWorkEditionsResponse();
        }

        public OlWorkResource GetWork(string olid) => GetWork(olid, verification: false);

        public OlWorkResource GetWork(string olid, bool verification)
        {
            if (!verification)
            {
                _logger.Debug("OpenLibrary get work: {0}", olid);
            }

            var cleanKey = StripKeyPrefix(olid);
            var request = _requestBuilder.Create()
                .SetSegment("route", $"works/{cleanKey}.json")
                .Build();

            request.RequestTimeout = TimeSpan.FromSeconds(GetTimeoutSeconds(OperationClass.Work));
            request.SuppressHttpError = true;

            if (verification)
            {
                request.AllowAutoRedirect = false;
            }

            return ExecuteWithRateLimitRetry<OlWorkResource>(
                "openlibrary.work",
                request,
                verification ? 0 : GetRetryBudget(OperationClass.Work),
                bounded: verification);
        }

        public OlAuthorResource GetAuthor(string olid)
        {
            return GetAuthor(olid, false);
        }

        public OlAuthorResource GetAuthor(string olid, bool confirmNotFound)
        {
            _logger.Debug("OpenLibrary get author: {0}", olid);

            var request = BuildAuthorRequest(StripKeyPrefix(olid));
            return ExecuteWithRateLimitRetry<OlAuthorResource>("openlibrary.author", request, GetRetryBudget(OperationClass.Work), confirmNotFound ? olid : null);
        }

        public OlAuthorResource GetAuthorProfile(string olid)
        {
            var authorId = OpenLibraryIdNormalizer.NormalizeAuthorId(olid);
            if (authorId == null)
            {
                return null;
            }

            var request = BuildAuthorRequest(authorId);
            request.AllowAutoRedirect = false;
            request.RequestTimeout = TimeSpan.FromSeconds(Math.Min(10, GetTimeoutSeconds(OperationClass.Work)));
            return ExecuteWithRateLimitRetry<OlAuthorResource>("openlibrary.author-profile", request, 0, bounded: true);
        }

        private HttpRequest BuildAuthorRequest(string authorId)
        {
            var request = _requestBuilder.Create()
                .SetSegment("route", $"authors/{authorId}.json")
                .Build();

            request.RequestTimeout = TimeSpan.FromSeconds(GetTimeoutSeconds(OperationClass.Work));
            request.SuppressHttpError = true;
            return request;
        }

        public OlEditionResource GetEditionByIsbn(string isbn) => GetEditionByIsbn(isbn, requireMatchingRedirect: false);

        public OlEditionResource GetExactEditionByIsbn(string isbn) => GetEditionByIsbn(isbn, requireMatchingRedirect: true);

        public OlEditionResource GetExactEditionByIsbn(string isbn, bool verification)
            => GetEditionByIsbn(isbn, requireMatchingRedirect: true, verification: verification);

        private OlEditionResource GetEditionByIsbn(string isbn, bool requireMatchingRedirect, bool verification = false)
        {
            _logger.Debug("OpenLibrary ISBN lookup (exact redirect required: {0}).", requireMatchingRedirect);

            var request = BuildIsbnRequest($"isbn/{isbn}.json");
            return ExecuteWithRateLimitRetry<OlEditionResource>(
                "openlibrary.isbn",
                request,
                verification ? 0 : GetRetryBudget(OperationClass.Isbn),
                followIsbnRedirect: true,
                requireMatchingIsbnRedirect: requireMatchingRedirect,
                unavailable: () => new OlEditionResource { RequestUnavailable = true },
                bounded: verification);
        }

        public OlEditionResource GetEdition(string olid)
        {
            _logger.Debug("OpenLibrary get edition: {0}", olid);

            var cleanKey = StripKeyPrefix(olid);
            var request = _requestBuilder.Create()
                .SetSegment("route", $"books/{cleanKey}.json")
                .Build();

            request.RequestTimeout = TimeSpan.FromSeconds(GetTimeoutSeconds(OperationClass.Work));
            request.SuppressHttpError = true;

            return ExecuteWithRateLimitRetry<OlEditionResource>("openlibrary.edition", request, GetRetryBudget(OperationClass.Work));
        }

        private HttpRequest BuildIsbnRequest(string route)
        {
            var request = _requestBuilder.Create()
                .SetSegment("route", route)
                .Build();

            request.RequestTimeout = TimeSpan.FromSeconds(GetTimeoutSeconds(OperationClass.Isbn));
            request.SuppressHttpError = true;
            request.AllowAutoRedirect = false;
            return request;
        }

        private static string GetIsbnEditionRoute(HttpResponse response)
        {
            var locations = response.Headers.GetValues("Location");
            if (locations?.Length != 1)
            {
                return null;
            }

            var location = locations[0];
            var match = IsbnEditionRedirect.Match(location ?? string.Empty);
            if (!match.Success || !Uri.TryCreate(OpenLibraryOrigin, location, out var target) ||
                target.Scheme != Uri.UriSchemeHttps || target.Port != OpenLibraryOrigin.Port ||
                !string.Equals(target.Host, OpenLibraryOrigin.Host, StringComparison.OrdinalIgnoreCase) ||
                target.UserInfo.Length != 0 || target.Query.Length != 0 || target.Fragment.Length != 0)
            {
                return null;
            }

            return match.Groups["path"].Value[1..];
        }

        private T ExecuteWithRateLimitRetry<T>(
            string endpointKey,
            HttpRequest request,
            int maxRetries,
            string authorId = null,
            bool followIsbnRedirect = false,
            bool requireMatchingIsbnRedirect = false,
            Func<T> unavailable = null,
            bool bounded = false)
            where T : class
        {
            var acquired = bounded && Monitor.TryEnter(_boundedRequestLock);
            if (bounded && !acquired)
            {
                return unavailable?.Invoke();
            }

            try
            {
                return ExecuteRequests(endpointKey, request, maxRetries, authorId, followIsbnRedirect, requireMatchingIsbnRedirect, unavailable, bounded);
            }
            finally
            {
                if (acquired)
                {
                    Monitor.Exit(_boundedRequestLock);
                }
            }
        }

        private T ExecuteRequests<T>(
            string endpointKey,
            HttpRequest request,
            int maxRetries,
            string authorId,
            bool followIsbnRedirect,
            bool requireMatchingIsbnRedirect,
            Func<T> unavailable,
            bool bounded)
            where T : class
        {
            maxRetries = Math.Max(0, maxRetries);
            var breakerThreshold = Math.Max(1, _configService.MetadataProviderCircuitBreakerThreshold);
            var breakerDurationSeconds = Math.Max(5, _configService.MetadataProviderCircuitBreakerDurationSeconds);
            var attempts = 0;
            string redirectedEditionRoute = null;

            var state = _circuitState.GetOrAdd(endpointKey, _ => new CircuitState());
            if (state.OpenUntilUtc > DateTime.UtcNow)
            {
                _logger.Debug("OpenLibrary endpoint {0} is in open-circuit until {1:o}", endpointKey, state.OpenUntilUtc);
                return unavailable?.Invoke();
            }

            while (true)
            {
                if (bounded)
                {
                    if (!_configService.EnableOpenLibraryProvider || _boundedUntilUtc > DateTime.UtcNow)
                    {
                        return unavailable?.Invoke();
                    }

                    request.RateLimit = TimeSpan.FromSeconds(1);
                    request.RateLimitKey = "OpenLibrary";
                }

                var logTarget = followIsbnRedirect || bounded ? endpointKey : request.Url.ToString();
                HttpResponse response;
                try
                {
                    response = _httpClient.Get(request);
                }
                catch (HttpException ex)
                {
                    state.ConsecutiveFailures++;
                    if (state.ConsecutiveFailures >= breakerThreshold)
                    {
                        state.OpenUntilUtc = DateTime.UtcNow.AddSeconds(breakerDurationSeconds);
                    }

                    if (followIsbnRedirect || bounded)
                    {
                        _logger.Warn("OpenLibrary HTTP error for {0}: {1}", logTarget, ex.Response?.StatusCode);
                    }
                    else
                    {
                        _logger.Warn(ex, "OpenLibrary HTTP error for {0}", logTarget);
                    }

                    if (unavailable != null)
                    {
                        return unavailable();
                    }

                    if (bounded)
                    {
                        return null;
                    }

                    throw new OpenLibraryException("HTTP error communicating with Open Library: {0}", ex, ex.Message);
                }

                if (followIsbnRedirect && response.HasHttpRedirect)
                {
                    var route = redirectedEditionRoute == null ? GetIsbnEditionRoute(response) : null;
                    if (route == null)
                    {
                        return null;
                    }

                    request = BuildIsbnRequest(route);
                    redirectedEditionRoute = route;
                    continue;
                }

                if (bounded && response.StatusCode == HttpStatusCode.TooManyRequests)
                {
                    var retry = new TooManyRequestsException(response.Headers["Retry-After"]).RetryAfter;
                    var delay = BookSearchFallbackExecutionService.GetRateLimitCooldown(retry);
                    _boundedUntilUtc = DateTime.UtcNow.Add(delay);
                }

                if (bounded && response.HasHttpRedirect)
                {
                    return unavailable?.Invoke();
                }

                if (response.StatusCode == HttpStatusCode.TooManyRequests)
                {
                    state.ConsecutiveFailures++;

                    if (attempts >= maxRetries)
                    {
                        _logger.Warn("OpenLibrary rate-limit (429) exceeded retry budget for {0}", logTarget);

                        if (state.ConsecutiveFailures >= breakerThreshold)
                        {
                            state.OpenUntilUtc = DateTime.UtcNow.AddSeconds(breakerDurationSeconds);
                        }

                        return unavailable?.Invoke();
                    }

                    var retryAfterSeconds = ParseRetryAfter(response);
                    _logger.Info("OpenLibrary rate-limited (429); backing off {0}s", retryAfterSeconds);
                    System.Threading.Thread.Sleep(TimeSpan.FromSeconds(retryAfterSeconds));
                    attempts++;
                    continue;
                }

                if (response.StatusCode == HttpStatusCode.NotFound)
                {
                    state.ConsecutiveFailures = 0;
                    if (authorId != null)
                    {
                        throw new AuthorNotFoundException(authorId, isConfirmedMissing: true);
                    }

                    return null;
                }

                if (response.HasHttpError)
                {
                    state.ConsecutiveFailures++;

                    if (state.ConsecutiveFailures >= breakerThreshold)
                    {
                        state.OpenUntilUtc = DateTime.UtcNow.AddSeconds(breakerDurationSeconds);
                    }

                    _logger.Warn("OpenLibrary returned HTTP {0} for {1}", (int)response.StatusCode, logTarget);
                    return unavailable?.Invoke();
                }

                if (bounded && response.StatusCode != HttpStatusCode.OK)
                {
                    return unavailable?.Invoke();
                }

                state.ConsecutiveFailures = 0;
                state.OpenUntilUtc = DateTime.MinValue;

                if (string.IsNullOrWhiteSpace(response.Content))
                {
                    return unavailable?.Invoke();
                }

                try
                {
                    var resource = JsonSerializer.Deserialize<T>(response.Content, JsonOptions);
                    if (requireMatchingIsbnRedirect && redirectedEditionRoute != null && resource is OlEditionResource edition &&
                        edition.Key != $"/{redirectedEditionRoute[..^5]}")
                    {
                        return null;
                    }

                    return resource ?? unavailable?.Invoke();
                }
                catch (JsonException ex)
                {
                    _logger.Warn(followIsbnRedirect || bounded ? null : ex, "OpenLibrary: failed to deserialize response from {0}", logTarget);
                    return unavailable?.Invoke();
                }
            }
        }

        /// <summary>Removes /works/, /books/, /authors/ prefix if present, returning just the ID token.</summary>
        private static string StripKeyPrefix(string key)
        {
            if (key == null)
            {
                return key;
            }

            var lastSlash = key.LastIndexOf('/');
            return lastSlash >= 0 ? key.Substring(lastSlash + 1) : key;
        }

        private static int ParseRetryAfter(HttpResponse response)
        {
            if (response.Headers.ContainsKey("Retry-After") &&
                int.TryParse(response.Headers["Retry-After"], out var seconds))
            {
                return Math.Min(seconds, 60);
            }

            return 5;
        }

        private enum OperationClass
        {
            Search,
            Isbn,
            Work
        }

        private int GetTimeoutSeconds(OperationClass operation)
        {
            var fallback = Math.Max(5, _configService.MetadataProviderTimeoutSeconds);

            var configured = operation switch
            {
                OperationClass.Search => _configService.OpenLibrarySearchTimeoutSeconds,
                OperationClass.Isbn => _configService.OpenLibraryIsbnTimeoutSeconds,
                _ => _configService.OpenLibraryWorkTimeoutSeconds
            };

            return configured > 0 ? Math.Max(5, configured) : fallback;
        }

        private int GetRetryBudget(OperationClass operation)
        {
            var fallback = Math.Max(0, _configService.MetadataProviderRetryBudget);

            var configured = operation switch
            {
                OperationClass.Search => _configService.OpenLibrarySearchRetryBudget,
                OperationClass.Isbn => _configService.OpenLibraryIsbnRetryBudget,
                _ => _configService.OpenLibraryWorkRetryBudget
            };

            return configured >= 0 ? configured : fallback;
        }
    }
}
