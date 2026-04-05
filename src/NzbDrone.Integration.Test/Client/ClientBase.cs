using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using Bibliophilarr.Http;
using Bibliophilarr.Http.REST;
using FluentAssertions;
using NLog;
using NzbDrone.Common.Serializer;

namespace NzbDrone.Integration.Test.Client
{
    public class SimpleRestRequest
    {
        public string Resource { get; set; }
        public HttpMethod Method { get; set; } = HttpMethod.Get;
        public Dictionary<string, string> Headers { get; } = new Dictionary<string, string>();
        public Dictionary<string, string> QueryParameters { get; } = new Dictionary<string, string>();
        public string Body { get; set; }

        public SimpleRestRequest(string resource)
        {
            Resource = resource;
        }

        public SimpleRestRequest(string resource, HttpMethod method)
        {
            Resource = resource;
            Method = method;
        }

        public void AddHeader(string name, string value) => Headers[name] = value;
        public void AddParameter(string name, object value) => QueryParameters[name] = value?.ToString();
        public void AddQueryParameter(string name, string value) => QueryParameters[name] = value;
        public void AddUrlSegment(string name, object value) => Resource = Resource.Replace("{" + name + "}", value?.ToString());

        public void AddJsonBody(object body)
        {
            Body = Json.ToJson(body);
        }
    }

    public class SimpleRestResponse
    {
        public HttpStatusCode StatusCode { get; set; }
        public string Content { get; set; }
        public string ContentType { get; set; }
        public Dictionary<string, string[]> Headers { get; set; } = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        public Exception ErrorException { get; set; }
        public string ErrorMessage { get; set; }
        public bool IsSuccessful => (int)StatusCode >= 200 && (int)StatusCode < 300;
    }

    public class ClientBase
    {
        protected readonly HttpClient _httpClient;
        protected readonly string _resource;
        protected readonly string _apiKey;
        protected readonly Logger _logger;

        public ClientBase(HttpClient httpClient, string apiKey, string resource)
        {
            _httpClient = httpClient;
            _resource = resource;
            _apiKey = apiKey;

            _logger = LogManager.GetLogger("REST");
        }

        public SimpleRestRequest BuildRequest(string command = "")
        {
            var request = new SimpleRestRequest(_resource + "/" + command.Trim('/'));
            request.AddHeader("Authorization", _apiKey);
            request.AddHeader("X-Api-Key", _apiKey);

            return request;
        }

        public SimpleRestResponse ExecuteRaw(SimpleRestRequest request)
        {
            var uriBuilder = new UriBuilder(_httpClient.BaseAddress + request.Resource.TrimStart('/'));

            if (request.QueryParameters.Count > 0)
            {
                var queryParts = request.QueryParameters.Select(kvp =>
                    Uri.EscapeDataString(kvp.Key) + "=" + Uri.EscapeDataString(kvp.Value ?? ""));
                uriBuilder.Query = string.Join("&", queryParts);
            }

            var httpRequest = new HttpRequestMessage(request.Method, uriBuilder.Uri);

            foreach (var header in request.Headers)
            {
                httpRequest.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }

            if (request.Body != null)
            {
                httpRequest.Content = new StringContent(request.Body, Encoding.UTF8, "application/json");
            }

            _logger.Info("{0}: {1}", request.Method, uriBuilder.Uri);

            try
            {
                var response = _httpClient.SendAsync(httpRequest).GetAwaiter().GetResult();
                var content = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();

                _logger.Info("Response: {0}", content);

                var headers = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
                foreach (var h in response.Headers)
                {
                    headers[h.Key] = h.Value.ToArray();
                }

                foreach (var h in response.Content.Headers)
                {
                    headers[h.Key] = h.Value.ToArray();
                }

                return new SimpleRestResponse
                {
                    StatusCode = response.StatusCode,
                    Content = content,
                    ContentType = response.Content.Headers.ContentType?.ToString(),
                    Headers = headers
                };
            }
            catch (Exception ex)
            {
                return new SimpleRestResponse
                {
                    StatusCode = 0,
                    ErrorException = ex,
                    ErrorMessage = ex.Message
                };
            }
        }

        public string Execute(SimpleRestRequest request, HttpStatusCode statusCode)
        {
            var response = ExecuteRaw(request);

            if (response.ErrorException != null)
            {
                throw response.ErrorException;
            }

            AssertDisableCache(response);

            response.ErrorMessage.Should().BeNullOrWhiteSpace();

            response.StatusCode.Should().Be(statusCode, response.Content ?? string.Empty);

            return response.Content;
        }

