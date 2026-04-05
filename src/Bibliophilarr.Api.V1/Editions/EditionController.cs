using System.Collections.Generic;
using Bibliophilarr.Api.V1.Books;
using Bibliophilarr.Http;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.Books;

namespace NzbDrone.Api.V1.Editions
{
    [V1ApiController]
    public class EditionController : Controller
    {
        private readonly IEditionService _editionService;

        public EditionController(IEditionService editionService)
        {
            _editionService = editionService;
        }

        [HttpGet]
        public List<EditionResource> GetEditions([FromQuery] List<int> bookId)
        {
            var editions = _editionService.GetEditionsByBook(bookId);

            return editions.ToResource();
        }
    }
}
