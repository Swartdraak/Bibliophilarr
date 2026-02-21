using NzbDrone.Core.Books;

namespace NzbDrone.Core.MetadataSource.Scoring
{
    /// <summary>
    /// Scores the completeness and quality of metadata for a book or author.
    /// A higher score indicates more complete metadata from a provider result.
    /// Scores are used by the aggregation layer to select the best provider
    /// result or to merge fields from multiple sources.
    /// </summary>
    public interface IMetadataQualityScorer
    {
        /// <summary>
        /// Returns a score in the range [0, 100] representing how complete
        /// the metadata for <paramref name="book"/> is.
        /// </summary>
        int CalculateBookScore(Book book);

        /// <summary>
        /// Returns a score in the range [0, 100] representing how complete
        /// the metadata for <paramref name="author"/> is.
        /// </summary>
        int CalculateAuthorScore(Author author);
    }
}
