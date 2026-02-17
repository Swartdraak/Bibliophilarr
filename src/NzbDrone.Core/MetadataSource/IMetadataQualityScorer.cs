using NzbDrone.Core.Books;

namespace NzbDrone.Core.MetadataSource
{
    /// <summary>
    /// Interface for calculating metadata quality scores.
    /// Used to compare and rank metadata from different providers.
    /// </summary>
    public interface IMetadataQualityScorer
    {
        /// <summary>
        /// Calculate a quality score for book metadata (0-100)
        /// </summary>
        /// <param name="book">Book to score</param>
        /// <returns>Quality score from 0 (worst) to 100 (best)</returns>
        int CalculateBookScore(Book book);

        /// <summary>
        /// Calculate a quality score for author metadata (0-100)
        /// </summary>
        /// <param name="author">Author to score</param>
        /// <returns>Quality score from 0 (worst) to 100 (best)</returns>
        int CalculateAuthorScore(Author author);

        /// <summary>
        /// Calculate a quality score for edition metadata (0-100)
        /// </summary>
        /// <param name="edition">Edition to score</param>
        /// <returns>Quality score from 0 (worst) to 100 (best)</returns>
        int CalculateEditionScore(Edition edition);

        /// <summary>
        /// Determine if metadata quality is acceptable based on configured thresholds
        /// </summary>
        /// <param name="score">Quality score to check</param>
        /// <returns>True if quality is acceptable, false otherwise</returns>
        bool IsQualityAcceptable(int score);
    }
}
