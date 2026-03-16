using System;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NLog;
using NUnit.Framework;
using NzbDrone.Common.Http;
using NzbDrone.Core.Books;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.MetadataSource;

namespace NzbDrone.Core.Test.MetadataSource
{
    [TestFixture]
    public class MetadataAggregatorConflictIntegrationFixture
    {
        [Test]
        public async Task should_apply_runtime_policy_tie_break_and_emit_conflict_metrics()
        {
            var inventaireBook = BuildBook("inventaire:work:1", "Dune", "inventaire:author:1", withCover: true);
            var googleBook = BuildBook("googlebooks:work:2", "Dune", "googlebooks:author:2", withCover: true);

            var registry = new MetadataProviderRegistry(new IMetadataProvider[]
            {
                new FakeBookSearchProvider("GoogleBooks", 10, true, _ => new List<Book> { googleBook }),
                new FakeBookSearchProvider("Inventaire", 20, true, _ => new List<Book> { inventaireBook })
            });

            var logger = LogManager.GetLogger("MetadataAggregatorConflictIntegrationFixture");
            var configService = new Mock<IConfigService>();
            configService.SetupGet(x => x.EnableMetadataConflictStrategyVariants).Returns(false);
            var conflictTelemetry = new MetadataConflictTelemetryService(logger);
            var conflictPolicy = new MetadataConflictResolutionPolicy(conflictTelemetry, logger, configService.Object);
            var providerTelemetry = new ProviderTelemetryService(registry, logger);
            var aggregator = new MetadataAggregator(registry, new MetadataQualityScorer(), conflictPolicy, providerTelemetry);

            var books = await aggregator.SearchBooksAsync("Dune", "Frank Herbert", new AggregationOptions
            {
                Strategy = AggregationStrategy.BestQuality,
                StopOnFirstSuccess = false,
                MaxProviders = 5
            });

            books.Should().NotBeEmpty();
            books[0].ForeignBookId.Should().Be("inventaire:work:1");

            var snapshot = conflictTelemetry.GetSnapshot();
            snapshot.TotalDecisions.Should().Be(1);
            snapshot.DecisionsByReason.Should().ContainKey("tie-break");
            snapshot.DecisionsByProvider["Inventaire"].Should().Be(1);
        }

        [Test]
        [TestCase(HttpStatusCode.RequestTimeout)]
        [TestCase(HttpStatusCode.TooManyRequests)]
        [TestCase(HttpStatusCode.ServiceUnavailable)]
        public async Task should_handle_transient_http_errors_without_failing_search(HttpStatusCode statusCode)
        {
            var fallbackBook = BuildBook("inventaire:work:transient", "Dune", "inventaire:author:1", withCover: true);
            var registry = new MetadataProviderRegistry(new IMetadataProvider[]
            {
                new FakeBookSearchProvider("GoogleBooks", 10, true, _ => throw CreateHttpException(statusCode)),
                new FakeBookSearchProvider("Inventaire", 20, true, _ => new List<Book> { fallbackBook })
            });

            var logger = LogManager.GetLogger("MetadataAggregatorTransientIntegrationFixture");
            var configService = new Mock<IConfigService>();
            configService.SetupGet(x => x.EnableMetadataConflictStrategyVariants).Returns(false);
            var conflictTelemetry = new MetadataConflictTelemetryService(logger);
            var conflictPolicy = new MetadataConflictResolutionPolicy(conflictTelemetry, logger, configService.Object);
            var providerTelemetry = new ProviderTelemetryService(registry, logger);
            var aggregator = new MetadataAggregator(registry, new MetadataQualityScorer(), conflictPolicy, providerTelemetry);

            var books = await aggregator.SearchBooksAsync("Dune", "Frank Herbert", new AggregationOptions
            {
                Strategy = AggregationStrategy.BestQuality,
                StopOnFirstSuccess = false,
                MaxProviders = 5
            });

            books.Should().NotBeEmpty();
            books[0].ForeignBookId.Should().Be("inventaire:work:transient");

            var health = registry.GetProvidersHealthStatus();
            health.Should().ContainKey("GoogleBooks");

            if (statusCode == HttpStatusCode.RequestTimeout)
            {
                health["GoogleBooks"].TimeoutCount.Should().Be(1);
                health["GoogleBooks"].ConsecutiveFailures.Should().Be(1);
            }
            else
            {
                health["GoogleBooks"].ConsecutiveFailures.Should().Be(1);
                health["GoogleBooks"].LastErrorMessage.Should().Contain(((int)statusCode).ToString());
            }
        }

