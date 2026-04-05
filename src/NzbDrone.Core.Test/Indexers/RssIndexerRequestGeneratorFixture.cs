using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Indexers.IPTorrents;
using NzbDrone.Core.Indexers.Nyaa;
using NzbDrone.Core.Indexers.Torrentleech;
using NzbDrone.Core.Indexers.TorrentRss;

namespace NzbDrone.Core.Test.Indexers
{
    [TestFixture]
    public class RssIndexerRequestGeneratorFixture
    {
        private static readonly NzbDrone.Core.IndexerSearch.Definitions.BookSearchCriteria BookCriteria = new NzbDrone.Core.IndexerSearch.Definitions.BookSearchCriteria();
        private static readonly NzbDrone.Core.IndexerSearch.Definitions.AuthorSearchCriteria AuthorCriteria = new NzbDrone.Core.IndexerSearch.Definitions.AuthorSearchCriteria();

        [Test]
        public void rss_only_generators_should_return_empty_book_search_chains()
        {
            AssertEmptyChain(new NzbDrone.Core.Indexers.RssIndexerRequestGenerator("https://example.com/rss").GetSearchRequests(BookCriteria));
            AssertEmptyChain(new IPTorrentsRequestGenerator().GetSearchRequests(BookCriteria));
            AssertEmptyChain(new NyaaRequestGenerator().GetSearchRequests(BookCriteria));
            AssertEmptyChain(new TorrentleechRequestGenerator().GetSearchRequests(BookCriteria));
            AssertEmptyChain(new TorrentRssIndexerRequestGenerator().GetSearchRequests(BookCriteria));
        }

        [Test]
        public void rss_only_generators_should_return_empty_author_search_chains()
        {
            AssertEmptyChain(new NzbDrone.Core.Indexers.RssIndexerRequestGenerator("https://example.com/rss").GetSearchRequests(AuthorCriteria));
            AssertEmptyChain(new IPTorrentsRequestGenerator().GetSearchRequests(AuthorCriteria));
            AssertEmptyChain(new NyaaRequestGenerator().GetSearchRequests(AuthorCriteria));
            AssertEmptyChain(new TorrentleechRequestGenerator().GetSearchRequests(AuthorCriteria));
            AssertEmptyChain(new TorrentRssIndexerRequestGenerator().GetSearchRequests(AuthorCriteria));
        }

        private static void AssertEmptyChain(NzbDrone.Core.Indexers.IndexerPageableRequestChain chain)
        {
            chain.Should().NotBeNull();
            chain.GetAllTiers().Should().BeEmpty();
        }
    }
}
