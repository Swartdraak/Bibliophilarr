using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NzbDrone.Core.Books;

namespace NzbDrone.Core.MetadataSource
{
    /// <summary>
    /// Options for retrieving author information
    /// </summary>
    public class AuthorInfoOptions
    {
        /// <summary>
        /// Whether to include the author's book list
        /// </summary>
        public bool IncludeBooks { get; set; } = false;

        /// <summary>
        /// Whether to include series information
        /// </summary>
        public bool IncludeSeries { get; set; } = false;

        /// <summary>
        /// Whether to use cached results
        /// </summary>
        public bool UseCache { get; set; } = true;

        /// <summary>
        /// Maximum number of books to include if IncludeBooks is true
        /// </summary>
        public int MaxBooks { get; set; } = 100;
    }

    /// <summary>
    /// Enhanced interface for retrieving detailed author information by ID.
    /// Extends the original IProvideAuthorInfo with async support and additional options.
    /// </summary>
    public interface IProvideAuthorInfoV2 : IMetadataProvider
    {
        /// <summary>
        /// Get detailed author information by provider-specific ID
        /// </summary>
        /// <param name="providerId">Provider-specific author identifier</param>
        /// <param name="options">Retrieval options</param>
        /// <returns>Author with metadata</returns>
        Task<Author> GetAuthorInfoAsync(string providerId, AuthorInfoOptions options = null);

        /// <summary>
        /// Get author information by any supported identifier type
        /// </summary>
        /// <param name="identifierType">Type of identifier (e.g., "openlibrary", "isni", "viaf")</param>
        /// <param name="identifier">The identifier value</param>
        /// <param name="options">Retrieval options</param>
        /// <returns>Author with metadata</returns>
        Task<Author> GetAuthorInfoByIdentifierAsync(
            string identifierType,
            string identifier,
            AuthorInfoOptions options = null);

        /// <summary>
        /// Get a set of author IDs that have been updated since the specified time
        /// </summary>
        /// <param name="startTime">Start time for change detection</param>
        /// <returns>Set of provider-specific author IDs that have changed</returns>
        Task<HashSet<string>> GetChangedAuthorsAsync(DateTime startTime);

        /// <summary>
        /// Synchronous version for backward compatibility
        /// </summary>
        Author GetAuthorInfo(string providerId, AuthorInfoOptions options = null);

        /// <summary>
        /// Synchronous version for backward compatibility
        /// </summary>
        HashSet<string> GetChangedAuthors(DateTime startTime);
    }
}
