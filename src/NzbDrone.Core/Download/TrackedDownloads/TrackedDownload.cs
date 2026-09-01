using NzbDrone.Core.Indexers;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.Download.TrackedDownloads
{
    public class TrackedDownload
    {
        public int DownloadClient { get; set; }
        public DownloadClientItem DownloadItem { get; set; }
        public DownloadClientItem ImportItem { get; set; }
        public TrackedDownloadState State { get; set; }
        public TrackedDownloadStatus Status { get; private set; }
        public RemoteBook RemoteBook { get; set; }
        public TrackedDownloadStatusMessage[] StatusMessages { get; private set; }
        public DownloadProtocol Protocol { get; set; }
        public string Indexer { get; set; }
        public bool IsTrackable { get; set; }

        // Incremented for each monitoring cycle in which a completed download had no
        // importable files, for the current tracked-download lifetime (in-memory only).
        // Once the configurable ZeroFileRetryThreshold is reached, the terminal
        // import-failure outcome is published to download history and survives restart;
        // the raw counter value itself is not durable across a restart before the
        // threshold is reached.
        public int ZeroFileRetryCount { get; set; }

        public TrackedDownload()
        {
            StatusMessages = new TrackedDownloadStatusMessage[] { };
        }

        public void Warn(string message, params object[] args)
        {
            var statusMessage = string.Format(message, args);
            Warn(new TrackedDownloadStatusMessage(DownloadItem.Title, statusMessage));
        }

        public void Warn(params TrackedDownloadStatusMessage[] statusMessages)
        {
            Status = TrackedDownloadStatus.Warning;
            StatusMessages = statusMessages;
        }
    }

    public enum TrackedDownloadState
    {
        Downloading,
        DownloadFailed,
        DownloadFailedPending,
        ImportPending,
        Importing,
        ImportFailed,
        Imported,
        Ignored
    }

    public enum TrackedDownloadStatus
    {
        Ok,
        Warning,
        Error
    }
}
