using System.Collections.Generic;
using System.Threading.Tasks;
using NzbDrone.Core.Books;

namespace NzbDrone.Core.MetadataSource
{
    /// <summary>
    /// Strategy for how to aggregate metadata from multiple providers
    /// </summary>
    public enum AggregationStrategy
    {
        /// <summary>
        /// Use the first provider that returns acceptable results
        /// </summary>
        FirstAcceptable,

        /// <summary>
        /// Use the best result based on quality scoring
        /// </summary>
        BestQuality,

        /// <summary>
        /// Merge metadata from multiple providers
        /// </summary>
        Merge,

        /// <summary>
        /// Use only the primary provider
        /// </summary>
        PrimaryOnly
    }

    /// <summary>
    /// Options for metadata aggregation
    /// </summary>
    public class AggregationOptions
    {
        /// <summary>
        /// Strategy to use for aggregation
        /// </summary>
        public AggregationStrategy Strategy { get; set; } = AggregationStrategy.FirstAcceptable;

        /// <summary>
        /// Minimum quality score to accept (0-100)
        /// </summary>
        public int MinimumQualityScore { get; set; } = 50;

        /// <summary>
        /// Maximum number of providers to query
        /// </summary>
        public int MaxProviders { get; set; } = 3;

        /// <summary>
        /// Whether to stop after first successful result
        /// </summary>
        public bool StopOnFirstSuccess { get; set; } = true;

        /// <summary>
        /// Timeout in milliseconds for each provider query
        /// </summary>
        public int ProviderTimeoutMs { get; set; } = 10000;
    }

    /// <summary>
    /// Result of an aggregated metadata query
    /// </summary>
    /// <typeparam name="T">Type of metadata (Book, Author, etc.)</typeparam>
    public class AggregatedResult<T>
    {
        /// <summary>
        /// The metadata result
        /// </summary>
        public T Result { get; set; }

        /// <summary>
        /// Name of the provider that supplied this result
        /// </summary>
        public string ProviderName { get; set; }

        /// <summary>
        /// Quality score of this result
        /// </summary>
        public int QualityScore { get; set; }

        /// <summary>
        /// All providers that were queried
        /// </summary>
        public List<string> QueriedProviders { get; set; }

        /// <summary>
        /// Providers that failed
        /// </summary>
        public Dictionary<string, string> FailedProviders { get; set; }

        /// <summary>
        /// Whether metadata was merged from multiple providers
        /// </summary>
        public bool IsMerged { get; set; }

        /// <summary>
        /// Provider names that contributed to merged result
        /// </summary>
        public List<string> MergedFromProviders { get; set; }

        public AggregatedResult()
        {
            QueriedProviders = new List<string>();
            FailedProviders = new Dictionary<string, string>();
            MergedFromProviders = new List<string>();
        }
    }

    /// <summary>
    /// Interface for aggregating metadata from multiple providers.
    /// Handles provider selection, fallback, and metadata merging.
    /// </summary>
    public interface IMetadataAggregator
    {
        /// <summary>
        /// Get book metadata using the configured aggregation strategy
        /// </summary>
        /// <param name="identifier">Book identifier (ISBN, ASIN, etc.)</param>
        /// <param name="identifierType">Type of identifier</param>
        /// <param name="options">Aggregation options</param>
        /// <returns>Aggregated book metadata result</returns>
        Task<AggregatedResult<Book>> GetBookMetadataAsync(
            string identifier,
            string identifierType,
            AggregationOptions options = null);

        /// <summary>
        /// Search for books across multiple providers
        /// </summary>
        /// <param name="title">Book title</param>
        /// <param name="author">Optional author name</param>
        /// <param name="options">Aggregation options</param>
        /// <returns>Merged list of books from all providers</returns>
        Task<List<Book>> SearchBooksAsync(
            string title,
            string author = null,
            AggregationOptions options = null);

        /// <summary>
        /// Get author metadata using the configured aggregation strategy
        /// </summary>
        /// <param name="identifier">Author identifier</param>
        /// <param name="identifierType">Type of identifier</param>
        /// <param name="options">Aggregation options</param>
        /// <returns>Aggregated author metadata result</returns>
        Task<AggregatedResult<Author>> GetAuthorMetadataAsync(
            string identifier,
            string identifierType,
            AggregationOptions options = null);

        /// <summary>
        /// Search for authors across multiple providers
        /// </summary>
        /// <param name="name">Author name</param>
        /// <param name="options">Aggregation options</param>
        /// <returns>Merged list of authors from all providers</returns>
        Task<List<Author>> SearchAuthorsAsync(
            string name,
            AggregationOptions options = null);

        /// <summary>
        /// Merge book metadata from multiple sources
        /// </summary>
        /// <param name="books">List of book metadata from different providers</param>
        /// <returns>Merged book metadata</returns>
        Book MergeBookMetadata(List<Book> books);

        /// <summary>
        /// Merge author metadata from multiple sources
        /// </summary>
        /// <param name="authors">List of author metadata from different providers</param>
        /// <returns>Merged author metadata</returns>
        Author MergeAuthorMetadata(List<Author> authors);

        /// <summary>
        /// Deduplicate and merge search results from multiple providers
        /// </summary>
        /// <param name="providerResults">Results from each provider</param>
        /// <returns>Deduplicated and merged list</returns>
        List<Book> MergeSearchResults(List<List<Book>> providerResults);
    }
}
