using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NzbDrone.Core.Books;

namespace NzbDrone.Core.MetadataSource
{
    /// <summary>
    /// Search options for book searches
    /// </summary>
    public class BookSearchOptions
    {
        /// <summary>
        /// Whether to retrieve all editions of a book
        /// </summary>
        public bool GetAllEditions { get; set; } = true;

        /// <summary>
        /// Maximum number of results to return
        /// </summary>
        public int MaxResults { get; set; } = 20;

        /// <summary>
        /// Whether to include cover images in results
        /// </summary>
        public bool IncludeCoverImages { get; set; } = true;

        /// <summary>
        /// Preferred language for results (ISO 639-1 code)
        /// </summary>
        public string PreferredLanguage { get; set; }

        /// <summary>
        /// Whether to use cached results if available
        /// </summary>
        public bool UseCache { get; set; } = true;
    }

    /// <summary>
    /// Enhanced interface for searching for books across multiple providers.
    /// Extends the original ISearchForNewBook with async support and additional options.
    /// </summary>
    public interface ISearchForNewBookV2 : IMetadataProvider
    {
        /// <summary>
        /// Search for books by title and optionally author
        /// </summary>
        /// <param name="title">Book title to search for</param>
        /// <param name="author">Optional author name to filter results</param>
        /// <param name="options">Search options</param>
        /// <returns>List of matching books</returns>
        Task<List<Book>> SearchForNewBookAsync(string title, string author = null, BookSearchOptions options = null);

        /// <summary>
        /// Search for books by ISBN
        /// </summary>
        /// <param name="isbn">ISBN-10 or ISBN-13</param>
        /// <param name="options">Search options</param>
        /// <returns>List of matching books (typically 0 or 1 results)</returns>
        Task<List<Book>> SearchByISBNAsync(string isbn, BookSearchOptions options = null);

        /// <summary>
        /// Search for books by ASIN
        /// </summary>
        /// <param name="asin">Amazon Standard Identification Number</param>
        /// <param name="options">Search options</param>
        /// <returns>List of matching books (typically 0 or 1 results)</returns>
        Task<List<Book>> SearchByASINAsync(string asin, BookSearchOptions options = null);

        /// <summary>
        /// Search for books by provider-specific identifier
        /// </summary>
        /// <param name="identifierType">Type of identifier (e.g., "openlibrary", "inventaire", "goodreads")</param>
        /// <param name="identifier">The identifier value</param>
        /// <param name="options">Search options</param>
        /// <returns>List of matching books</returns>
        Task<List<Book>> SearchByIdentifierAsync(string identifierType, string identifier, BookSearchOptions options = null);

        /// <summary>
        /// Synchronous version of SearchForNewBookAsync for backward compatibility
        /// </summary>
        List<Book> SearchForNewBook(string title, string author = null, BookSearchOptions options = null);

        /// <summary>
        /// Synchronous version of SearchByISBNAsync for backward compatibility
        /// </summary>
        List<Book> SearchByISBN(string isbn, BookSearchOptions options = null);

        /// <summary>
        /// Synchronous version of SearchByASINAsync for backward compatibility
        /// </summary>
        List<Book> SearchByASIN(string asin, BookSearchOptions options = null);
    }
}
