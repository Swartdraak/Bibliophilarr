namespace NzbDrone.Core.MetadataSource
{
    public sealed class SeriesSearchSummary
    {
        public string ForeignSeriesId { get; init; }
        public string Title { get; init; }
        public string Slug { get; init; }
        public string SourceProvider { get; init; }
        public string SourceUrl { get; init; }
    }
}
