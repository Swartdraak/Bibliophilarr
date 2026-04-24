namespace NzbDrone.Core.Books
{
    public class AddAuthorOptions : MonitoringOptions
    {
        public bool SearchForMissingBooks { get; set; }

        // Per-format overrides (used when EnableDualFormatTracking is true)
        public int? EbookQualityProfileId { get; set; }
        public int? AudiobookQualityProfileId { get; set; }
        public string EbookRootFolderPath { get; set; }
        public string AudiobookRootFolderPath { get; set; }
    }
}
