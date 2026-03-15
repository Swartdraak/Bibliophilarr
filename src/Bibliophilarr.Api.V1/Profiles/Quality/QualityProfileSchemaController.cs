using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.Profiles.Qualities;
using Bibliophilarr.Http;

namespace Bibliophilarr.Api.V1.Profiles.Quality
{
    [V1ApiController("qualityprofile/schema")]
    public class QualityProfileSchemaController : Controller
    {
        private readonly IQualityProfileService _qualityProfileService;

        public QualityProfileSchemaController(IQualityProfileService qualityProfileService)
        {
            _qualityProfileService = qualityProfileService;
        }

        [HttpGet]
        public QualityProfileResource GetSchema()
        {
            var qualityProfile = _qualityProfileService.GetDefaultProfile(string.Empty);

            return qualityProfile.ToResource();
        }
    }
}
