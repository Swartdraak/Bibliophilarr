using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Moq;
using NLog;
using NLog.Config;
using NLog.Targets;
using NUnit.Framework;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Exceptions;
using NzbDrone.Core.MetadataSource;
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

        [Test]
        public void isbn_failure_should_log_operation_without_the_lookup_value()
        {
            const string isbn = "9781442482890";
            _clientMock.Setup(client => client.GetEditionByIsbn(isbn)).Throws(new OpenLibraryException("Fixture lookup: {0}", isbn));
            var messages = new MemoryTarget { Layout = "${message} ${exception}" };
            var configuration = new LoggingConfiguration();
            configuration.AddRuleForAllLevels(messages);
            using var logFactory = new LogFactory { Configuration = configuration };
            var provider = new OpenLibraryProvider(
                _clientMock.Object,
                Mocker.GetMock<IConfigService>().Object,
                logFactory.GetLogger(nameof(OpenLibraryProvider)));

            provider.SearchByIsbn(isbn).Should().BeEmpty();

            messages.Logs.Should().Contain(message => message.Contains("OpenLibrary provider ISBN lookup failed"));
            messages.Logs.Should().NotContain(message => message.Contains(isbn));
            _clientMock.Verify(client => client.GetEditionByIsbn(isbn), Times.Once());
        }

        [TestCase("/authors/OL42A", true)]
        [TestCase("/authors/OL84A", false)]
        [TestCase(null, false)]
        public void should_fetch_only_the_exact_author_profile_without_paging_works(string responseKey, bool matches)
        {
            _clientMock.Setup(client => client.GetAuthor("OL42A", false))
                .Returns(new OlAuthorResource
                {
                    Key = responseKey,
                    Name = "Profile author",
                    Bio = "Profile biography",
                    Photos = new List<long> { -1, 123 }
                });

            var profile = Subject.GetAuthorProfileMetadata("OL42A");

            if (matches)
            {
                profile.Should().NotBeNull();
                profile.OpenLibraryAuthorId.Should().Be("OL42A");
                profile.Overview.Should().Be("Profile biography");
                var poster = profile.Images.Should().ContainSingle().Which;
                poster.SourceId.Should().Be("OL42A");
                poster.SourceProvider.Should().Be("OpenLibrary");
            }
            else
            {
                profile.Should().BeNull();
            }

            _clientMock.Verify(client => client.GetAuthor("OL42A", false), Times.Once());
            _clientMock.Verify(client => client.GetAuthorWorks(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>()), Times.Never());
            _clientMock.Verify(client => client.Search(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>()), Times.Never());
        }

        [TestCase("OL1482659A", false, true)]
        [TestCase("OL1482659A", true, false)]
        [TestCase("OL1482660A", false, false)]
        public void exact_author_work_witness_requires_one_raw_author_credit(string creditedAuthor, bool hasAnotherCredit, bool expected)
        {
            _clientMock.Setup(client => client.GetEditionByIsbn("9781442482890")).Returns(new OlEditionResource
            {
                Key = "/books/OL33516062M",
                Isbn13 = new List<string> { "9781442482890" },
                Works = new List<OlKeyRef> { new OlKeyRef { Key = "/works/OL16342401W" } }
            });
            var contributors = new List<OlWorkAuthorEntry>
            {
                new OlWorkAuthorEntry
                {
                    Author = new OlKeyRef { Key = $"/authors/{creditedAuthor}" },
                    Type = new OlKeyRef { Key = "/type/author_role" }
                }
            };
            if (hasAnotherCredit)
            {
                contributors.Add(new OlWorkAuthorEntry { Author = new OlKeyRef { Key = "/authors/OL2A" } });
            }

            _clientMock.Setup(client => client.GetWork("OL16342401W")).Returns(new OlWorkResource
            {
                Key = "/works/OL16342401W",
                Title = "Fixture",
                Authors = contributors
            });

            Subject.GetVerifiedAuthorWorkTitle("openlibrary:author:OL1482659A", "openlibrary:work:OL16342401W", "1-4424-8289-3")
                .Should().Be(expected ? "Fixture" : null);
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

        [TestCase("9781442423596")]
        [TestCase("1-4424-2359-5")]
        public void search_by_isbn_should_project_the_queried_identifier_when_it_is_not_first(string queriedIsbn)
        {
            var edition = BuildEdition();
            edition.Isbn13 = new List<string> { "9781416983309", "9781442423596" };
            _clientMock.Setup(c => c.GetEditionByIsbn(queriedIsbn)).Returns(edition);
            _clientMock.Setup(c => c.GetWork("OL45883W")).Returns(BuildWork());
            _clientMock.Setup(c => c.GetAuthor(It.IsAny<string>())).Returns(BuildAuthor());

            var found = Subject.SearchByIsbn(queriedIsbn).Should().ContainSingle().Which;

            found.Editions.Value.Should().ContainSingle(item => item.ForeignEditionId == "openlibrary:edition:OL7353617M")
                .Which.Isbn13.Should().Be("9781442423596");
            edition.Isbn13.Should().Equal("9781416983309", "9781442423596");
        }

        [Test]
        public void search_by_isbn_should_reject_an_edition_returned_without_the_queried_isbn()
        {
            const string queriedIsbn = "9781442423596";
            _clientMock.Setup(c => c.GetEditionByIsbn(queriedIsbn)).Returns(BuildEdition());
            _clientMock.Setup(c => c.GetWork(It.IsAny<string>())).Returns(BuildWork());

            Subject.SearchByIsbn(queriedIsbn).Should().BeEmpty();

            _clientMock.Verify(c => c.GetWork(It.IsAny<string>()), Times.Never);
        }

        [TestCase("unrelated")]
        [TestCase("ambiguous")]
        public void search_by_isbn_should_not_attach_the_edition_to_unproven_search_work_results(string scenario)
        {
            const string queriedIsbn = "9781442423596";
            var edition = BuildEdition();
            edition.Works = new List<OlKeyRef>();
            edition.Isbn13 = new List<string> { queriedIsbn };
            var docs = scenario == "ambiguous"
                ? new List<OlSearchDoc>
                {
                    new OlSearchDoc { Key = "/works/OL1W", Title = edition.Title, Isbn = new List<string> { queriedIsbn } },
                    new OlSearchDoc { Key = "/works/OL2W", Title = edition.Title, Isbn = new List<string> { queriedIsbn } }
                }
                : new List<OlSearchDoc>
                {
                    new OlSearchDoc { Key = "/works/OL1W", Title = "Unrelated title", Isbn = new List<string> { queriedIsbn } },
                    new OlSearchDoc { Key = "/works/OL2W", Title = edition.Title, Isbn = new List<string> { "9781416983309" } }
                };
            _clientMock.Setup(c => c.GetEditionByIsbn(queriedIsbn)).Returns(edition);
            _clientMock.Setup(c => c.Search(queriedIsbn, It.IsAny<int>(), It.IsAny<int>())).Returns(new OlSearchResponse { Docs = docs });

            var found = Subject.SearchByIsbn(queriedIsbn).Should().ContainSingle().Which;
            found.ForeignBookId.Should().Be("openlibrary:edition:OL7353617M");
            found.OpenLibraryWorkId.Should().BeNull();
            found.Editions.Value.Should().ContainSingle().Which.Isbn13.Should().Be(queriedIsbn);
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

        [TestCase("/authors/OL26320A", "/type/author_role", false, "OL26320A")]
        [TestCase("/authors/OL999A", "/type/author_role", false, null)]
        [TestCase("/authors/OL26320A", "/type/editor_role", false, null)]
        [TestCase("/authors/OL26320A", "/type/author_role", true, null)]
        public void get_author_info_should_verify_only_an_exclusive_author_credit(
            string creditedPath, string role, bool multipleCredits, string expectedCredit)
        {
            var work = BuildWork();
            work.Authors[0].Author.Key = creditedPath;
            work.Authors[0].Type.Key = role;
            if (multipleCredits)
            {
                work.Authors.Add(new OlWorkAuthorEntry { Author = new OlKeyRef { Key = "/authors/OL99A" } });
            }

            _clientMock.Setup(client => client.GetAuthor("OL26320A", true)).Returns(BuildAuthor());
            _clientMock.Setup(client => client.GetAuthorWorks("OL26320A", 100, 0))
                .Returns(new OlAuthorWorksResponse { Size = 1, Entries = new List<OlWorkResource> { work } });
            _clientMock.Setup(client => client.Search("author_key:OL26320A", It.IsAny<int>(), It.IsAny<int>()))
                .Returns(new OlSearchResponse { Docs = new List<OlSearchDoc>() });

            var book = Subject.GetAuthorInfo("openlibrary:author:OL26320A").Books.Value.Should().ContainSingle().Which;

            book.VerifiedOpenLibraryAuthorId.Should().Be(expectedCredit);
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

        [TestCase(false, true, false)]
        [TestCase(true, false, false)]
        [TestCase(true, true, true)]
        public void should_classify_only_matching_author_authoritative_early_end(bool authoritative, bool exactAuthor, bool recoverable)
        {
            var author = BuildAuthor();
            if (!exactAuthor)
            {
                author.Key = "/authors/OL999A";
            }

            _clientMock.Setup(client => client.GetAuthor("OL26320A", true)).Returns(author);
            if (authoritative)
            {
                _clientMock.Setup(client => client.GetAuthorWorks("OL26320A", 100, 0))
                    .Returns(new OlAuthorWorksResponse { Size = 2, Entries = new List<OlWorkResource> { BuildWork() } });
                _clientMock.Setup(client => client.GetAuthorWorks("OL26320A", 100, 1))
                    .Returns(new OlAuthorWorksResponse { Size = 2 });
            }
            else
            {
                _clientMock.Setup(client => client.Search("author_key:OL26320A", 100, 0))
                    .Returns(new OlSearchResponse { NumFound = 2, NumFoundExact = true, Docs = BuildSearchDocs(1, 0) });
                _clientMock.Setup(client => client.Search("author_key:OL26320A", 100, 1))
                    .Returns(new OlSearchResponse { Docs = new List<OlSearchDoc>() });
            }

            var lookup = () => Subject.GetAuthorInfo("openlibrary:author:OL26320A");

            lookup.Should().Throw<MetadataProviderUnavailableException>().Which.AuthoritativeBibliographyEndedEarly.Should().Be(recoverable);
            _clientMock.Verify(client => client.GetExactEditionByIsbn(It.IsAny<string>()), Times.Never());
        }

        [TestCase("exact", true)]
        [TestCase("wrong ISBN", false)]
        [TestCase("noncanonical edition", false)]
        [TestCase("two works", false)]
        [TestCase("wrong work response", false)]
        [TestCase("wrong credited author", false)]
        [TestCase("multiple credits", false)]
        public void should_resolve_an_isbn_only_to_one_exclusively_credited_open_library_work(string scenario, bool verified)
        {
            const string isbn = "9781442482890";
            var work = BuildWork();
            var edition = new OlEditionResource
            {
                Key = scenario == "noncanonical edition" ? "/works/OL123M" : "/books/OL123M",
                Isbn13 = new List<string> { scenario == "wrong ISBN" ? "9781455546176" : isbn },
                Works = new List<OlKeyRef> { new OlKeyRef { Key = work.Key } }
            };
            var editionWorkKey = work.Key;
            if (scenario == "two works")
            {
                edition.Works.Add(new OlKeyRef { Key = "/works/OL999W" });
            }

            if (scenario == "wrong work response")
            {
                work.Key = "/works/OL999W";
            }

            if (scenario == "wrong credited author")
            {
                work.Authors[0].Author.Key = "/authors/OL999A";
            }

            if (scenario == "multiple credits")
            {
                work.Authors.Add(new OlWorkAuthorEntry { Author = new OlKeyRef { Key = "/authors/OL999A" } });
            }

            _clientMock.Setup(client => client.GetExactEditionByIsbn(isbn)).Returns(edition);
            _clientMock.Setup(client => client.GetWork(editionWorkKey.Replace("/works/", string.Empty))).Returns(work);

            var result = ((IResolveAuthorWorkByIsbn)Subject).GetVerifiedAuthorWorkByIsbn("openlibrary:author:OL26320A", isbn);

            if (verified)
            {
                result.Should().Be(($"openlibrary:work:{editionWorkKey.Replace("/works/", string.Empty)}", work.Title));
            }
            else
            {
                result.Should().BeNull();
            }
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
