using System.Collections.Generic;
using System.Linq;
using Bibliophilarr.Http;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.MetadataSource;

namespace Readarr.Api.V1.Metadata
{
    public class MetadataProviderHealthResource
    {
        public string ProviderName { get; set; }
        public int Priority { get; set; }
        public bool IsEnabled { get; set; }
        public bool SupportsAuthorSearch { get; set; }
        public bool SupportsBookSearch { get; set; }
        public bool SupportsIsbnLookup { get; set; }
        public bool SupportsSeriesInfo { get; set; }
        public bool SupportsCoverImages { get; set; }
        public MetadataProviderTelemetrySnapshot Telemetry { get; set; }
    }

    [V1ApiController("metadata/providers")]
    public class MetadataProvidersController : Controller
    {
        private readonly IMetadataProviderRegistry _registry;
        private readonly IMetadataProviderTelemetryService _telemetry;

        public MetadataProvidersController(IMetadataProviderRegistry registry, IMetadataProviderTelemetryService telemetry)
        {
            _registry = registry;
            _telemetry = telemetry;
        }

        [HttpGet("health")]
        public IEnumerable<MetadataProviderHealthResource> GetHealth()
        {
            var telemetryByProvider = _telemetry.GetSnapshots().ToDictionary(x => x.ProviderName);

            return _registry.GetProviders().Select(provider => new MetadataProviderHealthResource
            {
                ProviderName = provider.ProviderName,
                Priority = provider.Priority,
                IsEnabled = provider.IsEnabled,
                SupportsAuthorSearch = provider.SupportsAuthorSearch,
                SupportsBookSearch = provider.SupportsBookSearch,
                SupportsIsbnLookup = provider.SupportsIsbnLookup,
                SupportsSeriesInfo = provider.SupportsSeriesInfo,
                SupportsCoverImages = provider.SupportsCoverImages,
                Telemetry = telemetryByProvider.TryGetValue(provider.ProviderName, out var data)
                    ? data
                    : new MetadataProviderTelemetrySnapshot
                    {
                        ProviderName = provider.ProviderName,
                        Operations = new List<MetadataProviderOperationTelemetrySnapshot>()
                    }
            });
        }

        [HttpGet("telemetry")]
        public IReadOnlyList<MetadataProviderTelemetrySnapshot> GetTelemetry()
        {
            return _telemetry.GetSnapshots();
        }

        [HttpGet("telemetry/operations")]
        public IReadOnlyList<MetadataProviderOperationTelemetrySnapshot> GetOperationTelemetry()
        {
            return _telemetry.GetOperationSnapshots();
        }
    }
}
