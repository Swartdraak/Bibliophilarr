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
using NzbDrone.Common.Disk;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Common.Http;
using NzbDrone.Core.Books;
using NzbDrone.Core.Books.Events;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.MediaCover;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Test.Framework;

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
            Mocker.GetMock<IAuthorRepository>().Setup(repository => repository.Find(_author.Id)).Returns(_author);

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
                Mocker.Resolve<IAuthorRepository>(),
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

            Subject.ConvertToLocalUrls(6, MediaCoverEntity.Book, covers);

            covers.Single().Url.Should().Be("/MediaCover/Books/6/disc" + extension + "?lastWrite=1234");
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

        [Test]
        public void should_not_download_a_poster_from_a_delayed_refresh_event()
        {
            const string oldUrl = "https://example.invalid/old.jpg";
            const string currentUrl = "https://example.invalid/current.jpg";
            var current = CurrentAuthorWithPoster(oldUrl, currentUrl);
            var http = Mocker.GetMock<IHttpClient>();
            var expected = Subject.GetCoverPath(current.Id, MediaCoverEntity.Author, MediaCoverTypes.Poster, ".jpg");

            Subject.HandleAsync(new AuthorRefreshCompleteEvent(_author));

            http.Verify(client => client.Get(It.Is<HttpRequest>(request => request.Url.ToString() == oldUrl)), Times.Never());
            http.Verify(client => client.DownloadFile(oldUrl, expected, It.IsAny<string>()), Times.Never());
            http.Verify(client => client.DownloadFile(currentUrl, expected, It.IsAny<string>()), Times.Once());
            Mocker.GetMock<IEventAggregator>().Verify(bus => bus.PublishEvent(
                It.Is<MediaCoversUpdatedEvent>(message => message.Author == current)), Times.Once());
        }

        [Test]
        public void should_keep_the_current_poster_when_an_old_refresh_had_a_different_extension()
        {
            var current = CurrentAuthorWithPoster("https://example.invalid/old.jpg", "https://example.invalid/current.png");
            var coverDirectory = Path.GetDirectoryName(Subject.GetCoverPath(current.Id, MediaCoverEntity.Author, MediaCoverTypes.Poster, ".png"));
            var currentPoster = Path.Combine(coverDirectory, "poster.png");
            var disk = Mocker.GetMock<IDiskProvider>();
            disk.Setup(provider => provider.FolderExists(coverDirectory)).Returns(true);
            disk.Setup(provider => provider.GetFiles(coverDirectory, true)).Returns(new[] { currentPoster });
            disk.Setup(provider => provider.FileExists(currentPoster)).Returns(true);
            disk.Setup(provider => provider.GetFileSize(currentPoster)).Returns(16000);

            Subject.HandleAsync(new AuthorRefreshCompleteEvent(_author));

            disk.Verify(provider => provider.DeleteFile(currentPoster), Times.Never());
        }

        [TestCase(false)]
        [TestCase(true)]
        public void should_not_restore_art_for_a_deleted_or_reassigned_author(bool reassigned)
        {
            CurrentAuthorWithPoster("https://example.invalid/old.jpg", "https://example.invalid/current.jpg");
            var persisted = reassigned
                ? new Author { Id = _author.Id, AuthorMetadataId = _author.AuthorMetadataId + 1, Metadata = new AuthorMetadata() }
                : null;
            Mocker.GetMock<IAuthorRepository>().Setup(repository => repository.Find(_author.Id)).Returns(persisted);

            Subject.HandleAsync(new AuthorRefreshCompleteEvent(_author));

            Mocker.GetMock<IHttpClient>().Verify(client => client.Get(It.IsAny<HttpRequest>()), Times.Never());
            Mocker.GetMock<IDiskProvider>().Verify(provider => provider.DeleteFile(It.IsAny<string>()), Times.Never());
            Mocker.GetMock<IEventAggregator>().Verify(bus => bus.PublishEvent(It.IsAny<MediaCoversUpdatedEvent>()), Times.Never());
        }

        private Author CurrentAuthorWithPoster(string eventUrl, string currentUrl)
        {
            const int metadataId = 21;
            const string foreignAuthorId = "openlibrary:author:OL123A";
            var older = new AuthorMetadata { Id = metadataId, ForeignAuthorId = foreignAuthorId };
            older.Images.Add(new MediaCover.MediaCover(MediaCoverTypes.Poster, eventUrl));
            _author.AuthorMetadataId = metadataId;
            _author.Metadata = older;

            var fresh = new AuthorMetadata { Id = metadataId, ForeignAuthorId = foreignAuthorId };
            fresh.Images.Add(new MediaCover.MediaCover(MediaCoverTypes.Poster, currentUrl));
            var current = new Author { Id = _author.Id, AuthorMetadataId = metadataId, Metadata = fresh };
            Mocker.GetMock<IAuthorRepository>().Setup(repository => repository.Find(_author.Id)).Returns(current);
            Mocker.GetMock<IBookService>().Setup(service => service.GetBooksByAuthor(_author.Id)).Returns(new List<Book>());
            return current;
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
