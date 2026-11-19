using System.Collections.Generic;
using System.Threading.Tasks;
using Bibliophilarr.Api.V1.Indexers;
using FluentAssertions;
using Moq;
using NLog;
using NUnit.Framework;
using NzbDrone.Common.Cache;
using NzbDrone.Core.Books;
using NzbDrone.Core.DecisionEngine;
using NzbDrone.Core.Download;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.IndexerSearch;
using NzbDrone.Core.Parser;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Qualities;

namespace NzbDrone.Api.Test.Indexers
{
    [TestFixture]
    public class BookReleaseFormatFixture
    {
        [Test]
        public async Task interactive_book_search_can_select_one_format()
        {
            const int bookId = 8;
            var search = new Mock<ISearchForReleases>();
            search.Setup(s => s.BookSearch(bookId, true, true, true)).ReturnsAsync(new List<DownloadDecision>
            {
                CreateDecision("ebook", Quality.EPUB),
                CreateDecision("audiobook", Quality.M4B)
            });
            var prioritize = new Mock<IPrioritizeDownloadDecision>();
            prioritize.Setup(s => s.PrioritizeDecisions(It.IsAny<List<DownloadDecision>>()))
                .Returns((List<DownloadDecision> decisions) => decisions);

            var controller = new ReleaseController(
                Mock.Of<IFetchAndParseRss>(),
                search.Object,
                Mock.Of<IMakeDownloadDecision>(),
                prioritize.Object,
                Mock.Of<IDownloadService>(),
                Mock.Of<IAuthorService>(),
                Mock.Of<IBookService>(),
                Mock.Of<IParsingService>(),
                new CacheManager(),
                LogManager.GetCurrentClassLogger());

            var all = await controller.GetReleases(bookId, null);
            var ebooks = await controller.GetReleases(bookId, null, FormatType.Ebook);
            var audiobooks = await controller.GetReleases(bookId, null, FormatType.Audiobook);

            all.Should().HaveCount(2);
            ebooks.Should().ContainSingle().Which.Title.Should().Be("ebook");
            audiobooks.Should().ContainSingle().Which.Title.Should().Be("audiobook");
        }

        private static DownloadDecision CreateDecision(string title, Quality quality)
        {
            return new DownloadDecision(new RemoteBook
            {
                Release = new ReleaseInfo { Title = title, Guid = title },
                ParsedBookInfo = new ParsedBookInfo { Quality = new QualityModel(quality) }
            });
        }
    }
}
