namespace NzbDrone.Core.MetadataSource
{
    public sealed class SeriesSearchSummary
    {
        public string ForeignSeriesId { get; init; }
        public string Title { get; init; }
        public string Slug { get; init; }
    }
}
