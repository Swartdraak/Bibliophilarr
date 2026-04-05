using System;
using System.Collections.Generic;
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
            var issues = new List<string>();

            // Check Hardcover - only if enabled
            if (_configService.EnableHardcoverFallback)
            {
                var hardcoverToken = Environment.GetEnvironmentVariable(HardcoverApiTokenEnvVar);
                if (hardcoverToken.IsNullOrWhiteSpace())
                {
                    hardcoverToken = _configService.HardcoverApiToken;
                }

                if (hardcoverToken.IsNullOrWhiteSpace())
                {
                    issues.Add("Hardcover is enabled but no API token configured. Set BIBLIOPHILARR_HARDCOVER_API_TOKEN environment variable or configure in Settings > Metadata.");
                }
                else if (hardcoverToken.Trim().Length < 10)
                {
                    issues.Add("Hardcover API token appears invalid (too short). Check your BIBLIOPHILARR_HARDCOVER_API_TOKEN environment variable or Settings > Metadata configuration.");
                }
            }

            // Check Google Books - only if enabled
            if (_configService.EnableGoogleBooksFallback)
            {
                var googleBooksKey = _configService.GoogleBooksApiKey;

                if (googleBooksKey.IsNullOrWhiteSpace())
                {
                    issues.Add("Google Books is enabled but no API key configured. Configure in Settings > Metadata.");
                }
                else if (googleBooksKey.Trim().Length < 10)
                {
                    issues.Add("Google Books API key appears invalid (too short). Check your Settings > Metadata configuration.");
                }
            }

            if (issues.Count == 0)
            {
                return new HealthCheck(GetType());
            }

            return new HealthCheck(
                GetType(),
                HealthCheckResult.Warning,
                string.Join(" ", issues),
                "#metadata-provider-api-keys");
        }

        public override bool CheckOnStartup => true;
        public override bool CheckOnSchedule => true;
    }
}
