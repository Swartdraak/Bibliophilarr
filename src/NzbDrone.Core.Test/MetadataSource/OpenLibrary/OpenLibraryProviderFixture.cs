using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Exceptions;
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
            _clientMock.Setup(client => client.GetAuthorWorks(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>()))
                .Returns(new OlAuthorWorksResponse());
            _clientMock.Setup(client => client.Search("author_key:OL26320A", It.IsAny<int>(), It.IsAny<int>()))
                .Returns(new OlSearchResponse());
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
                .Setup(c => c.GetAuthor("OL26320A", true))
                .Returns(BuildAuthor());

            _clientMock
                .Setup(c => c.Search("author_key:OL26320A", It.IsAny<int>(), It.IsAny<int>()))
                .Returns(new OlSearchResponse { Docs = new List<OlSearchDoc>() });

            var results = Subject.SearchForNewBook("author:OL26320A", null);

            results.Should().BeEmpty();
            _clientMock.Verify(c => c.GetAuthor("OL26320A", true), Times.Once);
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
                .Setup(c => c.GetAuthor("OL26320A", true))
                .Returns(BuildAuthor());

            _clientMock
                .Setup(c => c.Search("author_key:OL26320A", It.IsAny<int>(), It.IsAny<int>()))
                .Returns(new OlSearchResponse { Docs = new List<OlSearchDoc>() });

            var result = Subject.GetAuthorInfo("openlibrary:author:OL26320A");

            result.Should().NotBeNull();
            result.Metadata.Value.ForeignAuthorId.Should().Be("openlibrary:author:OL26320A");
            result.Metadata.Value.OpenLibraryAuthorId.Should().Be("OL26320A");

            _clientMock.Verify(c => c.GetAuthor("OL26320A", true), Times.Once);
            _clientMock.Verify(c => c.Search("author_key:OL26320A", It.IsAny<int>(), It.IsAny<int>()), Times.Once);
        }

        [Test]
        public void get_author_info_should_page_author_bibliography_results()
        {
            _clientMock
                .Setup(c => c.GetAuthor("OL26320A", true))
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
                .Setup(c => c.Search("author_key:OL26320A", 100, 0))
                .Returns(new OlSearchResponse { NumFound = 101, NumFoundExact = true, Docs = firstPage });

            _clientMock
                .Setup(c => c.Search("author_key:OL26320A", 100, 100))
                .Returns(new OlSearchResponse
                {
                    NumFound = 101,
                    NumFoundExact = true,
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
            _clientMock.Verify(c => c.Search("author_key:OL26320A", 100, 0), Times.Once);
            _clientMock.Verify(c => c.Search("author_key:OL26320A", 100, 100), Times.Once);
        }

        [Test]
        public void get_author_info_should_continue_after_short_search_pages_and_stop_on_clean_eof()
        {
            _clientMock.Setup(client => client.GetAuthor("OL26320A", true)).Returns(BuildAuthor());

            _clientMock.Setup(client => client.Search("author_key:OL26320A", 100, 0))
                .Returns(new OlSearchResponse { NumFound = 10000, NumFoundExact = false, Docs = BuildSearchDocs(100, 0) });
            _clientMock.Setup(client => client.Search("author_key:OL26320A", 100, 100))
                .Returns(new OlSearchResponse { NumFound = 10000, NumFoundExact = false, Docs = BuildSearchDocs(3, 100) });
            _clientMock.Setup(client => client.Search("author_key:OL26320A", 100, 103))
                .Returns(new OlSearchResponse { NumFound = 10000, NumFoundExact = false, Docs = BuildSearchDocs(2, 103) });

            var result = Subject.GetAuthorInfo("openlibrary:author:OL26320A");

            result.Books.Value.Should().HaveCount(105);
            _clientMock.Verify(client => client.Search("author_key:OL26320A", 100, 105), Times.Once);
        }

        [TestCase(true)]
        [TestCase(false)]
        public void get_author_info_should_continue_beyond_1000_results(bool useWorksEndpoint)
        {
            const int total = 1021;
            _clientMock.Setup(client => client.GetAuthor("OL26320A", true)).Returns(BuildAuthor());

            if (useWorksEndpoint)
            {
                _clientMock.Setup(client => client.GetAuthorWorks("OL26320A", 100, It.IsAny<int>()))
                    .Returns((string _, int _, int offset) => new OlAuthorWorksResponse
                    {
                        Size = total,
                        Entries = BuildSearchDocs(System.Math.Min(100, total - offset), offset)
                            .Select(doc => new OlWorkResource { Key = doc.Key, Title = doc.Title }).ToList()
                    });
            }
            else
            {
                _clientMock.Setup(client => client.Search("author_key:OL26320A", 100, It.IsAny<int>()))
                    .Returns((string _, int _, int offset) => new OlSearchResponse
                    {
                        NumFound = total,
                        NumFoundExact = true,
                        Docs = BuildSearchDocs(System.Math.Min(100, total - offset), offset)
                    });
            }

            var result = Subject.GetAuthorInfo("openlibrary:author:OL26320A");

            result.Books.Value.Should().HaveCount(total);
            if (useWorksEndpoint)
            {
                _clientMock.Verify(client => client.GetAuthorWorks("OL26320A", 100, 1000), Times.Once);
            }
            else
            {
                _clientMock.Verify(client => client.Search("author_key:OL26320A", 100, 1000), Times.Once);
            }
        }

        [TestCase(true)]
        [TestCase(false)]
        public void get_author_info_should_confirm_an_early_empty_works_page_once(bool recovers)
        {
            _clientMock.Setup(client => client.GetAuthor("OL26320A", true)).Returns(BuildAuthor());
            _clientMock.Setup(client => client.GetAuthorWorks("OL26320A", 100, 0))
                .Returns(new OlAuthorWorksResponse
                {
                    Size = 2,
                    Entries = new List<OlWorkResource> { new OlWorkResource { Key = "/works/OL1W", Title = "First Work" } }
                });
            var emptyPage = new OlAuthorWorksResponse { Size = 2 };
            var secondPage = new OlAuthorWorksResponse
            {
                Size = 2,
                Entries = new List<OlWorkResource> { new OlWorkResource { Key = "/works/OL2W", Title = "Second Work" } }
            };
            _clientMock.SetupSequence(client => client.GetAuthorWorks("OL26320A", 100, 1))
                .Returns(emptyPage)
                .Returns(recovers ? secondPage : new OlAuthorWorksResponse { Size = 1 });

            var lookup = () => Subject.GetAuthorInfo("openlibrary:author:OL26320A");
            if (recovers)
            {
                lookup().Books.Value.Select(book => book.OpenLibraryWorkId).Should().BeEquivalentTo("OL1W", "OL2W");
            }
            else
            {
                lookup.Should().Throw<MetadataProviderUnavailableException>();
            }

            _clientMock.Verify(client => client.GetAuthorWorks("OL26320A", 100, 1), Times.Exactly(2));
        }

        [TestCase(true, true)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        [TestCase(false, false)]
        public void get_author_info_should_require_distinct_work_ids_after_overlapping_pages(bool useWorksEndpoint, bool recovers)
        {
            _clientMock.Setup(client => client.GetAuthor("OL26320A", true)).Returns(BuildAuthor());
            var offsets = new List<int>();

            List<OlSearchDoc> ReadPage(int offset)
            {
                offsets.Add(offset);
                return offset switch
                {
                    0 => BuildSearchDocs(2, 1),
                    2 => BuildSearchDocs(2, 2),
                    4 when recovers && offsets.Count == 4 => BuildSearchDocs(1, 4),
                    4 => new List<OlSearchDoc>(),
                    _ => throw new System.InvalidOperationException($"Unexpected offset {offset}.")
                };
            }

            if (useWorksEndpoint)
            {
                _clientMock.Setup(client => client.GetAuthorWorks("OL26320A", 100, It.IsAny<int>()))
                    .Returns((string _, int _, int offset) => new OlAuthorWorksResponse
                    {
                        Size = offset == 4 ? 3 : 4,
                        Entries = ReadPage(offset).Select(doc => new OlWorkResource { Key = doc.Key, Title = doc.Title }).ToList()
                    });
            }
            else
            {
                _clientMock.Setup(client => client.Search("author_key:OL26320A", 100, It.IsAny<int>()))
                    .Returns((string _, int _, int offset) => new OlSearchResponse
                    {
                        NumFound = offset == 4 ? 3 : 4,
                        NumFoundExact = true,
                        Docs = ReadPage(offset)
                    });
            }

            var lookup = () => Subject.GetAuthorInfo("openlibrary:author:OL26320A");
            if (recovers)
            {
                lookup().Books.Value.Select(book => book.OpenLibraryWorkId).Should().BeEquivalentTo("OL1W", "OL2W", "OL3W", "OL4W");
            }
            else
            {
                lookup.Should().Throw<MetadataProviderUnavailableException>()
                    .WithInnerException<System.InvalidOperationException>()
                    .WithMessage("*at offset 4 with 3 distinct works before the reported total 4.");
            }

            offsets.Should().Equal(0, 2, 4, 4);
        }

        [TestCase("works failed")]
        [TestCase("search failed")]
        [TestCase("repeated")]
        [TestCase("unexpected empty")]
        public void get_author_info_should_reject_incomplete_bibliography_pages(string failure)
        {
            _clientMock.Setup(client => client.GetAuthor("OL26320A", true)).Returns(BuildAuthor());
            if (failure == "works failed")
            {
                _clientMock.Setup(client => client.GetAuthorWorks("OL26320A", 100, 0))
                    .Returns(new OlAuthorWorksResponse { Size = 5, Entries = new List<OlWorkResource> { new OlWorkResource { Key = "/works/OL1W", Title = "First Work" } } });
                _clientMock.Setup(client => client.GetAuthorWorks("OL26320A", 100, 1)).Returns((OlAuthorWorksResponse)null);
            }
            else
            {
                _clientMock.Setup(client => client.Search("author_key:OL26320A", 100, 0))
                    .Returns(new OlSearchResponse { NumFound = 50, NumFoundExact = true, Docs = BuildSearchDocs(3, 0) });
                var nextPage = failure switch
                {
                    "search failed" => null,
                    "repeated" => new OlSearchResponse { Docs = BuildSearchDocs(3, 0) },
                    _ => new OlSearchResponse()
                };
                _clientMock.Setup(client => client.Search("author_key:OL26320A", 100, 3)).Returns(nextPage);
            }

            var lookup = () => Subject.GetAuthorInfo("openlibrary:author:OL26320A");

            lookup.Should().Throw<MetadataProviderUnavailableException>();
        }

        [Test]
        public void get_author_info_should_enrich_series_from_search_docs_and_expose_author_series_links()
        {
            _clientMock
                .Setup(c => c.GetAuthor("OL26320A", true))
                .Returns(BuildAuthor());

            _clientMock
                .Setup(c => c.GetAuthorWorks("OL26320A", It.IsAny<int>(), It.IsAny<int>()))
                .Returns(new OlAuthorWorksResponse
                {
                    Entries = new List<OlWorkResource>
                    {
                        new OlWorkResource
                        {
                            Key = "/works/OL45883W",
                            Title = "The Lord of the Rings",
                            FirstPublishDate = "1954"
                        }
                    },
                    Size = 1
                });

            _clientMock
                .Setup(c => c.Search("author_key:OL26320A", It.IsAny<int>(), It.IsAny<int>()))
                .Returns(new OlSearchResponse
                {
                    NumFound = 1,
                    NumFoundExact = true,
                    Docs = new List<OlSearchDoc>
                    {
                        new OlSearchDoc
                        {
                            Key = "/works/OL45883W",
                            Title = "The Lord of the Rings",
                            AuthorName = new List<string> { "J.R.R. Tolkien" },
                            AuthorKey = new List<string> { "/authors/OL26320A" },
                            SeriesWithNumber = new List<string> { "The Lord of the Rings #1" },
                            Series = new List<string> { "The Lord of the Rings" }
                        }
                    }
                });

            var author = Subject.GetAuthorInfo("openlibrary:author:OL26320A");

            author.Should().NotBeNull();
            author.Books.Value.Should().HaveCount(1);
            author.Books.Value[0].SeriesLinks.Value.Should().NotBeEmpty();
            author.Books.Value[0].SeriesLinks.Value[0].Series.Value.Title.Should().Be("The Lord of the Rings");
            author.Series?.Value.Should().NotBeEmpty();
            author.Series.Value[0].LinkItems?.Value.Should().HaveCount(1);
        }

        [Test]
        public void get_author_info_should_use_works_as_identity_source_when_search_docs_do_not_match_work_keys()
        {
            _clientMock
                .Setup(c => c.GetAuthor("OL26320A", true))
                .Returns(BuildAuthor());

            _clientMock
                .Setup(c => c.GetAuthorWorks("OL26320A", It.IsAny<int>(), It.IsAny<int>()))
                .Returns(new OlAuthorWorksResponse
                {
                    Entries = new List<OlWorkResource>
                    {
                        new OlWorkResource
                        {
                            Key = "/works/OL45883W",
                            Title = "Canonical Work Title",
                            FirstPublishDate = "1954"
                        }
                    },
                    Size = 1
                });

            _clientMock
                .Setup(c => c.Search("author_key:OL26320A", It.IsAny<int>(), It.IsAny<int>()))
                .Returns(new OlSearchResponse
                {
                    NumFound = 1,
                    NumFoundExact = true,
                    Docs = new List<OlSearchDoc>
                    {
                        new OlSearchDoc
                        {
                            Key = "/works/OL99999W",
                            Title = "Non-matching Search Result",
                            AuthorName = new List<string> { "J.R.R. Tolkien" },
                            AuthorKey = new List<string> { "/authors/OL26320A" },
                            SeriesWithNumber = new List<string> { "Non Matching Series #9" }
                        }
                    }
                });

            var author = Subject.GetAuthorInfo("openlibrary:author:OL26320A");

            author.Should().NotBeNull();
            author.Books.Value.Should().HaveCount(1);
            author.Books.Value[0].ForeignBookId.Should().Be("openlibrary:work:OL45883W");
            author.Books.Value[0].Title.Should().Be("Canonical Work Title");
            if (author.Books.Value[0].SeriesLinks != null)
            {
                author.Books.Value[0].SeriesLinks.Value.Should().BeEmpty();
            }

            if (author.Series != null)
            {
                author.Series.Value.Should().BeEmpty();
            }
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
