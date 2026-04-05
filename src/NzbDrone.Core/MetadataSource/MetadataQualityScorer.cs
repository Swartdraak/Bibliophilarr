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
        // Books scoring below this threshold are considered low-quality and may
        // trigger additional provider lookups or operator warnings.
        private const int MinimumAcceptableScore = 50;

        /// <summary>
        /// Calculate a quality score for book metadata (0-100)
        /// </summary>
        public int CalculateBookScore(Book book)
        {
            return GetBookScoreBreakdown(book).Values.Sum();
        }

        /// <summary>
        /// Scoring rationale — weights reflect operational priority:
        ///   Essential (60 pts): title + author + ID uniquely identify a book; without these
        ///                      the library cannot match, merge, or display entries correctly.
        ///   Important (20 pts): release-date, edition-id, edition count, and ratings
        ///                      improve accuracy of matching and user-facing display.
        ///   Nice-to-have (20 pts): genres, links, series, related books, and cover images
        ///                         enhance discoverability but do not block core workflows.
        /// </summary>
        public static System.Collections.Generic.Dictionary<string, int> GetBookScoreBreakdown(Book book)
        {
            var breakdown = new System.Collections.Generic.Dictionary<string, int>(System.StringComparer.OrdinalIgnoreCase)
            {
                ["title"] = 0,
                ["author"] = 0,
                ["foreign-book-id"] = 0,
                ["release-date"] = 0,
                ["foreign-edition-id"] = 0,
                ["has-editions"] = 0,
                ["multiple-editions"] = 0,
                ["ratings"] = 0,
                ["genres"] = 0,
                ["links"] = 0,
                ["series-links"] = 0,
                ["related-books"] = 0,
                ["cover-images"] = 0
            };

            if (book == null)
            {
                return breakdown;
            }

            if (!string.IsNullOrWhiteSpace(book.Title))
            {
                breakdown["title"] = 20;
            }

            if (book.AuthorMetadata?.Value != null || book.Author?.Value != null)
            {
                breakdown["author"] = 20;
            }

            if (!string.IsNullOrWhiteSpace(book.ForeignBookId))
            {
                breakdown["foreign-book-id"] = 20;
            }

            if (book.ReleaseDate.HasValue)
            {
                breakdown["release-date"] = 5;
            }

            if (!string.IsNullOrWhiteSpace(book.ForeignEditionId))
            {
                breakdown["foreign-edition-id"] = 5;
            }

            if (book.Editions?.Value?.Any() == true)
            {
                breakdown["has-editions"] = 5;

                if (book.Editions.Value.Count > 1)
                {
                    breakdown["multiple-editions"] = 5;
                }
            }

            if (book.Ratings != null && book.Ratings.Votes > 0)
            {
                breakdown["ratings"] = 5;
            }

            if (book.Genres?.Any() == true)
            {
                breakdown["genres"] = 3;
            }

            if (book.Links?.Any() == true)
            {
                breakdown["links"] = 2;
            }

            if (book.SeriesLinks?.Value?.Any() == true)
            {
                breakdown["series-links"] = 5;
            }

            if (book.RelatedBooks?.Any() == true)
            {
                breakdown["related-books"] = 2;
            }

            if (book.Editions?.Value?.Any(e => e.Images?.Any() == true) == true)
            {
                breakdown["cover-images"] = 3;
            }

            return breakdown;
        }

        /// <summary>
        /// Calculate a quality score for author metadata (0-100)
        /// </summary>
        /// <summary>
        /// Author scoring rationale — weights reflect operational priority:
        ///   Essential (60 pts): name (25) + foreign-id (20) + has books (15).
        ///                      Name and ID are required for matching and display;
        ///                      books list indicates the author record is useful.
        ///   Important (20 pts): overview (10), images (5), ratings (5).
        ///                      Improve author pages but library functions without them.
        ///   Nice-to-have (20 pts): birth date (5), links (5), series (5), genres (5).
        ///                         Enrich the author profile for discovery.
        /// </summary>
        public int CalculateAuthorScore(Author author)
        {
            if (author == null)
            {
                return 0;
            }

            var score = 0;

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
        /// Edition scoring rationale — weights reflect operational priority:
        ///   Essential (60 pts): title (20) + foreign-edition-id (20) + ISBN-13 (20, ASIN=15).
        ///                      Title and ID are required for matching; ISBN/ASIN enables
        ///                      cross-provider correlation. ASIN scores lower as it is
        ///                      Amazon-specific and less universal than ISBN-13.
        ///   Important (25 pts): release-date (5), publisher (5), page-count (5),
        ///                      format (5), cover images (5). Needed for accurate
        ///                      edition differentiation and user display.
        ///   Nice-to-have (15 pts): overview (5), ratings (5), links (3), language (2).
        ///                         Enhance edition detail pages but are not required.
        /// </summary>
        public int CalculateEditionScore(Edition edition)
        {
            if (edition == null)
            {
                return 0;
            }

            var score = 0;

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
