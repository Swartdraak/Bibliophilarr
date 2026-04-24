using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using Bibliophilarr.Api.V1.Author;

namespace NzbDrone.Integration.Test.Client
{
    public class AuthorClient : ClientBase<AuthorResource>
    {
        public AuthorClient(HttpClient httpClient, string apiKey)
            : base(httpClient, apiKey)
        {
        }

        public List<AuthorResource> Lookup(string term)
        {
            var request = BuildRequest("lookup");
            request.AddQueryParameter("term", term);
            return Get<List<AuthorResource>>(request);
        }

        public List<AuthorResource> Editor(AuthorEditorResource author)
        {
            var request = BuildRequest("editor");
            request.AddJsonBody(author);
            return Put<List<AuthorResource>>(request);
        }

        public AuthorResource Get(string slug, HttpStatusCode statusCode = HttpStatusCode.OK)
        {
            var request = BuildRequest(slug);
            return Get<AuthorResource>(request, statusCode);
        }
    }

    public class SystemInfoClient : ClientBase<AuthorResource>
    {
        public SystemInfoClient(HttpClient httpClient, string apiKey)
            : base(httpClient, apiKey)
        {
        }
    }
}
