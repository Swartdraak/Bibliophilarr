using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NzbDrone.Core.Books;

namespace NzbDrone.Core.MetadataSource
{
    /// <summary>
    /// Options for retrieving book information
    /// </summary>
    public class BookInfoOptions
    {
        /// <summary>
        /// Whether to include all editions
        /// </summary>
        public bool IncludeEditions { get; set; } = true;

        /// <summary>
        /// Whether to include related books
        /// </summary>
        public bool IncludeRelatedBooks { get; set; } = false;

        /// <summary>
        /// Whether to use cached results
        /// </summary>
        public bool UseCache { get; set; } = true;
    }

    /// <summary>
    /// Enhanced interface for retrieving detailed book information by ID.
    /// Extends the original IProvideBookInfo with async support and additional options.
    /// </summary>
    public interface IProvideBookInfoV2 : IMetadataProvider
    {
        /// <summary>
        /// Get detailed book information by provider-specific ID
        /// </summary>
        /// <param name="providerId">Provider-specific book identifier</param>
        /// <param name="options">Retrieval options</param>
        /// <returns>Tuple containing foreign ID, book, and author metadata list</returns>
        Task<Tuple<string, Book, List<AuthorMetadata>>> GetBookInfoAsync(string providerId, BookInfoOptions options = null);

        /// <summary>
        /// Get book information by ISBN
        /// </summary>
        /// <param name="isbn">ISBN-10 or ISBN-13</param>
        /// <param name="options">Retrieval options</param>
        /// <returns>Tuple containing foreign ID, book, and author metadata list</returns>
        Task<Tuple<string, Book, List<AuthorMetadata>>> GetBookInfoByIsbnAsync(string isbn, BookInfoOptions options = null);

        /// <summary>
        /// Get book information by any supported identifier type
        /// </summary>
        /// <param name="identifierType">Type of identifier (e.g., "isbn", "asin", "goodreads")</param>
        /// <param name="identifier">The identifier value</param>
        /// <param name="options">Retrieval options</param>
        /// <returns>Tuple containing foreign ID, book, and author metadata list</returns>
        Task<Tuple<string, Book, List<AuthorMetadata>>> GetBookInfoByIdentifierAsync(
            string identifierType,
            string identifier,
            BookInfoOptions options = null);

        /// <summary>
        /// Synchronous version for backward compatibility
        /// </summary>
        Tuple<string, Book, List<AuthorMetadata>> GetBookInfo(string providerId, BookInfoOptions options = null);
    }
}
