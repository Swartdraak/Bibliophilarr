using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Sockets;
using FluentAssertions;
using Moq;
using NLog;
using NLog.Config;
using NLog.Targets;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.Exceptions;
using NzbDrone.Core.HealthCheck;
using NzbDrone.Core.HealthCheck.Checks;
using NzbDrone.Core.Localization;
using NzbDrone.Core.MediaCover;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.MetadataSource;

namespace NzbDrone.Core.Test.MetadataSource
{
    [TestFixture]
    public class MetadataProviderOrchestratorFixture
    {
        private class TestRegistry : IMetadataProviderRegistry
        {
            private readonly IReadOnlyList<IMetadataProvider> _providers;

            public TestRegistry(IReadOnlyList<IMetadataProvider> providers)
            {
                _providers = providers;
            }

            public IReadOnlyList<IMetadataProvider> GetProviders()
            {
                return _providers;
            }

            public void RegisterProvider(IMetadataProvider provider)
            {
            }

            public void UnregisterProvider(string providerName)
            {
            }

            public IMetadataProvider GetProvider(string providerName)
            {
                return _providers.FirstOrDefault(p => p.ProviderName == providerName);
            }

            public List<IMetadataProvider> GetAllProviders()
            {
                return _providers.ToList();
            }

            public List<IMetadataProvider> GetEnabledProviders()
            {
                return _providers.Where(p => p.IsEnabled).ToList();
            }

            public List<IMetadataProvider> GetProvidersWithCapability(string capability)
            {
                return new List<IMetadataProvider>();
            }

            public List<ISearchForNewBookV2> GetBookSearchProviders()
            {
                return new List<ISearchForNewBookV2>();
            }

            public List<ISearchForNewAuthorV2> GetAuthorSearchProviders()
            {
                return new List<ISearchForNewAuthorV2>();
            }

            public List<IProvideBookInfoV2> GetBookInfoProviders()
            {
                return new List<IProvideBookInfoV2>();
            }

            public List<IProvideAuthorInfoV2> GetAuthorInfoProviders()
            {
                return new List<IProvideAuthorInfoV2>();
            }

            public void EnableProvider(string providerName)
            {
            }

            public void DisableProvider(string providerName)
            {
            }

            public void SetProviderPriority(string providerName, int priority)
            {
            }

            public Dictionary<string, ProviderHealthStatus> GetProvidersHealthStatus()
            {
                return new Dictionary<string, ProviderHealthStatus>();
            }

            public void UpdateProviderHealth(string providerName, ProviderHealthStatus healthStatus)
            {
            }

            public int Count => _providers.Count;

            public T Execute<T>(Func<IMetadataProvider, T> operation, string operationName)
                where T : class
            {
                foreach (var provider in _providers)
                {
                    var result = operation(provider);
                    if (result != null)
                    {
                        return result;
                    }
                }

                return null;
            }
        }

        private class FailingSearchProvider : IMetadataProvider, ISearchForNewBook
        {
            public string ProviderName => "Failing";
            public int Priority => 1;
            public bool IsEnabled => true;
            public bool SupportsAuthorSearch => false;
            public bool SupportsBookSearch => true;
            public bool SupportsIsbnLookup => true;
            public bool SupportsSeriesInfo => false;
            public bool SupportsCoverImages => false;

            public List<Book> SearchForNewBook(string title, string author, bool getAllEditions = true)
            {
                throw new InvalidOperationException("simulated failure");
            }

            public List<Book> SearchByIsbn(string isbn)
            {
                throw new InvalidOperationException("simulated failure");
            }

            public List<Book> SearchByAsin(string asin)
            {
                return new List<Book>();
            }

            public List<Book> SearchByExternalId(string idType, string id)
            {
                return new List<Book>();
            }
        }

        private class SuccessfulSearchProvider : IMetadataProvider, ISearchForNewBook
        {
            public string ProviderName => "Successful";
            public int Priority => 2;
            public bool IsEnabled => true;
            public bool SupportsAuthorSearch => false;
            public bool SupportsBookSearch => true;
            public bool SupportsIsbnLookup => true;
            public bool SupportsSeriesInfo => false;
            public bool SupportsCoverImages => false;

            public List<Book> SearchForNewBook(string title, string author, bool getAllEditions = true)
            {
                return new List<Book>
                {
                    new Book
                    {
                        ForeignBookId = "OL123W",
                        OpenLibraryWorkId = "OL123W",
                        Title = "Fallback Hit",
                        Editions = new List<Edition> { new Edition { ForeignEditionId = "OL321M", Monitored = true } },
                        AuthorMetadata = new AuthorMetadata { ForeignAuthorId = "OL1A", Name = "Author" },
                        Author = new Author { Metadata = new AuthorMetadata { ForeignAuthorId = "OL1A", Name = "Author" } }
                    }
                };
            }

            public List<Book> SearchByIsbn(string isbn)
            {
                return SearchForNewBook(isbn, null, false);
            }

            public List<Book> SearchByAsin(string asin)
            {
                return new List<Book>();
            }

            public List<Book> SearchByExternalId(string idType, string id)
            {
                return SearchForNewBook(id, null, false);
            }
        }

        [Test]
        public void should_fallback_to_secondary_provider_when_primary_fails()
        {
            var registry = new TestRegistry(new IMetadataProvider[]
            {
                new FailingSearchProvider(),
                new SuccessfulSearchProvider()
            });

            var telemetry = new MetadataProviderTelemetryService();
            var orchestrator = new MetadataProviderOrchestrator(registry, telemetry, LogManager.GetCurrentClassLogger());

            var result = orchestrator.SearchForNewBook("fallback", null, false);

            result.Should().NotBeNull();
            result.Should().HaveCount(1);
            result[0].Title.Should().Be("Fallback Hit");

            var stats = telemetry.GetSnapshots();
            stats.Should().ContainSingle(x => x.ProviderName == "Failing");
            stats.Should().ContainSingle(x => x.ProviderName == "Successful" && x.FallbackHits == 1);
            telemetry.GetOperationSnapshots().Should().ContainSingle(x => x.ProviderName == "Successful" && x.OperationName == "search-for-new-book" && x.FallbackHits == 1);
        }

        [Test]
        public void should_respect_registry_order_and_stop_on_first_result()
        {
            var first = new OrderedSearchProvider("First", 1, "First Hit");
            var second = new OrderedSearchProvider("Second", 2, "Second Hit");

            var registry = new TestRegistry(new IMetadataProvider[] { first, second });
            var telemetry = new MetadataProviderTelemetryService();
            var orchestrator = new MetadataProviderOrchestrator(registry, telemetry, LogManager.GetCurrentClassLogger());

            var result = orchestrator.SearchForNewBook("order", null, false);

            result.Should().HaveCount(1);
            result[0].Title.Should().Be("First Hit");
            first.CallCount.Should().Be(1);
            second.CallCount.Should().Be(0);
        }

