using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace NzbDrone.Core.MetadataSource
{
    public class MetadataProviderRegistry : IMetadataProviderRegistry
    {
        private readonly object _syncRoot = new object();
        private readonly Dictionary<string, IMetadataProvider> _providers;
        private readonly Dictionary<string, bool> _enabledOverrides;
        private readonly Dictionary<string, int> _priorityOverrides;
        private readonly Dictionary<string, ProviderHealthStatus> _healthOverrides;

        public MetadataProviderRegistry(IEnumerable<IMetadataProvider> providers)
        {
            _providers = new Dictionary<string, IMetadataProvider>(StringComparer.OrdinalIgnoreCase);
            _enabledOverrides = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            _priorityOverrides = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            _healthOverrides = new Dictionary<string, ProviderHealthStatus>(StringComparer.OrdinalIgnoreCase);

            if (providers == null)
            {
                return;
            }

            foreach (var provider in providers)
            {
                RegisterProvider(provider);
            }
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
                    .Where(p => p.SupportsBookInfo)
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
                    .Where(p => p.SupportsAuthorInfo)
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
                        x => GetProviderHealthStatus(x.Key, x.Value),
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
                .OrderBy(GetPriority)
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

        private ProviderHealthStatus GetProviderHealthStatus(string providerName, IMetadataProvider provider)
        {
            if (_healthOverrides.TryGetValue(providerName, out var overridden))
            {
                return overridden;
            }

            return provider.GetHealthStatus() ?? new ProviderHealthStatus();
        }
    }
}
