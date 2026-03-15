using Bibliophilarr.Api.V1.Author;
using Bibliophilarr.Api.V1.Books;
using Bibliophilarr.Http.REST;

namespace Bibliophilarr.Api.V1.Search
{
    public class SearchResource : RestResource
    {
        public string ForeignId { get; set; }
        public AuthorResource Author { get; set; }
        public BookResource Book { get; set; }
    }
}
