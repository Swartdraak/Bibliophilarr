using System;

namespace NzbDrone.Core.MetadataSource
{
    /// <summary>
    /// Base interface for all metadata providers in Bibliophilarr.
    /// Provides common properties and capabilities that all providers must implement.
    /// </summary>
    public interface IMetadataProvider
    {
        /// <summary>
        /// Unique name of the provider (e.g., "OpenLibrary", "Inventaire", "GoogleBooks")
        /// </summary>
        string ProviderName { get; }

        /// <summary>
        /// Priority for provider selection. Lower numbers = higher priority.
        /// Primary providers should be 1-10, secondary 11-20, fallback 21+
        /// </summary>
        int Priority { get; }

        /// <summary>
        /// Whether this provider is currently enabled in settings
        /// </summary>
        bool IsEnabled { get; }

        /// <summary>
        /// Whether this provider supports searching for new books
        /// </summary>
        bool SupportsBookSearch { get; }

        /// <summary>
        /// Whether this provider supports searching for authors
        /// </summary>
        bool SupportsAuthorSearch { get; }

        /// <summary>
        /// Whether this provider supports ISBN-based lookups
        /// </summary>
        bool SupportsIsbnLookup { get; }

        /// <summary>
        /// Whether this provider supports ASIN-based lookups
        /// </summary>
        bool SupportsAsinLookup { get; }

        /// <summary>
        /// Whether this provider provides series information
        /// </summary>
        bool SupportsSeriesInfo { get; }

        /// <summary>
        /// Whether this provider provides book list information
        /// </summary>
        bool SupportsListInfo { get; }

        /// <summary>
        /// Whether this provider provides cover images
        /// </summary>
        bool SupportsCoverImages { get; }

        /// <summary>
        /// Whether this provider supports retrieving book information by its ID
        /// </summary>
        bool SupportsBookInfo { get; }

        /// <summary>
        /// Whether this provider supports retrieving author information by ID
        /// </summary>
        bool SupportsAuthorInfo { get; }

        /// <summary>
        /// Get rate limiting information for this provider
        /// </summary>
        ProviderRateLimitInfo GetRateLimitInfo();

        /// <summary>
        /// Get current health status of the provider
        /// </summary>
        ProviderHealthStatus GetHealthStatus();
    }
}
