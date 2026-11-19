using System.Collections.Generic;
using System.Linq;
using Bibliophilarr.Http;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.MediaCover;
using NzbDrone.Core.MetadataSource;

namespace Bibliophilarr.Api.V1.Books
{
    [V1ApiController("book/lookup")]
    public class BookLookupController : Controller
    {
        private readonly IMetadataProviderOrchestrator _searchProxy;
        private readonly IMapCoversToLocal _coverMapper;

        public BookLookupController(IMetadataProviderOrchestrator searchProxy, IMapCoversToLocal coverMapper)
        {
            _searchProxy = searchProxy;
            _coverMapper = coverMapper;
        }

        [HttpGet]
        public object Search(string term)
        {
            var searchResults = _searchProxy.SearchForNewBook(term, null);
            return MapToResource(searchResults ?? Enumerable.Empty<NzbDrone.Core.Books.Book>()).ToList();
        }

        private IEnumerable<BookResource> MapToResource(IEnumerable<NzbDrone.Core.Books.Book> books)
        {
            foreach (var currentBook in books)
            {
                var resource = currentBook.ToResource();

                _coverMapper.ConvertToLocalUrls(resource.Id, MediaCoverEntity.Book, resource.Images);

                var cover = resource.Images.FirstOrDefault(c => c.CoverType == MediaCoverTypes.Cover);

                if (cover != null)
                {
                    resource.RemoteCover = cover.RemoteUrl;
                }

                yield return resource;
            }
        }
    }
}
