using Bibliophilarr.Http;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.MetadataSource;

namespace Bibliophilarr.Api.V1.Metadata
{
    [V1ApiController("metadata/conflicts/telemetry")]
    public class MetadataConflictTelemetryController : Controller
    {
        private readonly IMetadataConflictTelemetryService _telemetryService;

        public MetadataConflictTelemetryController(IMetadataConflictTelemetryService telemetryService)
        {
            _telemetryService = telemetryService;
        }

        [HttpGet]
        public MetadataConflictTelemetryResource GetSnapshot()
        {
            return MetadataConflictTelemetryResourceMapper.ToResource(_telemetryService.GetSnapshot());
        }
    }
}
