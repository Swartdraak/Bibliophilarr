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

            public bool HasConfiguredBookLookup => GetEnabledProviders().Any(provider => provider is ISearchForNewBook && (provider.SupportsBookSearch || provider.SupportsIsbnLookup));

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