        [Test]
        public void library_import_should_try_the_next_provider_while_recording_nonempty_provider_responses()
        {
            var first = new OrderedSearchProvider("First", 1, "First Hit");
            var second = new OrderedSearchProvider("Second", 2, "Second Hit");
            var third = new OrderedSearchProvider("Third", 3, "Third Hit");
            var registry = new TestRegistry(new IMetadataProvider[] { first, second, third });
            var telemetry = new MetadataProviderTelemetryService();
            var orchestrator = new MetadataProviderOrchestrator(registry, telemetry, LogManager.GetCurrentClassLogger());
            var inspected = new List<string>();

            orchestrator.SearchForLibraryImport("target", "author", books =>
            {
                var title = books.Single().Title;
                inspected.Add(title);
                return title == "Second Hit";
            });

            inspected.Should().Equal("First Hit", "Second Hit");
            third.CallCount.Should().Be(0);
            telemetry.GetOperationSnapshots().Should().ContainSingle(snapshot => snapshot.ProviderName == "First" &&
                snapshot.Successes == 1 && snapshot.FallbackHits == 0 && snapshot.TotalResultCount == 1);
            telemetry.GetOperationSnapshots().Should().ContainSingle(snapshot => snapshot.ProviderName == "Second" &&
                snapshot.Successes == 1 && snapshot.FallbackHits == 1 && snapshot.TotalResultCount == 1);
        }

        [TestCase("isbn:9781442482890")]
        [TestCase(" ISBN:1-4424-8289-3 ")]
        public void explicit_isbn_search_should_skip_work_summaries_and_nonmatching_editions(string term)
        {
            const string isbn = "9781442482890";
            var hardcover = CreateIsbnProvider("Hardcover", out var hardcoverSearch);
            hardcoverSearch.Setup(provider => provider.SearchByIsbn(isbn))
                .Returns(new List<Book> { IsbnLookupBook("hardcover:work:292650", "hardcover:edition:292650", isbn) });
            var openLibrary = CreateIsbnProvider("OpenLibrary", out var openLibrarySearch);
            var exact = IsbnLookupBook("openlibrary:work:OL21108361W", "openlibrary:edition:OL33516062M", isbn);
            exact.Editions.Value.Insert(0, new Edition { ForeignEditionId = exact.ForeignBookId, Isbn13 = isbn, Monitored = true });
            openLibrarySearch.Setup(provider => provider.SearchByIsbn(isbn))
                .Returns(new List<Book>
                {
                    IsbnLookupBook("openlibrary:work:OL21108361W", "openlibrary:work:OL21108361W", isbn),
                    IsbnLookupBook("openlibrary:work:OL2W", "openlibrary:edition:OL2M", "9781442482883"),
                    IsbnLookupBook("openlibrary:work:OL3W", "openlibrary:edition:", isbn),
                    exact
                });
            var googleBooks = CreateIsbnProvider("GoogleBooks", out var googleBooksSearch);
            var registry = new TestRegistry(new[] { hardcover, openLibrary, googleBooks });
            var orchestrator = new MetadataProviderOrchestrator(registry, new MetadataProviderTelemetryService(), LogManager.GetCurrentClassLogger());

            var result = orchestrator.SearchForNewBook(term, null);

            var match = result.Should().ContainSingle().Which;
            match.Should().NotBeSameAs(exact);
            match.Author.Should().BeSameAs(exact.Author);
            match.Editions.Value.Should().ContainSingle().Which.ForeignEditionId.Should().Be("openlibrary:edition:OL33516062M");
            exact.Editions.Value.Select(edition => edition.ForeignEditionId)
                .Should().Equal("openlibrary:work:OL21108361W", "openlibrary:edition:OL33516062M");
            hardcoverSearch.Verify(provider => provider.SearchByIsbn(isbn), Times.Once);
            openLibrarySearch.Verify(provider => provider.SearchByIsbn(isbn), Times.Once);
            googleBooksSearch.Verify(provider => provider.SearchByIsbn(It.IsAny<string>()), Times.Never);
            hardcoverSearch.Verify(provider => provider.SearchForNewBook(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>()), Times.Never);
        }

        [Test]
        public void explicit_isbn_search_should_accept_a_real_hardcover_edition_with_the_same_numeric_work_id()
        {
            const string isbn = "9781442482890";
            var hardcover = CreateIsbnProvider("Hardcover", out var hardcoverSearch);
            var exact = IsbnLookupBook("hardcover:work:42", "hardcover:source-edition:42", isbn);
            hardcoverSearch.Setup(provider => provider.SearchByIsbn(isbn)).Returns(new List<Book> { exact });
            var registry = new TestRegistry(new[] { hardcover });
            var orchestrator = new MetadataProviderOrchestrator(registry, new MetadataProviderTelemetryService(), LogManager.GetCurrentClassLogger());

            orchestrator.SearchForNewBook($"isbn:{isbn}", null).Should().ContainSingle()
                .Which.Editions.Value.Should().ContainSingle().Which.ForeignEditionId.Should().Be("hardcover:source-edition:42");
        }

        [TestCase("isbn:9781442482891")]
        [TestCase("isbn:")]
        public void explicit_isbn_search_should_stop_on_invalid_identifiers_without_querying_providers(string term)
        {
            var provider = CreateIsbnProvider("Hardcover", out var search);
            var registry = new TestRegistry(new[] { provider });
            var orchestrator = new MetadataProviderOrchestrator(registry, new MetadataProviderTelemetryService(), LogManager.GetCurrentClassLogger());

            orchestrator.SearchForNewBook(term, null).Should().BeEmpty();

            search.Verify(provider => provider.SearchByIsbn(It.IsAny<string>()), Times.Never);
            search.Verify(provider => provider.SearchForNewBook(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>()), Times.Never);
        }

        [TestCase("isbn:9781442482890", "Ellen Hopkins")]
        [TestCase("Traffick", null)]
        [TestCase("9781442482890", null)]
        [TestCase("asin:B0171WIS9Y", null)]
        [TestCase(null, null)]
        [TestCase("", null)]
        public void normal_book_search_should_keep_existing_routing(string term, string author)
        {
            var provider = CreateIsbnProvider("Hardcover", out var search);
            var existing = IsbnLookupBook("hardcover:work:42", "hardcover:edition:42", "9781442482890");
            search.Setup(candidate => candidate.SearchForNewBook(term, author, false)).Returns(new List<Book> { existing });
            var registry = new TestRegistry(new[] { provider });
            var orchestrator = new MetadataProviderOrchestrator(registry, new MetadataProviderTelemetryService(), LogManager.GetCurrentClassLogger());

            orchestrator.SearchForNewBook(term, author, false).Should().ContainSingle().Which.Should().BeSameAs(existing);

            search.Verify(candidate => candidate.SearchByIsbn(It.IsAny<string>()), Times.Never);
        }

