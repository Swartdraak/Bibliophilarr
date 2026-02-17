using System.Linq;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Books;

namespace NzbDrone.Core.MetadataSource
{
    /// <summary>
    /// Implementation of metadata quality scoring.
    /// Scores metadata from 0-100 based on completeness and quality of fields.
    /// </summary>
    public class MetadataQualityScorer : IMetadataQualityScorer
    {
        private const int MinimumAcceptableScore = 50;

        /// <summary>
        /// Calculate a quality score for book metadata (0-100)
        /// </summary>
        public int CalculateBookScore(Book book)
        {
            if (book == null)
            {
                return 0;
            }

            int score = 0;

            // Essential fields (60 points total)
            if (!string.IsNullOrWhiteSpace(book.Title))
            {
                score += 20;
            }

            if (book.AuthorMetadata?.Value != null || book.Author?.Value != null)
            {
                score += 20;
            }

            if (!string.IsNullOrWhiteSpace(book.ForeignBookId))
            {
                score += 20;
            }

            // Important fields (25 points total)
            if (book.ReleaseDate.HasValue)
            {
                score += 5;
            }

            if (!string.IsNullOrWhiteSpace(book.ForeignEditionId))
            {
                score += 5;
            }

            if (book.Editions?.Value?.Any() == true)
            {
                score += 5;
                
                // Bonus for multiple editions
                if (book.Editions.Value.Count > 1)
                {
                    score += 5;
                }
            }

            if (book.Ratings != null && book.Ratings.Votes > 0)
            {
                score += 5;
            }

            // Nice to have fields (15 points total)
            if (book.Genres?.Any() == true)
            {
                score += 3;
            }

            if (book.Links?.Any() == true)
            {
                score += 2;
            }

            if (book.SeriesLinks?.Value?.Any() == true)
            {
                score += 5;
            }

            if (book.RelatedBooks?.Any() == true)
            {
                score += 2;
            }

            // Cover images (check if any edition has covers)
            if (book.Editions?.Value?.Any(e => e.Images?.Any() == true) == true)
            {
                score += 3;
            }

            return score;
        }

        /// <summary>
        /// Calculate a quality score for author metadata (0-100)
        /// </summary>
        public int CalculateAuthorScore(Author author)
        {
            if (author == null)
            {
                return 0;
            }

            int score = 0;

            // Essential fields (60 points total)
            if (author.Metadata?.Value != null && !string.IsNullOrWhiteSpace(author.Metadata.Value.Name))
            {
                score += 25;
            }
            else if (!string.IsNullOrWhiteSpace(author.Name))
            {
                score += 25;
            }

            if (!string.IsNullOrWhiteSpace(author.ForeignAuthorId))
            {
                score += 20;
            }

            if (author.Books?.Value?.Any() == true)
            {
                score += 15;
            }

            // Important fields (20 points total)
            if (author.Metadata?.Value?.Overview != null && !string.IsNullOrWhiteSpace(author.Metadata.Value.Overview))
            {
                score += 10;
            }

            if (author.Metadata?.Value?.Images?.Any() == true)
            {
                score += 5;
            }

            if (author.Metadata?.Value?.Ratings != null && author.Metadata.Value.Ratings.Votes > 0)
            {
                score += 5;
            }

            // Nice to have fields (20 points total)
            if (author.Metadata?.Value?.Born.HasValue == true)
            {
                score += 5;
            }

            if (author.Metadata?.Value?.Links?.Any() == true)
            {
                score += 5;
            }

            if (author.Series?.Value?.Any() == true)
            {
                score += 5;
            }

            if (author.Metadata?.Value?.Genres?.Any() == true)
            {
                score += 5;
            }

            return score;
        }

        /// <summary>
        /// Calculate a quality score for edition metadata (0-100)
        /// </summary>
        public int CalculateEditionScore(Edition edition)
        {
            if (edition == null)
            {
                return 0;
            }

            int score = 0;

            // Essential fields (60 points total)
            if (!string.IsNullOrWhiteSpace(edition.Title))
            {
                score += 20;
            }

            if (!string.IsNullOrWhiteSpace(edition.ForeignEditionId))
            {
                score += 20;
            }

            if (!string.IsNullOrWhiteSpace(edition.Isbn13))
            {
                score += 20;
            }
            else if (edition.Asin.IsNotNullOrWhiteSpace())
            {
                score += 15; // ASIN is less universal than ISBN
            }

            // Important fields (25 points total)
            if (edition.ReleaseDate.HasValue)
            {
                score += 5;
            }

            if (!string.IsNullOrWhiteSpace(edition.Publisher))
            {
                score += 5;
            }

            if (edition.PageCount > 0)
            {
                score += 5;
            }

            if (!string.IsNullOrWhiteSpace(edition.Format))
            {
                score += 5;
            }

            if (edition.Images?.Any() == true)
            {
                score += 5;
            }

            // Nice to have fields (15 points total)
            if (!string.IsNullOrWhiteSpace(edition.Overview))
            {
                score += 5;
            }

            if (edition.Ratings != null && edition.Ratings.Votes > 0)
            {
                score += 5;
            }

            if (edition.Links?.Any() == true)
            {
                score += 3;
            }

            if (!string.IsNullOrWhiteSpace(edition.Language))
            {
                score += 2;
            }

            return score;
        }

        /// <summary>
        /// Determine if metadata quality is acceptable
        /// </summary>
        public bool IsQualityAcceptable(int score)
        {
            return score >= MinimumAcceptableScore;
        }
    }
}
