using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Common;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.Analytics;
using NzbDrone.Core.Configuration;

namespace Bibliophilarr.Http.Frontend
{
    [AllowAnonymous]
    [ApiController]
    [ApiExplorerSettings(IgnoreApi = true)]
    public class InitializeJsonController : Controller
    {
        private readonly IConfigFileProvider _configFileProvider;
        private readonly IAnalyticsService _analyticsService;

        private string _generatedContent;

        public InitializeJsonController(IConfigFileProvider configFileProvider,
                                      IAnalyticsService analyticsService)
        {
            _configFileProvider = configFileProvider;
            _analyticsService = analyticsService;
        }

        [HttpGet("/initialize.json")]
        public IActionResult Index()
        {
            return Content(GetContent(), "application/json");
        }

        private string GetContent()
        {
            if (RuntimeInfo.IsProduction && _generatedContent != null)
            {
                return _generatedContent;
            }

            // Read the API key and URL base from the config provider per request rather than
            // caching them once in the constructor (issue #309 pattern, mirrored here for
            // issue #329). On a cold boot the value captured at construction time can be a
            // pre-finalization key that differs from the key ultimately persisted to
            // config.xml during startup; because the old cache was never invalidated, the
            // SPA could never bootstrap with the persisted key until a restart.
            var apiKey = _configFileProvider.ApiKey;
            var urlBase = _configFileProvider.UrlBase;

            var builder = new StringBuilder();
            builder.AppendLine("{");
            builder.AppendLine($"  \"apiRoot\": \"{urlBase}/api/v1\",");
            builder.AppendLine($"  \"apiKey\": \"{apiKey}\",");
            builder.AppendLine($"  \"release\": \"{BuildInfo.Release}\",");
            builder.AppendLine($"  \"version\": \"{BuildInfo.Version.ToString()}\",");
            builder.AppendLine($"  \"instanceName\": \"{_configFileProvider.InstanceName.ToString()}\",");
            builder.AppendLine($"  \"theme\": \"{_configFileProvider.Theme.ToString()}\",");
            builder.AppendLine($"  \"branch\": \"{_configFileProvider.Branch.ToLower()}\",");
            builder.AppendLine($"  \"analytics\": {_analyticsService.IsEnabled.ToString().ToLowerInvariant()},");
            builder.AppendLine($"  \"userHash\": \"{HashUtil.AnonymousToken()}\",");
            builder.AppendLine($"  \"urlBase\": \"{urlBase}\",");
            builder.AppendLine($"  \"isProduction\": {RuntimeInfo.IsProduction.ToString().ToLowerInvariant()}");
            builder.AppendLine("}");

            _generatedContent = builder.ToString();

            return _generatedContent;
        }
    }
}
