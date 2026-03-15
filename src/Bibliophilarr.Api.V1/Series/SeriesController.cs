using System.Collections.Generic;
using Bibliophilarr.Http;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.Books;

namespace Bibliophilarr.Api.V1.Series
{
    [V1ApiController]
    public class SeriesController : Controller
    {
        protected readonly ISeriesService _seriesService;

        public SeriesController(ISeriesService seriesService)
        {
            _seriesService = seriesService;
        }

        [HttpGet]
        public List<SeriesResource> GetSeries(int authorId)
        {
            return _seriesService.GetByAuthorId(authorId).ToResource();
        }
    }
}
