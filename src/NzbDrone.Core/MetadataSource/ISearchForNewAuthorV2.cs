using System.Collections.Generic;
using System.Threading.Tasks;
using NzbDrone.Core.Books;

namespace NzbDrone.Core.MetadataSource
{
    /// <summary>
    /// Search options for author searches
    /// </summary>
    public class AuthorSearchOptions
    {
        /// <summary>
        /// Maximum number of results to return
        /// </summary>
        public int MaxResults { get; set; } = 20;

        /// <summary>
        /// Whether to include author images in results
        /// </summary>
        public bool IncludeImages { get; set; } = true;

        /// <summary>
        /// Whether to include book list for the author
        /// </summary>
        public bool IncludeBooks { get; set; } = false;

        /// <summary>
        /// Whether to use cached results if available
        /// </summary>
        public bool UseCache { get; set; } = true;
    }

    /// <summary>
    /// Enhanced interface for searching for authors across multiple providers.
    /// Extends the original ISearchForNewAuthor with async support and additional options.
    /// </summary>
    public interface ISearchForNewAuthorV2 : IMetadataProvider
    {
        /// <summary>
        /// Search for authors by name
        /// </summary>
        /// <param name="name">Author name to search for</param>
        /// <param name="options">Search options</param>
        /// <returns>List of matching authors</returns>
        Task<List<Author>> SearchForNewAuthorAsync(string name, AuthorSearchOptions options = null);

        /// <summary>
        /// Search for author by provider-specific identifier
        /// </summary>
        /// <param name="identifierType">Type of identifier (e.g., "openlibrary", "inventaire", "goodreads")</param>
        /// <param name="identifier">The identifier value</param>
        /// <param name="options">Search options</param>
        /// <returns>Author if found, null otherwise</returns>
        Task<Author> SearchByIdentifierAsync(string identifierType, string identifier, AuthorSearchOptions options = null);

        /// <summary>
        /// Synchronous version of SearchForNewAuthorAsync for backward compatibility
        /// </summary>
        List<Author> SearchForNewAuthor(string name, AuthorSearchOptions options = null);
    }
}
