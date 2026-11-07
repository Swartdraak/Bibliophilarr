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
    /// <summary>
    /// Regression tests for issue #277: the metadata lookup fan-out must be diagnosable in
    /// telemetry. The defect: when an enabled provider is reachable but returns an HTTP-200
    /// with an empty body (or a provider call fails), the lookup endpoint returned 200 with
    /// zero results and NO per-provider telemetry that distinguishes "provider returned no
    /// data" from "provider call failed". The orchestrator now:
    ///   1. routes the /api/v1/book/lookup and /api/v1/author/lookup V1 contracts through the
    ///      same priority-ordered fan-out (the orchestrator implements ISearchForNewBook /
    ///      ISearchForNewAuthor), so those endpoints fall back across enabled providers instead
    ///      of hitting a single provider directly;
    ///   2. records the result count of every provider call in telemetry (TotalResultCount /
    ///      AverageResultCount) so a 200-empty fan-out is observable without an out-of-band
    ///      network probe.
    /// No live HTTP is made — providers are in-memory fakes.
    /// </summary>
    [TestFixture]
    public class Issue277OpenLibraryEmptyFanoutTelemetryFixture
    {
        private class FakeRegistry : IMetadataProviderRegistry
        {
            private readonly IReadOnlyList<IMetadataProvider> _providers;

            public FakeRegistry(IReadOnlyList<IMetadataProvider> providers)
            {
                _providers = providers;
            }

            public IReadOnlyList<IMetadataProvider> GetProviders() => _providers;

            public void RegisterProvider(IMetadataProvider provider)
            {
            }

            public void UnregisterProvider(string providerName)
            {
            }

            public IMetadataProvider GetProvider(string providerName) => _providers.FirstOrDefault(p => p.ProviderName == providerName);

            public List<IMetadataProvider> GetAllProviders() => _providers.ToList();

            public List<IMetadataProvider> GetEnabledProviders() => _providers.Where(p => p.IsEnabled).ToList();

            public List<IMetadataProvider> GetProvidersWithCapability(string capability) => new List<IMetadataProvider>();

            public List<ISearchForNewBookV2> GetBookSearchProviders() => new List<ISearchForNewBookV2>();

            public List<ISearchForNewAuthorV2> GetAuthorSearchProviders() => new List<ISearchForNewAuthorV2>();

            public List<IProvideBookInfoV2> GetBookInfoProviders() => new List<IProvideBookInfoV2>();

            public List<IProvideAuthorInfoV2> GetAuthorInfoProviders() => new List<IProvideAuthorInfoV2>();

            public void EnableProvider(string providerName)
            {
            }

            public void DisableProvider(string providerName)
            {
            }

            public void SetProviderPriority(string providerName, int priority)
            {
            }

            public Dictionary<string, ProviderHealthStatus> GetProvidersHealthStatus() => new Dictionary<string, ProviderHealthStatus>();

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

        // A provider that is reachable but returns an EMPTY result set (the HTTP-200-empty case).
        private class EmptyResultSearchProvider : IMetadataProvider, ISearchForNewBook, ISearchForNewAuthor
        {
            public string ProviderName { get; set; }
            public int Priority { get; set; }
            public bool IsEnabled => true;
            public bool SupportsAuthorSearch => true;
            public bool SupportsBookSearch => true;
            public bool SupportsIsbnLookup => true;
            public bool SupportsSeriesInfo => false;
            public bool SupportsCoverImages => false;

            public List<Book> SearchForNewBook(string title, string author, bool getAllEditions = true) => new List<Book>();
            public List<Book> SearchByIsbn(string isbn) => new List<Book>();
            public List<Book> SearchByAsin(string asin) => new List<Book>();
            public List<Book> SearchByExternalId(string idType, string id) => new List<Book>();
            public List<Author> SearchForNewAuthor(string title) => new List<Author>();
        }

        // A provider that returns real (non-empty) results.
        private class NonEmptySearchProvider : IMetadataProvider, ISearchForNewBook, ISearchForNewAuthor
        {
            public string ProviderName { get; set; }
            public int Priority { get; set; }
            public bool IsEnabled => true;
            public bool SupportsAuthorSearch => true;
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
                        Title = title,
                        Editions = new List<Edition> { new Edition { ForeignEditionId = "OL321M", Monitored = true } },
                        AuthorMetadata = new AuthorMetadata { ForeignAuthorId = "OL1A", Name = author ?? "Author" },
                        Author = new Author { Metadata = new AuthorMetadata { ForeignAuthorId = "OL1A", Name = author ?? "Author" } }
                    }
                };
            }

            public List<Book> SearchByIsbn(string isbn) => new List<Book>();
            public List<Book> SearchByAsin(string asin) => new List<Book>();
            public List<Book> SearchByExternalId(string idType, string id) => new List<Book>();

            public List<Author> SearchForNewAuthor(string title)
            {
                return new List<Author>
                {
                    new Author
                    {
                        ForeignAuthorId = "OL1A",
                        Metadata = new AuthorMetadata { ForeignAuthorId = "OL1A", Name = title }
                    }
                };
            }
        }

        // A provider whose search call fails (throws) — the "provider call failed" case.
        private class ThrowingSearchProvider : IMetadataProvider, ISearchForNewBook, ISearchForNewAuthor
        {
            public string ProviderName { get; set; }
            public int Priority { get; set; }
            public bool IsEnabled => true;
            public bool SupportsAuthorSearch => true;
            public bool SupportsBookSearch => true;
            public bool SupportsIsbnLookup => true;
            public bool SupportsSeriesInfo => false;
            public bool SupportsCoverImages => false;

            public List<Book> SearchForNewBook(string title, string author, bool getAllEditions = true)
            {
                throw new InvalidOperationException("simulated provider transport failure");
            }

            public List<Book> SearchByIsbn(string isbn) => new List<Book>();
            public List<Book> SearchByAsin(string asin) => new List<Book>();
            public List<Book> SearchByExternalId(string idType, string id) => new List<Book>();
            public List<Author> SearchForNewAuthor(string title) => new List<Author>();
        }

        private (MetadataProviderOrchestrator orchestrator, MetadataProviderTelemetryService telemetry) Build(IReadOnlyList<IMetadataProvider> providers)
        {
            var registry = new FakeRegistry(providers);
            var telemetry = new MetadataProviderTelemetryService();
            var orchestrator = new MetadataProviderOrchestrator(registry, telemetry, LogManager.GetCurrentClassLogger());
            return (orchestrator, telemetry);
        }

        [Test]
        public void book_lookup_falls_back_to_second_provider_when_primary_returns_empty()
        {
            // The issue's core failure: with both providers enabled (Hardcover p1, OpenLibrary p2)
            // and the primary returning nothing, the lookup must still return the secondary's
            // non-empty result rather than 0 results. This is what the /api/v1/book/lookup
            // endpoint now goes through via the orchestrator's ISearchForNewBook.
            var (orchestrator, _) = Build(new IMetadataProvider[]
            {
                new EmptyResultSearchProvider { ProviderName = "Hardcover", Priority = 1 },
                new NonEmptySearchProvider { ProviderName = "OpenLibrary", Priority = 2 }
            });

            var results = orchestrator.SearchForNewBook("Harry Potter", "J.K. Rowling", false);

            results.Should().NotBeEmpty("the second provider must supply the result when the primary returns an empty list");
            results.Should().HaveCount(1);
            results[0].Title.Should().Be("Harry Potter");
        }

        [Test]
        public void author_lookup_falls_back_to_second_provider_when_primary_returns_empty()
        {
            var (orchestrator, _) = Build(new IMetadataProvider[]
            {
                new EmptyResultSearchProvider { ProviderName = "Hardcover", Priority = 1 },
                new NonEmptySearchProvider { ProviderName = "OpenLibrary", Priority = 2 }
            });

            var results = orchestrator.SearchForNewAuthor("Tolkien");

            results.Should().NotBeEmpty();
            results.Should().HaveCount(1);
            results[0].Metadata.Value.Name.Should().Be("Tolkien");
        }

        [Test]
        public void provider_failure_is_recorded_in_telemetry_as_failure_not_silent_empty()
        {
            // AC1: a provider failure (injected) must produce a recorded telemetry entry with
            // the failure, not a silent []. The secondary then supplies the result.
            var (orchestrator, telemetry) = Build(new IMetadataProvider[]
            {
                new ThrowingSearchProvider { ProviderName = "Hardcover", Priority = 1 },
                new NonEmptySearchProvider { ProviderName = "OpenLibrary", Priority = 2 }
            });

            var results = orchestrator.SearchForNewBook("Harry Potter", null, false);

            results.Should().NotBeEmpty();

            var failing = telemetry.GetSnapshots().Single(x => x.ProviderName == "Hardcover");
            failing.Calls.Should().Be(1);
            failing.Failures.Should().Be(1, "the provider exception must be recorded as a failure");
            failing.Successes.Should().Be(0);

            var winner = telemetry.GetSnapshots().Single(x => x.ProviderName == "OpenLibrary");
            winner.Successes.Should().Be(1);
            winner.FallbackHits.Should().Be(1);
            winner.TotalResultCount.Should().Be(1, "the successful provider's result count must be recorded");

            // The failure is observable per-operation, not collapsed away.
            telemetry.GetOperationSnapshots()
                .Should().ContainSingle(x => x.ProviderName == "Hardcover" && x.OperationName == "search-for-new-book" && x.Failures == 1);
        }

        [Test]
        public void empty_200_fanout_is_diagnosable_in_telemetry_result_count()
        {
            // AC2: the empty-fan-out scenario (provider reachable, HTTP-200, zero results) must
            // produce a telemetry entry with result count == 0 that is distinguishable from a
            // failure. Both providers reachable but both return empty -> 200 with zero results,
            // yet telemetry shows each provider was called, succeeded, and returned 0 items.
            var (orchestrator, telemetry) = Build(new IMetadataProvider[]
            {
                new EmptyResultSearchProvider { ProviderName = "Hardcover", Priority = 1 },
                new EmptyResultSearchProvider { ProviderName = "OpenLibrary", Priority = 2 }
            });

            var results = orchestrator.SearchForNewBook("Harry Potter", null, false);

            // When no provider returns data the fan-out returns null (the endpoint's
            // MapToResource handles null/empty the same way -> 200 with zero results).
            results.Should().BeNull("the fan-out returns null when every provider is reachable but returned no data");

            // Every provider operation is recorded (no longer zero telemetry entries), and each
            // records result count 0 for the empty 200 response — the "provider returned no data"
            // signal the issue asked for.
            var ops = telemetry.GetOperationSnapshots();
            ops.Should().HaveCount(2, "each provider call must produce a telemetry entry");

            foreach (var op in ops)
            {
                op.Calls.Should().Be(1);
                op.Successes.Should().Be(1, "a 200 response (even empty) is a success, not a failure");
                op.Failures.Should().Be(0);
                op.TotalResultCount.Should().Be(0, "the empty 200 result count must be recorded as 0");
            }

            // Distinct from the failure case: nothing was recorded as a failure here.
            telemetry.GetSnapshots().Should().OnlyContain(s => s.Failures == 0);
        }

        [Test]
        public void empty_fanout_does_not_mask_a_later_providing_provider()
        {
            // Guards AC4: the empty-fan-out telemetry change must not regress the normal
            // fall-through. Primary empty + secondary non-empty -> the secondary wins and its
            // result count is recorded; the primary's empty 200 is recorded with count 0.
            var (orchestrator, telemetry) = Build(new IMetadataProvider[]
            {
                new EmptyResultSearchProvider { ProviderName = "Hardcover", Priority = 1 },
                new NonEmptySearchProvider { ProviderName = "OpenLibrary", Priority = 2 }
            });

            var results = orchestrator.SearchForNewBook("9780140328720", null, false);

            results.Should().HaveCount(1);
            results[0].Title.Should().Be("9780140328720");

            var primary = telemetry.GetSnapshots().Single(x => x.ProviderName == "Hardcover");
            primary.Successes.Should().Be(1);
            primary.TotalResultCount.Should().Be(0);
            primary.Failures.Should().Be(0);

            var secondary = telemetry.GetSnapshots().Single(x => x.ProviderName == "OpenLibrary");
            secondary.Successes.Should().Be(1);
            secondary.TotalResultCount.Should().Be(1);
            secondary.FallbackHits.Should().Be(1);
        }
    }
}
