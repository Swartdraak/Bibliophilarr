using System.Collections.Generic;
using System.Linq;
using Bibliophilarr.Http;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.MediaCover;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.Organizer;

namespace Bibliophilarr.Api.V1.Author
{
    [V1ApiController("author/lookup")]
    public class AuthorLookupController : Controller
    {
        private readonly IMetadataProviderOrchestrator _searchProxy;
        private readonly IBuildFileNames _fileNameBuilder;
        private readonly IMapCoversToLocal _coverMapper;

        public AuthorLookupController(IMetadataProviderOrchestrator searchProxy, IBuildFileNames fileNameBuilder, IMapCoversToLocal coverMapper)
        {
            _searchProxy = searchProxy;
            _fileNameBuilder = fileNameBuilder;
            _coverMapper = coverMapper;
        }

        [HttpGet]
        public object Search([FromQuery] string term)
        {
            var searchResults = _searchProxy.SearchForNewAuthor(term);
            return MapToResource(searchResults ?? Enumerable.Empty<NzbDrone.Core.Books.Author>()).ToList();
        }

        private IEnumerable<AuthorResource> MapToResource(IEnumerable<NzbDrone.Core.Books.Author> author)
        {
            foreach (var currentAuthor in author)
            {
                var resource = currentAuthor.ToResource();

                _coverMapper.ConvertToLocalUrls(resource.Id, MediaCoverEntity.Author, resource.Images);

                var poster = resource.Images.FirstOrDefault(c => c.CoverType == MediaCoverTypes.Poster);

                if (poster != null)
                {
                    resource.RemotePoster = poster.RemoteUrl;
                }

                resource.Folder = _fileNameBuilder.GetAuthorFolder(currentAuthor);

                yield return resource;
            }
        }
    }
}
