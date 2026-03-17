using System;
using System.Net;
using System.Text.Json;
using NLog;
using NzbDrone.Common.Http;
using NzbDrone.Core.MetadataSource.OpenLibrary.Resources;

namespace NzbDrone.Core.MetadataSource.OpenLibrary
{
    public interface IOpenLibraryClient
    {
        OlSearchResponse Search(string query, int limit = 20);
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
        private readonly Logger _logger;
        private readonly IHttpRequestBuilderFactory _requestBuilder;

        public OpenLibraryClient(IHttpClient httpClient, Logger logger)
        {
            _httpClient = httpClient;
            _logger = logger;
            _requestBuilder = new HttpRequestBuilder(BaseUrl + "/{route}")
                .SetHeader("Accept", "application/json")
                .KeepAlive()
                .CreateFactory();
        }

        public OlSearchResponse Search(string query, int limit = 20)
        {
            _logger.Debug("OpenLibrary search: {0}", query);

            var request = new HttpRequestBuilder(BaseUrl + "/search.json")
                .AddQueryParam("q", query)
                .AddQueryParam("limit", limit.ToString())
                .AddQueryParam("fields", "key,title,author_name,author_key,isbn,cover_i,first_publish_year,number_of_pages_median,language,subject,ratings_average,ratings_count,edition_count")
                .SetHeader("Accept", "application/json")
                .Build();

            request.RequestTimeout = TimeSpan.FromSeconds(30);
            request.SuppressHttpError = true;

            var response = ExecuteWithRateLimitRetry<OlSearchResponse>(request);
            return response ?? new OlSearchResponse();
        }

        public OlWorkResource GetWork(string olid)
        {
            _logger.Debug("OpenLibrary get work: {0}", olid);

            var cleanKey = StripKeyPrefix(olid);
            var request = _requestBuilder.Create()
                .SetSegment("route", $"works/{cleanKey}.json")
                .Build();

            request.RequestTimeout = TimeSpan.FromSeconds(30);
            request.SuppressHttpError = true;

            return ExecuteWithRateLimitRetry<OlWorkResource>(request);
        }

        public OlAuthorResource GetAuthor(string olid)
        {
            _logger.Debug("OpenLibrary get author: {0}", olid);

            var cleanKey = StripKeyPrefix(olid);
            var request = _requestBuilder.Create()
                .SetSegment("route", $"authors/{cleanKey}.json")
                .Build();

            request.RequestTimeout = TimeSpan.FromSeconds(30);
            request.SuppressHttpError = true;

            return ExecuteWithRateLimitRetry<OlAuthorResource>(request);
        }

        public OlEditionResource GetEditionByIsbn(string isbn)
        {
            _logger.Debug("OpenLibrary get edition by ISBN: {0}", isbn);

            var request = _requestBuilder.Create()
                .SetSegment("route", $"isbn/{isbn}.json")
                .Build();

            request.RequestTimeout = TimeSpan.FromSeconds(30);
            request.SuppressHttpError = true;

            return ExecuteWithRateLimitRetry<OlEditionResource>(request);
        }

        public OlEditionResource GetEdition(string olid)
        {
            _logger.Debug("OpenLibrary get edition: {0}", olid);

            var cleanKey = StripKeyPrefix(olid);
            var request = _requestBuilder.Create()
                .SetSegment("route", $"books/{cleanKey}.json")
                .Build();

            request.RequestTimeout = TimeSpan.FromSeconds(30);
            request.SuppressHttpError = true;

            return ExecuteWithRateLimitRetry<OlEditionResource>(request);
        }

        private T ExecuteWithRateLimitRetry<T>(HttpRequest request)
            where T : class
        {
            const int maxRetries = 2;
            var attempts = 0;

            while (true)
            {
                HttpResponse response;
                try
                {
                    response = _httpClient.Get(request);
                }
                catch (HttpException ex)
                {
                    _logger.Warn(ex, "OpenLibrary HTTP error for {0}", request.Url);
                    throw new OpenLibraryException("HTTP error communicating with Open Library: {0}", ex, ex.Message);
                }

                if (response.StatusCode == HttpStatusCode.TooManyRequests)
                {
                    if (attempts >= maxRetries)
                    {
                        _logger.Warn("OpenLibrary rate-limit (429) exceeded retry budget for {0}", request.Url);
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
                    return null;
                }

                if (response.HasHttpError)
                {
                    _logger.Warn("OpenLibrary returned HTTP {0} for {1}", (int)response.StatusCode, request.Url);
                    return null;
                }

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
    }
}
