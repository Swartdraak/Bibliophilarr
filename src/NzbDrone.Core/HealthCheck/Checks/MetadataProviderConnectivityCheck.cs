using NzbDrone.Core.Localization;
using NzbDrone.Core.MetadataSource;

namespace NzbDrone.Core.HealthCheck.Checks
{
    [CheckOn(typeof(MetadataProviderConnectivityChangedEvent))]
    public class MetadataProviderConnectivityCheck : HealthCheckBase
    {
        private readonly IMetadataProviderTelemetryService _telemetry;

        public MetadataProviderConnectivityCheck(IMetadataProviderTelemetryService telemetry, ILocalizationService localizationService)
            : base(localizationService)
        {
            _telemetry = telemetry;
        }

        public override HealthCheck Check()
        {
            if (!_telemetry.HasRecentTransportOutage())
            {
                return new HealthCheck(GetType());
            }

            return new HealthCheck(
                GetType(),
                HealthCheckResult.Warning,
                _localizationService.GetLocalizedString("MetadataProviderUnreachableHealthCheckMessage"),
                "#metadata-provider-unreachable");
        }
    }
}
