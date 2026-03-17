using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NLog;
using NzbDrone.Core.Configuration;

namespace NzbDrone.Core.MetadataSource
{
    public class MetadataProviderRegistry : IMetadataProviderRegistry
    {
        private readonly object _syncRoot = new object();
        private readonly Dictionary<string, IMetadataProvider> _providers;
        private readonly Dictionary<string, bool> _enabledOverrides;
        private readonly Dictionary<string, int> _priorityOverrides;
        private readonly Dictionary<string, ProviderHealthStatus> _healthOverrides;
        private readonly List<string> _configuredOrder;
        private readonly Logger _logger;

        public MetadataProviderRegistry(IEnumerable<IMetadataProvider> providers)
            : this(providers, null, LogManager.GetCurrentClassLogger())
        {
        }

        public MetadataProviderRegistry(IEnumerable<IMetadataProvider> providers, IConfigService configService, Logger logger)
        {
            _logger = logger ?? LogManager.GetCurrentClassLogger();
            _providers = new Dictionary<string, IMetadataProvider>(StringComparer.OrdinalIgnoreCase);
            _enabledOverrides = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            _priorityOverrides = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            _healthOverrides = new Dictionary<string, ProviderHealthStatus>(StringComparer.OrdinalIgnoreCase);
            _configuredOrder = LoadConfiguredOrder(configService);

            if (providers == null)
            {
                return;
            }

            foreach (var provider in providers)
            {
                RegisterProvider(provider);
            }

            _logger.Debug("MetadataProviderRegistry: {0} provider(s) registered: {1}",
                _providers.Count,
                string.Join(", ", SortProviders(_providers.Values).Select(p => $"{p.ProviderName}(P{GetPriority(p)})")));
        }

        public int Count
        {
            get
            {
                lock (_syncRoot)
                {
                    return _providers.Count;
                }
            }
        }

        public void RegisterProvider(IMetadataProvider provider)
        {
            if (provider == null)
            {
                throw new ArgumentNullException(nameof(provider));
            }

            if (string.IsNullOrWhiteSpace(provider.ProviderName))
            {
                throw new ArgumentException("ProviderName must be set.", nameof(provider));
            }

            lock (_syncRoot)
            {
                _providers[provider.ProviderName] = provider;
            }
        }

        public void UnregisterProvider(string providerName)
        {
            if (string.IsNullOrWhiteSpace(providerName))
            {
                return;
            }

            lock (_syncRoot)
            {
                _providers.Remove(providerName);
                _enabledOverrides.Remove(providerName);
                _priorityOverrides.Remove(providerName);
                _healthOverrides.Remove(providerName);
            }
        }

        public IMetadataProvider GetProvider(string providerName)
        {
            if (string.IsNullOrWhiteSpace(providerName))
            {
                return null;
            }

            lock (_syncRoot)
            {
                _providers.TryGetValue(providerName, out var provider);
                return provider;
            }
        }

        public List<IMetadataProvider> GetAllProviders()
        {
            lock (_syncRoot)
            {
                return SortProviders(_providers.Values).ToList();
            }
        }

        public List<IMetadataProvider> GetEnabledProviders()
        {
            lock (_syncRoot)
            {
                return SortProviders(_providers.Values.Where(IsEnabled)).ToList();
            }
        }

        public List<IMetadataProvider> GetProvidersWithCapability(string capability)
        {
            if (string.IsNullOrWhiteSpace(capability))
            {
                return new List<IMetadataProvider>();
            }

            lock (_syncRoot)
            {
                return SortProviders(_providers.Values.Where(p => SupportsCapability(p, capability))).ToList();
            }
        }

        public List<ISearchForNewBookV2> GetBookSearchProviders()
        {
            lock (_syncRoot)
            {
                return SortProviders(_providers.Values)
                    .Where(IsEnabled)
                    .Where(p => p.SupportsBookSearch)
                    .OfType<ISearchForNewBookV2>()
                    .ToList();
            }
        }

        public List<ISearchForNewAuthorV2> GetAuthorSearchProviders()
        {
            lock (_syncRoot)
            {
                return SortProviders(_providers.Values)
                    .Where(IsEnabled)
                    .Where(p => p.SupportsAuthorSearch)
                    .OfType<ISearchForNewAuthorV2>()
                    .ToList();
            }
        }

