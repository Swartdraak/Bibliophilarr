using System.Collections.Generic;
using System.Net.Http;
using Bibliophilarr.Api.V1.Books;
using Bibliophilarr.Http;

namespace NzbDrone.Integration.Test.Client
{
    public class WantedClient : ClientBase<BookResource>
    {
        public WantedClient(HttpClient httpClient, string apiKey, string resource)
            : base(httpClient, apiKey, resource)
        {
        }

        public PagingResource<BookResource> GetPagedIncludeAuthor(int pageNumber, int pageSize, string sortKey, string sortDir, string filterKey = null, string filterValue = null, bool includeAuthor = true)
        {
            var request = BuildRequest();
            request.AddParameter("page", pageNumber);
            request.AddParameter("pageSize", pageSize);
            request.AddParameter("sortKey", sortKey);
            request.AddParameter("sortDir", sortDir);

            if (filterKey != null && filterValue != null)
            {
                request.AddParameter("filterKey", filterKey);
                request.AddParameter("filterValue", filterValue);
            }

            request.AddParameter("includeAuthor", includeAuthor);

            return Get<PagingResource<BookResource>>(request);
        }

        public List<BookResource> GetBooksInAuthor(int authorId)
        {
            var request = BuildRequest("?authorId=" + authorId.ToString());
            return Get<List<BookResource>>(request);
        }
    }
}
