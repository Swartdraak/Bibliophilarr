namespace NzbDrone.Core.Books
{
    /// <summary>
    /// Aggregate metadata-completeness KPIs for the library (issue #207).
    /// Computed via a small set of aggregate queries over the Books / Series /
    /// SeriesBookLink / Editions / BookFiles / Authors tables.
    /// </summary>
    public class MetadataCompletenessStats
    {
        // Books
        public int TotalBooks { get; set; }
        public int BooksWithOpenLibraryWorkId { get; set; }
        public int BooksMissingOpenLibraryWorkId { get; set; }

        // Series
        public int TotalSeries { get; set; }
        public int SeriesWithLinkedBooks { get; set; }
        public int SeriesWithoutLinkedBooks { get; set; }

        // Editions / BookFiles (file-match coverage, issue #206)
        public int TotalEditions { get; set; }
        public int EditionsWithBookFile { get; set; }
        public int EditionsWithoutBookFile { get; set; }
        public int TotalBookFiles { get; set; }

        // Authors
        public int TotalAuthors { get; set; }
    }
}