        [Test]
        public void direct_isbn_search_should_keep_existing_provider_routing()
        {
            const string isbn = "9781442482890";
            var provider = CreateIsbnProvider("Hardcover", out var search);
            var existing = IsbnLookupBook("hardcover:work:42", "hardcover:edition:42", isbn);
            search.Setup(candidate => candidate.SearchByIsbn(isbn)).Returns(new List<Book> { existing });
            var registry = new TestRegistry(new[] { provider });
            var orchestrator = new MetadataProviderOrchestrator(registry, new MetadataProviderTelemetryService(), LogManager.GetCurrentClassLogger());

            orchestrator.SearchByIsbn(isbn).Should().ContainSingle().Which.Should().BeSameAs(existing);
        }

        private static IMetadataProvider CreateIsbnProvider(string name, out Mock<ISearchForNewBook> search)
        {
            var provider = new Mock<IMetadataProvider>();
            provider.SetupGet(candidate => candidate.ProviderName).Returns(name);
            provider.SetupGet(candidate => candidate.SupportsBookSearch).Returns(true);
            provider.SetupGet(candidate => candidate.SupportsIsbnLookup).Returns(true);
            search = provider.As<ISearchForNewBook>();
            return provider.Object;
        }

        private static Book IsbnLookupBook(string workId, string editionId, string isbn)
        {
            return new Book
            {
                ForeignBookId = workId,
                Editions = new List<Edition> { new Edition { ForeignEditionId = editionId, Isbn13 = isbn } }
            };
        }

        [Test]
        public void should_query_hardcover_first_and_not_call_openlibrary_when_hardcover_returns_results()
        {
            var hardcover = new OrderedSearchProvider("Hardcover", 1, "Hardcover Hit");
            var openLibrary = new OrderedSearchProvider("OpenLibrary", 2, "OpenLibrary Hit");

            var registry = new TestRegistry(new IMetadataProvider[] { hardcover, openLibrary });
            var telemetry = new MetadataProviderTelemetryService();
            var orchestrator = new MetadataProviderOrchestrator(registry, telemetry, LogManager.GetCurrentClassLogger());

            var result = orchestrator.SearchForNewBook("order", null, false);

            result.Should().HaveCount(1);
            result[0].Title.Should().Be("Hardcover Hit");
            hardcover.CallCount.Should().Be(1);
            openLibrary.CallCount.Should().Be(0);
        }

        [Test]
        public void should_record_success_failure_and_null_telemetry_counters()
        {
            var registry = new TestRegistry(new IMetadataProvider[]
            {
                new ThrowingSearchProvider(),
                new NullSearchProvider(),
                new OrderedSearchProvider("Winner", 3, "Telemetry Hit")
            });

            var telemetry = new MetadataProviderTelemetryService();
            var orchestrator = new MetadataProviderOrchestrator(registry, telemetry, LogManager.GetCurrentClassLogger());

            var result = orchestrator.SearchForNewBook("telemetry", null, false);

            result.Should().HaveCount(1);

            var failing = telemetry.GetSnapshots().Single(x => x.ProviderName == "Throwing");
            failing.Calls.Should().Be(1);
            failing.Failures.Should().Be(1);
            failing.Successes.Should().Be(0);

            var nullProvider = telemetry.GetSnapshots().Single(x => x.ProviderName == "NullProvider");
            nullProvider.Calls.Should().Be(1);
            nullProvider.NullResults.Should().Be(1);
            nullProvider.Failures.Should().Be(0);

            var winner = telemetry.GetSnapshots().Single(x => x.ProviderName == "Winner");
            winner.Calls.Should().Be(1);
            winner.Successes.Should().Be(1);
            winner.FallbackHits.Should().Be(1);

            telemetry.GetOperationSnapshots().Should().ContainSingle(x => x.ProviderName == "Winner" && x.OperationName == "search-for-new-book" && x.FallbackHits == 1);
        }

        [Test]
        public void should_fallback_to_secondary_provider_for_changed_author_lookup_when_primary_fails()
        {
            var registry = new TestRegistry(new IMetadataProvider[]
            {
                new FailingAuthorInfoProvider(),
                new SuccessfulAuthorInfoProvider()
            });

            var telemetry = new MetadataProviderTelemetryService();
            var orchestrator = new MetadataProviderOrchestrator(registry, telemetry, LogManager.GetCurrentClassLogger());
            var changedAuthors = orchestrator.GetChangedAuthors(DateTime.UtcNow.AddDays(-1));

            changedAuthors.Should().NotBeNull();
            changedAuthors.Should().Contain("openlibrary:author:OL23919A");

            var stats = telemetry.GetSnapshots();
            stats.Should().ContainSingle(x => x.ProviderName == "FailingAuthorInfo");
            stats.Should().ContainSingle(x => x.ProviderName == "SuccessfulAuthorInfo" && x.FallbackHits == 1);
        }

        [Test]
        public void should_record_get_author_info_operation_fallback_telemetry()
        {
            var registry = new TestRegistry(new IMetadataProvider[]
            {
                new FailingAuthorInfoProvider(),
                new SuccessfulGetAuthorInfoProvider()
            });

            var telemetry = new MetadataProviderTelemetryService();
            var orchestrator = new MetadataProviderOrchestrator(registry, telemetry, LogManager.GetCurrentClassLogger());

            var result = orchestrator.GetAuthorInfo("openlibrary:author:OL23919A", true);

            result.Should().NotBeNull();
            result.Metadata.Value.ForeignAuthorId.Should().Be("openlibrary:author:OL23919A");

            telemetry.GetOperationSnapshots().Should().ContainSingle(x =>
                x.ProviderName == "SuccessfulGetAuthorInfo" &&
                x.OperationName == "get-author-info" &&
                x.FallbackHits == 1 &&
                x.Successes == 1);
        }

        [Test]
        public void should_not_route_openlibrary_author_ids_to_incompatible_provider()
        {
            var compatible = new SuccessfulOpenLibraryAuthorInfoProvider();
            var incompatible = new IncompatibleAuthorInfoProvider();

            var registry = new TestRegistry(new IMetadataProvider[]
            {
                incompatible,
                compatible
            });

            var telemetry = new MetadataProviderTelemetryService();
            var orchestrator = new MetadataProviderOrchestrator(registry, telemetry, LogManager.GetCurrentClassLogger());

            var result = orchestrator.GetAuthorInfo("openlibrary:author:OL23919A", true);

            result.Should().NotBeNull();
            result.Metadata.Value.ForeignAuthorId.Should().Be("openlibrary:author:OL23919A");
            incompatible.CallCount.Should().Be(0);
        }

