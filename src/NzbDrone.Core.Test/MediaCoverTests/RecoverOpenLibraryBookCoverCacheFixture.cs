using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using FluentAssertions;
using Moq;
using NLog;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Common.Http;
using NzbDrone.Core.Books;
using NzbDrone.Core.Books.Events;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.MediaCover;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Test.Framework;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.PixelFormats;

namespace NzbDrone.Core.Test.MediaCoverTests
{
    [TestFixture]
    [NonParallelizable]
    public class RecoverOpenLibraryBookCoverCacheFixture : CoreTest<MediaCoverService>
    {
        private readonly FixtureClock _clock = new FixtureClock();

        [TestCase(WebExceptionStatus.Timeout)]
        [TestCase(WebExceptionStatus.ReceiveFailure)]
        public void should_defer_cover_recovery_after_a_transport_timeout(WebExceptionStatus status)
        {
            const string coverUrl = "https://covers.openlibrary.org/b/id/41-L.jpg";
            var suffix = OpenLibraryMissingBookCover.GetRetryFileSuffix("openlibrary:work:OL41W", "OL-work-OL41W", coverUrl);
            var cause = status == WebExceptionStatus.ReceiveFailure ? new OperationCanceledException() : null;
            var transport = new WebException("Fixture transport deadline", cause, status, null);
            Mocker.GetMock<IAppFolderInfo>().SetupGet(folders => folders.AppDataFolder).Returns(TempFolder);
            var http = Mocker.GetMock<IHttpClient>();
            http.Setup(client => client.Get(It.IsAny<HttpRequest>())).Throws(transport);

            var cache = (IOpenLibraryBookCoverCache)CreateTimedService();

            cache.TryRecover(4, coverUrl, suffix, () => true, TimeSpan.FromSeconds(5))
                .Should().Be(OpenLibraryCoverRecovery.DeferredAfterRequest);
            http.Verify(client => client.Get(It.IsAny<HttpRequest>()), Times.Once());
        }

        [TestCase(true)]
        [TestCase(false)]
        public void should_keep_only_the_current_nonempty_retry_file_after_an_author_refresh_without_network(bool populated)
        {
            var profile = TempFolder;
            Mocker.GetMock<IAppFolderInfo>().SetupGet(folders => folders.AppDataFolder).Returns(profile);
            var disk = Mocker.GetMock<IDiskProvider>();
            disk.Setup(service => service.FolderExists(It.IsAny<string>())).Returns<string>(Directory.Exists);
            disk.Setup(service => service.FileExists(It.IsAny<string>())).Returns<string>(File.Exists);
            disk.Setup(service => service.GetFileSize(It.IsAny<string>())).Returns<string>(p => new FileInfo(p).Length);
            disk.Setup(service => service.GetFiles(It.IsAny<string>(), true))
                .Returns<string, bool>((path, _) => Directory.GetFiles(path, "*", SearchOption.AllDirectories));
            disk.Setup(service => service.DeleteFile(It.IsAny<string>())).Callback<string>(File.Delete);
            const int bookId = 4;
            const string workId = "openlibrary:work:OL31W";
            const string editionId = "OL-work-OL31W";
            const string coverUrl = "https://covers.openlibrary.org/b/id/31-L.jpg";
            const string staleUrl = "https://covers.openlibrary.org/b/id/32-L.jpg";
            var current = Subject.GetCoverPath(
                bookId,
                MediaCoverEntity.Book,
                MediaCoverTypes.Cover,
                OpenLibraryMissingBookCover.GetRetryFileSuffix(workId, editionId, coverUrl));
            var stale = Subject.GetCoverPath(
                bookId,
                MediaCoverEntity.Book,
                MediaCoverTypes.Cover,
                OpenLibraryMissingBookCover.GetRetryFileSuffix(workId, editionId, staleUrl));
            var normal = Subject.GetCoverPath(bookId, MediaCoverEntity.Book, MediaCoverTypes.Cover, ".jpg");
            Directory.CreateDirectory(Path.GetDirectoryName(current));
            File.WriteAllText(current, populated ? "current artwork" : string.Empty);
            File.WriteAllText(stale, "stale artwork");
            File.WriteAllText(normal, "existing normal artwork");

            var author = new Author { Id = 1, AuthorMetadataId = 11, Metadata = new AuthorMetadata { Id = 11 } };
            Mocker.GetMock<IAuthorRepository>().Setup(repository => repository.Find(author.Id)).Returns(author);
            var edition = new Edition
            {
                Id = 10,
                BookId = bookId,
                ForeignEditionId = editionId,
                Monitored = false,
                Images = new List<MediaCover.MediaCover> { new MediaCover.MediaCover(MediaCoverTypes.Cover, coverUrl) }
            };
            var book = new Book { Id = bookId, ForeignBookId = workId, Editions = new List<Edition> { edition } };
            Mocker.GetMock<IBookService>().Setup(service => service.GetBooksByAuthor(author.Id)).Returns(new List<Book> { book });

            Subject.HandleAsync(new AuthorRefreshCompleteEvent(author));

            File.Exists(current).Should().Be(populated);
            File.Exists(stale).Should().BeFalse();
            File.ReadAllText(normal).Should().Be("existing normal artwork");
            Mocker.GetMock<IHttpClient>().Verify(client => client.Get(It.IsAny<HttpRequest>()), Times.Never);
        }

