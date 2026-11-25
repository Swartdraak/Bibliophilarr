using System;
using System.IO;
using Bibliophilarr.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.MediaCover;

namespace Bibliophilarr.Api.V1.MediaCovers
{
    [V1ApiController]
    public class EmbeddedBookCoverController : Controller
    {
        private readonly IEmbeddedBookCoverService _embeddedBookCovers;

        public EmbeddedBookCoverController(IEmbeddedBookCoverService embeddedBookCovers)
        {
            _embeddedBookCovers = embeddedBookCovers;
        }

        [HttpGet("{bookId:int}/{fileId:int}/{token}")]
        [Produces("image/jpeg")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public IActionResult GetCover(int bookId, int fileId, string token)
        {
            Response.Headers.CacheControl = "private, no-store";
            var path = _embeddedBookCovers.GetPath(bookId, fileId, token);
            if (path == null)
            {
                return NotFound();
            }

            try
            {
                return File(global::System.IO.File.OpenRead(path), "image/jpeg");
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                return NotFound();
            }
        }
    }
}
