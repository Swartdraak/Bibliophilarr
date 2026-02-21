using System.Collections.Generic;

namespace NzbDrone.Core.MetadataSource.Registry
{
    /// <summary>
    /// Manages the set of registered metadata providers and exposes
    /// ordered views used by the aggregation/fallback layer.
    /// </summary>
    public interface IMetadataProviderRegistry
    {
        /// <summary>Registers a provider with the registry.</summary>
        void Register(IMetadataProvider provider);

        /// <summary>Returns all registered providers, ordered by <see cref="IMetadataProvider.Priority"/>.</summary>
        IReadOnlyList<IMetadataProvider> GetProviders();

        /// <summary>Returns only enabled providers, ordered by <see cref="IMetadataProvider.Priority"/>.</summary>
        IReadOnlyList<IMetadataProvider> GetEnabledProviders();

        /// <summary>
        /// Returns the highest-priority enabled provider, or <c>null</c> if none are registered and enabled.
        /// </summary>
        IMetadataProvider GetPrimaryProvider();
    }
}