        [Test]
        public void should_publish_only_the_current_successfully_decoded_book_cover_and_never_overwrite_normal_art()
        {
            var profile = TempFolder;
            Mocker.GetMock<IAppFolderInfo>().SetupGet(folders => folders.AppDataFolder).Returns(profile);
            var disk = Mocker.GetMock<IDiskProvider>();
            disk.Setup(service => service.FileExists(It.IsAny<string>())).Returns<string>(File.Exists);
            disk.Setup(service => service.GetFileSize(It.IsAny<string>())).Returns<string>(p => new FileInfo(p).Length);
            disk.Setup(service => service.FileGetLastWrite(It.IsAny<string>())).Returns<string>(File.GetLastWriteTimeUtc);
            disk.Setup(service => service.OpenWriteStream(It.IsAny<string>())).Returns<string>(p => File.Create(p));
            disk.Setup(service => service.CreateFolder(It.IsAny<string>())).Callback<string>(p => Directory.CreateDirectory(p));
            disk.Setup(service => service.MoveFile(It.IsAny<string>(), It.IsAny<string>(), false)).Callback<string, string, bool>(File.Move);
            disk.Setup(service => service.DeleteFile(It.IsAny<string>())).Callback<string>(File.Delete);

            const int bookId = 4;
            const string workId = "openlibrary:work:OL31W";
            const string editionId = "OL-work-OL31W";
            const string originalUrl = "https://covers.openlibrary.org/b/id/31-L.jpg";
            const string changedUrl = "https://covers.openlibrary.org/b/id/32-L.jpg";
            const string placeholderUrl = "https://covers.openlibrary.org/b/id/33-L.jpg";
            var original = OpenLibraryMissingBookCover.GetRetryFileSuffix(workId, editionId, originalUrl);
            var changed = OpenLibraryMissingBookCover.GetRetryFileSuffix(workId, editionId, changedUrl);
            var placeholder = OpenLibraryMissingBookCover.GetRetryFileSuffix(workId, editionId, placeholderUrl);
            var http = Mocker.GetMock<IHttpClient>();
            http.Setup(client => client.Get(It.IsAny<HttpRequest>())).Returns<HttpRequest>(request =>
            {
                request.AllowAutoRedirect.Should().BeFalse();
                request.RequestTimeout.Should().Be(TimeSpan.FromSeconds(5));
                var size = request.Url.FullUri == placeholderUrl ? 1 : 12;
                using var image = new Image<Rgba32>(size, size);
                image.Save(request.ResponseStream, new JpegEncoder());
                return new HttpResponse(request, new HttpHeader(), Array.Empty<byte>(), HttpStatusCode.OK);
            });

            var subject = CreateTimedService();
            var cache = (IOpenLibraryBookCoverCache)subject;
            var observations = 0;
            cache.TryRecover(bookId, changedUrl, changed, () => ++observations == 1, TimeSpan.FromSeconds(5))
                .Should().Be(OpenLibraryCoverRecovery.Skipped);
            File.Exists(subject.GetCoverPath(bookId, MediaCoverEntity.Book, MediaCoverTypes.Cover, changed)).Should().BeFalse();
            cache.TryRecover(bookId, placeholderUrl, placeholder, () => true, TimeSpan.FromSeconds(5))
                .Should().Be(OpenLibraryCoverRecovery.Unavailable);
            File.Exists(subject.GetCoverPath(bookId, MediaCoverEntity.Book, MediaCoverTypes.Cover, placeholder)).Should().BeFalse();

            cache.TryRecover(bookId, originalUrl, original, () => true, TimeSpan.FromSeconds(5))
                .Should().Be(OpenLibraryCoverRecovery.Recovered);
            cache.IsMissing(bookId, original).Should().BeFalse();
            var cached = subject.GetCoverPath(bookId, MediaCoverEntity.Book, MediaCoverTypes.Cover, original);
            using (var image = Image.Load(cached))
            {
                image.Width.Should().Be(12);
            }

            var art = new MediaCover.MediaCover(MediaCoverTypes.Cover, "proxy") { RemoteUrl = originalUrl };
            subject.MapRecoveredBookCover(bookId, workId, editionId, new List<MediaCover.MediaCover> { art });
            art.Url.Should().StartWith($"/MediaCover/Books/{bookId}/cover{original}?lastWrite=");
            art.Url = "fallback";
            subject.MapRecoveredBookCover(bookId, workId, "openlibrary:edition:OL99M", new List<MediaCover.MediaCover> { art });
            art.Url.Should().Be("fallback");

            var normal = subject.GetCoverPath(bookId, MediaCoverEntity.Book, MediaCoverTypes.Cover, ".jpg");
            File.WriteAllText(normal, "ordinary cover sentinel");
            cache.TryRecover(bookId, changedUrl, changed, () => true, TimeSpan.FromSeconds(5))
                .Should().Be(OpenLibraryCoverRecovery.Skipped);
            Subject.MapRecoveredBookCover(bookId, workId, editionId, new List<MediaCover.MediaCover> { art });
            art.Url.Should().Be("fallback");
            File.ReadAllText(normal).Should().Be("ordinary cover sentinel");
            Directory.EnumerateFiles(Path.Combine(profile, "MediaCoverRetry")).Should().BeEmpty();
            http.Verify(client => client.Get(It.IsAny<HttpRequest>()), Times.Exactly(3));
            http.VerifyNoOtherCalls();
        }

        private MediaCoverService CreateTimedService()
        {
            // Requests to the exact Open Library host share a process-wide bucket.
            // Refill it between independent test cases without waiting for wall time.
            _clock.UtcNow = _clock.UtcNow.AddSeconds(3);
            return new MediaCoverService(
                Mocker.Resolve<IMediaCoverProxy>(),
                Mocker.Resolve<IImageResizer>(),
                Mocker.Resolve<IBookService>(),
                Mocker.Resolve<IAuthorRepository>(),
                Mocker.Resolve<IHttpClient>(),
                Mocker.Resolve<IDiskProvider>(),
                Mocker.Resolve<IAppFolderInfo>(),
                Mocker.Resolve<ICoverExistsSpecification>(),
                Mocker.Resolve<IConfigFileProvider>(),
                Mocker.Resolve<IEventAggregator>(),
                Mocker.Resolve<Logger>(),
                _clock);
        }

        private sealed class FixtureClock : TimeProvider
        {
            public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.UtcNow;

            public override DateTimeOffset GetUtcNow() => UtcNow;
        }
    }
}
