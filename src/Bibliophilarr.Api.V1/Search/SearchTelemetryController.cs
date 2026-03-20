using Bibliophilarr.Http;
using Microsoft.AspNetCore.Mvc;

namespace Bibliophilarr.Api.V1.Search
{
    [V1ApiController("diagnostics/search/telemetry")]
    public class SearchTelemetryController : Controller
    {
        private readonly ISearchTelemetryService _telemetryService;

        public SearchTelemetryController(ISearchTelemetryService telemetryService = null)
        {
            _telemetryService = telemetryService ?? SearchTelemetryService.Shared;
        }

        [HttpGet]
        public SearchTelemetryResource GetSnapshot()
        {
            return SearchTelemetryResourceMapper.ToResource(_telemetryService.GetSnapshot());
        }
    }
}
