using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NLog;
using NLog.Config;
using NLog.Targets;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.DecisionEngine;
using NzbDrone.Core.Download;
using NzbDrone.Core.Download.Clients;
using NzbDrone.Core.Download.Pending;
using NzbDrone.Core.Exceptions;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Download
{
    [TestFixture]
    public class ProcessDownloadDecisionsLoggingFixture : CoreTest<ProcessDownloadDecisions>
    {
        [TestCase(ProcessedDecisionResult.Grabbed, null)]
        [TestCase(ProcessedDecisionResult.Rejected, "Failed to download release from indexer, no longer available. City Lights Retail")]
        [TestCase(ProcessedDecisionResult.Failed, "Failed to send release to download client, storing until later. City Lights Retail")]
        [TestCase(ProcessedDecisionResult.Skipped, "Couldn't add report to download queue. City Lights Retail")]
        public async Task should_keep_download_diagnostics_on_one_line_and_preserve_the_outcome(ProcessedDecisionResult expected, string diagnostic)
        {
            var messages = new MemoryTarget { Layout = "${message}" };
            var configuration = new LoggingConfiguration();
            configuration.AddRule(LogLevel.Trace, LogLevel.Error, messages);
            using var logging = new LogFactory { Configuration = configuration };
            Mocker.SetConstant(logging.GetLogger(nameof(ProcessDownloadDecisions)));

            const string title = "City Lights\r\n\tRetail";
            const string indexer = "Library Catalogue\r\n\tFeed";
            const string downloadUrl = "https://fixture.invalid/release";
            var release = new ReleaseInfo { Title = title, Indexer = indexer, IndexerPriority = 3, IndexerId = 7, DownloadUrl = downloadUrl };
            var remote = new RemoteBook { Release = release, Books = new List<Book> { new Book { Id = 1 } }, ReleaseSource = ReleaseSourceType.ReleasePush };
            var decision = new DownloadDecision(remote);
            var failure = expected switch
            {
                ProcessedDecisionResult.Rejected => new ReleaseUnavailableException(release, "Fixture release expired."),
                ProcessedDecisionResult.Failed => new DownloadClientUnavailableException("Fixture client paused."),
                ProcessedDecisionResult.Skipped => new InvalidOperationException("Fixture queue stopped."),
                _ => (Exception)null
            };

            var download = Mocker.GetMock<IDownloadService>();
            download.Setup(service => service.DownloadReport(remote, 9))
                .Returns(failure == null ? Task.CompletedTask : Task.FromException(failure));

            (await Subject.ProcessDecision(decision, 9)).Should().Be(expected);

            download.Verify(service => service.DownloadReport(remote, 9), Times.Once);
            Mocker.GetMock<IPendingReleaseService>().Verify(service => service.Add(decision, PendingReleaseReason.DownloadClientUnavailable),
                expected == ProcessedDecisionResult.Failed ? Times.Once() : Times.Never());
            remote.Release.Should().BeSameAs(release);
            release.Title.Should().Be(title);
            release.Indexer.Should().Be(indexer);
            release.IndexerId.Should().Be(7);
            release.DownloadUrl.Should().Be(downloadUrl);
            remote.ReleaseSource.Should().Be(ReleaseSourceType.ReleasePush);
            messages.Logs.Should().Contain("Grabbing from Indexer Library Catalogue Feed at priority 3.");
            if (diagnostic != null)
            {
                messages.Logs.Should().Contain(diagnostic);
            }
        }
    }
}