        public List<IProvideBookInfoV2> GetBookInfoProviders()
        {
            lock (_syncRoot)
            {
                return SortProviders(_providers.Values)
                    .Where(IsEnabled)
                    .OfType<IProvideBookInfoV2>()
                    .ToList();
            }
        }

        public List<IProvideAuthorInfoV2> GetAuthorInfoProviders()
        {
            lock (_syncRoot)
            {
                return SortProviders(_providers.Values)
                    .Where(IsEnabled)
                    .OfType<IProvideAuthorInfoV2>()
                    .ToList();
            }
        }

        public void EnableProvider(string providerName)
        {
            SetProviderEnabled(providerName, true);
        }

        public void DisableProvider(string providerName)
        {
            SetProviderEnabled(providerName, false);
        }

        public void SetProviderPriority(string providerName, int priority)
        {
            if (string.IsNullOrWhiteSpace(providerName))
            {
                return;
            }

            lock (_syncRoot)
            {
                if (_providers.ContainsKey(providerName))
                {
                    _priorityOverrides[providerName] = priority;
                }
            }
        }

        public Dictionary<string, ProviderHealthStatus> GetProvidersHealthStatus()
        {
            lock (_syncRoot)
            {
                var registered = _providers
                    .ToDictionary(
                        x => x.Key,
                        _ => new ProviderHealthStatus(),
                        StringComparer.OrdinalIgnoreCase);

                foreach (var kvp in _healthOverrides)
                {
                    registered[kvp.Key] = kvp.Value;
                }

                return registered;
            }
        }

        public void UpdateProviderHealth(string providerName, ProviderHealthStatus healthStatus)
        {
            if (string.IsNullOrWhiteSpace(providerName) || healthStatus == null)
            {
                return;
            }

            lock (_syncRoot)
            {
                _healthOverrides[providerName] = healthStatus;
            }
        }

        public IReadOnlyList<IMetadataProvider> GetProviders()
        {
            lock (_syncRoot)
            {
                return SortProviders(_providers.Values.Where(IsEnabled)).ToList();
            }
        }

        public T Execute<T>(Func<IMetadataProvider, T> operation, string operationName)
            where T : class
        {
            Exception lastException = null;

            foreach (var provider in GetProviders())
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

        private List<string> LoadConfiguredOrder(IConfigService configService)
        {
            if (configService == null)
            {
                return new List<string>();
            }

            try
            {
                return ParseProviderOrder(configService.MetadataProviderPriorityOrder);
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "MetadataProviderRegistry: unable to read configured provider order during construction; using provider defaults.");
                return new List<string>();
            }
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

        private void SetProviderEnabled(string providerName, bool enabled)
        {
            if (string.IsNullOrWhiteSpace(providerName))
            {
                return;
            }

            lock (_syncRoot)
            {
                if (_providers.ContainsKey(providerName))
                {
                    _enabledOverrides[providerName] = enabled;
                }
            }
        }

        private IEnumerable<IMetadataProvider> SortProviders(IEnumerable<IMetadataProvider> providers)
        {
            return providers
                .OrderBy(p => GetConfiguredOrderIndex(_configuredOrder, p.ProviderName))
                .ThenBy(GetPriority)
                .ThenBy(p => p.ProviderName, StringComparer.OrdinalIgnoreCase);
        }

        private bool IsEnabled(IMetadataProvider provider)
        {
            if (_enabledOverrides.TryGetValue(provider.ProviderName, out var enabled))
            {
                return enabled;
            }

            return provider.IsEnabled;
        }

        private int GetPriority(IMetadataProvider provider)
        {
            if (_priorityOverrides.TryGetValue(provider.ProviderName, out var priority))
            {
                return priority;
            }

            return provider.Priority;
        }

        private static bool SupportsCapability(IMetadataProvider provider, string capability)
        {
            if (provider == null)
            {
                return false;
            }

            var property = typeof(IMetadataProvider).GetProperty(
                capability,
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            if (property == null || property.PropertyType != typeof(bool))
            {
                return false;
            }

            return (bool)property.GetValue(provider);
        }
    }
}
