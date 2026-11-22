namespace NzbDrone.Core.MediaFiles.Azw
{
    public readonly struct SectionInfo
    {
        public SectionInfo(long start, long end)
        {
            Start = start;
            End = end;
        }

        public long Start { get; }
        public long End { get; }
    }
}
