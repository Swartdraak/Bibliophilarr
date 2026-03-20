using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using NLog;
using NUnit.Framework;
using NzbDrone.Core.Books;
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
    }
}
