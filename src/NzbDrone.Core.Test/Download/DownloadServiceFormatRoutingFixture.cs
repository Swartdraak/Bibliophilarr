using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FizzWare.NBuilder;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Download;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Download
{
    [TestFixture]
    public class DownloadServiceFormatRoutingFixture : CoreTest<DownloadService>
    {
        private RemoteBook _remoteBook;
        private List<IDownloadClient> _downloadClients;

        [SetUp]
        public void Setup()
        {
            _downloadClients = new List<IDownloadClient>();

            Mocker.GetMock<IProvideDownloadClient>()
                .Setup(v => v.GetDownloadClient(It.IsAny<DownloadProtocol>(), It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<HashSet<int>>()))
                .Returns<DownloadProtocol, int, bool, HashSet<int>>((v, i, f, t) => _downloadClients.FirstOrDefault(d => d.Protocol == v));

            var releaseInfo = Builder<ReleaseInfo>.CreateNew()
                .With(v => v.DownloadProtocol = DownloadProtocol.Usenet)
                .With(v => v.DownloadUrl = "http://test.site/download1.ext")
                .Build();

            var books = Builder<Book>.CreateListOfSize(1)
                .All().With(s => s.AuthorId = 1)
                .Build().ToList();

            var author = Builder<Author>.CreateNew()
                .With(a => a.Id = 1)
                .With(a => a.Tags = new HashSet<int> { 100 })
                .Build();

            _remoteBook = Builder<RemoteBook>.CreateNew()
                .With(c => c.Author = author)
                .With(c => c.Release = releaseInfo)
                .With(c => c.Books = books)
                .With(c => c.ResolvedFormatType = null)
                .With(c => c.ResolvedQualityProfile = null)
                .Build();

            var usenetClient = new Mock<IDownloadClient>(MockBehavior.Default);
            usenetClient.SetupGet(s => s.Definition).Returns(Builder<IndexerDefinition>.CreateNew().Build());
            usenetClient.SetupGet(v => v.Protocol).Returns(DownloadProtocol.Usenet);
            usenetClient.Setup(v => v.Download(It.IsAny<RemoteBook>(), It.IsAny<IIndexer>()))
                .ReturnsAsync("usenet-id");
            _downloadClients.Add(usenetClient.Object);
        }

        [Test]
        public async Task should_use_author_tags_when_flag_off()
        {
            Mocker.GetMock<IConfigService>()
                .Setup(s => s.EnableDualFormatTracking).Returns(false);

            await Subject.DownloadReport(_remoteBook, null);

            Mocker.GetMock<IProvideDownloadClient>()
                .Verify(v => v.GetDownloadClient(
                    DownloadProtocol.Usenet,
                    It.IsAny<int>(),
                    It.IsAny<bool>(),
                    It.Is<HashSet<int>>(t => t.Contains(100))),
                    Times.Once());
        }

        [Test]
        public async Task should_use_format_profile_tags_when_flag_on_and_format_resolved()
        {
            Mocker.GetMock<IConfigService>()
                .Setup(s => s.EnableDualFormatTracking).Returns(true);

            _remoteBook.ResolvedFormatType = FormatType.Audiobook;

            var formatProfile = new AuthorFormatProfile
            {
                Tags = new HashSet<int> { 200, 201 }
            };

            Mocker.GetMock<IAuthorFormatProfileService>()
                .Setup(s => s.GetByAuthorIdAndFormat(1, FormatType.Audiobook))
                .Returns(formatProfile);

            await Subject.DownloadReport(_remoteBook, null);

            Mocker.GetMock<IProvideDownloadClient>()
                .Verify(v => v.GetDownloadClient(
                    DownloadProtocol.Usenet,
                    It.IsAny<int>(),
                    It.IsAny<bool>(),
                    It.Is<HashSet<int>>(t => t.Contains(200) && t.Contains(201))),
                    Times.Once());
        }

        [Test]
        public async Task should_fallback_to_author_tags_when_format_profile_has_no_tags()
        {
            Mocker.GetMock<IConfigService>()
                .Setup(s => s.EnableDualFormatTracking).Returns(true);

            _remoteBook.ResolvedFormatType = FormatType.Ebook;

            var formatProfile = new AuthorFormatProfile
            {
                Tags = new HashSet<int>()
            };

            Mocker.GetMock<IAuthorFormatProfileService>()
                .Setup(s => s.GetByAuthorIdAndFormat(1, FormatType.Ebook))
                .Returns(formatProfile);

            await Subject.DownloadReport(_remoteBook, null);

            Mocker.GetMock<IProvideDownloadClient>()
                .Verify(v => v.GetDownloadClient(
                    DownloadProtocol.Usenet,
                    It.IsAny<int>(),
                    It.IsAny<bool>(),
                    It.Is<HashSet<int>>(t => t.Contains(100))),
                    Times.Once());
        }

        [Test]
        public async Task should_fallback_to_author_tags_when_no_format_profile()
        {
            Mocker.GetMock<IConfigService>()
                .Setup(s => s.EnableDualFormatTracking).Returns(true);

            _remoteBook.ResolvedFormatType = FormatType.Ebook;

            Mocker.GetMock<IAuthorFormatProfileService>()
                .Setup(s => s.GetByAuthorIdAndFormat(1, FormatType.Ebook))
                .Returns((AuthorFormatProfile)null);

            await Subject.DownloadReport(_remoteBook, null);

            Mocker.GetMock<IProvideDownloadClient>()
                .Verify(v => v.GetDownloadClient(
                    DownloadProtocol.Usenet,
                    It.IsAny<int>(),
                    It.IsAny<bool>(),
                    It.Is<HashSet<int>>(t => t.Contains(100))),
                    Times.Once());
        }
    }
}
