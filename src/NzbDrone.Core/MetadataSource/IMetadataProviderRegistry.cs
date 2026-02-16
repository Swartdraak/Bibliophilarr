using System.Collections.Generic;

namespace NzbDrone.Core.MetadataSource
{
    /// <summary>
    /// Interface for managing metadata providers.
    /// Handles provider registration, selection, and health monitoring.
    /// </summary>
    public interface IMetadataProviderRegistry
    {
        /// <summary>
        /// Register a metadata provider
        /// </summary>
        /// <param name="provider">Provider to register</param>
        void RegisterProvider(IMetadataProvider provider);

        /// <summary>
        /// Unregister a metadata provider
        /// </summary>
        /// <param name="providerName">Name of provider to unregister</param>
        void UnregisterProvider(string providerName);

        /// <summary>
        /// Get a specific provider by name
        /// </summary>
        /// <param name="providerName">Name of the provider</param>
        /// <returns>The provider, or null if not found</returns>
        IMetadataProvider GetProvider(string providerName);

        /// <summary>
        /// Get all registered providers
        /// </summary>
        /// <returns>List of all providers</returns>
        List<IMetadataProvider> GetAllProviders();

        /// <summary>
        /// Get all enabled providers sorted by priority
        /// </summary>
        /// <returns>List of enabled providers in priority order</returns>
        List<IMetadataProvider> GetEnabledProviders();

        /// <summary>
        /// Get providers that support a specific capability
        /// </summary>
        /// <param name="capability">Capability to check (e.g., "BookSearch", "ISBNLookup")</param>
        /// <returns>List of providers supporting the capability</returns>
        List<IMetadataProvider> GetProvidersWithCapability(string capability);

        /// <summary>
        /// Get providers that support book searching
        /// </summary>
        List<ISearchForNewBookV2> GetBookSearchProviders();

        /// <summary>
        /// Get providers that support author searching
        /// </summary>
        List<ISearchForNewAuthorV2> GetAuthorSearchProviders();

        /// <summary>
        /// Get providers that support retrieving book info
        /// </summary>
        List<IProvideBookInfoV2> GetBookInfoProviders();

        /// <summary>
        /// Get providers that support retrieving author info
        /// </summary>
        List<IProvideAuthorInfoV2> GetAuthorInfoProviders();

        /// <summary>
        /// Enable a provider
        /// </summary>
        /// <param name="providerName">Name of provider to enable</param>
        void EnableProvider(string providerName);

        /// <summary>
        /// Disable a provider
        /// </summary>
        /// <param name="providerName">Name of provider to disable</param>
        void DisableProvider(string providerName);

        /// <summary>
        /// Set the priority of a provider
        /// </summary>
        /// <param name="providerName">Name of provider</param>
        /// <param name="priority">New priority (lower = higher priority)</param>
        void SetProviderPriority(string providerName, int priority);

        /// <summary>
        /// Get health status of all providers
        /// </summary>
        /// <returns>Dictionary of provider name to health status</returns>
        Dictionary<string, ProviderHealthStatus> GetProvidersHealthStatus();

        /// <summary>
        /// Update health status for a provider
        /// </summary>
        /// <param name="providerName">Name of provider</param>
        /// <param name="healthStatus">New health status</param>
        void UpdateProviderHealth(string providerName, ProviderHealthStatus healthStatus);

        /// <summary>
        /// Get number of registered providers
        /// </summary>
        int Count { get; }
    }
}
