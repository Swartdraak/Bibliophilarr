using System;

namespace NzbDrone.Core.MetadataSource
{
    /// <summary>
    /// Describes the rate-limit characteristics of a metadata provider.
    /// </summary>
    public class RateLimitInfo
    {
        public int MaxRequestsPerWindow { get; set; }
        public TimeSpan Window { get; set; }
        public bool RequiresApiKey { get; set; }
    }

    /// <summary>
    /// Base interface for all metadata providers.
    /// Implement this on every provider (Open Library, Inventaire, Google Books, etc.)
    /// so that the registry and aggregation layer can discover and manage them uniformly.
    /// </summary>
    public interface IMetadataProvider
    {
        /// <summary>Human-readable provider name (e.g. "Open Library").</summary>
        string ProviderName { get; }

        /// <summary>
        /// Lower values win: 1 = highest priority.
        /// Used by the registry when selecting the default/fallback provider.
        /// </summary>
        int Priority { get; }

        /// <summary>Whether this provider is currently active and should be queried.</summary>
        bool IsEnabled { get; }

        bool SupportsAuthorSearch { get; }
        bool SupportsBookSearch { get; }
        bool SupportsIsbnLookup { get; }
        bool SupportsSeriesInfo { get; }
        bool SupportsCoverImages { get; }

        /// <summary>Returns the rate-limit constraints for this provider.</summary>
        RateLimitInfo GetRateLimits();
    }
}
