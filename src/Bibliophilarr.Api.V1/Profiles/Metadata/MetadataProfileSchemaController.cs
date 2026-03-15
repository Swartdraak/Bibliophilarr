using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.Profiles.Metadata;
using Bibliophilarr.Http;

namespace Bibliophilarr.Api.V1.Profiles.Metadata
{
    [V1ApiController("metadataprofile/schema")]
    public class MetadataProfileSchemaController : Controller
    {
        [HttpGet]
        public MetadataProfileResource GetAll()
        {
            var profile = new MetadataProfile
            {
                AllowedLanguages = "eng"
            };

            return profile.ToResource();
        }
    }
}
