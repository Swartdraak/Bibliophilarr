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

        [TestCase("https://archive.org/download/olcovers1234/olcovers1234-L.zip/12345678-L.jpg")]
        [TestCase("https://archive.org/download/l_covers_0012/l_covers_0012_34.zip/0012345678-L.jpg")]
        public void should_follow_one_verified_archive_image_location_and_publish_the_decoded_cover(string location)
        {
            const string source = "https://covers.openlibrary.org/b/id/12345678-L.jpg?default=false";
            var suffix = OpenLibraryMissingBookCover.GetRetryFileSuffix("openlibrary:work:OL41W", "OL-work-OL41W", source);
            Mocker.GetMock<IAppFolderInfo>().SetupGet(folders => folders.AppDataFolder).Returns(TempFolder);
            var disk = Mocker.GetMock<IDiskProvider>();
            disk.Setup(service => service.FileExists(It.IsAny<string>())).Returns<string>(File.Exists);
            disk.Setup(service => service.GetFileSize(It.IsAny<string>())).Returns<string>(path => new FileInfo(path).Length);
            disk.Setup(service => service.OpenWriteStream(It.IsAny<string>())).Returns<string>(File.Create);
            disk.Setup(service => service.CreateFolder(It.IsAny<string>())).Callback<string>(path => Directory.CreateDirectory(path));
            disk.Setup(service => service.MoveFile(It.IsAny<string>(), It.IsAny<string>(), false)).Callback<string, string, bool>(File.Move);
            disk.Setup(service => service.DeleteFile(It.IsAny<string>())).Callback<string>(File.Delete);

            var http = Mocker.GetMock<IHttpClient>();
            var requests = new List<HttpRequest>();
            http.Setup(client => client.Get(It.IsAny<HttpRequest>())).Returns<HttpRequest>(request =>
            {
                requests.Add(request);
                request.AllowAutoRedirect.Should().BeFalse();
                request.Credentials.Should().BeNull();
                request.Cookies.Should().BeEmpty();
                if (request.Url.FullUri == source)
                {
                    return ArchiveRedirect(request, location);
                }

                request.Url.FullUri.Should().Be(location);
                using var image = new Image<Rgba32>(12, 12);
                image.Save(request.ResponseStream, new JpegEncoder());
                return new HttpResponse(request, new HttpHeader(), Array.Empty<byte>(), HttpStatusCode.OK);
            });

            var subject = CreateTimedService();
            var timeout = TimeSpan.FromSeconds(5);
            ((IOpenLibraryBookCoverCache)subject).TryRecover(4, source, suffix, () => true, timeout)
                .Should().Be(OpenLibraryCoverRecovery.Recovered);
            requests.Should().HaveCount(2);
            Assert.That(requests[1].RequestTimeout, Is.GreaterThan(TimeSpan.Zero).And.LessThanOrEqualTo(timeout));
            using var recovered = Image.Load(subject.GetCoverPath(4, MediaCoverEntity.Book, MediaCoverTypes.Cover, suffix));
            recovered.Width.Should().Be(12);
        }

        [TestCase(null)]
        [TestCase("https://archive.org/download/olcovers1234/olcovers1234-L.zip/12345679-L.jpg")]
        [TestCase("https://archive.org/download/olcovers1235/olcovers1235-L.zip/12345678-L.jpg")]
        [TestCase("https://archive.org/download/another-collection/12345678-L.jpg")]
        [TestCase("https://archive.org/download/olcovers1234/olcovers1234-L.zip/12345678-L.jpg?unexpected=1")]
        public void should_not_dispatch_an_unrecognized_archive_image_location(string location)
        {
            const string source = "https://covers.openlibrary.org/b/id/12345678-L.jpg";
            var suffix = OpenLibraryMissingBookCover.GetRetryFileSuffix("openlibrary:work:OL41W", "OL-work-OL41W", source);
            Mocker.GetMock<IAppFolderInfo>().SetupGet(folders => folders.AppDataFolder).Returns(TempFolder);
            var http = Mocker.GetMock<IHttpClient>();
            http.Setup(client => client.Get(It.IsAny<HttpRequest>())).Returns<HttpRequest>(request => ArchiveRedirect(request, location));

            ((IOpenLibraryBookCoverCache)CreateTimedService()).TryRecover(4, source, suffix, () => true, TimeSpan.FromSeconds(5))
                .Should().Be(OpenLibraryCoverRecovery.Unavailable);

            http.Verify(client => client.Get(It.IsAny<HttpRequest>()), Times.Once());
        }

        [Test]
        public void should_stop_at_a_second_archive_response_redirect()
        {
            const string source = "https://covers.openlibrary.org/b/id/12345678-L.jpg";
            const string approved = "https://archive.org/download/olcovers1234/olcovers1234-L.zip/12345678-L.jpg";
            var suffix = OpenLibraryMissingBookCover.GetRetryFileSuffix("openlibrary:work:OL41W", "OL-work-OL41W", source);
            Mocker.GetMock<IAppFolderInfo>().SetupGet(folders => folders.AppDataFolder).Returns(TempFolder);
            var http = Mocker.GetMock<IHttpClient>();
            http.Setup(client => client.Get(It.IsAny<HttpRequest>())).Returns<HttpRequest>(request => ArchiveRedirect(request, approved));

            ((IOpenLibraryBookCoverCache)CreateTimedService()).TryRecover(4, source, suffix, () => true, TimeSpan.FromSeconds(5))
                .Should().Be(OpenLibraryCoverRecovery.Unavailable);

            http.Verify(client => client.Get(It.IsAny<HttpRequest>()), Times.Exactly(2));
        }

        private static HttpResponse ArchiveRedirect(HttpRequest request, string location)
        {
            var headers = new HttpHeader();
            if (location != null)
            {
                headers.Add("Location", location);
            }

            return new HttpResponse(request, headers, Array.Empty<byte>(), HttpStatusCode.Redirect);
        }

        [TestCase(true)]
        [TestCase(false)]
        public void should_keep_only_the_current_nonempty_retry_file_after_an_author_refresh(bool populated)
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
            var http = Mocker.GetMock<IHttpClient>();
            http.Setup(client => client.Get(It.IsAny<HttpRequest>()))
                .Returns<HttpRequest>(request => new HttpResponse(request, new HttpHeader(), Array.Empty<byte>(), HttpStatusCode.OK));
            Mocker.GetMock<ICoverExistsSpecification>().Setup(service => service.AlreadyExists(It.IsAny<DateTime?>(), It.IsAny<long?>(), It.IsAny<string>()))
                .Returns(true);

            var subject = CreateTimedService();
            const int bookId = 4;
            const string workId = "openlibrary:work:OL31W";
            const string editionId = "OL-work-OL31W";
            const string coverUrl = "https://covers.openlibrary.org/b/id/31-L.jpg";
            const string staleUrl = "https://covers.openlibrary.org/b/id/32-L.jpg";
            var current = subject.GetCoverPath(
                bookId,
                MediaCoverEntity.Book,
                MediaCoverTypes.Cover,
                OpenLibraryMissingBookCover.GetRetryFileSuffix(workId, editionId, coverUrl));
            var stale = subject.GetCoverPath(
                bookId,
                MediaCoverEntity.Book,
                MediaCoverTypes.Cover,
                OpenLibraryMissingBookCover.GetRetryFileSuffix(workId, editionId, staleUrl));
            var normal = subject.GetCoverPath(bookId, MediaCoverEntity.Book, MediaCoverTypes.Cover, ".jpg");
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

            subject.HandleAsync(new AuthorRefreshCompleteEvent(author));

            File.Exists(current).Should().Be(populated);
            if (populated)
            {
                File.ReadAllText(current).Should().Be("current artwork");
            }

            File.Exists(stale).Should().BeFalse();
            File.ReadAllText(normal).Should().Be("existing normal artwork");
            http.Verify(client => client.Get(It.Is<HttpRequest>(request => request.Headers["Range"] == "bytes=0-0")), Times.Once);
            http.VerifyNoOtherCalls();
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

            var edition = new Edition
            {
                Id = 10,
                BookId = bookId,
                ForeignEditionId = editionId,
                Monitored = true,
                Images = new List<MediaCover.MediaCover> { new MediaCover.MediaCover(MediaCoverTypes.Cover, originalUrl) }
            };
            var book = new Book { Id = bookId, ForeignBookId = workId, Editions = new List<Edition> { edition } };
            var art = new MediaCover.MediaCover(MediaCoverTypes.Cover, "proxy") { RemoteUrl = originalUrl };
            subject.MapRecoveredBookCover(book, new List<MediaCover.MediaCover> { art });
            art.Url.Should().StartWith($"/MediaCover/Books/{bookId}/cover{original}?lastWrite=");
            art.Url = "fallback";
            edition.ForeignEditionId = "openlibrary:edition:OL99M";
            subject.MapRecoveredBookCover(book, new List<MediaCover.MediaCover> { art });
            edition.ForeignEditionId = editionId;
            art.Url.Should().Be("fallback");

            var normal = subject.GetCoverPath(bookId, MediaCoverEntity.Book, MediaCoverTypes.Cover, ".jpg");
            File.WriteAllText(normal, "ordinary cover sentinel");
            cache.TryRecover(bookId, changedUrl, changed, () => true, TimeSpan.FromSeconds(5))
                .Should().Be(OpenLibraryCoverRecovery.Skipped);
            subject.MapRecoveredBookCover(book, new List<MediaCover.MediaCover> { art });
            art.Url.Should().Be("fallback");
            File.ReadAllText(normal).Should().Be("ordinary cover sentinel");
            Directory.EnumerateFiles(Path.Combine(profile, "MediaCoverRetry")).Should().BeEmpty();
            http.Verify(client => client.Get(It.IsAny<HttpRequest>()), Times.Exactly(3));
            http.VerifyNoOtherCalls();
        }

        [Test]
        public void should_publish_a_verified_author_portrait_and_map_only_the_current_cold_source()
        {
            const string url = "https://covers.openlibrary.org/a/id/41-L.jpg";
            const string secondUrl = "https://covers.openlibrary.org/a/id/42-L.jpg";
            var profile = TempFolder;
            Mocker.GetMock<IAppFolderInfo>().SetupGet(folders => folders.AppDataFolder).Returns(profile);
            var disk = Mocker.GetMock<IDiskProvider>();
            disk.Setup(service => service.FileExists(It.IsAny<string>())).Returns<string>(File.Exists);
            disk.Setup(service => service.GetFileSize(It.IsAny<string>())).Returns<string>(path => new FileInfo(path).Length);
            disk.Setup(service => service.FileGetLastWrite(It.IsAny<string>())).Returns<string>(File.GetLastWriteTimeUtc);
            disk.Setup(service => service.OpenWriteStream(It.IsAny<string>())).Returns<string>(File.Create);
            disk.Setup(service => service.CreateFolder(It.IsAny<string>())).Callback<string>(path => Directory.CreateDirectory(path));
            disk.Setup(service => service.MoveFile(It.IsAny<string>(), It.IsAny<string>(), false)).Callback<string, string, bool>(File.Move);
            disk.Setup(service => service.DeleteFile(It.IsAny<string>())).Callback<string>(File.Delete);

            var poster = new MediaCover.MediaCover(MediaCoverTypes.Poster, url) { SourceProvider = "OpenLibrary", SourceId = "OL41A" };
            var author = new Author
            {
                Id = 4,
                AuthorMetadataId = 11,
                Metadata = new AuthorMetadata
                {
                    Id = 11,
                    CanonicalId = "112233445566478899aabbccddeeff01",
                    ForeignAuthorId = "openlibrary:author:OL41A",
                    Images = new List<MediaCover.MediaCover> { poster }
                }
            };
            var identity = Mocker.GetMock<ICatalogueIdentityRepository>();
            var ownership = new List<CatalogueSourceAssociation>
            {
                new CatalogueSourceAssociation { Provider = "OpenLibrary", ProviderId = "OL41A", Evidence = "primary-provider-id" }
            };
            identity.Setup(repository => repository.GetSources(CatalogueEntityKind.Author, 11)).Returns(ownership);
            var subject = CreateTimedService();
            var cache = (IOpenLibraryAuthorPortraitCache)subject;
            var descriptor = OpenLibraryMissingAuthorPortrait.SelectArtwork(author).Value;
            var path = subject.GetCoverPath(author.Id, MediaCoverEntity.Author, MediaCoverTypes.Poster, descriptor.FileSuffix);
            var http = Mocker.GetMock<IHttpClient>();
            http.Setup(client => client.Get(It.IsAny<HttpRequest>())).Returns<HttpRequest>(request =>
            {
                request.AllowAutoRedirect.Should().BeFalse();
                request.RequestTimeout.Should().Be(TimeSpan.FromSeconds(5));
                var width = request.Url.FullUri == secondUrl ? 1 : 192;
                var height = request.Url.FullUri == secondUrl ? 1 : 256;
                using var image = new Image<Rgba32>(width, height);
                image.Save(request.ResponseStream, new JpegEncoder());
                return new HttpResponse(request, new HttpHeader(), Array.Empty<byte>(), HttpStatusCode.OK);
            });

            var reads = 0;
            cache.TryRecover(author.Id, url, descriptor.FileSuffix, () => ++reads == 1, TimeSpan.FromSeconds(5))
                .Should().Be(OpenLibraryCoverRecovery.Skipped);
            File.Exists(path).Should().BeFalse();

            poster.Url = secondUrl;
            var placeholder = OpenLibraryMissingAuthorPortrait.SelectArtwork(author).Value;
            cache.TryRecover(author.Id, secondUrl, placeholder.FileSuffix, () => true, TimeSpan.FromSeconds(5))
                .Should().Be(OpenLibraryCoverRecovery.Unavailable);
            poster.Url = url;

            cache.TryRecover(author.Id, url, descriptor.FileSuffix, () => true, TimeSpan.FromSeconds(5))
                .Should().Be(OpenLibraryCoverRecovery.Recovered);
            using (var image = Image.Load(path))
            {
                image.Width.Should().Be(192);
                image.Height.Should().Be(256);
            }

            var mapped = new MediaCover.MediaCover(MediaCoverTypes.Poster, "proxy")
            {
                RemoteUrl = url,
                SourceProvider = "OpenLibrary",
                SourceId = "openlibrary:author:OL41A"
            };
            subject.MapRecoveredAuthorPortrait(author, new[] { mapped });
            mapped.Url.Should().StartWith($"/MediaCover/{author.Id}/poster{descriptor.FileSuffix}?lastWrite=");

            mapped.Url = "proxy";
            ownership[0].Evidence = "legacy-linked-id";
            subject.MapRecoveredAuthorPortrait(author, new[] { mapped });
            mapped.Url.Should().Be("proxy");
            ownership[0].Evidence = "primary-provider-id";
            poster.Url = secondUrl;
            subject.MapRecoveredAuthorPortrait(author, new[] { mapped });
            mapped.Url.Should().Be("proxy");
            poster.Url = url;

            var normal = subject.GetCoverPath(author.Id, MediaCoverEntity.Author, MediaCoverTypes.Poster, ".jpg");
            File.WriteAllText(normal, "normal portrait sentinel");
            cache.IsMissing(author.Id, placeholder.FileSuffix).Should().BeFalse();
            subject.MapRecoveredAuthorPortrait(author, new[] { mapped });
            mapped.Url.Should().Be("proxy");
            File.ReadAllText(normal).Should().Be("normal portrait sentinel");
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
                _clock,
                Mocker.Resolve<ICatalogueIdentityRepository>());
        }

        private sealed class FixtureClock : TimeProvider
        {
            public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.UtcNow;

            public override DateTimeOffset GetUtcNow() => UtcNow;
        }
    }
}
