using System;
using System.Collections.Generic;

namespace NzbDrone.Core.MetadataSource
{
    /// <summary>
    /// Interface for managing metadata providers and their runtime overrides.
    /// </summary>
    public interface IMetadataProviderRegistry
    {
        void RegisterProvider(IMetadataProvider provider);

        void UnregisterProvider(string providerName);

        IMetadataProvider GetProvider(string providerName);

        List<IMetadataProvider> GetAllProviders();

        List<IMetadataProvider> GetEnabledProviders();

        List<IMetadataProvider> GetProvidersWithCapability(string capability);

        List<ISearchForNewBookV2> GetBookSearchProviders();

        List<ISearchForNewAuthorV2> GetAuthorSearchProviders();

        List<IProvideBookInfoV2> GetBookInfoProviders();

        List<IProvideAuthorInfoV2> GetAuthorInfoProviders();

        void EnableProvider(string providerName);

        void DisableProvider(string providerName);

        void SetProviderPriority(string providerName, int priority);

        Dictionary<string, ProviderHealthStatus> GetProvidersHealthStatus();

        void UpdateProviderHealth(string providerName, ProviderHealthStatus healthStatus);

        IReadOnlyList<IMetadataProvider> GetProviders();

        /// <summary>
        /// Executes <paramref name="operation"/> against each provider in priority order,
        /// returning the first non-null result. Returns the default if all providers fail.
        /// </summary>
        T Execute<T>(Func<IMetadataProvider, T> operation, string operationName)
            where T : class;

        int Count { get; }
    }
}