        [Test]
        public void should_not_route_openlibrary_work_ids_to_incompatible_book_info_provider()
        {
            var compatible = new SuccessfulOpenLibraryBookInfoProvider();
            var incompatible = new IncompatibleBookInfoProvider();

            var registry = new TestRegistry(new IMetadataProvider[]
            {
                incompatible,
                compatible
            });

            var telemetry = new MetadataProviderTelemetryService();
            var orchestrator = new MetadataProviderOrchestrator(registry, telemetry, LogManager.GetCurrentClassLogger());

            var result = orchestrator.GetBookInfo("openlibrary:work:OL45883W");

            result.Should().NotBeNull();
            result.Item2.ForeignBookId.Should().Be("openlibrary:work:OL45883W");
            incompatible.CallCount.Should().Be(0);
        }

        [Test]
        public void should_enrich_missing_overview_and_cover_only_from_matching_isbn()
        {
            var edition = new Edition
            {
                ForeignEditionId = "hardcover:edition:123",
                Isbn13 = "9780141439518",
                Asin = "B000000001",
                Language = "eng",
                PageCount = 317
            };
            var mismatchedEdition = new Edition
            {
                Isbn13 = "9780141439525",
                Overview = "Unrelated overview",
                Images = new List<MediaCover.MediaCover> { new MediaCover.MediaCover(MediaCoverTypes.Cover, "https://covers.example/wrong.jpg") }
            };
            var matchedEdition = new Edition
            {
                ForeignEditionId = "openlibrary:edition:OL123M",
                Isbn13 = "0-14-143951-3",
                PageCount = 500,
                Publisher = "Verified publisher",
                ReleaseDate = new DateTime(2001, 2, 3),
                Overview = "Verified overview",
                Images = new List<MediaCover.MediaCover> { new MediaCover.MediaCover(MediaCoverTypes.Cover, "https://covers.example/match.jpg") }
            };

            EnrichFromIsbnFixture(edition, mismatchedEdition, matchedEdition);

            edition.Overview.Should().Be("Verified overview");
            edition.Images.Should().ContainSingle().Which.Url.Should().Be("https://covers.example/match.jpg");
            edition.Images[0].SourceProvider.Should().Be("OpenLibrary");
            edition.Images[0].SourceId.Should().Be("openlibrary:edition:OL123M");
            edition.Publisher.Should().Be("Verified publisher");
            edition.ReleaseDate.Should().Be(new DateTime(2001, 2, 3));
            edition.ForeignEditionId.Should().Be("hardcover:edition:123");
            edition.PageCount.Should().Be(317);
        }

        [Test]
        public void should_keep_missing_fields_when_supplementary_result_has_a_different_isbn()
        {
            var edition = new Edition { Isbn13 = "9780141439518" };
            var mismatchedEdition = new Edition
            {
                Isbn13 = "9780141439525",
                PageCount = 300,
                Asin = "B000000002",
                Overview = "Unrelated overview"
            };

            EnrichFromIsbnFixture(edition, mismatchedEdition);

            edition.Asin.Should().BeNull();
            edition.PageCount.Should().Be(0);
            edition.Overview.Should().BeEmpty();
            edition.Images.Should().BeEmpty();
        }

        [Test]
        public void should_not_choose_one_of_multiple_provider_editions_claiming_the_same_isbn()
        {
            const string isbn = "9780141439518";
            var edition = new Edition { Isbn13 = isbn };

            EnrichFromIsbnFixture(
                edition,
                new Edition { ForeignEditionId = "openlibrary:edition:OL123M", Isbn13 = isbn, Publisher = "First publisher", PageCount = 1 },
                new Edition { ForeignEditionId = "openlibrary:edition:OL456M", Isbn13 = isbn, Publisher = "Second publisher", PageCount = 2 });

            edition.Publisher.Should().BeNull();
            edition.PageCount.Should().Be(0);
        }

        [TestCase("Fixture Work", "Fixture Author", "OL123W", true)]
        [TestCase("Different Work", "Fixture Author", "OL123W", false)]
        [TestCase("Fixture Work", "Different Author", "OL123W", false)]
        [TestCase("Fixture Work", "Fixture Author", "OL123M", false)]
        public void should_stage_only_corroborated_work_sources(string sourceTitle, string sourceAuthor, string sourceId, bool expectSource)
        {
            const string isbn = "9780141439518";
            var candidate = new Book
            {
                ForeignBookId = $"openlibrary:work:{sourceId}",
                Title = sourceTitle,
                AuthorMetadata = new AuthorMetadata { Name = sourceAuthor, OpenLibraryAuthorId = "OL1A" }
            };
            var matchedEdition = new Edition { Isbn13 = isbn, Book = candidate, Publisher = "Supplementary publisher" };
            candidate.Editions = new List<Edition> { matchedEdition };
            var book = EnrichFromIsbnFixture(new Edition { Isbn13 = isbn }, matchedEdition);

            book.ForeignBookId.Should().Be("hardcover:work:123");
            book.Editions.Value.Single().Publisher.Should().Be(
                sourceTitle == "Fixture Work" && sourceAuthor == "Fixture Author" ? matchedEdition.Publisher : null);
            if (expectSource)
            {
                book.PendingSourceAssociations.Should().ContainSingle().Which
                    .Should().BeEquivalentTo(new CatalogueSourceAssociation
                    {
                        Provider = "OpenLibrary",
                        ProviderId = "OL123W",
                        Evidence = "exact-isbn-and-title-and-author"
                    });
            }
            else
            {
                book.PendingSourceAssociations.Should().BeEmpty();
            }
        }

        [Test]
        public void should_keep_the_series_description_from_a_previous_refresh_when_the_provider_omits_it()
        {
            var series = new Series { ForeignSeriesId = "hardcover:series:123", Description = "Existing description" };

            series.UseMetadataFrom(new Series { ForeignSeriesId = series.ForeignSeriesId, Title = "Current series title" });

            series.Title.Should().Be("Current series title");
            series.Description.Should().Be("Existing description");
        }

        [TestCase(true)]
        [TestCase(false)]
        public void should_retain_alternate_source_metadata_when_the_refresh_has_the_same_identity(bool sameIdentity)
        {
            var author = new AuthorMetadata { ForeignAuthorId = "hardcover:author:1", OpenLibraryAuthorId = "OL1A" };
            var book = new Book { ForeignBookId = "hardcover:work:1", OpenLibraryWorkId = "OL1W", ReleaseDate = new DateTime(2000, 1, 1) };

            author.UseMetadataFrom(new AuthorMetadata { ForeignAuthorId = sameIdentity ? author.ForeignAuthorId : "hardcover:author:2" });
            book.UseMetadataFrom(new Book { ForeignBookId = sameIdentity ? book.ForeignBookId : "hardcover:work:2" });

            author.OpenLibraryAuthorId.Should().Be(sameIdentity ? "OL1A" : null);
            book.OpenLibraryWorkId.Should().Be(sameIdentity ? "OL1W" : null);
            book.ReleaseDate.Should().Be(sameIdentity ? new DateTime(2000, 1, 1) : null);
        }

