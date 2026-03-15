using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.MediaFiles;
using Bibliophilarr.Http;
using Bibliophilarr.Http.REST;

namespace Bibliophilarr.Api.V1.Books
{
    [V1ApiController("retag")]
    public class RetagBookController : Controller
    {
        private readonly IMetadataTagService _metadataTagService;

        public RetagBookController(IMetadataTagService metadataTagService)
        {
            _metadataTagService = metadataTagService;
        }

        [HttpGet]
        public List<RetagBookResource> GetBooks(int? authorId, int? bookId)
        {
            if (bookId.HasValue)
            {
                return _metadataTagService.GetRetagPreviewsByBook(bookId.Value).Where(x => x.Changes.Any()).ToResource();
            }
            else if (authorId.HasValue)
            {
                return _metadataTagService.GetRetagPreviewsByAuthor(authorId.Value).Where(x => x.Changes.Any()).ToResource();
            }
            else
            {
                throw new BadRequestException("One of authorId or bookId must be specified");
            }
        }
    }
}
