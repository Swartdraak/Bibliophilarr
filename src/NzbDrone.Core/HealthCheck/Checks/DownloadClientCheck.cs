using System;
using System.Linq;
using NLog;
using NzbDrone.Core.Download;
using NzbDrone.Core.Localization;
using NzbDrone.Core.ThingiProvider.Events;

namespace NzbDrone.Core.HealthCheck.Checks
{
    [CheckOn(typeof(ProviderAddedEvent<IDownloadClient>))]
    [CheckOn(typeof(ProviderUpdatedEvent<IDownloadClient>))]
    [CheckOn(typeof(ProviderDeletedEvent<IDownloadClient>))]
    [CheckOn(typeof(ProviderStatusChangedEvent<IDownloadClient>))]
    public class DownloadClientCheck : HealthCheckBase
    {
        private readonly IProvideDownloadClient _downloadClientProvider;
        private readonly IDownloadClientFactory _downloadClientFactory;
        private readonly Logger _logger;

        public DownloadClientCheck(
            IProvideDownloadClient downloadClientProvider,
            IDownloadClientFactory downloadClientFactory,
            ILocalizationService localizationService,
            Logger logger)
            : base(localizationService)
        {
            _downloadClientProvider = downloadClientProvider;
            _downloadClientFactory = downloadClientFactory;
            _logger = logger;
        }

        public override HealthCheck Check()
        {
            var allClients = _downloadClientFactory.All();
            var downloadClients = _downloadClientProvider.GetDownloadClients().ToList();

            // No download clients enabled
            if (!downloadClients.Any())
            {
                // Check if any are configured but disabled
                var disabledClients = allClients.Where(c => !c.Enable).ToList();

                if (disabledClients.Any())
                {
                    var message = disabledClients.Count == 1
                        ? $"Download client '{disabledClients[0].Name}' is configured but disabled. Enable it in Settings > Download Clients."
                        : $"{disabledClients.Count} download clients are configured but all are disabled. Enable at least one in Settings > Download Clients.";

                    return new HealthCheck(GetType(), HealthCheckResult.Warning, message, "#download-client-is-disabled");
                }

                return new HealthCheck(GetType(), HealthCheckResult.Warning, _localizationService.GetLocalizedString("DownloadClientCheckNoneAvailableMessage"), "#no-download-client-is-available");
            }

            foreach (var downloadClient in downloadClients)
            {
                try
                {
                    downloadClient.GetItems();
                }
                catch (Exception ex)
                {
                    _logger.Debug(ex, "Unable to communicate with {0}", downloadClient.Definition.Name);

                    var message = string.Format(_localizationService.GetLocalizedString("DownloadClientCheckUnableToCommunicateMessage"), downloadClient.Definition.Name);
                    return new HealthCheck(GetType(), HealthCheckResult.Error, $"{message} {ex.Message}", "#unable-to-communicate-with-download-client");
                }
            }

            return new HealthCheck(GetType());
        }
    }
}
