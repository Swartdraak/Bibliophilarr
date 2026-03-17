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
using NzbDrone.Core.Datastore;
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
            snapshot.FieldSelectionsByProvider.Should().ContainKey("title:Inventaire");
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

        [Test]
        [TestCase(HttpStatusCode.RequestTimeout)]
        [TestCase(HttpStatusCode.TooManyRequests)]
        [TestCase(HttpStatusCode.ServiceUnavailable)]
        public async Task should_handle_transient_http_errors_without_failing_author_metadata(HttpStatusCode statusCode)
        {
            var fallbackAuthor = BuildAuthor("inventaire:author:transient", "Frank Herbert");
            var registry = new MetadataProviderRegistry(new IMetadataProvider[]
            {
                new FakeAuthorInfoProvider("OpenLibrary", 10, true, (_, _) => throw CreateHttpException(statusCode)),
                new FakeAuthorInfoProvider("Inventaire", 20, true, (_, _) => fallbackAuthor)
            });

            var logger = LogManager.GetLogger("MetadataAggregatorAuthorTransientFixture");
            var configService = new Mock<IConfigService>();
            configService.SetupGet(x => x.EnableMetadataConflictStrategyVariants).Returns(false);
            var conflictTelemetry = new MetadataConflictTelemetryService(logger);
            var conflictPolicy = new MetadataConflictResolutionPolicy(conflictTelemetry, logger, configService.Object);
            var providerTelemetry = new ProviderTelemetryService(registry, logger);
            var aggregator = new MetadataAggregator(registry, new MetadataQualityScorer(), conflictPolicy, providerTelemetry);

            var result = await aggregator.GetAuthorMetadataAsync("OL23919A", "openlibrary", new AggregationOptions
            {
                Strategy = AggregationStrategy.BestQuality,
                StopOnFirstSuccess = false,
                MaxProviders = 5
            });

            result.Result.Should().NotBeNull();
            result.Result.ForeignAuthorId.Should().Be("inventaire:author:transient");
            result.ProviderName.Should().Be("Inventaire");

            var health = registry.GetProvidersHealthStatus();
            health.Should().ContainKey("OpenLibrary");

            if (statusCode == HttpStatusCode.RequestTimeout)
            {
                health["OpenLibrary"].TimeoutCount.Should().Be(1);
                health["OpenLibrary"].ConsecutiveFailures.Should().Be(1);
            }
            else
            {
                health["OpenLibrary"].ConsecutiveFailures.Should().Be(1);
                health["OpenLibrary"].LastErrorMessage.Should().Contain(((int)statusCode).ToString());
            }
        }

        [Test]
        [TestCase(HttpStatusCode.RequestTimeout)]
        [TestCase(HttpStatusCode.TooManyRequests)]
        [TestCase(HttpStatusCode.ServiceUnavailable)]
        public async Task should_handle_transient_http_errors_without_failing_author_search(HttpStatusCode statusCode)
        {
            var fallbackAuthor = BuildAuthor("inventaire:author:search", "Frank Herbert");
            var registry = new MetadataProviderRegistry(new IMetadataProvider[]
            {
                new FakeAuthorSearchProvider("OpenLibrary", 10, true, _ => throw CreateHttpException(statusCode)),
                new FakeAuthorSearchProvider("Inventaire", 20, true, _ => new List<Author> { fallbackAuthor })
            });

            var logger = LogManager.GetLogger("MetadataAggregatorAuthorSearchTransientFixture");
            var configService = new Mock<IConfigService>();
            configService.SetupGet(x => x.EnableMetadataConflictStrategyVariants).Returns(false);
            var conflictTelemetry = new MetadataConflictTelemetryService(logger);
            var conflictPolicy = new MetadataConflictResolutionPolicy(conflictTelemetry, logger, configService.Object);
            var providerTelemetry = new ProviderTelemetryService(registry, logger);
            var aggregator = new MetadataAggregator(registry, new MetadataQualityScorer(), conflictPolicy, providerTelemetry);

            var results = await aggregator.SearchAuthorsAsync("Frank Herbert", new AggregationOptions
            {
                Strategy = AggregationStrategy.BestQuality,
                StopOnFirstSuccess = false,
                MaxProviders = 5
            });

            results.Should().ContainSingle();
            results[0].ForeignAuthorId.Should().Be("inventaire:author:search");

            var health = registry.GetProvidersHealthStatus();
            health.Should().ContainKey("OpenLibrary");

            if (statusCode == HttpStatusCode.RequestTimeout)
            {
                health["OpenLibrary"].TimeoutCount.Should().Be(1);
                health["OpenLibrary"].ConsecutiveFailures.Should().Be(1);
            }
            else
            {
                health["OpenLibrary"].ConsecutiveFailures.Should().Be(1);
                health["OpenLibrary"].LastErrorMessage.Should().Contain(((int)statusCode).ToString());
            }
        }

        private static HttpException CreateHttpException(HttpStatusCode statusCode)
        {
            var request = new HttpRequest("https://provider.test/search");
            var response = new HttpResponse(request, new HttpHeader(), "transient error", statusCode);
            return new HttpException(request, response);
        }

        private static Author BuildAuthor(string authorId, string name)
        {
            var metadata = new AuthorMetadata
            {
                ForeignAuthorId = authorId,
                Name = name,
                SortName = name,
                NameLastFirst = name,
                SortNameLastFirst = name,
                Overview = "Science fiction author"
            };

            var books = new List<Book>
            {
                BuildBook(authorId + ":book", "Representative Work", authorId, withCover: false)
            };

            return new Author
            {
                AuthorMetadataId = metadata.Id,
                CleanName = name,
                ForeignAuthorId = authorId,
                Name = name,
                Monitored = true,
                Metadata = new LazyLoaded<AuthorMetadata>(metadata),
                Books = new LazyLoaded<List<Book>>(books)
            };
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

        private class FakeAuthorInfoProvider : IProvideAuthorInfoV2
        {
            private readonly Func<string, string, Author> _lookup;

            public FakeAuthorInfoProvider(string providerName, int priority, bool isEnabled, Func<string, string, Author> lookup)
            {
                ProviderName = providerName;
                Priority = priority;
                IsEnabled = isEnabled;
                _lookup = lookup;
            }

            public string ProviderName { get; }
            public int Priority { get; }
            public bool IsEnabled { get; }
            public bool SupportsBookSearch => false;
            public bool SupportsAuthorSearch => false;
            public bool SupportsIsbnLookup => false;
            public bool SupportsAsinLookup => false;
            public bool SupportsSeriesInfo => false;
            public bool SupportsListInfo => false;
            public bool SupportsCoverImages => false;
            public bool SupportsBookInfo => false;
            public bool SupportsAuthorInfo => true;

            public ProviderRateLimitInfo GetRateLimitInfo()
            {
                return new ProviderRateLimitInfo();
            }

            public ProviderHealthStatus GetHealthStatus()
            {
                return new ProviderHealthStatus();
            }

            public Task<Author> GetAuthorInfoAsync(string providerId, AuthorInfoOptions options = null)
            {
                return Task.FromResult(_lookup("providerId", providerId));
            }

            public Task<Author> GetAuthorInfoByIdentifierAsync(string identifierType, string identifier, AuthorInfoOptions options = null)
            {
                return Task.FromResult(_lookup(identifierType, identifier));
            }

            public Task<HashSet<string>> GetChangedAuthorsAsync(DateTime startTime)
            {
                return Task.FromResult(new HashSet<string>());
            }

            public Author GetAuthorInfo(string providerId, AuthorInfoOptions options = null)
            {
                return _lookup("providerId", providerId);
            }

            public HashSet<string> GetChangedAuthors(DateTime startTime)
            {
                return new HashSet<string>();
            }
        }

        private class FakeAuthorSearchProvider : ISearchForNewAuthorV2
        {
            private readonly Func<string, List<Author>> _search;

            public FakeAuthorSearchProvider(string providerName, int priority, bool isEnabled, Func<string, List<Author>> search)
            {
                ProviderName = providerName;
                Priority = priority;
                IsEnabled = isEnabled;
                _search = search;
            }

            public string ProviderName { get; }
            public int Priority { get; }
            public bool IsEnabled { get; }
            public bool SupportsBookSearch => false;
            public bool SupportsAuthorSearch => true;
            public bool SupportsIsbnLookup => false;
            public bool SupportsAsinLookup => false;
            public bool SupportsSeriesInfo => false;
            public bool SupportsListInfo => false;
            public bool SupportsCoverImages => false;
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

            public Task<List<Author>> SearchForNewAuthorAsync(string name, AuthorSearchOptions options = null)
            {
                return Task.FromResult(_search(name));
            }

            public Task<Author> SearchByIdentifierAsync(string identifierType, string identifier, AuthorSearchOptions options = null)
            {
                return Task.FromResult<Author>(null);
            }

            public List<Author> SearchForNewAuthor(string name, AuthorSearchOptions options = null)
            {
                return _search(name);
            }
        }
    }
}