        [TestCase(true)]
        [TestCase(false)]
        public void should_keep_enriched_fields_only_when_refresh_preserves_edition_identity(bool sameEdition)
        {
            var edition = new Edition
            {
                ForeignEditionId = "hardcover:edition:1",
                Isbn13 = "9780141439518",
                Asin = "B000000001",
                Language = "eng",
                Publisher = "Verified publisher",
                PageCount = 317,
                ReleaseDate = new DateTime(2001, 2, 3)
            };

            edition.UseMetadataFrom(new Edition { ForeignEditionId = sameEdition ? edition.ForeignEditionId : "hardcover:edition:2" });

            edition.Isbn13.Should().Be(sameEdition ? "9780141439518" : null);
            edition.Asin.Should().Be(sameEdition ? "B000000001" : null);
            edition.Language.Should().Be(sameEdition ? "eng" : null);
            edition.Publisher.Should().Be(sameEdition ? "Verified publisher" : null);
            edition.PageCount.Should().Be(sameEdition ? 317 : 0);
            edition.ReleaseDate.Should().Be(sameEdition ? new DateTime(2001, 2, 3) : null);
        }

        private static Book EnrichFromIsbnFixture(Edition edition, params Edition[] supplementaryEditions)
        {
            const string workId = "hardcover:work:123";

            var primary = new Mock<IMetadataProvider>();
            primary.SetupGet(provider => provider.ProviderName).Returns("Hardcover");
            primary.SetupGet(provider => provider.SupportsBookSearch).Returns(true);
            var book = new Book
            {
                ForeignBookId = workId,
                Title = "Fixture Work",
                AuthorMetadata = new AuthorMetadata { Name = "Fixture Author" },
                ReleaseDate = new DateTime(2000, 1, 1),
                Editions = new List<Edition> { edition }
            };
            primary.As<IProvideBookInfo>().Setup(provider => provider.GetBookInfo(workId))
                .Returns(Tuple.Create("hardcover:author:1", book, new List<AuthorMetadata>()));

            var supplementary = new Mock<IMetadataProvider>();
            supplementary.SetupGet(provider => provider.ProviderName).Returns("OpenLibrary");
            supplementary.SetupGet(provider => provider.SupportsIsbnLookup).Returns(true);
            supplementary.SetupGet(provider => provider.SupportsCoverImages).Returns(true);
            supplementary.As<ISearchForNewBook>().Setup(provider => provider.SearchByIsbn(edition.Isbn13))
                .Returns(supplementaryEditions.Select(result => result.Book?.Value ?? new Book
                {
                    Editions = new List<Edition> { result }
                }).ToList());

            var registry = new TestRegistry(new[] { primary.Object, supplementary.Object });
            var orchestrator = new MetadataProviderOrchestrator(registry, new MetadataProviderTelemetryService(), LogManager.GetCurrentClassLogger());
            return orchestrator.GetBookInfo(workId).Item2;
        }

        [TestCase("Verified Author", null, "9780141439518", true)]
        [TestCase("Wrong Author", null, "9780141439518", false)]
        [TestCase("Verified Author", "OL2A", "9780141439518", false)]
        [TestCase("Verified Author", null, "9780141439525", false)]
        public void should_require_author_and_exact_book_evidence_before_filling_a_missing_portrait(string matchedName, string existingOpenLibraryId, string resultIsbn, bool expectPortrait)
        {
            const string authorId = "hardcover:author:123";
            const string isbn = "9780141439518";
            const string portrait = "https://covers.example/author.jpg";

            var primary = new Mock<IMetadataProvider>();
            primary.SetupGet(provider => provider.ProviderName).Returns("Hardcover");
            primary.SetupGet(provider => provider.SupportsAuthorSearch).Returns(true);
            var author = new Author
            {
                Metadata = new AuthorMetadata
                {
                    ForeignAuthorId = authorId,
                    Name = "Verified Author",
                    OpenLibraryAuthorId = existingOpenLibraryId
                },
                Books = new List<Book> { new Book { Editions = new List<Edition> { new Edition { Isbn13 = isbn } } } }
            };
            primary.As<IProvideAuthorInfo>().Setup(provider => provider.GetAuthorInfo(authorId, true)).Returns(author);

            var supplementary = new Mock<IMetadataProvider>();
            supplementary.SetupGet(provider => provider.ProviderName).Returns("OpenLibrary");
            supplementary.SetupGet(provider => provider.SupportsIsbnLookup).Returns(true);
            supplementary.SetupGet(provider => provider.SupportsCoverImages).Returns(true);
            supplementary.As<ISearchForNewBook>().Setup(provider => provider.SearchByIsbn(isbn))
                .Returns(new List<Book>
                {
                    new Book
                    {
                        Editions = new List<Edition> { new Edition { Isbn13 = resultIsbn } },
                        AuthorMetadata = new AuthorMetadata
                        {
                            Name = matchedName,
                            OpenLibraryAuthorId = "OL1A",
                            Overview = "Corroborated biography",
                            Born = new DateTime(1875, 1, 13),
                            Images = new List<MediaCover.MediaCover> { new MediaCover.MediaCover(MediaCoverTypes.Poster, portrait) }
                        }
                    }
                });

            var registry = new TestRegistry(new[] { primary.Object, supplementary.Object });
            var orchestrator = new MetadataProviderOrchestrator(registry, new MetadataProviderTelemetryService(), LogManager.GetCurrentClassLogger());
            var metadata = orchestrator.GetAuthorInfo(authorId).Metadata.Value;

            metadata.ForeignAuthorId.Should().Be(authorId);
            metadata.OpenLibraryAuthorId.Should().Be(existingOpenLibraryId);

            if (expectPortrait)
            {
                metadata.Images.Should().ContainSingle().Which.Url.Should().Be(portrait);
                metadata.Images[0].SourceProvider.Should().Be("OpenLibrary");
                metadata.Images[0].SourceId.Should().Be("OL1A");
                metadata.Overview.Should().Be("Corroborated biography");
                metadata.Born.Should().Be(new DateTime(1875, 1, 13));
                metadata.PendingSourceAssociations.Should().BeEmpty();
            }
            else
            {
                metadata.Images.Should().BeEmpty();
                metadata.Overview.Should().BeNull();
                metadata.PendingSourceAssociations.Should().BeEmpty();
            }
        }

