using System;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Localization;

namespace NzbDrone.Core.HealthCheck.Checks
{
    public class MetadataProviderApiKeyCheck : HealthCheckBase
    {
        private const string HardcoverApiTokenEnvVar = "BIBLIOPHILARR_HARDCOVER_API_TOKEN";
        private readonly IConfigService _configService;

        public MetadataProviderApiKeyCheck(IConfigService configService, ILocalizationService localizationService)
            : base(localizationService)
        {
            _configService = configService;
        }

        public override HealthCheck Check()
        {
            var hardcoverToken = Environment.GetEnvironmentVariable(HardcoverApiTokenEnvVar);
            if (hardcoverToken.IsNullOrWhiteSpace())
            {
                hardcoverToken = _configService.HardcoverApiToken;
            }

            var googleBooksKey = _configService.GoogleBooksApiKey;

            if (hardcoverToken.IsNullOrWhiteSpace() && googleBooksKey.IsNullOrWhiteSpace())
            {
                return new HealthCheck(
                    GetType(),
                    HealthCheckResult.Warning,
                    "No metadata provider API keys configured. Hardcover and Google Books fallback providers will be unavailable. Set BIBLIOPHILARR_HARDCOVER_API_TOKEN environment variable or configure keys in Settings > Metadata.",
                    "#metadata-provider-api-keys");
            }

            if (hardcoverToken.IsNotNullOrWhiteSpace() && hardcoverToken.Trim().Length < 10)
            {
                return new HealthCheck(
                    GetType(),
                    HealthCheckResult.Warning,
                    "Hardcover API token appears invalid (too short). Check your BIBLIOPHILARR_HARDCOVER_API_TOKEN environment variable or Settings > Metadata configuration.",
                    "#metadata-provider-api-keys");
            }

            if (googleBooksKey.IsNotNullOrWhiteSpace() && googleBooksKey.Trim().Length < 10)
            {
                return new HealthCheck(
                    GetType(),
                    HealthCheckResult.Warning,
                    "Google Books API key appears invalid (too short). Check your Settings > Metadata configuration.",
                    "#metadata-provider-api-keys");
            }

            return new HealthCheck(GetType());
        }

        public override bool CheckOnStartup => true;
        public override bool CheckOnSchedule => true;
    }
}
