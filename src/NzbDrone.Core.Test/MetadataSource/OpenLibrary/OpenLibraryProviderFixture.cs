using FluentAssertions;
using System.Collections.Generic;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.MetadataSource.OpenLibrary;
using NzbDrone.Core.MetadataSource.OpenLibrary.Resources;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.MetadataSource.OpenLibrary
{
    /// <summary>
    /// Unit tests for OpenLibraryProvider using a mocked IOpenLibraryClient.
    /// No live HTTP calls are made — all responses are explicit test fixtures.
    /// </summary>
    [TestFixture]
    public class OpenLibraryProviderFixture : CoreTest<OpenLibraryProvider>
    {
        private Mock<IOpenLibraryClient> _clientMock;

        [SetUp]
        public void Setup()
        {
            _clientMock = Mocker.GetMock<IOpenLibraryClient>();
        }

        // ── SearchForNewBook ──────────────────────────────────────────────────

        [Test]
        public void search_returns_mapped_books()
        {
            _clientMock
                .Setup(c => c.Search(It.IsAny<string>(), It.IsAny<int>()))
                .Returns(BuildSearchResponse());

            var results = Subject.SearchForNewBook("Tolkien", null);

            results.Should().HaveCount(1);
            results[0].Title.Should().Be("The Lord of the Rings");
        }

        [Test]
        public void search_returns_empty_when_client_returns_null()
        {
            _clientMock
                .Setup(c => c.Search(It.IsAny<string>(), It.IsAny<int>()))
                .Returns((OlSearchResponse)null);

            var results = Subject.SearchForNewBook("nothing", null);

            results.Should().BeEmpty();
        }

        [Test]
        public void search_with_empty_docs_returns_empty()
        {
            _clientMock
                .Setup(c => c.Search(It.IsAny<string>(), It.IsAny<int>()))
                .Returns(new OlSearchResponse { Docs = new List<OlSearchDoc>() });

            Subject.SearchForNewBook("empty", null).Should().BeEmpty();
        }

        // ── SearchByIsbn ──────────────────────────────────────────────────────

        [Test]
        public void search_by_isbn_hit_returns_book()
        {
            _clientMock
                .Setup(c => c.GetEditionByIsbn("9780618346257"))
                .Returns(BuildEdition());

            _clientMock
                .Setup(c => c.GetWork(It.IsAny<string>()))
                .Returns(BuildWork());

            _clientMock
                .Setup(c => c.GetAuthor(It.IsAny<string>()))
                .Returns(BuildAuthor());

            var results = Subject.SearchByIsbn("9780618346257");

            results.Should().HaveCount(1);
        }

        [Test]
        public void search_by_isbn_miss_returns_empty()
        {
            _clientMock
                .Setup(c => c.GetEditionByIsbn(It.IsAny<string>()))
                .Returns((OlEditionResource)null);

            Subject.SearchByIsbn("0000000000").Should().BeEmpty();
        }

        // ── SearchByExternalId ────────────────────────────────────────────────

        [Test]
        public void search_by_external_id_olid_calls_get_book_info()
        {
            _clientMock
                .Setup(c => c.GetWork("OL45883W"))
                .Returns(BuildWork());
            _clientMock
                .Setup(c => c.GetAuthor(It.IsAny<string>()))
                .Returns(BuildAuthor());

            var results = Subject.SearchByExternalId("olid", "OL45883W");

            results.Should().HaveCount(1);
            _clientMock.Verify(c => c.GetWork("OL45883W"), Times.Once);
        }

        [Test]
        public void search_by_external_id_asin_returns_empty()
        {
            var results = Subject.SearchByExternalId("asin", "B001234");
            results.Should().BeEmpty();
        }

        [Test]
        public void search_by_external_id_goodreads_returns_empty()
        {
            // OpenLibraryProvider cannot resolve Goodreads IDs
            var results = Subject.SearchByExternalId("goodreads", "12345");
            results.Should().BeEmpty();
        }

        // ── SearchByAsin ──────────────────────────────────────────────────────

        [Test]
        public void search_by_asin_not_supported_returns_empty()
        {
            Subject.SearchByAsin("B00ABC").Should().BeEmpty();
        }

        // ── GetChangedAuthors ─────────────────────────────────────────────────

        [Test]
        public void get_changed_authors_returns_null()
        {
            // OL has no changed-feed; expected contract is null
            Subject.GetChangedAuthors(System.DateTime.UtcNow.AddDays(-1)).Should().BeNull();
        }

        // ── Rate-limit response (429) ──────────────────────────────────────────

        [Test]
        public void search_when_client_throws_open_library_exception_returns_empty()
        {
            _clientMock
                .Setup(c => c.Search(It.IsAny<string>(), It.IsAny<int>()))
                .Throws(new OpenLibraryException("429 rate limited"));

            Subject.SearchForNewBook("rate limit test", null).Should().BeEmpty();
            ExceptionVerification.ExpectedWarns(1);
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private static OlSearchResponse BuildSearchResponse() => new OlSearchResponse
        {
            NumFound = 1,
            Docs = new List<OlSearchDoc>
            {
                new OlSearchDoc
                {
                    Key = "/works/OL45883W",
                    Title = "The Lord of the Rings",
                    AuthorName = new List<string> { "J.R.R. Tolkien" },
                    AuthorKey = new List<string> { "/authors/OL26320A" },
                    Isbn = new List<string> { "9780618346257" },
                    CoverId = 8739161,
                    FirstPublishYear = 1954,
                    RatingsAverage = 4.5,
                    RatingsCount = 1000
                }
            }
        };

        private static OlEditionResource BuildEdition() => new OlEditionResource
        {
            Key = "/books/OL7353617M",
            Title = "The Fellowship of the Ring",
            Isbn13 = new List<string> { "9780618346257" },
            Publishers = new List<string> { "Houghton Mifflin" },
            Works = new List<OlKeyRef> { new OlKeyRef { Key = "/works/OL45883W" } },
            Authors = new List<OlKeyRef> { new OlKeyRef { Key = "/authors/OL26320A" } },
            NumberOfPages = 398
        };

        private static OlWorkResource BuildWork() => new OlWorkResource
        {
            Key = "/works/OL45883W",
            Title = "The Lord of the Rings",
            Description = "An epic high-fantasy novel.",
            Authors = new List<OlWorkAuthorEntry>
            {
                new OlWorkAuthorEntry
                {
                    Author = new OlKeyRef { Key = "/authors/OL26320A" },
                    Type = new OlKeyRef { Key = "/type/author_role" }
                }
            },
            FirstPublishDate = "1954"
        };

        private static OlAuthorResource BuildAuthor() => new OlAuthorResource
        {
            Key = "/authors/OL26320A",
            Name = "J.R.R. Tolkien",
            Bio = "English author.",
            BirthDate = "3 January 1892"
        };
    }
}
