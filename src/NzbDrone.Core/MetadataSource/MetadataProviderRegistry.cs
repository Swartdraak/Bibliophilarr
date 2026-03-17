using System;
using System.Collections.Generic;
using System.Linq;
using NLog;
using NzbDrone.Core.Configuration;

namespace NzbDrone.Core.MetadataSource
{
    /// <summary>
    /// Aggregates all registered <see cref="IMetadataProvider"/> implementations, ordered by
    /// ascending <see cref="IMetadataProvider.Priority"/>.  Priority 1 = primary; higher numbers
    /// are fallbacks.  The registry is intended to be used by consumers that want automatic
    /// fallback behaviour without knowing about individual providers.
    ///
    /// Current provider map:
    ///   Priority 1 — BookInfoProxy (primary, Bibliophilarr cloud metadata)
    ///   Priority 2 — OpenLibraryProvider (secondary FOSS fallback)
    /// </summary>
    public class MetadataProviderRegistry : IMetadataProviderRegistry
    {
        private readonly IReadOnlyList<IMetadataProvider> _providers;
        private readonly Logger _logger;

        /// <summary>
        /// Receives all DI-registered <see cref="IMetadataProvider"/> implementations via
        /// DryIoC's <c>IEnumerable&lt;T&gt;</c> collection injection.
        /// </summary>
        public MetadataProviderRegistry(IEnumerable<IMetadataProvider> providers, IConfigService configService, Logger logger)
        {
            var configuredOrder = ParseProviderOrder(configService.MetadataProviderPriorityOrder);

            _providers = providers
                .Where(p => p.IsEnabled)
                .OrderBy(p => GetConfiguredOrderIndex(configuredOrder, p.ProviderName))
                .ThenBy(p => p.Priority)
                .ToList();

            _logger = logger;
            _logger.Debug("MetadataProviderRegistry: {0} provider(s) registered: {1}",
                _providers.Count,
                string.Join(", ", _providers.Select(p => $"{p.ProviderName}(P{p.Priority})")));
        }

        private static List<string> ParseProviderOrder(string order)
        {
            if (string.IsNullOrWhiteSpace(order))
            {
                return new List<string>();
            }

            return order
                .Split(',')
                .Select(x => x.Trim())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToList();
        }

        private static int GetConfiguredOrderIndex(IReadOnlyList<string> order, string providerName)
        {
            if (order == null || order.Count == 0)
            {
                return int.MaxValue;
            }

            for (var i = 0; i < order.Count; i++)
            {
                if (string.Equals(order[i], providerName, StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }

            return int.MaxValue;
        }

        public IReadOnlyList<IMetadataProvider> GetProviders() => _providers;

        /// <inheritdoc/>
        public T Execute<T>(Func<IMetadataProvider, T> operation, string operationName)
            where T : class
        {
            Exception lastException = null;

            foreach (var provider in _providers)
            {
                _logger.Debug("MetadataProviderRegistry: attempting '{0}' via {1}", operationName, provider.ProviderName);

                try
                {
                    var result = operation(provider);
                    if (result != null)
                    {
                        _logger.Debug("MetadataProviderRegistry: '{0}' succeeded via {1}", operationName, provider.ProviderName);
                        return result;
                    }

                    _logger.Debug("MetadataProviderRegistry: '{0}' returned null from {1}; trying next provider.", operationName, provider.ProviderName);
                }
                catch (Exception ex)
                {
                    _logger.Warn(ex, "MetadataProviderRegistry: '{0}' failed on {1}; trying next provider.", operationName, provider.ProviderName);
                    lastException = ex;
                }
            }

            if (lastException != null)
            {
                _logger.Warn("MetadataProviderRegistry: all providers failed for '{0}'. Last error: {1}", operationName, lastException.Message);
            }
            else
            {
                _logger.Debug("MetadataProviderRegistry: all providers returned null for '{0}'.", operationName);
            }

            return null;
        }
    }
}