        public T Execute<T>(SimpleRestRequest request, HttpStatusCode statusCode)
            where T : class, new()
        {
            var content = Execute(request, statusCode);

            return Json.Deserialize<T>(content);
        }

        private static void AssertDisableCache(SimpleRestResponse response)
        {
            var cacheControl = response.Headers.ContainsKey("Cache-Control")
                ? string.Join(", ", response.Headers["Cache-Control"])
                : string.Empty;
            cacheControl.Split(',').Select(x => x.Trim())
                .Should().BeEquivalentTo("no-store, no-cache".Split(',').Select(x => x.Trim()));

            response.Headers.Should().ContainKey("Pragma");
            string.Join(", ", response.Headers["Pragma"]).Should().Be("no-cache");

            response.Headers.Should().ContainKey("Expires");
            string.Join(", ", response.Headers["Expires"]).Should().Be("-1");
        }
    }

    public class ClientBase<TResource> : ClientBase
        where TResource : RestResource, new()
    {
        public ClientBase(HttpClient httpClient, string apiKey, string resource = null)
            : base(httpClient, apiKey, resource ?? new TResource().ResourceName)
        {
        }

        public List<TResource> All()
        {
            var request = BuildRequest();
            return Get<List<TResource>>(request);
        }

        public PagingResource<TResource> GetPaged(int pageNumber, int pageSize, string sortKey, string sortDir, string filterKey = null, object filterValue = null)
        {
            var request = BuildRequest();
            request.AddParameter("page", pageNumber);
            request.AddParameter("pageSize", pageSize);
            request.AddParameter("sortKey", sortKey);
            request.AddParameter("sortDir", sortDir);

            if (filterKey != null && filterValue != null)
            {
                request.AddParameter(filterKey, filterValue);
            }

            return Get<PagingResource<TResource>>(request);
        }

        public TResource Post(TResource body, HttpStatusCode statusCode = HttpStatusCode.Created)
        {
            var request = BuildRequest();
            request.AddJsonBody(body);
            return Post<TResource>(request, statusCode);
        }

        public TResource Put(TResource body, HttpStatusCode statusCode = HttpStatusCode.Accepted)
        {
            var request = BuildRequest();
            request.AddJsonBody(body);
            return Put<TResource>(request, statusCode);
        }

        public TResource Get(int id, HttpStatusCode statusCode = HttpStatusCode.OK)
        {
            var request = BuildRequest(id.ToString());
            return Get<TResource>(request, statusCode);
        }

        public TResource GetSingle(HttpStatusCode statusCode = HttpStatusCode.OK)
        {
            var request = BuildRequest();
            return Get<TResource>(request, statusCode);
        }

        public void Delete(int id)
        {
            var request = BuildRequest(id.ToString());
            Delete(request);
        }

        public object InvalidGet(int id, HttpStatusCode statusCode = HttpStatusCode.NotFound)
        {
            var request = BuildRequest(id.ToString());
            return Get<object>(request, statusCode);
        }

        public object InvalidPost(TResource body, HttpStatusCode statusCode = HttpStatusCode.BadRequest)
        {
            var request = BuildRequest();
            request.AddJsonBody(body);
            return Post<object>(request, statusCode);
        }

        public object InvalidPut(TResource body, HttpStatusCode statusCode = HttpStatusCode.BadRequest)
        {
            var request = BuildRequest();
            request.AddJsonBody(body);
            return Put<object>(request, statusCode);
        }

        public T Get<T>(SimpleRestRequest request, HttpStatusCode statusCode = HttpStatusCode.OK)
            where T : class, new()
        {
            request.Method = HttpMethod.Get;
            return Execute<T>(request, statusCode);
        }

        public T Post<T>(SimpleRestRequest request, HttpStatusCode statusCode = HttpStatusCode.Created)
            where T : class, new()
        {
            request.Method = HttpMethod.Post;
            return Execute<T>(request, statusCode);
        }

        public T Put<T>(SimpleRestRequest request, HttpStatusCode statusCode = HttpStatusCode.Accepted)
            where T : class, new()
        {
            request.Method = HttpMethod.Put;
            return Execute<T>(request, statusCode);
        }

        public void Delete(SimpleRestRequest request, HttpStatusCode statusCode = HttpStatusCode.OK)
        {
            request.Method = HttpMethod.Delete;
            Execute<object>(request, statusCode);
        }
    }
}