        [Test]
        public void should_log_diagnostic_when_no_enabled_provider_is_compatible_with_scoped_id()
        {
            // Only a "Hardcover" provider is enabled; the id is "openlibrary:work:*",
            // so no enabled provider is compatible with the scoped id.
            var hardcoverOnly = new HardcoverBookInfoProvider();

            var registry = new TestRegistry(new IMetadataProvider[] { hardcoverOnly });

            var telemetry = new MetadataProviderTelemetryService();

            // Capture the warning via a MemoryTarget.
            var memoryTarget = new MemoryTarget("scoped-id-diagnostic") { Layout = "${level}|${message}" };
            var originalConfiguration = LogManager.Configuration;
            var configuration = new LoggingConfiguration();
            configuration.AddTarget(memoryTarget);
            configuration.LoggingRules.Add(new LoggingRule("*", LogLevel.Warn, memoryTarget));
            LogManager.Configuration = configuration;

            try
            {
                var orchestrator = new MetadataProviderOrchestrator(registry, telemetry, LogManager.GetCurrentClassLogger());

                // The lookup fails (no compatible provider) and a diagnostic is logged.
                var act = () => orchestrator.GetBookInfo("openlibrary:work:OL45883W");

                act.Should().Throw<NzbDrone.Core.Exceptions.BookNotFoundException>();
                memoryTarget.Logs.Should().Contain(log => log.Contains("No enabled metadata provider is compatible"));
            }
            finally
            {
                LogManager.Configuration = originalConfiguration;
            }
        }

        private class HardcoverBookInfoProvider : IMetadataProvider, IProvideBookInfo
        {
            public string ProviderName => "Hardcover";
            public int Priority => 1;
            public bool IsEnabled => true;
            public bool SupportsAuthorSearch => false;
            public bool SupportsBookSearch => true;
            public bool SupportsIsbnLookup => true;
            public bool SupportsSeriesInfo => false;
            public bool SupportsCoverImages => true;

            public Tuple<string, Book, List<AuthorMetadata>> GetBookInfo(string id)
            {
                // Hardcover cannot resolve an openlibrary:work: id.
                return null;
            }
        }

        [Test]
        public void should_throw_metadata_provider_unavailable_when_all_providers_fail_with_transport_error()
        {
            var provider = new TransportFailingAuthorInfoProvider();
            var registry = new TestRegistry(new IMetadataProvider[] { provider });

            var telemetry = new MetadataProviderTelemetryService();
            var orchestrator = new MetadataProviderOrchestrator(registry, telemetry, LogManager.GetCurrentClassLogger());

            var act = () => orchestrator.GetAuthorInfo("hardcover:author:171873", true);

            act.Should().Throw<MetadataProviderUnavailableException>()
                .WithMessage("*get-author-info*");
            provider.Attempts.Should().Be(2);
        }

        [Test]
        public void should_retry_only_once_across_consecutive_operations_and_clear_health_after_recovery()
        {
            var provider = new Mock<IMetadataProvider>();
            provider.SetupGet(x => x.ProviderName).Returns("Hardcover");
            provider.SetupGet(x => x.SupportsAuthorSearch).Returns(true);
            provider.SetupGet(x => x.SupportsBookSearch).Returns(true);
            var search = provider.As<ISearchForNewBook>();
            var author = provider.As<IProvideAuthorInfo>();
            var outage = new HttpRequestException("No route to host");
            search.SetupSequence(p => p.SearchForNewBook(It.IsAny<string>(), null, true))
                .Throws(outage)
                .Throws(outage)
                .Returns(new List<Book>());
            author.Setup(p => p.GetAuthorInfo("hardcover:author:4821", true)).Throws(outage);

            var events = new Mock<IEventAggregator>();
            var telemetry = new MetadataProviderTelemetryService(events.Object);
            var orchestrator = new MetadataProviderOrchestrator(new TestRegistry(new[] { provider.Object }), telemetry, LogManager.GetCurrentClassLogger());
            var health = new MetadataProviderConnectivityCheck(telemetry, Mock.Of<ILocalizationService>());

            health.Check().Type.Should().Be(HealthCheckResult.Ok);
            orchestrator.SearchForNewBook("outage", null).Should().BeNull();
            search.Verify(p => p.SearchForNewBook("outage", null, true), Times.Exactly(2));
            health.Check().Type.Should().Be(HealthCheckResult.Warning);

            var lookup = () => orchestrator.GetAuthorInfo("hardcover:author:4821", true);
            lookup.Should().Throw<MetadataProviderUnavailableException>();
            author.Verify(p => p.GetAuthorInfo("hardcover:author:4821", true), Times.Once());

            author.Setup(p => p.GetAuthorInfo("hardcover:author:4821", true)).Returns(new Author());
            orchestrator.GetAuthorInfo("hardcover:author:4821", true).Should().NotBeNull();
            orchestrator.SearchForNewBook("recovery", null).Should().BeNull();
            health.Check().Type.Should().Be(HealthCheckResult.Ok);
            events.Verify(e => e.PublishEvent(It.IsAny<MetadataProviderConnectivityChangedEvent>()), Times.Exactly(4));
        }

        [Test]
        public void should_report_provider_404_as_reachable_absence()
        {
            const string requestedId = "openlibrary:author:OL4821A";
            var provider = new Mock<IMetadataProvider>();
            provider.SetupGet(x => x.ProviderName).Returns("OpenLibrary");
            provider.SetupGet(x => x.SupportsAuthorSearch).Returns(true);
            provider.As<IProvideAuthorInfo>().Setup(p => p.GetAuthorInfo(requestedId, true))
                .Throws(new AuthorNotFoundException("OL4821A", isConfirmedMissing: true));
            var telemetry = new MetadataProviderTelemetryService();
            telemetry.RecordTransportOutage("get-author-info", true);
            var orchestrator = new MetadataProviderOrchestrator(new TestRegistry(new[] { provider.Object }), telemetry, LogManager.GetCurrentClassLogger());

            var lookup = () => orchestrator.GetAuthorInfo(requestedId);
            lookup.Should().Throw<AuthorNotFoundException>().Which.IsConfirmedMissing.Should().BeTrue();
            telemetry.HasRecentTransportOutage().Should().BeFalse();
        }

        [Test]
        public void should_retain_author_lookup_fallback_after_a_provider_404()
        {
            const string requestedId = "unscoped-author";
            var primary = new Mock<IMetadataProvider>();
            primary.SetupGet(x => x.ProviderName).Returns("Primary");
            primary.SetupGet(x => x.SupportsAuthorSearch).Returns(true);
            primary.As<IProvideAuthorInfo>().Setup(p => p.GetAuthorInfo(requestedId, true))
                .Throws(new AuthorNotFoundException(requestedId, isConfirmedMissing: true));
            var fallback = new Mock<IMetadataProvider>();
            fallback.SetupGet(x => x.ProviderName).Returns("Fallback");
            fallback.SetupGet(x => x.SupportsAuthorSearch).Returns(true);
            var author = new Author();
            fallback.As<IProvideAuthorInfo>().Setup(p => p.GetAuthorInfo(requestedId, true)).Returns(author);

            var orchestrator = new MetadataProviderOrchestrator(
                new TestRegistry(new[] { primary.Object, fallback.Object }),
                new MetadataProviderTelemetryService(),
                LogManager.GetCurrentClassLogger());

            orchestrator.GetAuthorInfo(requestedId).Should().BeSameAs(author);
        }

