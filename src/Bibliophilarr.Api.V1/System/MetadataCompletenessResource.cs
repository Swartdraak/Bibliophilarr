using System;

namespace Bibliophilarr.Api.V1.System
{
    /// <summary>
    /// Metadata-completeness KPI report (issue #207). Exposes the hydration gaps
    /// (OpenLibraryWorkId coverage, series linkage density, file-match coverage) so
    /// operators can see the state and track improvement over time.
    /// </summary>
    public class MetadataCompletenessResource
    {
        public DateTime GeneratedAtUtc { get; set; }

        // Books
        public int TotalBooks { get; set; }
        public int BooksWithOpenLibraryWorkId { get; set; }
        public int BooksMissingOpenLibraryWorkId { get; set; }
        public double OpenLibraryWorkIdCoveragePercent { get; set; }

        // Series
        public int TotalSeries { get; set; }
        public int SeriesWithLinkedBooks { get; set; }
        public int SeriesWithoutLinkedBooks { get; set; }
        public double SeriesLinkageDensityPercent { get; set; }

        // Editions / BookFiles (file-match coverage, issue #206)
        public int TotalEditions { get; set; }
        public int EditionsWithBookFile { get; set; }
        public int EditionsWithoutBookFile { get; set; }
        public int TotalBookFiles { get; set; }
        public double EditionFileCoveragePercent { get; set; }

        // Authors
        public int TotalAuthors { get; set; }

        public static MetadataCompletenessResource FromStats(NzbDrone.Core.Books.MetadataCompletenessStats stats)
        {
            return new MetadataCompletenessResource
            {
                GeneratedAtUtc = DateTime.UtcNow,
                TotalBooks = stats.TotalBooks,
                BooksWithOpenLibraryWorkId = stats.BooksWithOpenLibraryWorkId,
                BooksMissingOpenLibraryWorkId = stats.BooksMissingOpenLibraryWorkId,
                OpenLibraryWorkIdCoveragePercent = Percent(stats.BooksWithOpenLibraryWorkId, stats.TotalBooks),

                TotalSeries = stats.TotalSeries,
                SeriesWithLinkedBooks = stats.SeriesWithLinkedBooks,
                SeriesWithoutLinkedBooks = stats.SeriesWithoutLinkedBooks,
                SeriesLinkageDensityPercent = Percent(stats.SeriesWithLinkedBooks, stats.TotalSeries),

                TotalEditions = stats.TotalEditions,
                EditionsWithBookFile = stats.EditionsWithBookFile,
                EditionsWithoutBookFile = stats.EditionsWithoutBookFile,
                TotalBookFiles = stats.TotalBookFiles,
                EditionFileCoveragePercent = Percent(stats.EditionsWithBookFile, stats.TotalEditions),

                TotalAuthors = stats.TotalAuthors
            };
        }

        private static double Percent(int part, int total)
        {
            return total <= 0 ? 0.0 : Math.Round(100.0 * part / total, 2);
        }
    }
}
