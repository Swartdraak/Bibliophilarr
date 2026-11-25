using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Common.Cache;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Http.Dispatchers;
using NzbDrone.Common.TPL;

namespace NzbDrone.Common.Http
{
    public interface IHttpClient
    {
        HttpResponse Execute(HttpRequest request);
        void DownloadFile(string url, string fileName, string userAgent = null);
        HttpResponse Get(HttpRequest request);
        HttpResponse<T> Get<T>(HttpRequest request)
            where T : new();
        HttpResponse Head(HttpRequest request);
        HttpResponse Post(HttpRequest request);
        HttpResponse<T> Post<T>(HttpRequest request)
            where T : new();

        Task<HttpResponse> ExecuteAsync(HttpRequest request);
        Task DownloadFileAsync(string url, string fileName, string userAgent = null);
        Task<HttpResponse> GetAsync(HttpRequest request);
        Task<HttpResponse<T>> GetAsync<T>(HttpRequest request)
            where T : new();
        Task<HttpResponse> HeadAsync(HttpRequest request);
        Task<HttpResponse> PostAsync(HttpRequest request);
        Task<HttpResponse<T>> PostAsync<T>(HttpRequest request)
            where T : new();
    }

    public class HttpClient : IHttpClient
    {
        private const int MaxRedirects = 5;

        private readonly Logger _logger;
        private readonly IRateLimitService _rateLimitService;
        private readonly ICached<CookieContainer> _cookieContainerCache;
        private readonly HardcoverRateLimitGate _hardcoverRateLimitGate;
        private readonly List<IHttpRequestInterceptor> _requestInterceptors;
        private readonly IHttpDispatcher _httpDispatcher;

        public HttpClient(IEnumerable<IHttpRequestInterceptor> requestInterceptors,
            ICacheManager cacheManager,
            IRateLimitService rateLimitService,
            IHttpDispatcher httpDispatcher,
            Logger logger,
            IAppFolderInfo appFolderInfo = null)
        {
            _requestInterceptors = requestInterceptors.ToList();
            _rateLimitService = rateLimitService;
            _httpDispatcher = httpDispatcher;
            _logger = logger;

#pragma warning disable SYSLIB0014 // Required for compatibility with legacy HttpWebRequest/ServicePoint behavior still used by this client stack.
            ServicePointManager.DefaultConnectionLimit = 12;
#pragma warning restore SYSLIB0014
            _cookieContainerCache = cacheManager.GetCache<CookieContainer>(typeof(HttpClient));
            _hardcoverRateLimitGate = HardcoverRateLimitGate.Get(cacheManager, appFolderInfo, _logger);
        }

        public virtual async Task<HttpResponse> ExecuteAsync(HttpRequest request)
        {
            var cookieContainer = InitializeRequestCookies(request);

            var response = await ExecuteRequestAsync(request, cookieContainer);

            if (request.AllowAutoRedirect && response.HasHttpRedirect)
            {
                var autoRedirectChain = new List<string> { request.Url.ToString() };

                do
                {
                    request.Url += new HttpUri(response.Headers.GetSingleValue("Location"));
                    autoRedirectChain.Add(request.Url.ToString());

                    if (request.OmitSensitiveDiagnostics)
                    {
                        _logger.Trace("Following HTTP redirect ({0}).", (int)response.StatusCode);
                    }
                    else
                    {
                        _logger.Trace("Redirected to {0}", request.Url);
                    }

                    if (autoRedirectChain.Count > MaxRedirects)
                    {
                        throw new WebException($"Too many automatic redirections were attempted for {autoRedirectChain.Join(" -> ")}", WebExceptionStatus.ProtocolError);
                    }

                    // 302 or 303 should default to GET on redirect even if POST on original
                    if (RequestRequiresForceGet(response.StatusCode, response.Request.Method))
                    {
                        request.Method = HttpMethod.Get;
                        request.ContentData = null;
                        request.ContentSummary = null;
                    }

                    response = await ExecuteRequestAsync(request, cookieContainer);
                }
                while (response.HasHttpRedirect);
            }

            if (response.HasHttpRedirect && !request.AllowAutoRedirect)
            {
                if (request.OmitSensitiveDiagnostics)
                {
                    _logger.Debug("HTTP redirect ({0}) received with automatic redirects disabled.", (int)response.StatusCode);
                }
                else
                {
                    _logger.Debug("Server requested a redirect to [{0}] with automatic redirects disabled for [{1}].", response.Headers["Location"], request.Url);
                }
            }

            if (!request.SuppressHttpError && response.HasHttpError && (request.SuppressHttpErrorStatusCodes == null || !request.SuppressHttpErrorStatusCodes.Contains(response.StatusCode)))
            {
                if (request.LogHttpError && !response.IsLocallySuppressed)
                {
                    if (response.HardcoverCooldownUntilUtc is { } deadline)
                    {
                        _logger.Warn("Hardcover GraphQL returned HTTP 429. Requests held until {0:O}", deadline);
                    }
                    else
                    {
                        if (request.OmitSensitiveDiagnostics)
                        {
                            _logger.Warn("HTTP request failed (HTTP {0}).", (int)response.StatusCode);
                        }
                        else
                        {
                            _logger.Warn("HTTP Error - {0}", response);
                        }
                    }
                }

                if ((int)response.StatusCode == 429)
                {
                    if (response.HardcoverCooldownUntilUtc is { } hardcoverDeadline)
                    {
                        throw new TooManyRequestsException(request, response, hardcoverDeadline);
                    }

                    throw new TooManyRequestsException(request, response);
                }
                else
                {
                    throw new HttpException(request, response);
                }
            }

            return response;
        }

