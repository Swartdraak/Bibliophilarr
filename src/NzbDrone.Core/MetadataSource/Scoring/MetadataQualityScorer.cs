using System.Linq;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Books;

namespace NzbDrone.Core.MetadataSource.Scoring
{
    /// <summary>
    /// Scores metadata completeness for books and authors on a 0–100 scale.
    ///
    /// Book scoring examines the work-level fields on <see cref="Book"/> and the
    /// first monitored edition from <see cref="Book.Editions"/> (when loaded), or
    /// the first edition if none are monitored.
    /// Author scoring examines fields on <see cref="AuthorMetadata"/> via the
    /// <see cref="Author.Metadata"/> lazy property (when loaded).
    ///
    /// Lazy-loaded relations that have not yet been loaded are treated as absent
    /// (zero points awarded) to keep scoring side-effect-free and avoid implicit DB reads.
    /// </summary>
    public class MetadataQualityScorer : IMetadataQualityScorer
    {
        // ── Book scoring weights ─────────────────────────────── total = 100 ──
        private const int BookTitleWeight = 20;
        private const int BookAuthorWeight = 20;
        private const int BookIsbnWeight = 15;
        private const int BookOverviewWeight = 10;
        private const int BookReleaseDateWeight = 5;
        private const int BookPublisherWeight = 5;
        private const int BookCoverWeight = 10;
        private const int BookGenresWeight = 5;
        private const int BookPageCountWeight = 5;
        private const int BookLanguageWeight = 5;

        // ── Author scoring weights ───────────────────────────── total = 100 ──
        private const int AuthorNameWeight = 30;
        private const int AuthorOverviewWeight = 20;
        private const int AuthorImageWeight = 20;
        private const int AuthorBornWeight = 10;
        private const int AuthorGenresWeight = 10;
        private const int AuthorHometownWeight = 5;
        private const int AuthorAliasesWeight = 5;

        /// <inheritdoc/>
        public int CalculateBookScore(Book book)
        {
            if (book == null)
            {
                return 0;
            }

            var score = 0;

            // Work-level fields
            if (!book.Title.IsNullOrWhiteSpace())
            {
                score += BookTitleWeight;
            }

            if (book.AuthorMetadata != null &&
                book.AuthorMetadata.IsLoaded &&
                book.AuthorMetadata.Value?.Name.IsNotNullOrWhiteSpace() == true)
            {
                score += BookAuthorWeight;
            }

            if (book.Genres?.Any() == true)
            {
                score += BookGenresWeight;
            }

            if (book.ReleaseDate.HasValue)
            {
                score += BookReleaseDateWeight;
            }

            // Edition-level fields (use the first monitored edition, or the first edition)
            var edition = (Edition)null;

            if (book.Editions?.IsLoaded == true)
            {
                var editions = book.Editions.Value;
                edition = editions?.FirstOrDefault(e => e.Monitored)
                          ?? editions?.FirstOrDefault();
            }

            if (edition != null)
            {
                if (!edition.Isbn13.IsNullOrWhiteSpace() || !edition.Asin.IsNullOrWhiteSpace())
                {
                    score += BookIsbnWeight;
                }

                if (!edition.Overview.IsNullOrWhiteSpace())
                {
                    score += BookOverviewWeight;
                }

                if (!edition.Publisher.IsNullOrWhiteSpace())
                {
                    score += BookPublisherWeight;
                }

                if (edition.Images?.Any() == true)
                {
                    score += BookCoverWeight;
                }

                if (edition.PageCount > 0)
                {
                    score += BookPageCountWeight;
                }

                if (!edition.Language.IsNullOrWhiteSpace())
                {
                    score += BookLanguageWeight;
                }
            }

            return score;
        }

        /// <inheritdoc/>
        public int CalculateAuthorScore(Author author)
        {
            if (author == null)
            {
                return 0;
            }

            if (author.Metadata == null || !author.Metadata.IsLoaded)
            {
                return 0;
            }

            var meta = author.Metadata.Value;
            if (meta == null)
            {
                return 0;
            }

            var score = 0;

            if (!meta.Name.IsNullOrWhiteSpace())
            {
                score += AuthorNameWeight;
            }

            if (!meta.Overview.IsNullOrWhiteSpace())
            {
                score += AuthorOverviewWeight;
            }

            if (meta.Images?.Any() == true)
            {
                score += AuthorImageWeight;
            }

            if (meta.Born.HasValue)
            {
                score += AuthorBornWeight;
            }

            if (meta.Genres?.Any() == true)
            {
                score += AuthorGenresWeight;
            }

            if (!meta.Hometown.IsNullOrWhiteSpace())
            {
                score += AuthorHometownWeight;
            }

            if (meta.Aliases?.Any() == true)
            {
                score += AuthorAliasesWeight;
            }

            return score;
        }
    }
}
