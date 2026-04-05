using System.Linq;
using NLog;

namespace NzbDrone.Core.MetadataSource
{
    public interface IMetadataProviderSettingsService
    {
        void ApplyPersistedSettings(IMetadataProviderRegistry registry);

        void SaveProviderEnabled(string providerName, bool isEnabled);

        void SaveProviderPriority(string providerName, int priority);
    }

    public class MetadataProviderSettingsService : IMetadataProviderSettingsService
    {
        private readonly IMetadataProviderSettingsRepository _repository;
        private readonly Logger _logger;

        public MetadataProviderSettingsService(
            IMetadataProviderSettingsRepository repository,
            Logger logger)
        {
            _repository = repository;
            _logger = logger;
        }

        public void ApplyPersistedSettings(IMetadataProviderRegistry registry)
        {
            var allSettings = _repository.All().ToList();

            foreach (var settings in allSettings)
            {
                var known = registry.GetProvider(settings.ProviderName);
                if (known == null)
                {
                    _logger.Debug("Skipping persisted settings for unknown provider '{0}'", settings.ProviderName);
                    continue;
                }

                if (!settings.IsEnabled)
                {
                    registry.DisableProvider(settings.ProviderName);
                }
                else
                {
                    registry.EnableProvider(settings.ProviderName);
                }

                registry.SetProviderPriority(settings.ProviderName, settings.Priority);

                _logger.Debug(
                    "Applied persisted settings to provider '{0}': enabled={1}, priority={2}",
                    settings.ProviderName,
                    settings.IsEnabled,
                    settings.Priority);
            }
        }

        public void SaveProviderEnabled(string providerName, bool isEnabled)
        {
            var existing = _repository.FindByProviderName(providerName);

            if (existing == null)
            {
                _repository.Insert(new MetadataProviderSettings
                {
                    ProviderName = providerName,
                    IsEnabled = isEnabled,
                    Priority = 10
                });
            }
            else
            {
                existing.IsEnabled = isEnabled;
                _repository.Update(existing);
            }

            _logger.Debug("Persisted enabled={0} for provider '{1}'", isEnabled, providerName);
        }

        public void SaveProviderPriority(string providerName, int priority)
        {
            var existing = _repository.FindByProviderName(providerName);

            if (existing == null)
            {
                _repository.Insert(new MetadataProviderSettings
                {
                    ProviderName = providerName,
                    IsEnabled = true,
                    Priority = priority
                });
            }
            else
            {
                existing.Priority = priority;
                _repository.Update(existing);
            }

            _logger.Debug("Persisted priority={0} for provider '{1}'", priority, providerName);
        }
    }
}