        public HttpResponse Execute(HttpRequest request)
        {
            return ExecuteAsync(request).GetAwaiter().GetResult();
        }

        private static bool RequestRequiresForceGet(HttpStatusCode statusCode, HttpMethod requestMethod)
        {
            return statusCode switch
            {
                HttpStatusCode.Moved or HttpStatusCode.Found or HttpStatusCode.MultipleChoices => requestMethod == HttpMethod.Post,
                HttpStatusCode.SeeOther => requestMethod != HttpMethod.Get && requestMethod != HttpMethod.Head,
                _ => false,
            };
        }

        private async Task<HttpResponse> ExecuteRequestAsync(HttpRequest request, CookieContainer cookieContainer)
        {
            foreach (var interceptor in _requestInterceptors)
            {
                request = interceptor.PreRequest(request);
            }

            var hardcoverGate = HardcoverRateLimitGate.AppliesTo(request) ? _hardcoverRateLimitGate : null;
            var held = hardcoverGate?.GetHeldResponse(request);
            if (held != null)
            {
                return held;
            }

            var response = hardcoverGate == null
                ? await DispatchRateLimitedRequestAsync(request, cookieContainer)
                : await hardcoverGate.DispatchAsync(request, () => DispatchRateLimitedRequestAsync(request, cookieContainer));

            foreach (var interceptor in _requestInterceptors)
            {
                response = interceptor.PostResponse(response);
            }

            if (request.LogResponseContent && !request.OmitSensitiveDiagnostics && !response.OmitContentFromDiagnostics && response.ResponseData != null)
            {
                _logger.Trace("Response content ({0} bytes): {1}", response.ResponseData.Length, response.Content);
            }

            return response;
        }

        private async Task<HttpResponse> DispatchRateLimitedRequestAsync(HttpRequest request, CookieContainer cookieContainer)
        {
            if (request.RateLimit != TimeSpan.Zero)
            {
                await _rateLimitService.WaitAndPulseAsync(request.Url.Host, request.RateLimitKey, request.RateLimit);
            }

            if (request.OmitSensitiveDiagnostics)
            {
                _logger.Trace("Sending HTTP {0} request.", request.Method);
            }
            else
            {
                _logger.Trace(request);
            }

            var stopWatch = Stopwatch.StartNew();
            var response = await _httpDispatcher.GetResponseAsync(request, cookieContainer);
            if (response.StatusCode == HttpStatusCode.TooManyRequests && HardcoverRateLimitGate.AppliesTo(request))
            {
                response.OmitContentFromDiagnostics = true;
            }

            HandleResponseCookies(response, cookieContainer);

            stopWatch.Stop();
            if (request.OmitSensitiveDiagnostics)
            {
                _logger.Trace("HTTP {0} ({1} bytes, {2} ms).", (int)response.StatusCode, response.ResponseData?.Length ?? 0, stopWatch.ElapsedMilliseconds);
            }
            else
            {
                _logger.Trace("{0} ({1} ms)", response, stopWatch.ElapsedMilliseconds);
            }

            return response;
        }

        private CookieContainer InitializeRequestCookies(HttpRequest request)
        {
            lock (_cookieContainerCache)
            {
                var sourceContainer = new CookieContainer();

                var presistentContainer = _cookieContainerCache.Get("container", () => new CookieContainer());
                var persistentCookies = presistentContainer.GetCookies((Uri)request.Url);
                sourceContainer.Add(persistentCookies);

                if (request.Cookies.Count != 0)
                {
                    foreach (var pair in request.Cookies)
                    {
                        Cookie cookie;
                        if (pair.Value == null)
                        {
                            cookie = new Cookie(pair.Key, "", "/")
                            {
                                Expires = DateTime.Now.AddDays(-1)
                            };
                        }
                        else
                        {
                            cookie = new Cookie(pair.Key, pair.Value, "/")
                            {
                                // Use Now rather than UtcNow to work around Mono cookie expiry bug.
                                // See https://gist.github.com/ta264/7822b1424f72e5b4c961
                                Expires = DateTime.Now.AddHours(1)
                            };
                        }

                        sourceContainer.Add((Uri)request.Url, cookie);

                        if (request.StoreRequestCookie)
                        {
                            presistentContainer.Add((Uri)request.Url, cookie);
                        }
                    }
                }

                return sourceContainer;
            }
        }

