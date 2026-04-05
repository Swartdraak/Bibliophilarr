using System.Collections.Generic;
using System.Net.Http;
using Bibliophilarr.Api.V1.Books;

namespace NzbDrone.Integration.Test.Client
{
    public class BookClient : ClientBase<BookResource>
    {
        public BookClient(HttpClient httpClient, string apiKey)
            : base(httpClient, apiKey, "book")
        {
        }

        public List<BookResource> GetBooksInAuthor(int authorId)
        {
            var request = BuildRequest("?authorId=" + authorId.ToString());
            return Get<List<BookResource>>(request);
        }
    }
}
