using System.Collections.Generic;
using Bibliophilarr.Api.V1.Author;
using Bibliophilarr.Api.V1.Books;
using Bibliophilarr.Http.REST;
using NzbDrone.Core.Parser.Model;

namespace Bibliophilarr.Api.V1.Parse
{
    public class ParseResource : RestResource
    {
        public string Title { get; set; }
        public ParsedBookInfo ParsedBookInfo { get; set; }
        public AuthorResource Author { get; set; }
        public List<BookResource> Books { get; set; }
    }
}
