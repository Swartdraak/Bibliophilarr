using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Common.Instrumentation.Extensions;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Download.TrackedDownloads;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.Download
{
    public class DownloadProcessingService : IExecute<ProcessMonitoredDownloadsCommand>
    {
        private readonly IConfigService _configService;
        private readonly ICompletedDownloadService _completedDownloadService;
        private readonly IFailedDownloadService _failedDownloadService;
        private readonly ITrackedDownloadService _trackedDownloadService;
        private readonly IEventAggregator _eventAggregator;
        private readonly Logger _logger;

        public DownloadProcessingService(IConfigService configService,
                                         ICompletedDownloadService completedDownloadService,
                                         IFailedDownloadService failedDownloadService,
                                         ITrackedDownloadService trackedDownloadService,
                                         IEventAggregator eventAggregator,
                                         Logger logger)
        {
            _configService = configService;
            _completedDownloadService = completedDownloadService;
            _failedDownloadService = failedDownloadService;
            _trackedDownloadService = trackedDownloadService;
            _eventAggregator = eventAggregator;
            _logger = logger;
        }

        private void RemoveCompletedDownloads()
        {
            var trackedDownloads = _trackedDownloadService.GetTrackedDownloads()
                                                          .Where(t => !t.DownloadItem.Removed && t.DownloadItem.CanBeRemoved && t.State == TrackedDownloadState.Imported)
                                                          .ToList();

            foreach (var trackedDownload in trackedDownloads)
            {
                _eventAggregator.PublishEvent(new DownloadCanBeRemovedEvent(trackedDownload));
            }
        }

        public void Execute(ProcessMonitoredDownloadsCommand message)
        {
            var enableCompletedDownloadHandling = _configService.EnableCompletedDownloadHandling;
            var trackedDownloads = _trackedDownloadService.GetTrackedDownloads()
                                                          .Where(t => t.IsTrackable)
                                                          .ToList();

            // Process failed downloads sequentially (rare, quick operations)
            var failedDownloads = trackedDownloads
                .Where(t => t.State == TrackedDownloadState.DownloadFailedPending)
                .ToList();

            foreach (var trackedDownload in failedDownloads)
            {
                try
                {
                    _failedDownloadService.ProcessFailed(trackedDownload);
                }
                catch (Exception e)
                {
                    _logger.Debug(e, "Failed to process failed download: {0}", trackedDownload.DownloadItem.Title);
                }
            }

            // Process completed downloads in parallel (I/O-bound: file reads, identification, API calls)
            if (enableCompletedDownloadHandling)
            {
                var pendingImports = trackedDownloads
                    .Where(t => t.State == TrackedDownloadState.ImportPending)
                    .ToList();

                if (pendingImports.Any())
                {
                    var maxWorkers = Math.Max(1, Math.Min(_configService.DownloadProcessingWorkerCount, pendingImports.Count));
                    var processed = 0;

                    _logger.Debug("Processing {0} pending import(s) with up to {1} worker(s)", pendingImports.Count, maxWorkers);

                    var options = new ParallelOptions { MaxDegreeOfParallelism = maxWorkers };
                    Parallel.ForEach(pendingImports, options, trackedDownload =>
                    {
                        var current = Interlocked.Increment(ref processed);
                        _logger.ProgressInfo($"Processing download {current}/{pendingImports.Count}: {trackedDownload.DownloadItem.Title}");

                        try
                        {
                            _completedDownloadService.Import(trackedDownload);
                        }
                        catch (Exception e)
                        {
                            _logger.Debug(e, "Failed to process download: {0}", trackedDownload.DownloadItem.Title);

                            if (trackedDownload.State == TrackedDownloadState.Importing)
                            {
                                trackedDownload.State = TrackedDownloadState.ImportFailed;
                                trackedDownload.Warn("Import error: {0}", e.Message);
                            }
                        }
                    });
                }
            }

            // Imported downloads are no longer trackable so process them after processing trackable downloads
            RemoveCompletedDownloads();

            _eventAggregator.PublishEvent(new DownloadsProcessedEvent());
        }
    }
}
