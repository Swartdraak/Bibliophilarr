using System.Collections.Generic;
using System.Linq;
using Bibliophilarr.Http;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.MetadataSource;

namespace Bibliophilarr.Api.V1.Metadata
{
    [V1ApiController("metadata/providers/health/basic")]
    public class ProviderHealthController : Controller
    {
        private readonly IMetadataProviderRegistry _providerRegistry;

        public ProviderHealthController(IMetadataProviderRegistry providerRegistry)
        {
            _providerRegistry = providerRegistry;
        }

        [HttpGet]
        public IEnumerable<ProviderHealthResource> GetProvidersHealth()
        {
            return _providerRegistry.GetProvidersHealthStatus()
                .Select(kvp => ProviderHealthResourceMapper.ToResource(kvp.Key, kvp.Value))
                .OrderBy(r => r.ProviderName);
        }
    }
}