        [TestCase(System.Net.WebExceptionStatus.Timeout, true)]
        [TestCase(System.Net.WebExceptionStatus.ReceiveFailure, true)]
        [TestCase(System.Net.WebExceptionStatus.ProtocolError, false)]
        public void should_recognize_dispatcher_timeout_and_body_read_failures(System.Net.WebExceptionStatus status, bool transport)
        {
            var failure = new System.Net.WebException("Synthetic dispatcher failure", new System.OperationCanceledException(), status, null);
            MetadataProviderOrchestrator.IsTransportError(failure).Should().Be(transport);
        }

        [Test]
        public void should_throw_metadata_provider_unavailable_when_all_providers_fail_with_nested_socket_error()
        {
            var registry = new TestRegistry(new IMetadataProvider[]
            {
                new NestedSocketFailingAuthorInfoProvider()
            });

            var telemetry = new MetadataProviderTelemetryService();
            var orchestrator = new MetadataProviderOrchestrator(registry, telemetry, LogManager.GetCurrentClassLogger());

            var act = () => orchestrator.GetAuthorInfo("hardcover:author:171873", true);

            act.Should().Throw<MetadataProviderUnavailableException>();
        }

        [Test]
        public void should_report_non_transport_provider_failure_without_marking_outage()
        {
            var registry = new TestRegistry(new IMetadataProvider[]
            {
                new NonTransportFailingAuthorInfoProvider()
            });

            var telemetry = new MetadataProviderTelemetryService();
            var orchestrator = new MetadataProviderOrchestrator(registry, telemetry, LogManager.GetCurrentClassLogger());

            var act = () => orchestrator.GetAuthorInfo("hardcover:author:171873", true);

            act.Should().Throw<MetadataProviderUnavailableException>().WithInnerException<InvalidOperationException>();
            telemetry.HasRecentTransportOutage().Should().BeFalse();
        }

        [Test]
        public void should_fallback_to_secondary_provider_when_primary_fails_with_transport_error()
        {
            // A transport failure on the primary must still fall through to the secondary
            // provider; only when ALL providers fail on transport do we throw unavailable.
            var registry = new TestRegistry(new IMetadataProvider[]
            {
                new TransportFailingAuthorInfoProvider(),
                new SuccessfulGetAuthorInfoProvider()
            });

            var telemetry = new MetadataProviderTelemetryService();
            var orchestrator = new MetadataProviderOrchestrator(registry, telemetry, LogManager.GetCurrentClassLogger());

            var result = orchestrator.GetAuthorInfo("openlibrary:author:OL23919A", true);

            result.Should().NotBeNull();
            result.Metadata.Value.ForeignAuthorId.Should().Be("openlibrary:author:OL23919A");
        }

        private class FailingAuthorInfoProvider : IMetadataProvider, IProvideAuthorInfo
        {
            public string ProviderName => "FailingAuthorInfo";
            public int Priority => 1;
            public bool IsEnabled => true;
            public bool SupportsAuthorSearch => true;
            public bool SupportsBookSearch => false;
            public bool SupportsIsbnLookup => false;
            public bool SupportsSeriesInfo => false;
            public bool SupportsCoverImages => false;

            public Author GetAuthorInfo(string bibliophilarrId, bool useCache = true)
            {
                throw new InvalidOperationException("simulated failure");
            }

            public HashSet<string> GetChangedAuthors(DateTime startTime)
            {
                throw new InvalidOperationException("simulated failure");
            }
        }

        private class SuccessfulAuthorInfoProvider : IMetadataProvider, IProvideAuthorInfo
        {
            public string ProviderName => "SuccessfulAuthorInfo";
            public int Priority => 2;
            public bool IsEnabled => true;
            public bool SupportsAuthorSearch => true;
            public bool SupportsBookSearch => false;
            public bool SupportsIsbnLookup => false;
            public bool SupportsSeriesInfo => false;
            public bool SupportsCoverImages => false;

            public Author GetAuthorInfo(string bibliophilarrId, bool useCache = true)
            {
                return null;
            }

            public HashSet<string> GetChangedAuthors(DateTime startTime)
            {
                return new HashSet<string> { "openlibrary:author:OL23919A" };
            }
        }

        private class SuccessfulGetAuthorInfoProvider : IMetadataProvider, IProvideAuthorInfo
        {
            public string ProviderName => "SuccessfulGetAuthorInfo";
            public int Priority => 2;
            public bool IsEnabled => true;
            public bool SupportsAuthorSearch => true;
            public bool SupportsBookSearch => false;
            public bool SupportsIsbnLookup => false;
            public bool SupportsSeriesInfo => false;
            public bool SupportsCoverImages => false;

            public Author GetAuthorInfo(string bibliophilarrId, bool useCache = true)
            {
                return new Author
                {
                    Metadata = new AuthorMetadata
                    {
                        ForeignAuthorId = bibliophilarrId,
                        Name = "Fallback Author"
                    },
                    Books = new List<Book>()
                };
            }

            public HashSet<string> GetChangedAuthors(DateTime startTime)
            {
                return new HashSet<string>();
            }
        }

        private class IncompatibleAuthorInfoProvider : IMetadataProvider, IProvideAuthorInfo
        {
            public int CallCount { get; private set; }

            public string ProviderName => "GoogleBooks";
            public int Priority => 1;
            public bool IsEnabled => true;
            public bool SupportsAuthorSearch => true;
            public bool SupportsBookSearch => false;
            public bool SupportsIsbnLookup => false;
            public bool SupportsSeriesInfo => false;
            public bool SupportsCoverImages => false;

            public Author GetAuthorInfo(string bibliophilarrId, bool useCache = true)
            {
                CallCount++;
                throw new InvalidOperationException("incompatible provider should not be called");
            }

            public HashSet<string> GetChangedAuthors(DateTime startTime)
            {
                return new HashSet<string>();
            }
        }

        private class SuccessfulOpenLibraryAuthorInfoProvider : IMetadataProvider, IProvideAuthorInfo
        {
            public string ProviderName => "OpenLibrary";
            public int Priority => 2;
            public bool IsEnabled => true;
            public bool SupportsAuthorSearch => true;
            public bool SupportsBookSearch => true;
            public bool SupportsIsbnLookup => true;
            public bool SupportsSeriesInfo => true;
            public bool SupportsCoverImages => true;

            public Author GetAuthorInfo(string bibliophilarrId, bool useCache = true)
            {
                return new Author
                {
                    Metadata = new AuthorMetadata
                    {
                        ForeignAuthorId = bibliophilarrId,
                        Name = "Compatible Author"
                    },
                    Books = new List<Book>()
                };
            }

            public HashSet<string> GetChangedAuthors(DateTime startTime)
            {
                return new HashSet<string>();
            }
        }

