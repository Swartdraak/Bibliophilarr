using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Common;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.Analytics;
using NzbDrone.Core.Configuration;

namespace Bibliophilarr.Http.Frontend
{
    [Authorize(Policy = "UI")]
    [ApiController]
    [ApiExplorerSettings(IgnoreApi = true)]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public class InitializeJsonController : Controller
    {
        private readonly IConfigFileProvider _configFileProvider;
        private readonly IAnalyticsService _analyticsService;

        public InitializeJsonController(IConfigFileProvider configFileProvider,
                                      IAnalyticsService analyticsService)
        {
            _configFileProvider = configFileProvider;
            _analyticsService = analyticsService;
        }

        [HttpGet("/initialize.json")]
        public IActionResult Index()
        {
            var urlBase = _configFileProvider.UrlBase;
            return Json(new
            {
                ApiRoot = $"{urlBase}/api/v1",
                _configFileProvider.ApiKey,
                BuildInfo.Release,
                Version = BuildInfo.Version.ToString(),
                _configFileProvider.InstanceName,
                Theme = _configFileProvider.Theme.ToString(),
                Branch = _configFileProvider.Branch.ToLowerInvariant(),
                Analytics = _analyticsService.IsEnabled,
                UserHash = HashUtil.AnonymousToken(),
                UrlBase = urlBase,
                RuntimeInfo.IsProduction
            });
        }
    }
}
