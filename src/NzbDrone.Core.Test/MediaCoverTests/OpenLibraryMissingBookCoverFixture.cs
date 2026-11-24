using System.Collections.Generic;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.MediaCover;

namespace NzbDrone.Core.Test.MediaCoverTests
{
    [TestFixture]
    public class OpenLibraryMissingBookCoverFixture
    {
        private const string Work = "openlibrary:work:OL101W";
        private const string Edition = "openlibrary:edition:OL102M";
        private const string Cover = "https://covers.openlibrary.org/b/id/103-L.jpg";
        private const string Canonical = "1234567890abcdedabcd1234567890ac";

        [Test]
        public void should_use_the_owned_open_library_work_art_of_a_generated_audiobook_edition()
        {
            var book = FileBackedBook();
            var editions = book.Editions.Value;
            var selection = OpenLibraryMissingBookCover.SelectArtwork(book, editions);
            selection.Should().NotBeNull();
            var recovery = selection.Value;
            var identities = new Mock<ICatalogueIdentityRepository>();
            identities.Setup(repository => repository.FindByProviderId(CatalogueEntityKind.Work, "OpenLibrary", "OL101W"))
                .Returns(book.Id);

            recovery.EditionId.Should().Be(editions[0].Id);
            recovery.SourceEditionId.Should().Be(editions[1].Id);
            recovery.CoverUrl.Should().Be(Cover);
            OpenLibraryMissingBookCover.HasVerifiedOwner(recovery.WorkSourceId, book.Id, identities.Object).Should().BeTrue();
            book.GetPreferredEdition().Should().BeSameAs(editions[0]);
            editions[0].Images.Should().BeEmpty();
            editions[0].Format.Should().Be("Audiobook");
            editions[0].BookId.Should().Be(book.Id);

            identities.Setup(repository => repository.FindByProviderId(CatalogueEntityKind.Work, "OpenLibrary", "OL101W"))
                .Returns(book.Id + 1);
            OpenLibraryMissingBookCover.HasVerifiedOwner(recovery.WorkSourceId, book.Id, identities.Object).Should().BeFalse();
        }

        [TestCase("other-work")]
        [TestCase("different-edition-owner")]
        [TestCase("wrong-bare-source-id")]
        [TestCase("wrong-source-provider")]
        [TestCase("wrong-source-link")]
        [TestCase("two-work-placeholders")]
        [TestCase("earlier-competing-cover")]
        [TestCase("wrong-canonical-owner")]
        [TestCase("own-cover")]
        public void should_not_use_a_local_sibling_cover_with_missing_or_conflicting_authority(string conflict)
        {
            var book = FileBackedBook();
            var editions = book.Editions.Value;
            var preferred = editions[0];
            var provider = editions[1];
            switch (conflict)
            {
                case "other-work":
                    book.OpenLibraryWorkId = "OL102W";
                    break;
                case "different-edition-owner":
                    provider.BookId = 11;
                    break;
                case "wrong-bare-source-id":
                    provider.Images[0].SourceId = "OL103W";
                    break;
                case "wrong-source-provider":
                    provider.Images[0].SourceProvider = "Hardcover";
                    break;
                case "wrong-source-link":
                    provider.Links[0].Url = "https://openlibrary.org/works/OL103W";
                    break;
                case "two-work-placeholders":
                    editions.Add(new Edition { Id = 30, BookId = book.Id, ForeignEditionId = Work });
                    break;
                case "earlier-competing-cover":
                    editions.Insert(1, new Edition
                    {
                        Id = 30,
                        BookId = book.Id,
                        ForeignEditionId = "hardcover:edition:125",
                        Images = new List<MediaCover.MediaCover>
                        {
                            new MediaCover.MediaCover(MediaCoverTypes.Cover, "https://assets.hardcover.app/cover.jpg")
                        }
                    });
                    break;
                case "wrong-canonical-owner":
                    book.CanonicalId = "a234567890abcdedabcd1234567890ac";
                    break;
                case "own-cover":
                    preferred.Images.Add(new MediaCover.MediaCover(MediaCoverTypes.Cover, "https://images.example/own.jpg"));
                    break;
            }

            OpenLibraryMissingBookCover.SelectArtwork(book, editions).Should().BeNull();
        }

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

        private static Book FileBackedBook()
        {
            const int bookId = 4;
            return new Book
            {
                Id = bookId,
                CanonicalId = Canonical,
                ForeignBookId = Work,
                OpenLibraryWorkId = "OL101W",
                Editions = new List<Edition>
                {
                    new Edition
                    {
                        Id = 10,
                        BookId = bookId,
                        ForeignEditionId = $"bibliophilarr:edition:{Canonical}:audiobook",
                        Format = "Audiobook",
                        Monitored = true
                    },
                    new Edition
                    {
                        Id = 11,
                        BookId = bookId,
                        ForeignEditionId = Work,
                        Links = new List<Links> { new Links { Url = "https://openlibrary.org/works/OL101W" } },
                        Images = new List<MediaCover.MediaCover>
                        {
                            new MediaCover.MediaCover(MediaCoverTypes.Cover, Cover)
                            {
                                SourceProvider = "OpenLibrary",
                                SourceId = "OL101W"
                            }
                        }
                    }
                }
            };
        }
    }
}
