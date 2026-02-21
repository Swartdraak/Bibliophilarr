using System.Collections.Generic;
using System.Linq;
using NLog;

namespace NzbDrone.Core.MetadataSource.Registry
{
    /// <summary>
    /// Thread-safe, in-process registry of all <see cref="IMetadataProvider"/> implementations.
    ///
    /// Providers are registered at startup (via DI) by calling <see cref="Register"/>.
    /// The registry does not itself perform any I/O; it is a pure coordination surface
    /// that the aggregation/fallback layer queries at request time.
    /// </summary>
    public class MetadataProviderRegistry : IMetadataProviderRegistry
    {
        private readonly List<IMetadataProvider> _providers = new List<IMetadataProvider>();
        private readonly object _lock = new object();
        private readonly Logger _logger;

        public MetadataProviderRegistry(Logger logger)
        {
            _logger = logger;
        }

        /// <inheritdoc/>
        public void Register(IMetadataProvider provider)
        {
            lock (_lock)
            {
                _logger.Debug("Registering metadata provider: {0} (priority {1})", provider.ProviderName, provider.Priority);
                _providers.Add(provider);
            }
        }

        /// <inheritdoc/>
        public IReadOnlyList<IMetadataProvider> GetProviders()
        {
            lock (_lock)
            {
                return _providers.OrderBy(p => p.Priority).ToList().AsReadOnly();
            }
        }

        /// <inheritdoc/>
        public IReadOnlyList<IMetadataProvider> GetEnabledProviders()
        {
            lock (_lock)
            {
                return _providers
                    .Where(p => p.IsEnabled)
                    .OrderBy(p => p.Priority)
                    .ToList()
                    .AsReadOnly();
            }
        }

        /// <inheritdoc/>
        public IMetadataProvider GetPrimaryProvider()
        {
            lock (_lock)
            {
                return _providers
                    .Where(p => p.IsEnabled)
                    .OrderBy(p => p.Priority)
                    .FirstOrDefault();
            }
        }
    }
}
