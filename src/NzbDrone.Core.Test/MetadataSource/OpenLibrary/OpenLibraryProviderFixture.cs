using System.Collections.Generic;
using FluentAssertions;
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
                .Setup(c => c.Search(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>()))
                .Returns(BuildSearchResponse());

            var results = Subject.SearchForNewBook("Tolkien", null);

            results.Should().HaveCount(1);
            results[0].Title.Should().Be("The Lord of the Rings");
        }

        [Test]
        public void search_returns_empty_when_client_returns_null()
        {
            _clientMock
                .Setup(c => c.Search(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>()))
                .Returns((OlSearchResponse)null);

            var results = Subject.SearchForNewBook("nothing", null);

            results.Should().BeEmpty();
        }

        [Test]
        public void search_with_empty_docs_returns_empty()
        {
            _clientMock
                .Setup(c => c.Search(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>()))
                .Returns(new OlSearchResponse { Docs = new List<OlSearchDoc>() });

            Subject.SearchForNewBook("empty", null).Should().BeEmpty();
        }

        [Test]
        public void search_with_author_prefix_should_route_to_author_lookup()
        {
            _clientMock
                .Setup(c => c.GetAuthor("OL26320A"))
                .Returns(BuildAuthor());

            _clientMock
                .Setup(c => c.Search("author_key:/authors/OL26320A", It.IsAny<int>(), It.IsAny<int>()))
                .Returns(new OlSearchResponse { Docs = new List<OlSearchDoc>() });

            var results = Subject.SearchForNewBook("author:OL26320A", null);

            results.Should().BeEmpty();
            _clientMock.Verify(c => c.GetAuthor("OL26320A"), Times.Once);
        }

        [Test]
        public void search_with_work_prefix_should_route_to_work_lookup()
        {
            _clientMock
                .Setup(c => c.GetWork("OL45883W"))
                .Returns(BuildWork());

            _clientMock
                .Setup(c => c.GetAuthor(It.IsAny<string>()))
                .Returns(BuildAuthor());

            var results = Subject.SearchForNewBook("work:OL45883W", null);

            results.Should().HaveCount(1);
            results[0].ForeignBookId.Should().Be("openlibrary:work:OL45883W");
            _clientMock.Verify(c => c.GetWork("OL45883W"), Times.Once);
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

        [Test]
        public void get_author_info_should_normalize_prefixed_open_library_author_ids()
        {
            _clientMock
                .Setup(c => c.GetAuthor("OL26320A"))
                .Returns(BuildAuthor());

            _clientMock
                .Setup(c => c.Search("author_key:/authors/OL26320A", It.IsAny<int>(), It.IsAny<int>()))
                .Returns(new OlSearchResponse { Docs = new List<OlSearchDoc>() });

            var result = Subject.GetAuthorInfo("openlibrary:author:OL26320A");

            result.Should().NotBeNull();
            result.Metadata.Value.ForeignAuthorId.Should().Be("openlibrary:author:OL26320A");
            result.Metadata.Value.OpenLibraryAuthorId.Should().Be("OL26320A");

            _clientMock.Verify(c => c.GetAuthor("OL26320A"), Times.Once);
            _clientMock.Verify(c => c.Search("author_key:/authors/OL26320A", It.IsAny<int>(), It.IsAny<int>()), Times.Once);
        }

        [Test]
        public void get_author_info_should_page_author_bibliography_results()
        {
            _clientMock
                .Setup(c => c.GetAuthor("OL26320A"))
                .Returns(BuildAuthor());

            var firstPage = new List<OlSearchDoc>();
            for (var i = 0; i < 100; i++)
            {
                firstPage.Add(new OlSearchDoc
                {
                    Key = $"/works/OL{i}W",
                    Title = $"Book {i}",
                    AuthorName = new List<string> { "Author" },
                    AuthorKey = new List<string> { "OL26320A" }
                });
            }

            _clientMock
                .Setup(c => c.Search("author_key:/authors/OL26320A", 100, 0))
                .Returns(new OlSearchResponse { NumFound = 101, Docs = firstPage });

            _clientMock
                .Setup(c => c.Search("author_key:/authors/OL26320A", 100, 100))
                .Returns(new OlSearchResponse
                {
                    NumFound = 101,
                    Docs = new List<OlSearchDoc>
                    {
                        new OlSearchDoc
                        {
                            Key = "/works/OL101W",
                            Title = "Book 101",
                            AuthorName = new List<string> { "Author" },
                            AuthorKey = new List<string> { "OL26320A" }
                        }
                    }
                });

            var result = Subject.GetAuthorInfo("openlibrary:author:OL26320A");

            result.Should().NotBeNull();
            result.Books.Value.Should().HaveCount(101);
            _clientMock.Verify(c => c.Search("author_key:/authors/OL26320A", 100, 0), Times.Once);
            _clientMock.Verify(c => c.Search("author_key:/authors/OL26320A", 100, 100), Times.Once);
        }

        [Test]
        public void get_author_info_should_stop_on_sparse_final_page_even_when_num_found_is_high()
        {
            _clientMock
                .Setup(c => c.GetAuthor("OL26320A"))
                .Returns(BuildAuthor());

            _clientMock
                .Setup(c => c.Search("author_key:/authors/OL26320A", 100, 0))
                .Returns(new OlSearchResponse { NumFound = 10000, Docs = BuildSearchDocs(100, 0) });

            _clientMock
                .Setup(c => c.Search("author_key:/authors/OL26320A", 100, 100))
                .Returns(new OlSearchResponse { NumFound = 10000, Docs = BuildSearchDocs(3, 100) });

            var result = Subject.GetAuthorInfo("openlibrary:author:OL26320A");

            result.Should().NotBeNull();
            result.Books.Value.Should().HaveCount(103);
            _clientMock.Verify(c => c.Search("author_key:/authors/OL26320A", 100, 0), Times.Once);
            _clientMock.Verify(c => c.Search("author_key:/authors/OL26320A", 100, 100), Times.Once);
            _clientMock.Verify(c => c.Search("author_key:/authors/OL26320A", 100, 103), Times.Never);
        }

        [Test]
        public void get_author_info_should_cap_bibliography_documents_at_1000()
        {
            _clientMock
                .Setup(c => c.GetAuthor("OL26320A"))
                .Returns(BuildAuthor());

            _clientMock
                .Setup(c => c.Search("author_key:/authors/OL26320A", 100, It.IsAny<int>()))
                .Returns((string _, int _, int offset) => new OlSearchResponse
                {
                    NumFound = 5000,
                    Docs = BuildSearchDocs(100, offset)
                });

            var result = Subject.GetAuthorInfo("openlibrary:author:OL26320A");

            result.Should().NotBeNull();
            result.Books.Value.Should().HaveCount(1000);
            _clientMock.Verify(c => c.Search("author_key:/authors/OL26320A", 100, 0), Times.Once);
            _clientMock.Verify(c => c.Search("author_key:/authors/OL26320A", 100, 900), Times.Once);
            _clientMock.Verify(c => c.Search("author_key:/authors/OL26320A", 100, 1000), Times.Never);
        }

        [Test]
        public void get_book_info_should_normalize_prefixed_open_library_work_ids()
        {
            _clientMock
                .Setup(c => c.GetWork("OL45883W"))
                .Returns(BuildWork());

            _clientMock
                .Setup(c => c.GetAuthor(It.IsAny<string>()))
                .Returns(BuildAuthor());

            var result = Subject.GetBookInfo("openlibrary:work:OL45883W");

            result.Should().NotBeNull();
            result.Item1.Should().Be("openlibrary:author:OL26320A");
            result.Item2.ForeignBookId.Should().Be("openlibrary:work:OL45883W");
            result.Item2.OpenLibraryWorkId.Should().Be("OL45883W");
            result.Item2.AuthorMetadata.Value.ForeignAuthorId.Should().Be("openlibrary:author:OL26320A");

            _clientMock.Verify(c => c.GetWork("OL45883W"), Times.Once);
        }

        [Test]
        public void get_book_info_should_resolve_open_library_edition_ids_to_work_ids()
        {
            var work = BuildWork();
            work.Key = "/works/OL8547083W";
            work.Authors = new List<OlWorkAuthorEntry>
            {
                new OlWorkAuthorEntry
                {
                    Author = new OlKeyRef { Key = "/authors/OL29303A" },
                    Type = new OlKeyRef { Key = "/type/author_role" }
                }
            };

            var edition = new OlEditionResource
            {
                Key = "/books/OL8547083M",
                Works = new List<OlKeyRef> { new OlKeyRef { Key = "/works/OL8547083W" } },
                Authors = new List<OlKeyRef> { new OlKeyRef { Key = "/authors/OL29303A" } }
            };

            _clientMock
                .Setup(c => c.GetWork("OL8547083M"))
                .Returns((OlWorkResource)null);

            _clientMock
                .Setup(c => c.GetEdition("OL8547083M"))
                .Returns(edition);

            _clientMock
                .Setup(c => c.GetWork("OL8547083W"))
                .Returns(work);

            _clientMock
                .Setup(c => c.GetAuthor(It.IsAny<string>()))
                .Returns(new OlAuthorResource { Key = "/authors/OL29303A", Name = "Dante Alighieri" });

            var result = Subject.GetBookInfo("openlibrary:work:OL8547083M");

            result.Should().NotBeNull();
            result.Item2.ForeignBookId.Should().Be("openlibrary:work:OL8547083W");
            result.Item2.OpenLibraryWorkId.Should().Be("OL8547083W");

            _clientMock.Verify(c => c.GetWork("OL8547083M"), Times.Once);
            _clientMock.Verify(c => c.GetEdition("OL8547083M"), Times.Once);
            _clientMock.Verify(c => c.GetWork("OL8547083W"), Times.Once);
        }

        [Test]
        public void get_book_info_should_resolve_edition_ids_without_work_links_via_isbn_search()
        {
            var edition = new OlEditionResource
            {
                Key = "/books/OL9205704M",
                Title = "Hickory Dickory Death",
                Isbn13 = new List<string> { "9789992296004" }
            };

            var searchResponse = new OlSearchResponse
            {
                Docs = new List<OlSearchDoc>
                {
                    new OlSearchDoc
                    {
                        Key = "/works/OL9205704W",
                        Title = "Hickory Dickory Death",
                        AuthorName = new List<string> { "Agatha Christie" },
                        AuthorKey = new List<string> { "OL27695A" },
                        Isbn = new List<string> { "9789992296004" }
                    }
                }
            };

            _clientMock
                .Setup(c => c.GetWork("OL9205704M"))
                .Returns((OlWorkResource)null);

            _clientMock
                .Setup(c => c.GetEdition("OL9205704M"))
                .Returns(edition);

            _clientMock
                .Setup(c => c.GetEditionByIsbn("9789992296004"))
                .Returns(edition);

            _clientMock
                .Setup(c => c.Search("9789992296004", It.IsAny<int>(), It.IsAny<int>()))
                .Returns(searchResponse);

            var result = Subject.GetBookInfo("openlibrary:work:OL9205704M");

            result.Should().NotBeNull();
            result.Item2.ForeignBookId.Should().Be("openlibrary:work:OL9205704W");
            result.Item2.OpenLibraryWorkId.Should().Be("OL9205704W");
            result.Item2.Editions.Value.Should().Contain(x => x.Isbn13 == "9789992296004");
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
        public void search_by_external_id_openlibrary_work_id_calls_get_book_info()
        {
            _clientMock
                .Setup(c => c.GetWork("OL45883W"))
                .Returns(BuildWork());
            _clientMock
                .Setup(c => c.GetAuthor(It.IsAny<string>()))
                .Returns(BuildAuthor());

            var results = Subject.SearchByExternalId("openlibrary", "openlibrary:work:OL45883W");

            results.Should().HaveCount(1);
            _clientMock.Verify(c => c.GetWork("OL45883W"), Times.Once);
        }

        // ── SearchByAsin ──────────────────────────────────────────────────────
        [Test]
        public void search_by_asin_should_fallback_to_query_search()
        {
            _clientMock
                .Setup(c => c.Search("B00ABC", It.IsAny<int>(), It.IsAny<int>()))
                .Returns(BuildSearchResponse());

            var results = Subject.SearchByAsin("B00ABC");

            results.Should().HaveCount(1);
            _clientMock.Verify(c => c.Search("B00ABC", It.IsAny<int>(), It.IsAny<int>()), Times.Once);
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
                .Setup(c => c.Search(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>()))
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

        private static List<OlSearchDoc> BuildSearchDocs(int count, int startIndex)
        {
            var docs = new List<OlSearchDoc>();

            for (var i = 0; i < count; i++)
            {
                var id = startIndex + i;
                docs.Add(new OlSearchDoc
                {
                    Key = $"/works/OL{id}W",
                    Title = $"Book {id}",
                    AuthorName = new List<string> { "Author" },
                    AuthorKey = new List<string> { "OL26320A" }
                });
            }

            return docs;
        }

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
