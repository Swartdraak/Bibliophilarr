using System.Threading;
using NLog;

namespace NzbDrone.Core.MediaFiles.BookImport
{
    public class ImportRunSummary
    {
        private int _errors;

        public int FilesScanned { get; set; }
        public int FilesFiltered { get; set; }
        public int ReleasesGrouped { get; set; }
        public int ReleasesIdentified { get; set; }
        public int ReleasesUnmatched { get; set; }
        public int RemoteSearchesRequired { get; set; }
        public int PerfectMatches { get; set; }
        public int GoodMatches { get; set; }
        public int PoorMatches { get; set; }
        public int Imported { get; set; }
        public int Rejected { get; set; }
        public int Errors
        {
            get => _errors;
            set => _errors = value;
        }

        public ref int ErrorsRef => ref _errors;
        public long TagReadMs { get; set; }
        public long GroupingMs { get; set; }
        public long IdentificationMs { get; set; }
        public long TotalMs { get; set; }

        public double ThroughputPerMinute => TotalMs > 0
            ? FilesScanned / (TotalMs / 60000.0)
            : 0;

        public double MatchRate => ReleasesGrouped > 0
            ? (double)ReleasesIdentified / ReleasesGrouped
            : 0;
    }

    public interface IImportRunTracker
    {
        ImportRunSummary CreateRun();
        void RecordMatchQuality(ImportRunSummary summary, double normalizedDistance);
        void IncrementRemoteSearches(ImportRunSummary summary);
        void LogSummary(ImportRunSummary summary, Logger logger);
    }

    public class ImportRunTracker : IImportRunTracker
    {
        private int _remoteSearches;
        private int _perfect;
        private int _good;
        private int _poor;
        private int _unmatched;

        public ImportRunSummary CreateRun()
        {
            _remoteSearches = 0;
            _perfect = 0;
            _good = 0;
            _poor = 0;
            _unmatched = 0;

            return new ImportRunSummary();
        }

        public void RecordMatchQuality(ImportRunSummary summary, double normalizedDistance)
        {
            if (normalizedDistance == 0.0)
            {
                Interlocked.Increment(ref _perfect);
            }
            else if (normalizedDistance <= 0.15)
            {
                Interlocked.Increment(ref _good);
            }
            else if (normalizedDistance <= 0.5)
            {
                Interlocked.Increment(ref _poor);
            }
            else
            {
                Interlocked.Increment(ref _unmatched);
            }
        }

        public void IncrementRemoteSearches(ImportRunSummary summary)
        {
            Interlocked.Increment(ref _remoteSearches);
        }

        public void LogSummary(ImportRunSummary summary, Logger logger)
        {
            summary.PerfectMatches = _perfect;
            summary.GoodMatches = _good;
            summary.PoorMatches = _poor;
            summary.ReleasesUnmatched = _unmatched;
            summary.ReleasesIdentified = _perfect + _good + _poor;
            summary.RemoteSearchesRequired = _remoteSearches;

            logger.Info(
                "Import run complete: " +
                "files={FilesScanned} (filtered={FilesFiltered}), " +
                "releases={ReleasesGrouped}, " +
                "identified={ReleasesIdentified} (perfect={PerfectMatches}, good={GoodMatches}, poor={PoorMatches}, unmatched={ReleasesUnmatched}), " +
                "remote_searches={RemoteSearchesRequired}, " +
                "timing: tag_read={TagReadMs}ms, grouping={GroupingMs}ms, identification={IdentificationMs}ms, total={TotalMs}ms, " +
                "throughput={ThroughputPerMinute:F1} files/min, " +
                "match_rate={MatchRate:P1}",
                summary.FilesScanned,
                summary.FilesFiltered,
                summary.ReleasesGrouped,
                summary.ReleasesIdentified,
                summary.PerfectMatches,
                summary.GoodMatches,
                summary.PoorMatches,
                summary.ReleasesUnmatched,
                summary.RemoteSearchesRequired,
                summary.TagReadMs,
                summary.GroupingMs,
                summary.IdentificationMs,
                summary.TotalMs,
                summary.ThroughputPerMinute,
                summary.MatchRate);
        }
    }
}
