using System;
using System.Collections.Generic;
using System.Linq;
using Bibliophilarr.Http;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.MetadataSource;

namespace Bibliophilarr.Api.V1.Metadata
{
    public class MetadataProviderHealthResource
    {
        public string ProviderName { get; set; }
        public int Priority { get; set; }
        public bool IsEnabled { get; set; }
        public bool IsFallbackOnly { get; set; }
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
        private readonly IEnumerable<IBookSearchFallbackProvider> _fallbackProviders;

        public MetadataProvidersController(IMetadataProviderRegistry registry,
                                           IMetadataProviderTelemetryService telemetry,
                                           IEnumerable<IBookSearchFallbackProvider> fallbackProviders)
        {
            _registry = registry;
            _telemetry = telemetry;
            _fallbackProviders = fallbackProviders;
        }

        [HttpGet("health")]
        public IEnumerable<MetadataProviderHealthResource> GetHealth()
        {
            var telemetryByProvider = _telemetry.GetSnapshots().ToDictionary(x => x.ProviderName, StringComparer.OrdinalIgnoreCase);
            var primaryNames = _registry.GetAllProviders().Select(provider => provider.ProviderName).ToHashSet(StringComparer.OrdinalIgnoreCase);

            MetadataProviderTelemetrySnapshot LookupTelemetry(string providerName) =>
                telemetryByProvider.TryGetValue(providerName, out var data)
                    ? data
                    : new MetadataProviderTelemetrySnapshot
                    {
                        ProviderName = providerName,
                        Operations = new List<MetadataProviderOperationTelemetrySnapshot>()
                    };

            var primary = _registry.GetProviders().Select(provider => new MetadataProviderHealthResource
            {
                ProviderName = provider.ProviderName,
                Priority = provider.Priority,
                IsEnabled = provider.IsEnabled,
                SupportsAuthorSearch = provider.SupportsAuthorSearch,
                SupportsBookSearch = provider.SupportsBookSearch,
                SupportsIsbnLookup = provider.SupportsIsbnLookup,
                SupportsSeriesInfo = provider.SupportsSeriesInfo,
                SupportsCoverImages = provider.SupportsCoverImages,
                Telemetry = LookupTelemetry(provider.ProviderName)
            });

            // Disabled or cooling primary providers still own their registered names.
            var fallbackOnly = _fallbackProviders
                .Where(provider => !primaryNames.Contains(provider.ProviderName))
                .DistinctBy(provider => provider.ProviderName, StringComparer.OrdinalIgnoreCase)
                .Select(provider => new MetadataProviderHealthResource
                {
                    ProviderName = provider.ProviderName,
                    IsEnabled = provider.IsFallbackEnabled,
                    IsFallbackOnly = true,
                    SupportsBookSearch = true,
                    Priority = 0,
                    Telemetry = LookupTelemetry(provider.ProviderName)
                });

            return primary.Concat(fallbackOnly);
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