        [Test]
        [TestCase("isbn", 1, 0, 0, "route:isbn")]
        [TestCase("asin", 0, 1, 0, "route:asin")]
        [TestCase("olid", 0, 0, 1, "route:identifier")]
        public async Task should_route_identifier_lookups_to_expected_provider_methods(string identifierType,
                                                                                        int expectedIsbnCalls,
                                                                                        int expectedAsinCalls,
                                                                                        int expectedIdentifierCalls,
                                                                                        string expectedBookId)
        {
            var tracker = new IdentifierRoutingBookSearchProvider("OpenLibrary", 10, true);
            var registry = new MetadataProviderRegistry(new IMetadataProvider[] { tracker });

            var logger = LogManager.GetLogger("MetadataAggregatorIdentifierRoutingFixture");
            var configService = new Mock<IConfigService>();
            configService.SetupGet(x => x.EnableMetadataConflictStrategyVariants).Returns(false);
            var conflictTelemetry = new MetadataConflictTelemetryService(logger);
            var conflictPolicy = new MetadataConflictResolutionPolicy(conflictTelemetry, logger, configService.Object);
            var providerTelemetry = new ProviderTelemetryService(registry, logger);
            var aggregator = new MetadataAggregator(registry, new MetadataQualityScorer(), conflictPolicy, providerTelemetry);

            var result = await aggregator.GetBookMetadataAsync("value-123", identifierType, new AggregationOptions
            {
                Strategy = AggregationStrategy.BestQuality,
                StopOnFirstSuccess = false,
                MaxProviders = 1
            });

            result.Result.Should().NotBeNull();
            result.Result.ForeignBookId.Should().Be(expectedBookId);
            tracker.IsbnCalls.Should().Be(expectedIsbnCalls);
            tracker.AsinCalls.Should().Be(expectedAsinCalls);
            tracker.IdentifierCalls.Should().Be(expectedIdentifierCalls);
        }

        private static HttpException CreateHttpException(HttpStatusCode statusCode)
        {
            var request = new HttpRequest("https://provider.test/search");
            var response = new HttpResponse(request, new HttpHeader(), "transient error", statusCode);
            return new HttpException(request, response);
        }

        private static Book BuildBook(string bookId, string title, string authorId, bool withCover)
        {
            var metadata = new AuthorMetadata
            {
                ForeignAuthorId = authorId,
                Name = "Frank Herbert",
                SortName = "Frank Herbert",
                NameLastFirst = "Frank Herbert",
                SortNameLastFirst = "Frank Herbert"
            };

            var book = new Book
            {
                ForeignBookId = bookId,
                Title = title,
                CleanTitle = title,
                AuthorMetadata = metadata,
                AuthorMetadataId = metadata.Id,
                Ratings = new Ratings()
            };

            var edition = new Edition
            {
                ForeignEditionId = bookId + ":edition",
                Title = title,
                Isbn13 = "9780441013593",
                Book = book,
                Ratings = new Ratings()
            };

            if (withCover)
            {
                edition.Images.Add(new MediaCover.MediaCover
                {
                    Url = "https://covers.example/dune.jpg",
                    CoverType = MediaCover.MediaCoverTypes.Cover
                });
            }

            book.Editions = new List<Edition> { edition };
            return book;
        }

        private class FakeBookSearchProvider : ISearchForNewBookV2
        {
            private readonly Func<string, List<Book>> _search;

            public FakeBookSearchProvider(string providerName, int priority, bool isEnabled, Func<string, List<Book>> search)
            {
                ProviderName = providerName;
                Priority = priority;
                IsEnabled = isEnabled;
                _search = search;
            }