        private void HandleResponseCookies(HttpResponse response, CookieContainer container)
        {
            foreach (Cookie cookie in container.GetCookies((Uri)response.Request.Url))
            {
                cookie.Expired = true;
            }

            var cookieHeaders = response.GetCookieHeaders();

            if (cookieHeaders.Empty())
            {
                return;
            }

            AddCookiesToContainer(response.Request, cookieHeaders, container);

            if (response.Request.StoreResponseCookie)
            {
                lock (_cookieContainerCache)
                {
                    var persistentCookieContainer = _cookieContainerCache.Get("container", () => new CookieContainer());

                    AddCookiesToContainer(response.Request, cookieHeaders, persistentCookieContainer);
                }
            }
        }

        private void AddCookiesToContainer(HttpRequest request, string[] cookieHeaders, CookieContainer container)
        {
            foreach (var cookieHeader in cookieHeaders)
            {
                try
                {
                    container.SetCookies((Uri)request.Url, cookieHeader);
                }
                catch (Exception ex)
                {
                    if (request.OmitSensitiveDiagnostics)
                    {
                        _logger.Debug("Ignoring invalid HTTP response cookie.");
                    }
                    else
                    {
                        _logger.Debug(ex, "Invalid cookie in {0}", request.Url);
                    }
                }
            }
        }

        public async Task DownloadFileAsync(string url, string fileName, string userAgent = null)
        {
            var fileNamePart = fileName + ".part";

            try
            {
                var fileInfo = new FileInfo(fileName);
                if (fileInfo.Directory != null && !fileInfo.Directory.Exists)
                {
                    fileInfo.Directory.Create();
                }

                _logger.Debug("Downloading [{0}] to [{1}]", url, fileName);

                var stopWatch = Stopwatch.StartNew();
                await using (var fileStream = new FileStream(fileNamePart, FileMode.Create, FileAccess.ReadWrite))
                {
                    var request = new HttpRequest(url);
                    request.AllowAutoRedirect = true;
                    request.ResponseStream = fileStream;
                    request.RequestTimeout = TimeSpan.FromSeconds(300);
                    var response = await GetAsync(request);

                    if (response.Headers.ContentType != null && response.Headers.ContentType.Contains("text/html"))
                    {
                        throw new HttpException(request, response, "Site responded with html content.");
                    }
                }

                stopWatch.Stop();

                if (File.Exists(fileName))
                {
                    File.Delete(fileName);
                }

                File.Move(fileNamePart, fileName);
                _logger.Debug("Downloading Completed. took {0:0}s", stopWatch.Elapsed.Seconds);
            }
            finally
            {
                if (File.Exists(fileNamePart))
                {
                    File.Delete(fileNamePart);
                }
            }
        }

        public void DownloadFile(string url, string fileName, string userAgent = null)
        {
            // NOTE: Sync-over-async via Task.Run wrapper to avoid deadlocks.
            // See: https://docs.microsoft.com/en-us/archive/msdn-magazine/2015/july/async-programming-brownfield-async-development#the-thread-pool-hack
            Task.Run(() => DownloadFileAsync(url, fileName, userAgent)).GetAwaiter().GetResult();
        }

        public Task<HttpResponse> GetAsync(HttpRequest request)
        {
            request.Method = HttpMethod.Get;
            return ExecuteAsync(request);
        }

        public HttpResponse Get(HttpRequest request)
        {
            return Task.Run(() => GetAsync(request)).GetAwaiter().GetResult();
        }

        public async Task<HttpResponse<T>> GetAsync<T>(HttpRequest request)
            where T : new()
        {
            var response = await GetAsync(request);
            CheckResponseContentType(response);
            return new HttpResponse<T>(response);
        }

        public HttpResponse<T> Get<T>(HttpRequest request)
            where T : new()
        {
            return Task.Run(() => GetAsync<T>(request)).GetAwaiter().GetResult();
        }

        public Task<HttpResponse> HeadAsync(HttpRequest request)
        {
            request.Method = HttpMethod.Head;
            return ExecuteAsync(request);
        }

        public HttpResponse Head(HttpRequest request)
        {
            return Task.Run(() => HeadAsync(request)).GetAwaiter().GetResult();
        }

        public Task<HttpResponse> PostAsync(HttpRequest request)
        {
            request.Method = HttpMethod.Post;
            return ExecuteAsync(request);
        }

        public HttpResponse Post(HttpRequest request)
        {
            return Task.Run(() => PostAsync(request)).GetAwaiter().GetResult();
        }

        public async Task<HttpResponse<T>> PostAsync<T>(HttpRequest request)
            where T : new()
        {
            var response = await PostAsync(request);
            CheckResponseContentType(response);
            return new HttpResponse<T>(response);
        }

        public HttpResponse<T> Post<T>(HttpRequest request)
            where T : new()
        {
            return Task.Run(() => PostAsync<T>(request)).GetAwaiter().GetResult();
        }

        private void CheckResponseContentType(HttpResponse response)
        {
            if (response.Headers.ContentType != null && response.Headers.ContentType.Contains("text/html"))
            {
                throw new UnexpectedHtmlContentException(response);
            }
        }
    }
}