        private class SuccessfulOpenLibraryBookInfoProvider : IMetadataProvider, IProvideBookInfo
        {
            public string ProviderName => "OpenLibrary";
            public int Priority => 2;
            public bool IsEnabled => true;
            public bool SupportsAuthorSearch => true;
            public bool SupportsBookSearch => true;
            public bool SupportsIsbnLookup => true;
            public bool SupportsSeriesInfo => true;
            public bool SupportsCoverImages => true;

            public Tuple<string, Book, List<AuthorMetadata>> GetBookInfo(string id)
            {
                var metadata = new AuthorMetadata
                {
                    ForeignAuthorId = "openlibrary:author:OL26320A",
                    Name = "J.R.R. Tolkien"
                };

                return Tuple.Create(
                    metadata.ForeignAuthorId,
                    new Book
                    {
                        ForeignBookId = "openlibrary:work:OL45883W",
                        AuthorMetadata = metadata,
                        Author = new Author { Metadata = metadata },
                        Editions = new List<Edition>()
                    },
                    new List<AuthorMetadata> { metadata });
            }
        }

        private class IncompatibleBookInfoProvider : IMetadataProvider, IProvideBookInfo
        {
            public int CallCount { get; private set; }

            public string ProviderName => "GoogleBooks";
            public int Priority => 1;
            public bool IsEnabled => true;
            public bool SupportsAuthorSearch => false;
            public bool SupportsBookSearch => true;
            public bool SupportsIsbnLookup => true;
            public bool SupportsSeriesInfo => false;
            public bool SupportsCoverImages => true;

            public Tuple<string, Book, List<AuthorMetadata>> GetBookInfo(string id)
            {
                CallCount++;
                throw new InvalidOperationException("incompatible provider should not be called");
            }
        }

        private class OrderedSearchProvider : IMetadataProvider, ISearchForNewBook
        {
            private readonly string _title;

            public OrderedSearchProvider(string name, int priority, string title)
            {
                ProviderName = name;
                Priority = priority;
                _title = title;
            }

            public int CallCount { get; private set; }
            public string ProviderName { get; }
            public int Priority { get; }
            public bool IsEnabled => true;
            public bool SupportsAuthorSearch => false;
            public bool SupportsBookSearch => true;
            public bool SupportsIsbnLookup => true;
            public bool SupportsSeriesInfo => false;
            public bool SupportsCoverImages => false;

            public List<Book> SearchForNewBook(string title, string author, bool getAllEditions = true)
            {
                CallCount++;
                return new List<Book>
                {
                    new Book
                    {
                        ForeignBookId = ProviderName + "-id",
                        Title = _title,
                        AuthorMetadata = new AuthorMetadata { ForeignAuthorId = ProviderName + "-author", Name = "Author" }
                    }
                };
            }

            public List<Book> SearchByIsbn(string isbn)
            {
                return SearchForNewBook(isbn, null, false);
            }

            public List<Book> SearchByAsin(string asin)
            {
                return new List<Book>();
            }

            public List<Book> SearchByExternalId(string idType, string id)
            {
                return SearchForNewBook(id, null, false);
            }
        }

        private class ThrowingSearchProvider : IMetadataProvider, ISearchForNewBook
        {
            public string ProviderName => "Throwing";
            public int Priority => 1;
            public bool IsEnabled => true;
            public bool SupportsAuthorSearch => false;
            public bool SupportsBookSearch => true;
            public bool SupportsIsbnLookup => false;
            public bool SupportsSeriesInfo => false;
            public bool SupportsCoverImages => false;

            public List<Book> SearchForNewBook(string title, string author, bool getAllEditions = true)
            {
                throw new InvalidOperationException("boom");
            }

            public List<Book> SearchByIsbn(string isbn)
            {
                throw new InvalidOperationException("boom");
            }

            public List<Book> SearchByAsin(string asin)
            {
                return new List<Book>();
            }

            public List<Book> SearchByExternalId(string idType, string id)
            {
                return new List<Book>();
            }
        }

        private class NullSearchProvider : IMetadataProvider, ISearchForNewBook
        {
            public string ProviderName => "NullProvider";
            public int Priority => 2;
            public bool IsEnabled => true;
            public bool SupportsAuthorSearch => false;
            public bool SupportsBookSearch => true;
            public bool SupportsIsbnLookup => false;
            public bool SupportsSeriesInfo => false;
            public bool SupportsCoverImages => false;

            public List<Book> SearchForNewBook(string title, string author, bool getAllEditions = true)
            {
                return null;
            }

            public List<Book> SearchByIsbn(string isbn)
            {
                return null;
            }

            public List<Book> SearchByAsin(string asin)
            {
                return new List<Book>();
            }

            public List<Book> SearchByExternalId(string idType, string id)
            {
                return null;
            }
        }

        private class TransportFailingAuthorInfoProvider : IMetadataProvider, IProvideAuthorInfo
        {
            public int Attempts { get; private set; }
            public string ProviderName => "Hardcover";
            public int Priority => 1;
            public bool IsEnabled => true;
            public bool SupportsAuthorSearch => true;
            public bool SupportsBookSearch => false;
            public bool SupportsIsbnLookup => false;
            public bool SupportsSeriesInfo => false;
            public bool SupportsCoverImages => false;

            public Author GetAuthorInfo(string bibliophilarrId, bool useCache = true)
            {
                Attempts++;
                throw new HttpRequestException("Simulated network failure");
            }

            public HashSet<string> GetChangedAuthors(DateTime startTime)
            {
                return new HashSet<string>();
            }
        }

        private class NestedSocketFailingAuthorInfoProvider : IMetadataProvider, IProvideAuthorInfo
        {
            public string ProviderName => "Hardcover";
            public int Priority => 1;
            public bool IsEnabled => true;
            public bool SupportsAuthorSearch => true;
            public bool SupportsBookSearch => false;
            public bool SupportsIsbnLookup => false;
            public bool SupportsSeriesInfo => false;
            public bool SupportsCoverImages => false;

            public Author GetAuthorInfo(string bibliophilarrId, bool useCache = true)
            {
                throw new HttpRequestException("Simulated connection reset", new SocketException(104));
            }

            public HashSet<string> GetChangedAuthors(DateTime startTime)
            {
                return new HashSet<string>();
            }
        }

        private class NonTransportFailingAuthorInfoProvider : IMetadataProvider, IProvideAuthorInfo
        {
            public string ProviderName => "Hardcover";
            public int Priority => 1;
            public bool IsEnabled => true;
            public bool SupportsAuthorSearch => true;
            public bool SupportsBookSearch => false;
            public bool SupportsIsbnLookup => false;
            public bool SupportsSeriesInfo => false;
            public bool SupportsCoverImages => false;

            public Author GetAuthorInfo(string bibliophilarrId, bool useCache = true)
            {
                throw new InvalidOperationException("Simulated non-transport failure");
            }

            public HashSet<string> GetChangedAuthors(DateTime startTime)
            {
                return new HashSet<string>();
            }
        }
    }
}
