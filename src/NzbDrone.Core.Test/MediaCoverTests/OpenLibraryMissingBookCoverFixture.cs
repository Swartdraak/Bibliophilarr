using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.MediaCover;

namespace NzbDrone.Core.Test.MediaCoverTests
{
    [TestFixture]
    public class OpenLibraryMissingBookCoverFixture
    {
        private const string Work = "openlibrary:work:OL101W";
        private const string Edition = "openlibrary:edition:OL102M";
        private const string Cover = "https://covers.openlibrary.org/b/id/103-L.jpg";

        [TestCase(Edition)]
        [TestCase(Work)]
        [TestCase("OL-work-OL101W")]
        public void should_build_the_cache_address_for_existing_open_library_edition_shapes(string editionId)
        {
            var suffix = OpenLibraryMissingBookCover.GetRetryFileSuffix(Work, editionId, Cover);

            suffix.Should().MatchRegex(@"\A\.ol-retry-[0-9a-f]{64}\.jpg\z");
            OpenLibraryMissingBookCover.GetRetryFileSuffix(Work, editionId, Cover).Should().Be(suffix);
            OpenLibraryMissingBookCover.GetRetryFileSuffix(Work, editionId, Cover + "?default=false").Should().NotBe(suffix);
        }

        [TestCase("hardcover:work:101", Edition, Cover)]
        [TestCase("OL101W", Edition, Cover)]
        [TestCase("openlibrary:work:OL0W", Edition, Cover)]
        [TestCase(Work, "OL-work-OL999W", Cover)]
        [TestCase(Work, "openlibrary:work:OL999W", Cover)]
        [TestCase(Work, "openlibrary:edition:OL102W", Cover)]
        [TestCase(Work, Edition, "https://covers.openlibrary.org/a/id/103-L.jpg")]
        [TestCase(Work, Edition, "https://covers.openlibrary.org/b/id/103-L.jpg?ignored=value")]
        [TestCase(Work, Edition, "https://covers.openlibrary.org/b/id/103-M.jpg")]
        [TestCase(Work, Edition, "https://example.invalid/book-cover.jpg")]
        [TestCase(Work, Edition, null)]
        public void should_decline_unverified_owner_edition_or_cover_shapes(string workId, string editionId, string coverUrl)
        {
            OpenLibraryMissingBookCover.GetRetryFileSuffix(workId, editionId, coverUrl).Should().BeNull();
        }

        [Test]
        public void should_not_reuse_cached_artwork_when_the_work_edition_or_cover_changes()
        {
            var original = OpenLibraryMissingBookCover.GetRetryFileSuffix(Work, Edition, Cover);

            OpenLibraryMissingBookCover.GetRetryFileSuffix("openlibrary:work:OL104W", Edition, Cover).Should().NotBe(original);
            OpenLibraryMissingBookCover.GetRetryFileSuffix(Work, "openlibrary:edition:OL105M", Cover).Should().NotBe(original);
            OpenLibraryMissingBookCover.GetRetryFileSuffix(Work, Edition, "https://covers.openlibrary.org/b/id/106-L.jpg").Should().NotBe(original);
        }
    }
}