            public string ProviderName { get; }
            public int Priority { get; }
            public bool IsEnabled { get; }
            public bool SupportsBookSearch => true;
            public bool SupportsAuthorSearch => false;
            public bool SupportsIsbnLookup => true;
            public bool SupportsAsinLookup => true;
            public bool SupportsSeriesInfo => false;
            public bool SupportsListInfo => false;
            public bool SupportsCoverImages => true;
            public bool SupportsBookInfo => false;
            public bool SupportsAuthorInfo => false;

            public ProviderRateLimitInfo GetRateLimitInfo()
            {
                return new ProviderRateLimitInfo();
            }

            public ProviderHealthStatus GetHealthStatus()
            {
                return new ProviderHealthStatus();
            }

            public Task<List<Book>> SearchForNewBookAsync(string title, string author = null, BookSearchOptions options = null)
            {
                return Task.FromResult(_search(title));
            }

            public Task<List<Book>> SearchByIsbnAsync(string isbn, BookSearchOptions options = null)
            {
                return Task.FromResult(new List<Book>());
            }

            public Task<List<Book>> SearchByAsinAsync(string asin, BookSearchOptions options = null)
            {
                return Task.FromResult(new List<Book>());
            }

            public Task<List<Book>> SearchByIdentifierAsync(string identifierType, string identifier, BookSearchOptions options = null)
            {
                return Task.FromResult(new List<Book>());
            }

            public List<Book> SearchForNewBook(string title, string author = null, BookSearchOptions options = null)
            {
                return _search(title);
            }

            public List<Book> SearchByIsbn(string isbn, BookSearchOptions options = null)
            {
                return new List<Book>();
            }

            public List<Book> SearchByAsin(string asin, BookSearchOptions options = null)
            {
                return new List<Book>();
            }
        }

        private class IdentifierRoutingBookSearchProvider : ISearchForNewBookV2
        {
            public IdentifierRoutingBookSearchProvider(string providerName, int priority, bool isEnabled)
            {
                ProviderName = providerName;
                Priority = priority;
                IsEnabled = isEnabled;
            }

            public int IsbnCalls { get; private set; }
            public int AsinCalls { get; private set; }
            public int IdentifierCalls { get; private set; }

            public string ProviderName { get; }
            public int Priority { get; }
            public bool IsEnabled { get; }
            public bool SupportsBookSearch => true;
            public bool SupportsAuthorSearch => false;
            public bool SupportsIsbnLookup => true;
            public bool SupportsAsinLookup => true;
            public bool SupportsSeriesInfo => false;
            public bool SupportsListInfo => false;
            public bool SupportsCoverImages => true;
            public bool SupportsBookInfo => false;
            public bool SupportsAuthorInfo => false;

            public ProviderRateLimitInfo GetRateLimitInfo()
            {
                return new ProviderRateLimitInfo();
            }

            public ProviderHealthStatus GetHealthStatus()
            {
                return new ProviderHealthStatus();
            }

            public Task<List<Book>> SearchForNewBookAsync(string title, string author = null, BookSearchOptions options = null)
            {
                return Task.FromResult(new List<Book>());
            }

            public Task<List<Book>> SearchByIsbnAsync(string isbn, BookSearchOptions options = null)
            {
                IsbnCalls += 1;
                return Task.FromResult(new List<Book> { BuildBook("route:isbn", "Routed ISBN", "route:author:isbn", withCover: false) });
            }

            public Task<List<Book>> SearchByAsinAsync(string asin, BookSearchOptions options = null)
            {
                AsinCalls += 1;
                return Task.FromResult(new List<Book> { BuildBook("route:asin", "Routed ASIN", "route:author:asin", withCover: false) });
            }

            public Task<List<Book>> SearchByIdentifierAsync(string identifierType, string identifier, BookSearchOptions options = null)
            {
                IdentifierCalls += 1;
                return Task.FromResult(new List<Book> { BuildBook("route:identifier", "Routed Identifier", "route:author:identifier", withCover: false) });
            }

            public List<Book> SearchForNewBook(string title, string author = null, BookSearchOptions options = null)
            {
                return new List<Book>();
            }

            public List<Book> SearchByIsbn(string isbn, BookSearchOptions options = null)
            {
                return new List<Book>();
            }

            public List<Book> SearchByAsin(string asin, BookSearchOptions options = null)
            {
                return new List<Book>();
            }
        }
    }
}
