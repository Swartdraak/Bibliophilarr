using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using FizzWare.NBuilder;
using FluentAssertions;
using Moq;
using NLog;
using NLog.Config;
using NLog.Targets;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Common.Http;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Download;
using NzbDrone.Core.Download.Clients.Blackhole;
using NzbDrone.Core.Exceptions;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.RemotePathMappings;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.Download.DownloadClientTests.Blackhole
{
    [TestFixture]
    public class UsenetBlackholeFixture : DownloadClientFixtureBase<UsenetBlackhole>
    {
        protected string _completedDownloadFolder;
        protected string _blackholeFolder;
        protected string _filePath;
        protected DownloadClientItem _downloadClientItem;

        [SetUp]
        public void Setup()
        {
            _completedDownloadFolder = @"c:\blackhole\completed".AsOsAgnostic();
            _blackholeFolder = @"c:\blackhole\nzb".AsOsAgnostic();
            _filePath = (@"c:\blackhole\nzb\" + _title + ".nzb").AsOsAgnostic();

            _downloadClientItem = Builder<DownloadClientItem>
                                  .CreateNew()
                                  .With(d => d.DownloadId = "_Droned.S01E01.Pilot.1080p.WEB-DL-DRONE_0")
                                  .With(d => d.OutputPath = new OsPath(Path.Combine(_completedDownloadFolder, _title)))
                                  .Build();

            Mocker.SetConstant<IScanWatchFolder>(Mocker.Resolve<ScanWatchFolder>());

            Subject.Definition = new DownloadClientDefinition();
            Subject.Definition.Settings = new UsenetBlackholeSettings
            {
                NzbFolder = _blackholeFolder,
                WatchFolder = _completedDownloadFolder
            };

            Mocker.GetMock<IDiskProvider>()
                .Setup(c => c.OpenWriteStream(It.IsAny<string>()))
                .Returns(() => new FileStream(GetTempFilePath(), FileMode.Create));

            Mocker.GetMock<IDiskScanService>().Setup(c => c.FilterPaths(It.IsAny<string>(), It.IsAny<IEnumerable<string>>()))
                  .Returns<string, IEnumerable<string>>((b, s) => s.ToList());

            Mocker.GetMock<IDiskScanService>().Setup(c => c.FilterFiles(It.IsAny<string>(), It.IsAny<IEnumerable<IFileInfo>>()))
                .Returns<string, IEnumerable<IFileInfo>>((b, s) => s.ToList());
        }

        protected void GivenFailedDownload()
        {
            Mocker.GetMock<IHttpClient>()
                .Setup(c => c.Get(It.IsAny<HttpRequest>()))
                .Throws(new WebException());
        }

        protected void GivenCompletedItem()
        {
            var targetDir = Path.Combine(_completedDownloadFolder, _title);

            Mocker.GetMock<IDiskProvider>()
                .Setup(c => c.GetDirectories(_completedDownloadFolder))
                .Returns(new[] { targetDir });

            Mocker.GetMock<IDiskProvider>()
                .Setup(c => c.GetFiles(targetDir, true))
                .Returns(new[] { Path.Combine(targetDir, "somefile.flac") });

            Mocker.GetMock<IDiskProvider>()
                .Setup(c => c.GetFileSize(It.IsAny<string>()))
                .Returns(1000000);
        }

        [Test]
        public void completed_download_should_have_required_properties()
        {
            Subject.ScanGracePeriod = TimeSpan.Zero;

            GivenCompletedItem();

            var result = Subject.GetItems().Single();

            VerifyCompleted(result);

            result.CanBeRemoved.Should().BeTrue();
            result.CanMoveFiles.Should().BeTrue();
        }

        [Test]
        public void partial_download_should_have_required_properties()
        {
            GivenCompletedItem();

            var result = Subject.GetItems().Single();

            VerifyPostprocessing(result);
        }

        [Test]
        public void should_return_category()
        {
            GivenCompletedItem();

            var result = Subject.GetItems().Single();

            // We must have a category or CDH won't pick it up.
            result.Category.Should().NotBeNullOrWhiteSpace();
        }

        [TestCase(HttpStatusCode.OK)]
        [TestCase(HttpStatusCode.NotFound)]
        [TestCase(HttpStatusCode.TooManyRequests)]
        public async Task Download_should_preserve_transport_and_log_only_safe_diagnostics(HttpStatusCode status)
        {
            const string title = "Fixture\tVolume";
            const string url = "https://download.example.invalid/book";
            const string externalResponse = "Fixture response detail";
            var remoteBook = CreateRemoteBook();
            remoteBook.Release.Title = title;
            remoteBook.Release.DownloadUrl = url;
            remoteBook.Release.IndexerId = 42;
            var messages = new MemoryTarget { Layout = "${message}|${exception:format=ToString}|${exception:format=Data}" };
            var configuration = new LoggingConfiguration();
            configuration.AddRuleForAllLevels(messages);
            using var logFactory = new LogFactory { Configuration = configuration };
            var downloader = new UsenetBlackhole(
                Mocker.Resolve<IScanWatchFolder>(),
                Mocker.Resolve<IHttpClient>(),
                Mocker.Resolve<IConfigService>(),
                Mocker.Resolve<IDiskProvider>(),
                Mocker.Resolve<IRemotePathMappingService>(),
                Mocker.Resolve<IValidateNzbs>(),
                logFactory.GetLogger(nameof(UsenetBlackhole)))
            {
                Definition = Subject.Definition
            };

            if (status == HttpStatusCode.OK)
            {
                await downloader.Download(remoteBook, null);
            }
            else
            {
                var request = new HttpRequest(url);
                var response = new HttpResponse(request, new HttpHeader { ["Retry-After"] = "60" }, externalResponse, status);
                var failure = status == HttpStatusCode.NotFound
                    ? new HttpException(request, response)
                    : new TooManyRequestsException(request, response);
                Mocker.GetMock<IHttpClient>().Setup(transport => transport.GetAsync(It.IsAny<HttpRequest>()))
                    .ThrowsAsync(failure);

                var thrown = Assert.CatchAsync<ReleaseDownloadException>(() => downloader.Download(remoteBook, null));
                thrown.InnerException.Should().BeSameAs(failure);
                if (status == HttpStatusCode.NotFound)
                {
                    thrown.Should().BeOfType<ReleaseUnavailableException>();
                }
                else
                {
                    thrown.Should().BeOfType<ReleaseDownloadException>();
                    ((TooManyRequestsException)thrown.InnerException).RetryAfter.Should().Be(TimeSpan.FromSeconds(60));
                }
            }

            remoteBook.Release.Title.Should().Be(title);
            Mocker.GetMock<IHttpClient>()
                .Verify(transport => transport.GetAsync(It.Is<HttpRequest>(request => request.Url.FullUri == url)), Times.Once());
            messages.Logs.Should().NotContain(entry =>
                entry.Contains(url, StringComparison.Ordinal) || entry.Contains(externalResponse, StringComparison.Ordinal));

            if (status == HttpStatusCode.OK)
            {
                messages.Logs.Should().ContainSingle(entry =>
                    entry.StartsWith("Downloaded nzb for release 'Fixture Volume' finished", StringComparison.Ordinal) &&
                    entry.Contains("from indexer 42", StringComparison.Ordinal));
            }
            else
            {
                messages.Logs.Should().ContainSingle(entry =>
                    entry.Contains("indexer 42", StringComparison.Ordinal) && entry.Contains($"HTTP {(int)status}", StringComparison.Ordinal));
            }
        }

        [Test]
        public async Task Download_should_download_file_if_it_doesnt_exist()
        {
            var remoteBook = CreateRemoteBook();

            await Subject.Download(remoteBook, CreateIndexer());

            Mocker.GetMock<IHttpClient>().Verify(c => c.GetAsync(It.Is<HttpRequest>(v => v.Url.FullUri == _downloadUrl)), Times.Once());
            Mocker.GetMock<IDiskProvider>().Verify(c => c.OpenWriteStream(_filePath), Times.Once());
            Mocker.GetMock<IHttpClient>().Verify(c => c.DownloadFileAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never());
        }

        [Test]
        public async Task Download_should_replace_illegal_characters_in_title()
        {
            var illegalTitle = "Radiohead - Scotch Mist [2008/FLAC/Lossless]";
            var expectedFilename = Path.Combine(_blackholeFolder, "Radiohead - Scotch Mist [2008+FLAC+Lossless]" + Path.GetExtension(_filePath));

            var remoteBook = CreateRemoteBook();
            remoteBook.Release.Title = illegalTitle;

            await Subject.Download(remoteBook, CreateIndexer());

            Mocker.GetMock<IHttpClient>().Verify(c => c.GetAsync(It.Is<HttpRequest>(v => v.Url.FullUri == _downloadUrl)), Times.Once());
            Mocker.GetMock<IDiskProvider>().Verify(c => c.OpenWriteStream(expectedFilename), Times.Once());
            Mocker.GetMock<IHttpClient>().Verify(c => c.DownloadFileAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never());
        }

        [Test]
        public void RemoveItem_should_delete_file()
        {
            GivenCompletedItem();

            Mocker.GetMock<IDiskProvider>()
                .Setup(c => c.FileExists(It.IsAny<string>()))
                .Returns(true);

            Subject.RemoveItem(_downloadClientItem, true);

            Mocker.GetMock<IDiskProvider>()
                .Verify(c => c.DeleteFile(It.IsAny<string>()), Times.Once());
        }

        [Test]
        public void RemoveItem_should_delete_directory()
        {
            GivenCompletedItem();

            Mocker.GetMock<IDiskProvider>()
                .Setup(c => c.FolderExists(It.IsAny<string>()))
                .Returns(true);

            Subject.RemoveItem(_downloadClientItem, true);

            Mocker.GetMock<IDiskProvider>()
                .Verify(c => c.DeleteFolder(It.IsAny<string>(), true), Times.Once());
        }

        [Test]
        public void RemoveItem_should_ignore_if_unknown_item()
        {
            Subject.RemoveItem(_downloadClientItem, true);

            Mocker.GetMock<IDiskProvider>()
                .Verify(c => c.DeleteFile(It.IsAny<string>()), Times.Never());

            Mocker.GetMock<IDiskProvider>()
                .Verify(c => c.DeleteFolder(It.IsAny<string>(), true), Times.Never());
        }

        [Test]
        public void RemoveItem_should_throw_if_deleteData_is_false()
        {
            GivenCompletedItem();

            Assert.Throws<NotSupportedException>(() => Subject.RemoveItem(_downloadClientItem, false));

            Mocker.GetMock<IDiskProvider>()
                .Verify(c => c.DeleteFile(It.IsAny<string>()), Times.Never());

            Mocker.GetMock<IDiskProvider>()
                .Verify(c => c.DeleteFolder(It.IsAny<string>(), true), Times.Never());
        }

        [Test]
        public void should_return_status_with_outputdirs()
        {
            var result = Subject.GetStatus();

            result.IsLocalhost.Should().BeTrue();
            result.OutputRootFolders.Should().NotBeNull();
            result.OutputRootFolders.First().Should().Be(_completedDownloadFolder);
        }
    }
}
