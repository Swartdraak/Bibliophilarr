using System;
using System.Collections.Generic;

namespace NzbDrone.Core.MetadataSource
{
    /// <summary>
    /// Orchestrates metadata lookups across multiple ordered <see cref="IMetadataProvider"/> implementations.
    /// Providers are tried in ascending Priority order; on failure the next provider is attempted.
    /// </summary>
    public interface IMetadataProviderRegistry
    {
        /// <summary>Returns all registered providers sorted by ascending priority.</summary>
        IReadOnlyList<IMetadataProvider> GetProviders();

        /// <summary>
        /// Executes <paramref name="operation"/> against each provider in priority order,
        /// returning the first non-null result. Returns the default if all providers fail.
        /// </summary>
        T Execute<T>(Func<IMetadataProvider, T> operation, string operationName)
            where T : class;
    }
}
