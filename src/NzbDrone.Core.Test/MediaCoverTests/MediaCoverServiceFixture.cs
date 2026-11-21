using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions;
using System.Linq;
using FizzWare.NBuilder;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Cache;
using NzbDrone.Common.Disk;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Common.Http;
using NzbDrone.Core.Books;
using NzbDrone.Core.Books.Events;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.MediaCover;
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
