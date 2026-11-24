using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Abstractions;
using System.Linq;
using System.Net;
using FizzWare.NBuilder;
using FluentAssertions;
using Moq;
using NLog;
using NLog.Config;
using NLog.Targets;
using NUnit.Framework;
using NzbDrone.Common.Cache;
using NzbDrone.Common.Disk;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Common.Http;
using NzbDrone.Core.Books;
using NzbDrone.Core.Books.Events;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.MediaCover;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.MediaCoverTests
{
    [TestFixture]
    public class MediaCoverServiceFixture : CoreTest<MediaCoverService>
    {
        private Author _author;
        private Book _book;
        private Edition _edition;
        private HttpResponse _httpResponse;

        [SetUp]
        public void Setup()
        {
            Mocker.SetConstant<IAppFolderInfo>(new AppFolderInfo(Mocker.Resolve<IStartupContext>()));

            _author = Builder<Author>.CreateNew()
                .With(v => v.Id = 2)
                .With(v => v.Metadata.Value.Images = new List<MediaCover.MediaCover> { new MediaCover.MediaCover(MediaCoverTypes.Poster, "") })
                .Build();

            _edition = Builder<Edition>.CreateNew()
                .With(v => v.Id = 8)
                .With(v => v.Images = new List<MediaCover.MediaCover> { new MediaCover.MediaCover(MediaCoverTypes.Cover, "") })
                .With(v => v.Monitored = true)
                .Build();

            _book = Builder<Book>.CreateNew()
                .With(v => v.Id = 4)
                .With(v => v.Editions = new List<Edition> { _edition })
                .Build();

            _httpResponse = new HttpResponse(null, new HttpHeader(), "");
            Mocker.GetMock<IHttpClient>().Setup(c => c.Get(It.IsAny<HttpRequest>())).Returns(_httpResponse);
        }

        [TestCase(HttpStatusCode.TooManyRequests, false)]
        [TestCase(HttpStatusCode.ServiceUnavailable, true)]
        public void should_honor_a_longer_retry_after_without_adding_an_artwork_request(HttpStatusCode status, bool bodyFailure)
        {
            var url = UseNewOpenLibraryCover();
            var clock = new FixtureClock();
            var retryAt = clock.UtcNow.AddMinutes(3);
            var failure = CreateCoverHttpFailure(url, status, bodyFailure ? retryAt.ToString("r", CultureInfo.InvariantCulture) : "180");
            var http = Mocker.GetMock<IHttpClient>();

            if (bodyFailure)
            {
                http.SetupSequence(client => client.DownloadFile(url, It.IsAny<string>(), It.IsAny<string>())).Throws(failure).Pass();
            }
            else
            {
                http.SetupSequence(client => client.Get(It.IsAny<HttpRequest>())).Throws(failure).Returns(_httpResponse);
            }

            using var logs = CaptureCooldownDiagnostics(out var messages);
            var subject = CreateService(clock);

            subject.EnsureBookCovers(_book);
            subject.EnsureBookCovers(_book);

            var diagnostic = messages.Logs.Single(message => message.Contains("host cooldown active until", StringComparison.Ordinal));
            var deadline = diagnostic.Split("host cooldown active until ")[1];
            DateTimeOffset.Parse(deadline, CultureInfo.InvariantCulture).Should().Be(retryAt);

            clock.UtcNow = retryAt.AddMilliseconds(-1);
            subject.EnsureBookCovers(_book);
            http.Verify(client => client.Get(It.IsAny<HttpRequest>()), Times.Once);

            clock.UtcNow = retryAt;
            subject.EnsureBookCovers(_book);
            http.Verify(client => client.Get(It.IsAny<HttpRequest>()), Times.Exactly(2));
            http.Verify(client => client.DownloadFile(url, It.IsAny<string>(), It.IsAny<string>()), bodyFailure ? Times.Exactly(2) : Times.Once());
            http.VerifyNoOtherCalls();
        }

        [TestCase(HttpStatusCode.TooManyRequests, null, 19)]
        [TestCase(HttpStatusCode.ServiceUnavailable, "not an HTTP date", 12)]
        [TestCase(HttpStatusCode.TooManyRequests, "1", 19)]
        [TestCase(HttpStatusCode.ServiceUnavailable, "Thu, 01 Jan 2004 00:00:00 GMT", 12)]
        [TestCase(HttpStatusCode.RequestTimeout, "180", 12)]
        public void should_keep_the_local_cooldown_for_missing_invalid_or_shorter_retry_after(
            HttpStatusCode status, string retryAfter, int fallbackSeconds)
        {
            var url = UseNewOpenLibraryCover();
            var clock = new FixtureClock();
            var failure = CreateCoverHttpFailure(url, status, retryAfter);
            var http = Mocker.GetMock<IHttpClient>();
            http.SetupSequence(client => client.Get(It.IsAny<HttpRequest>())).Throws(failure).Returns(_httpResponse);
            using var logs = CaptureCooldownDiagnostics(out _);
            var subject = CreateService(clock);

            subject.EnsureBookCovers(_book);
            clock.UtcNow = clock.UtcNow.AddSeconds(fallbackSeconds).AddMilliseconds(-1);
            subject.EnsureBookCovers(_book);
            http.Verify(client => client.Get(It.IsAny<HttpRequest>()), Times.Once);

            clock.UtcNow = clock.UtcNow.AddMilliseconds(402);
            subject.EnsureBookCovers(_book);
            http.Verify(client => client.Get(It.IsAny<HttpRequest>()), Times.Exactly(2));
            http.Verify(client => client.DownloadFile(url, It.IsAny<string>(), It.IsAny<string>()), Times.Once);
            http.VerifyNoOtherCalls();
        }

        [Test]
        public void should_keep_the_four_request_burst_and_resume_when_the_cover_budget_refills()
        {
            UseNewOpenLibraryCover();
            var clock = new FixtureClock();
            var http = Mocker.GetMock<IHttpClient>();
            Mocker.GetMock<ICoverExistsSpecification>()
                .Setup(specification => specification.AlreadyExists(It.IsAny<DateTime?>(), It.IsAny<long?>(), It.IsAny<string>()))
                .Returns(true);
            var subject = CreateService(clock);

            for (var i = 0; i < 5; i++)
            {
                subject.EnsureBookCovers(_book);
            }

            http.Verify(client => client.Get(It.IsAny<HttpRequest>()), Times.Exactly(4));
            clock.UtcNow = clock.UtcNow.AddMilliseconds(899);
            subject.EnsureBookCovers(_book);
            http.Verify(client => client.Get(It.IsAny<HttpRequest>()), Times.Exactly(4));

            clock.UtcNow = clock.UtcNow.AddMilliseconds(402);
            subject.EnsureBookCovers(_book);
            http.Verify(client => client.Get(It.IsAny<HttpRequest>()), Times.Exactly(5));
            http.VerifyNoOtherCalls();
        }

        [Test]
        public void should_not_shorten_a_longer_retry_after_when_an_in_flight_request_fails()
        {
            var url = UseNewOpenLibraryCover();
            var alternate = url.Replace("1-L.jpg", "2-L.jpg", StringComparison.Ordinal);
            var otherBook = new Book
            {
                Id = _book.Id + 1,
                Editions = new List<Edition>
                {
                    new Edition
                    {
                        Monitored = true,
                        Images = new List<MediaCover.MediaCover> { new MediaCover.MediaCover(MediaCoverTypes.Cover, alternate) }
                    }
                }
            };
            var clock = new FixtureClock();
            var http = Mocker.GetMock<IHttpClient>();
            using var logs = CaptureCooldownDiagnostics(out _);
            var subject = CreateService(clock);
            var shortFailure = CreateCoverHttpFailure(url, HttpStatusCode.ServiceUnavailable, "1");
            http.Setup(client => client.Get(It.Is<HttpRequest>(request => request.Url.FullUri == url)))
                .Returns(() =>
                {
                    subject.EnsureBookCovers(otherBook);
                    throw shortFailure;
                });
            http.Setup(client => client.Get(It.Is<HttpRequest>(request => request.Url.FullUri == alternate)))
                .Throws(CreateCoverHttpFailure(alternate, HttpStatusCode.TooManyRequests, "180"));

            subject.EnsureBookCovers(_book);
            clock.UtcNow = clock.UtcNow.AddMinutes(1);
            subject.EnsureBookCovers(_book);

            http.Verify(client => client.Get(It.Is<HttpRequest>(request => request.Url.FullUri == url)), Times.Once);
            http.Verify(client => client.Get(It.Is<HttpRequest>(request => request.Url.FullUri == alternate)), Times.Once);
            http.VerifyNoOtherCalls();
        }

        private string UseNewOpenLibraryCover()
        {
            var url = $"https://{Guid.NewGuid():N}.covers.openlibrary.org/b/id/1-L.jpg";
            _edition.Images[0].Url = url;
            return url;
        }

        private static HttpException CreateCoverHttpFailure(string url, HttpStatusCode status, string retryAfter)
        {
            var request = new HttpRequest(url);
            var headers = new HttpHeader();
            if (retryAfter != null)
            {
                headers["Retry-After"] = retryAfter;
            }

            var response = new HttpResponse(request, headers, string.Empty, status);
            return status == HttpStatusCode.TooManyRequests
                ? new TooManyRequestsException(request, response)
                : new HttpException(request, response);
        }

        private LogFactory CaptureCooldownDiagnostics(out MemoryTarget messages)
        {
            messages = new MemoryTarget { Layout = "${message}" };
            var configuration = new LoggingConfiguration();
            configuration.AddRule(LogLevel.Debug, LogLevel.Debug, messages);
            var logFactory = new LogFactory { Configuration = configuration };
            Mocker.SetConstant(logFactory.GetLogger(nameof(MediaCoverService)));
            return logFactory;
        }

        private MediaCoverService CreateService(TimeProvider clock)
        {
            return new MediaCoverService(
                Mocker.Resolve<IMediaCoverProxy>(),
                Mocker.Resolve<IImageResizer>(),
                Mocker.Resolve<IBookService>(),
                Mocker.Resolve<IHttpClient>(),
                Mocker.Resolve<IDiskProvider>(),
                Mocker.Resolve<IAppFolderInfo>(),
                Mocker.Resolve<ICoverExistsSpecification>(),
                Mocker.Resolve<IConfigFileProvider>(),
                Mocker.Resolve<IEventAggregator>(),
                Mocker.Resolve<Logger>(),
                clock);
        }

        private sealed class FixtureClock : TimeProvider
        {
            public DateTimeOffset UtcNow { get; set; } = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

            public override DateTimeOffset GetUtcNow() => UtcNow;
        }

        [TestCase(".png")]
        [TestCase(".jpg")]
        public void should_convert_cover_urls_to_local(string extension)
        {
            var covers = new List<MediaCover.MediaCover>
                {
                    new MediaCover.MediaCover
                    {
                        Url = "http://dummy.com/test" + extension,
                        CoverType = MediaCoverTypes.Banner
                    }
                };

            Mocker.GetMock<IDiskProvider>().Setup(c => c.FileGetLastWrite(It.IsAny<string>()))
                  .Returns(new DateTime(1234));

            Mocker.GetMock<IDiskProvider>().Setup(c => c.FileExists(It.IsAny<string>()))
                  .Returns(true);

            Mocker.GetMock<IDiskProvider>().Setup(c => c.GetFileSize(It.IsAny<string>()))
                  .Returns(1000);

            Subject.ConvertToLocalUrls(12, MediaCoverEntity.Author, covers);

            covers.Single().Url.Should().Be("/MediaCover/12/banner" + extension + "?lastWrite=1234");
        }

        [TestCase(".png")]
        [TestCase(".jpg")]
        public void convert_to_local_url_should_not_change_extension(string extension)
        {
            var covers = new List<MediaCover.MediaCover>
                {
                    new MediaCover.MediaCover
                    {
                        Url = "http://dummy.com/test" + extension,
                        CoverType = MediaCoverTypes.Banner
                    }
                };

            Mocker.GetMock<IDiskProvider>().Setup(c => c.FileGetLastWrite(It.IsAny<string>()))
                  .Returns(new DateTime(1234));

            Mocker.GetMock<IDiskProvider>().Setup(c => c.FileExists(It.IsAny<string>()))
                  .Returns(true);

            Mocker.GetMock<IDiskProvider>().Setup(c => c.GetFileSize(It.IsAny<string>()))
                  .Returns(1000);

            Subject.ConvertToLocalUrls(12, MediaCoverEntity.Author, covers);

            covers.Single().Extension.Should().Be(extension);
        }

        [TestCase(".png")]
        [TestCase(".jpg")]
        public void should_convert_book_cover_urls_to_local(string extension)
        {
            var covers = new List<MediaCover.MediaCover>
                {
                    new MediaCover.MediaCover
                    {
                        Url = "http://dummy.com/test" + extension,
                        CoverType = MediaCoverTypes.Disc
                    }
                };

            Mocker.GetMock<IDiskProvider>().Setup(c => c.FileGetLastWrite(It.IsAny<string>()))
                  .Returns(new DateTime(1234));

            Mocker.GetMock<IDiskProvider>().Setup(c => c.FileExists(It.IsAny<string>()))
                  .Returns(true);

            Mocker.GetMock<IDiskProvider>().Setup(c => c.GetFileSize(It.IsAny<string>()))
                  .Returns(1000);

            Subject.ConvertToLocalUrls(6, MediaCoverEntity.Book, covers);

            covers.Single().Url.Should().Be("/MediaCover/Books/6/disc" + extension + "?lastWrite=1234");
        }

        [TestCase(MediaCoverEntity.Author, false)]
        [TestCase(MediaCoverEntity.Book, false)]
        [TestCase(MediaCoverEntity.Author, true)]
        [TestCase(MediaCoverEntity.Book, true)]
        public void should_proxy_the_origin_when_the_cached_image_is_empty_or_disappears(MediaCoverEntity entity, bool disappeared)
        {
            const string origin = "https://covers.example/art.jpg";
            const string proxyUrl = "/MediaCoverProxy/test-hash/image.jpg";
            var type = entity == MediaCoverEntity.Author ? MediaCoverTypes.Poster : MediaCoverTypes.Cover;
            var cover = new MediaCover.MediaCover(type, origin);
            var disk = Mocker.GetMock<IDiskProvider>();
            disk.Setup(provider => provider.FileExists(It.IsAny<string>())).Returns(true);
            var length = disk.Setup(provider => provider.GetFileSize(It.IsAny<string>()));
            if (disappeared)
            {
                length.Throws(new FileNotFoundException());
            }
            else
            {
                length.Returns(0);
            }

            Mocker.GetMock<IMediaCoverProxy>().Setup(proxy => proxy.RegisterUrl(origin)).Returns(proxyUrl);

            Subject.ConvertToLocalUrls(12, entity, new[] { cover });

            cover.Url.Should().Be(proxyUrl);
            cover.RemoteUrl.Should().Be(origin);
            disk.Verify(provider => provider.FileGetLastWrite(It.IsAny<string>()), Times.Never());
        }

        [Test]
        public void should_keep_the_local_placeholder_when_the_empty_cache_has_no_origin()
        {
            var cover = new MediaCover.MediaCover(MediaCoverTypes.Poster, string.Empty);
            var disk = Mocker.GetMock<IDiskProvider>();
            disk.Setup(provider => provider.FileExists(It.IsAny<string>())).Returns(true);
            disk.Setup(provider => provider.GetFileSize(It.IsAny<string>())).Returns(0);

            Subject.ConvertToLocalUrls(12, MediaCoverEntity.Author, new[] { cover });

            cover.Url.Should().Be("/MediaCover/12/poster");
            cover.RemoteUrl.Should().BeEmpty();
            Mocker.GetMock<IMediaCoverProxy>().Verify(proxy => proxy.RegisterUrl(It.IsAny<string>()), Times.Never());
        }

        [TestCase(".png")]
        [TestCase(".jpg")]
        public void should_convert_media_urls_to_local_without_time_if_file_doesnt_exist(string extension)
        {
            var covers = new List<MediaCover.MediaCover>
                {
                    new MediaCover.MediaCover
                    {
                        Url = "http://dummy.com/test" + extension,
                        CoverType = MediaCoverTypes.Banner
                    }
                };

            var path = Path.Combine(TestContext.CurrentContext.TestDirectory, "Files", "Media", "NonExistant.mp4");
            var fileInfo = new FileInfo(path);

            Mocker.GetMock<IDiskProvider>()
                .Setup(c => c.GetFileInfo(It.IsAny<string>()))
                .Returns((FileInfoBase)fileInfo);

            var proxyUrl = "/MediaCoverProxy/test-hash/banner" + extension;
            Mocker.GetMock<IMediaCoverProxy>()
                .Setup(c => c.RegisterUrl("http://dummy.com/test" + extension))
                .Returns(proxyUrl);

            Subject.ConvertToLocalUrls(12, MediaCoverEntity.Author, covers);

            covers.Single().Url.Should().Be(proxyUrl);
        }

        [TestCase("https://books.google.example/content?id=ABC&img=1", ".jpg")]
        [TestCase("https://covers.example/art.PNG?signature=xyz.jpg", ".PNG")]
        public void should_use_a_supported_filename_for_provider_image_urls(string url, string extension)
        {
            var cover = new MediaCover.MediaCover(MediaCoverTypes.Cover, url);
            var proxy = new MediaCoverProxy(Mocker.Resolve<IHttpClient>(), Mocker.Resolve<IConfigFileProvider>(), new CacheManager());

            cover.Extension.Should().Be(extension);
            var localUrl = proxy.RegisterUrl(url);

            localUrl.Should().EndWith("/image" + extension.ToLowerInvariant());
            proxy.GetUrl(localUrl.Split('/')[^2]).Should().Be(url);
        }

        [TestCase(true, true)]
        [TestCase(false, false)]
        public void should_cache_preferred_book_cover_regardless_of_number_of_monitored_editions(bool primaryMonitored, bool otherMonitored)
        {
            const string preferredCover = "https://covers.example/preferred.jpg";
            _edition.Monitored = primaryMonitored;
            _edition.Images = new List<MediaCover.MediaCover> { new MediaCover.MediaCover(MediaCoverTypes.Cover, preferredCover) };
            _book.Editions.Value.Add(new Edition
            {
                Monitored = otherMonitored,
                Images = new List<MediaCover.MediaCover> { new MediaCover.MediaCover(MediaCoverTypes.Cover, "https://covers.example/other.jpg") }
            });

            Subject.EnsureBookCovers(_book);

            Mocker.GetMock<IHttpClient>()
                .Verify(client => client.DownloadFile(preferredCover, It.IsAny<string>(), It.IsAny<string>()), Times.Once());
        }

        [Test]
        public void should_cache_book_cover_from_another_edition_of_the_same_work_when_preferred_has_none()
        {
            const string availableCover = "https://covers.example/other.jpg";
            _edition.Images.Clear();
            _book.Editions.Value.Add(new Edition
            {
                Images = new List<MediaCover.MediaCover> { new MediaCover.MediaCover(MediaCoverTypes.Cover, availableCover) }
            });

            Subject.EnsureBookCovers(_book);

            Mocker.GetMock<IHttpClient>()
                .Verify(client => client.DownloadFile(availableCover, It.IsAny<string>(), It.IsAny<string>()), Times.Once());
        }

        [TestCase(MediaCoverEntity.Author, HttpStatusCode.NotFound, false, true, true, ".jpg", false, false)]
        [TestCase(MediaCoverEntity.Book, HttpStatusCode.NotFound, false, true, true, ".jpg", false, false)]
        [TestCase(MediaCoverEntity.Book, HttpStatusCode.Gone, false, true, true, ".jpg", false, false)]
        [TestCase(MediaCoverEntity.Book, HttpStatusCode.NotFound, true, false, true, ".jpg", false, false)]
        [TestCase(MediaCoverEntity.Book, HttpStatusCode.TooManyRequests, false, false, true, ".jpg", false, false)]
        [TestCase(MediaCoverEntity.Book, HttpStatusCode.RequestTimeout, false, false, true, ".jpg", false, false)]
        [TestCase(MediaCoverEntity.Book, HttpStatusCode.ServiceUnavailable, false, false, true, ".jpg", false, false)]
        [TestCase(MediaCoverEntity.Book, HttpStatusCode.NotFound, false, false, false, ".jpg", false, false)]
        [TestCase(MediaCoverEntity.Author, HttpStatusCode.NotFound, false, false, false, ".jpg", false, false)]
        [TestCase(MediaCoverEntity.Book, HttpStatusCode.NotFound, false, false, true, ".svg", false, false)]
        [TestCase(MediaCoverEntity.Book, HttpStatusCode.NotFound, false, true, true, ".jpg", true, false)]
        [TestCase(MediaCoverEntity.Book, HttpStatusCode.NotFound, false, false, true, ".jpg", true, true)]
        [TestCase(MediaCoverEntity.Book, HttpStatusCode.NotFound, false, false, false, ".jpg", true, false)]
        public void should_cache_a_source_bound_secondary_only_after_definitive_absence(
            MediaCoverEntity entity,
            HttpStatusCode status,
            bool existingCache,
            bool expectSecondary,
            bool verified,
            string extension,
            bool usesSourceEdition,
            bool differentOwner)
        {
            const string primaryUrl = "https://covers.example/old.jpg";
            var secondaryUrl = "https://covers.example/verified" + extension;
            var entityId = entity == MediaCoverEntity.Author ? _author.Id : _book.Id;
            var coverType = entity == MediaCoverEntity.Author ? MediaCoverTypes.Poster : MediaCoverTypes.Cover;
            var primary = new MediaCover.MediaCover(coverType, primaryUrl) { SourceProvider = "Hardcover" };
            var secondary = new MediaCover.MediaCover(coverType, secondaryUrl) { SourceProvider = "Hardcover" };

            if (entity == MediaCoverEntity.Author)
            {
                _author.Metadata.Value.Id = 3;
                _author.Metadata.Value.ForeignAuthorId = "hardcover:author:701";
                primary.SourceId = _author.Metadata.Value.ForeignAuthorId;
                secondary.SourceId = primary.SourceId;
                _author.Metadata.Value.Images = new List<MediaCover.MediaCover> { primary, secondary };
                Mocker.GetMock<IBookService>().Setup(service => service.GetBooksByAuthor(_author.Id)).Returns(new List<Book>());
            }
            else
            {
                _edition.BookId = _book.Id;
                _edition.ForeignEditionId = "hardcover:edition:801";
                primary.SourceId = _edition.ForeignEditionId;
                _edition.Images = new List<MediaCover.MediaCover> { primary };
                secondary.SourceId = usesSourceEdition ? "hardcover:source-edition:802" : "hardcover:edition:802";
                _book.Editions.Value.Add(new Edition
                {
                    Id = 9,
                    BookId = differentOwner ? _book.Id + 1 : _book.Id,
                    ForeignEditionId = secondary.SourceId,
                    Images = new List<MediaCover.MediaCover> { secondary }
                });
            }

            if (!verified)
            {
                secondary.SourceId = (entity, usesSourceEdition) switch
                {
                    (MediaCoverEntity.Author, _) => "hardcover:author:702",
                    (MediaCoverEntity.Book, true) => "hardcover:edition:802",
                    _ => "hardcover:edition:803"
                };
            }

            var originalSource = secondary.SourceId;
            var request = new HttpRequest(primaryUrl);
            var failure = new HttpException(new HttpResponse(request, new HttpHeader(), string.Empty, status));
            var client = Mocker.GetMock<IHttpClient>();
            client.Setup(value => value.Get(It.Is<HttpRequest>(candidate => candidate.Url.FullUri == primaryUrl))).Throws(failure);

            var primaryPath = Subject.GetCoverPath(entityId, entity, coverType, ".jpg");
            var directory = Path.GetDirectoryName(primaryPath);
            var stale = Path.Combine(directory, $"{coverType.ToString().ToLowerInvariant()}.source-{new string('a', 64)}.jpg");
            var unrelated = Path.Combine(directory, "unrelated.jpg");
            var cachedPaths = new HashSet<string>();
            var disk = Mocker.GetMock<IDiskProvider>();
            disk.Setup(value => value.FileExists(It.IsAny<string>()))
                .Returns((string path) => (existingCache && path == primaryPath) || cachedPaths.Contains(path));
            disk.Setup(value => value.FolderExists(directory)).Returns(true);
            disk.Setup(value => value.GetFiles(directory, true)).Returns(Array.Empty<string>());
            disk.Setup(value => value.GetFiles(directory, false))
                .Returns(() => cachedPaths.Concat(new[] { stale, unrelated, primaryPath }).ToArray());
            disk.Setup(value => value.GetFileSize(It.IsAny<string>())).Returns(1024);
            disk.Setup(value => value.FileGetLastWrite(It.IsAny<string>())).Returns(new DateTime(2026, 1, 1));
            client.Setup(value => value.DownloadFile(secondaryUrl, It.IsAny<string>(), It.IsAny<string>()))
                .Callback<string, string, string>((_, path, _) => cachedPaths.Add(path));

            if (entity == MediaCoverEntity.Author)
            {
                Subject.HandleAsync(new AuthorRefreshCompleteEvent(_author));
            }
            else
            {
                Subject.EnsureBookCovers(_book);
            }

            ExceptionVerification.ExpectedWarns(1);
            client.Verify(value => value.DownloadFile(secondaryUrl, It.IsAny<string>(), It.IsAny<string>()),
                expectSecondary ? Times.Once() : Times.Never());
            cachedPaths.Should().NotContain(primaryPath);
            disk.Verify(value => value.DeleteFile(primaryPath), Times.Never());
            disk.Verify(value => value.DeleteFile(unrelated), Times.Never());
            disk.Verify(value => value.DeleteFile(stale), Times.Once());

            if (expectSecondary)
            {
                Subject.ConvertToLocalUrls(entityId, entity, new[] { primary, secondary });
                secondary.Url.Should().Contain("/MediaCover/").And.Contain("?lastWrite=");
                secondary.RemoteUrl.Should().Be(secondaryUrl);
                secondary.SourceId.Should().Be(originalSource);
                primary.Url.Should().NotBe(secondary.Url);
            }
        }

        [Test]
        public void should_preserve_cached_files_when_refresh_has_no_image_references()
        {
            _author.Metadata.Value.Images.Clear();
            _edition.Images.Clear();
            Mocker.GetMock<IBookService>().Setup(service => service.GetBooksByAuthor(_author.Id)).Returns(new List<Book> { _book });
            var disk = Mocker.GetMock<IDiskProvider>();
            disk.Setup(value => value.FolderExists(It.IsAny<string>())).Returns(true);
            disk.Setup(value => value.GetFileSize(It.IsAny<string>())).Returns(1024);
            disk.Setup(value => value.GetFiles(It.IsAny<string>(), It.IsAny<bool>()))
                .Returns((string folder, bool _) => new[] { Path.Combine(folder, "cover.jpg"), Path.Combine(folder, "poster.jpg") });

            Subject.HandleAsync(new AuthorRefreshCompleteEvent(_author));

            disk.Verify(value => value.DeleteFile(It.IsAny<string>()), Times.Never());
        }

        [Test]
        public void should_resize_covers_if_main_downloaded()
        {
            Mocker.GetMock<ICoverExistsSpecification>()
                  .Setup(v => v.AlreadyExists(It.IsAny<DateTime?>(), It.IsAny<long?>(), It.IsAny<string>()))
                  .Returns(false);

            Mocker.GetMock<IBookService>()
                  .Setup(v => v.GetBooksByAuthor(It.IsAny<int>()))
                  .Returns(new List<Book> { _book });

            Mocker.GetMock<IDiskProvider>()
                  .Setup(v => v.FileExists(It.IsAny<string>()))
                  .Returns(true);

            Subject.HandleAsync(new AuthorRefreshCompleteEvent(_author));

            Mocker.GetMock<IImageResizer>()
                  .Verify(v => v.Resize(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>()), Times.Exactly(2));
        }

        [Test]
        public void should_resize_covers_if_missing()
        {
            Mocker.GetMock<ICoverExistsSpecification>()
                  .Setup(v => v.AlreadyExists(It.IsAny<DateTime?>(), It.IsAny<long?>(), It.IsAny<string>()))
                  .Returns(true);

            Mocker.GetMock<IBookService>()
                  .Setup(v => v.GetBooksByAuthor(It.IsAny<int>()))
                  .Returns(new List<Book> { _book });

            Mocker.GetMock<IDiskProvider>()
                  .Setup(v => v.FileExists(It.IsAny<string>()))
                  .Returns(false);

            Subject.HandleAsync(new AuthorRefreshCompleteEvent(_author));

            Mocker.GetMock<IImageResizer>()
                  .Verify(v => v.Resize(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>()), Times.Exactly(2));
        }

        [Test]
        public void should_not_resize_covers_if_exists()
        {
            Mocker.GetMock<ICoverExistsSpecification>()
                  .Setup(v => v.AlreadyExists(It.IsAny<DateTime?>(), It.IsAny<long?>(), It.IsAny<string>()))
                  .Returns(true);

            Mocker.GetMock<IDiskProvider>()
                  .Setup(v => v.FileExists(It.IsAny<string>()))
                  .Returns(true);

            Mocker.GetMock<IBookService>()
                  .Setup(v => v.GetBooksByAuthor(It.IsAny<int>()))
                  .Returns(new List<Book> { _book });

            Mocker.GetMock<IDiskProvider>()
                  .Setup(v => v.GetFileSize(It.IsAny<string>()))
                  .Returns(1000);

            Subject.HandleAsync(new AuthorRefreshCompleteEvent(_author));

            Mocker.GetMock<IImageResizer>()
                  .Verify(v => v.Resize(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>()), Times.Never());
        }

        [Test]
        public void should_resize_covers_if_existing_is_empty()
        {
            Mocker.GetMock<ICoverExistsSpecification>()
                  .Setup(v => v.AlreadyExists(It.IsAny<DateTime?>(), It.IsAny<long?>(), It.IsAny<string>()))
                  .Returns(true);

            Mocker.GetMock<IDiskProvider>()
                  .Setup(v => v.FileExists(It.IsAny<string>()))
                  .Returns(true);

            Mocker.GetMock<IBookService>()
                  .Setup(v => v.GetBooksByAuthor(It.IsAny<int>()))
                  .Returns(new List<Book> { _book });

            Mocker.GetMock<IDiskProvider>()
                  .Setup(v => v.GetFileSize(It.IsAny<string>()))
                  .Returns(0);

            Subject.HandleAsync(new AuthorRefreshCompleteEvent(_author));

            Mocker.GetMock<IImageResizer>()
                  .Verify(v => v.Resize(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>()), Times.Exactly(2));
        }

        [Test]
        public void should_log_error_if_resize_failed()
        {
            Mocker.GetMock<ICoverExistsSpecification>()
                  .Setup(v => v.AlreadyExists(It.IsAny<DateTime?>(), It.IsAny<long?>(), It.IsAny<string>()))
                  .Returns(true);

            Mocker.GetMock<IDiskProvider>()
                  .Setup(v => v.FileExists(It.IsAny<string>()))
                  .Returns(false);

            Mocker.GetMock<IBookService>()
                  .Setup(v => v.GetBooksByAuthor(It.IsAny<int>()))
                  .Returns(new List<Book> { _book });

            Mocker.GetMock<IImageResizer>()
                  .Setup(v => v.Resize(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>()))
                  .Throws<ApplicationException>();

            Subject.HandleAsync(new AuthorRefreshCompleteEvent(_author));

            Mocker.GetMock<IImageResizer>()
                  .Verify(v => v.Resize(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>()), Times.Exactly(2));
        }
    }
}
