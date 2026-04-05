using System;
using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using NLog;
using NzbDrone.Common.Http;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.MetadataSource.OpenLibrary.Resources;

namespace NzbDrone.Core.MetadataSource.OpenLibrary
{
    public interface IOpenLibraryClient
    {
        OlSearchResponse Search(string query, int limit = 20, int offset = 0);
        OlAuthorWorksResponse GetAuthorWorks(string olid, int limit = 100, int offset = 0);
        OlWorkEditionsResponse GetWorkEditions(string olid, int limit = 20, int offset = 0);
        OlWorkResource GetWork(string olid);
        OlAuthorResource GetAuthor(string olid);
        OlEditionResource GetEditionByIsbn(string isbn);
        OlEditionResource GetEdition(string olid);
    }

    public class OpenLibraryClient : IOpenLibraryClient
    {
        private const string BaseUrl = "https://openlibrary.org";
        private const string CoverBaseUrl = "https://covers.openlibrary.org";

        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        private readonly IHttpClient _httpClient;
        private readonly IConfigService _configService;
        private readonly Logger _logger;
        private readonly IHttpRequestBuilderFactory _requestBuilder;
        private readonly ConcurrentDictionary<string, CircuitState> _circuitState = new ConcurrentDictionary<string, CircuitState>();

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

            var response = ExecuteWithRateLimitRetry<OlSearchResponse>("openlibrary.search", request, GetRetryBudget(OperationClass.Search));
            return response ?? new OlSearchResponse();
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

            var response = ExecuteWithRateLimitRetry<OlAuthorWorksResponse>("openlibrary.author-works", request, GetRetryBudget(OperationClass.Work));
            return response ?? new OlAuthorWorksResponse();
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

        public OlWorkResource GetWork(string olid)
        {
            _logger.Debug("OpenLibrary get work: {0}", olid);

            var cleanKey = StripKeyPrefix(olid);
            var request = _requestBuilder.Create()
                .SetSegment("route", $"works/{cleanKey}.json")
                .Build();

            request.RequestTimeout = TimeSpan.FromSeconds(GetTimeoutSeconds(OperationClass.Work));
            request.SuppressHttpError = true;

            return ExecuteWithRateLimitRetry<OlWorkResource>("openlibrary.work", request, GetRetryBudget(OperationClass.Work));
        }

        public OlAuthorResource GetAuthor(string olid)
        {
            _logger.Debug("OpenLibrary get author: {0}", olid);

            var cleanKey = StripKeyPrefix(olid);
            var request = _requestBuilder.Create()
                .SetSegment("route", $"authors/{cleanKey}.json")
                .Build();

            request.RequestTimeout = TimeSpan.FromSeconds(GetTimeoutSeconds(OperationClass.Work));
            request.SuppressHttpError = true;

            return ExecuteWithRateLimitRetry<OlAuthorResource>("openlibrary.author", request, GetRetryBudget(OperationClass.Work));
        }

        public OlEditionResource GetEditionByIsbn(string isbn)
        {
            _logger.Debug("OpenLibrary get edition by ISBN: {0}", isbn);

            var request = _requestBuilder.Create()
                .SetSegment("route", $"isbn/{isbn}.json")
                .Build();

            request.RequestTimeout = TimeSpan.FromSeconds(GetTimeoutSeconds(OperationClass.Isbn));
            request.SuppressHttpError = true;

            return ExecuteWithRateLimitRetry<OlEditionResource>("openlibrary.isbn", request, GetRetryBudget(OperationClass.Isbn));
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

        private T ExecuteWithRateLimitRetry<T>(string endpointKey, HttpRequest request, int maxRetries)
            where T : class
        {
            maxRetries = Math.Max(0, maxRetries);
            var breakerThreshold = Math.Max(1, _configService.MetadataProviderCircuitBreakerThreshold);
            var breakerDurationSeconds = Math.Max(5, _configService.MetadataProviderCircuitBreakerDurationSeconds);
            var attempts = 0;

            var state = _circuitState.GetOrAdd(endpointKey, _ => new CircuitState());
            if (state.OpenUntilUtc > DateTime.UtcNow)
            {
                _logger.Debug("OpenLibrary endpoint {0} is in open-circuit until {1:o}", endpointKey, state.OpenUntilUtc);
                return null;
            }

            while (true)
            {
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

                    _logger.Warn(ex, "OpenLibrary HTTP error for {0}", request.Url);
                    throw new OpenLibraryException("HTTP error communicating with Open Library: {0}", ex, ex.Message);
                }

                if (response.StatusCode == HttpStatusCode.TooManyRequests)
                {
                    state.ConsecutiveFailures++;

                    if (attempts >= maxRetries)
                    {
                        _logger.Warn("OpenLibrary rate-limit (429) exceeded retry budget for {0}", request.Url);

                        if (state.ConsecutiveFailures >= breakerThreshold)
                        {
                            state.OpenUntilUtc = DateTime.UtcNow.AddSeconds(breakerDurationSeconds);
                        }

                        return null;
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
                    return null;
                }

                if (response.HasHttpError)
                {
                    state.ConsecutiveFailures++;

                    if (state.ConsecutiveFailures >= breakerThreshold)
                    {
                        state.OpenUntilUtc = DateTime.UtcNow.AddSeconds(breakerDurationSeconds);
                    }

                    _logger.Warn("OpenLibrary returned HTTP {0} for {1}", (int)response.StatusCode, request.Url);
                    return null;
                }

                state.ConsecutiveFailures = 0;
                state.OpenUntilUtc = DateTime.MinValue;

                if (string.IsNullOrWhiteSpace(response.Content))
                {
                    return null;
                }

                try
                {
                    return JsonSerializer.Deserialize<T>(response.Content, JsonOptions);
                }
                catch (JsonException ex)
                {
                    _logger.Warn(ex, "OpenLibrary: failed to deserialize response from {0}", request.Url);
                    return null;
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
