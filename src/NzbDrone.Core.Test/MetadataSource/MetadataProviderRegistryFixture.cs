using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NLog;
using NUnit.Framework;
using NzbDrone.Common.Cache;
using NzbDrone.Common.Http;
using NzbDrone.Common.Http.Dispatchers;
using NzbDrone.Common.TPL;
using NzbDrone.Core.Books;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Exceptions;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.MetadataSource.GoogleBooks;
using NzbDrone.Core.MetadataSource.Hardcover;

namespace NzbDrone.Core.Test.MetadataSource
{
    [TestFixture]
    public class MetadataProviderRegistryFixture
    {
        [TestCase(false)]
        [TestCase(true)]
        public void should_report_the_same_hardcover_deadline_after_a_direct_or_fallback_response(bool fallbackFirst)
        {
            const string upstreamBody = """{"error":"fixture-marker: retry in 72096 seconds"}""";
            var logger = LogManager.GetCurrentClassLogger();
            var cache = new CacheManager();
            var dispatcher = new Mock<IHttpDispatcher>(MockBehavior.Strict);
            dispatcher.Setup(x => x.GetResponseAsync(It.IsAny<HttpRequest>(), It.IsAny<CookieContainer>()))
                .ReturnsAsync((HttpRequest request, CookieContainer _) => new HttpResponse(
                    request,
                    new HttpHeader { ContentType = "application/json", ["Retry-After"] = "30" },
                    upstreamBody,
                    HttpStatusCode.TooManyRequests));
            var pace = Mock.Of<IRateLimitService>();
            var http = new HttpClient(Array.Empty<IHttpRequestInterceptor>(), cache, pace, dispatcher.Object, logger);
            var config = new Mock<IConfigService>();
            config.SetupGet(x => x.EnableHardcoverFallback).Returns(true);
            config.SetupGet(x => x.HardcoverApiToken).Returns("synthetic-provider-token");
            var hardcover = new HardcoverFallbackSearchProvider(config.Object, http, logger);
            var alternate = new TestBookSearchProvider("Other", 20, true);
            var registry = new MetadataProviderRegistry(new IMetadataProvider[] { hardcover, alternate }, config.Object, logger, cache);
            var operationTelemetry = new MetadataProviderTelemetryService();
            var fallback = new BookSearchFallbackExecutionService(pace, new ProviderTelemetryService(registry, logger), operationTelemetry, registry, cache, logger);
            var orchestrator = new MetadataProviderOrchestrator(registry, operationTelemetry, logger);

            var before = DateTime.UtcNow;
            if (fallbackFirst)
            {
                fallback.Search(hardcover, "Fixture work", "Fixture author").Should().BeEmpty();
            }
            else
            {
                orchestrator.SearchForNewBook("Fixture work", "Fixture author").Should().BeNull();
            }

            var active = registry.GetProvidersHealthStatus()["Hardcover"];
            active.CooldownUntilUtc.Should().BeOnOrAfter(before.AddSeconds(72096));
            active.CooldownUntilUtc.Should().BeOnOrBefore(DateTime.UtcNow.AddSeconds(72096));
            active.RetryAfterRemainingSeconds.Should().BeInRange(72090, 72096);
            active.ConsecutiveFailures.Should().Be(fallbackFirst ? 1 : 0);
            active.RateLimitWindowRequests.Should().Be(fallbackFirst ? 1 : 0);

            fallback.Search(hardcover, "Fixture work", "Fixture author").Should().BeEmpty();
            orchestrator.SearchForNewBook("Fixture work", "Fixture author").Should().BeNull();
            orchestrator.SearchForNewAuthor("Fixture author").Should().BeNull();
            var scoped = Assert.Throws<MetadataProviderUnavailableException>(() => orchestrator.GetAuthorInfo("hardcover:author:42"));
            scoped.InnerException.Should().BeOfType<TooManyRequestsException>().Which.IsLocallySuppressed.Should().BeTrue();

            var snapshots = operationTelemetry.GetOperationSnapshots();
            snapshots.Should().ContainSingle(x => x.ProviderName == "Hardcover" && x.Calls == 1 && x.Failures == 1);
            snapshots.Count(x => x.ProviderName == "Hardcover").Should().Be(1);
            var health = registry.GetProvidersHealthStatus();
            health["Hardcover"].CooldownUntilUtc.Should().Be(active.CooldownUntilUtc);
            health["Hardcover"].ConsecutiveFailures.Should().Be(active.ConsecutiveFailures);
            health["Hardcover"].RateLimitWindowRequests.Should().Be(active.RateLimitWindowRequests);
            health["Other"].CooldownUntilUtc.Should().BeNull();
            dispatcher.Verify(x => x.GetResponseAsync(It.IsAny<HttpRequest>(), It.IsAny<CookieContainer>()), Times.Once());

            var independent = active.CooldownUntilUtc.Value.AddHours(1);
            registry.UpdateProviderHealth("Hardcover", new ProviderHealthStatus { CooldownUntilUtc = independent });
            registry.GetProvidersHealthStatus()["Hardcover"].CooldownUntilUtc.Should().Be(independent);
            HardcoverRateLimitGate.Get(cache).CooldownUntilUtc.Should().Be(active.CooldownUntilUtc);
        }

        [Test]
        public void should_not_count_a_hardcover_override_until_the_adapter_can_search()
        {
            var config = new Mock<IConfigService>();
            config.SetupGet(settings => settings.HardcoverApiToken).Returns("synthetic-provider-token");
            var http = new Mock<IHttpClient>(MockBehavior.Strict);
            var hardcover = new HardcoverFallbackSearchProvider(config.Object, http.Object, LogManager.GetCurrentClassLogger());
            var registry = new MetadataProviderRegistry(new[] { hardcover });
            registry.EnableProvider("Hardcover");

            registry.GetProviders().Should().Contain(hardcover);
            registry.HasConfiguredBookLookup.Should().BeFalse();
            config.SetupGet(settings => settings.EnableHardcoverFallback).Returns(true);
            registry.HasConfiguredBookLookup.Should().BeTrue();
            registry.DisableProvider("Hardcover");
            registry.HasConfiguredBookLookup.Should().BeFalse();
            http.VerifyNoOtherCalls();
        }

        [Test]
        public void should_return_enabled_providers_sorted_by_priority()
        {
            var registry = new MetadataProviderRegistry(new IMetadataProvider[]
            {
                new TestBookSearchProvider("Fallback", 30, true),
                new TestBookSearchProvider("Primary", 10, true),
                new TestBookSearchProvider("Disabled", 1, false)
            });

            var names = registry.GetEnabledProviders().Select(p => p.ProviderName).ToList();

            names.Should().Equal("Primary", "Fallback");
        }

        [Test]
        public void should_apply_enable_disable_and_priority_overrides()
        {
            var first = new TestBookSearchProvider("First", 10, true);
            var second = new TestBookSearchProvider("Second", 20, true);
            var registry = new MetadataProviderRegistry(new IMetadataProvider[] { first, second });

            registry.DisableProvider("First");
            registry.EnableProvider("First");
            registry.SetProviderPriority("Second", 1);

            var names = registry.GetEnabledProviders().Select(p => p.ProviderName).ToList();

            names.Should().Equal("Second", "First");
        }

        [Test]
        public void should_prioritize_hardcover_first_when_configured_order_places_it_first()
        {
            var config = new Mock<IConfigService>();
            config.SetupGet(x => x.MetadataProviderPriorityOrder)
                .Returns("Hardcover,OpenLibrary,GoogleBooks,Inventaire");

            var registry = new MetadataProviderRegistry(
                new IMetadataProvider[]
                {
                    new TestBookSearchProvider("OpenLibrary", 10, true),
                    new TestBookSearchProvider("Hardcover", 30, true)
                },
                config.Object,
                LogManager.GetCurrentClassLogger());

            var names = registry.GetBookSearchProviders().Select(p => p.ProviderName).ToList();

            names.Should().Equal("Hardcover", "OpenLibrary");
        }

        [Test]
        public void should_filter_by_capability_case_insensitive()
        {
            var registry = new MetadataProviderRegistry(new IMetadataProvider[]
            {
                new TestBookSearchProvider("BookOnly", 10, true),
                new TestAuthorSearchProvider("AuthorOnly", 10, true)
            });

            var bookProviders = registry.GetProvidersWithCapability("supportsbooksearch");
            var authorProviders = registry.GetProvidersWithCapability("SupportsAuthorSearch");

            bookProviders.Select(p => p.ProviderName).Should().Equal("BookOnly");
            authorProviders.Select(p => p.ProviderName).Should().Equal("AuthorOnly");
        }

        [Test]
        public void should_return_typed_provider_lists()
        {
            var registry = new MetadataProviderRegistry(new IMetadataProvider[]
            {
                new TestBookSearchProvider("BookProvider", 10, true),
                new TestAuthorSearchProvider("AuthorProvider", 10, true),
                new TestBookInfoProvider("BookInfoProvider", 10, true),
                new TestAuthorInfoProvider("AuthorInfoProvider", 10, true)
            });

            registry.GetBookSearchProviders().Should().ContainSingle();
            registry.GetAuthorSearchProviders().Should().ContainSingle();
            registry.GetBookInfoProviders().Should().ContainSingle();
            registry.GetAuthorInfoProviders().Should().ContainSingle();
        }

        [Test]
        public void should_store_health_override_for_registered_provider()
        {
            var registry = new MetadataProviderRegistry(new IMetadataProvider[]
            {
                new TestBookSearchProvider("OpenLibrary", 10, true)
            });

            var degraded = new ProviderHealthStatus
            {
                Health = ProviderHealth.Degraded,
                SuccessRate = 0.6
            };

            registry.UpdateProviderHealth("OpenLibrary", degraded);

            var health = registry.GetProvidersHealthStatus();

            health["OpenLibrary"].Health.Should().Be(ProviderHealth.Degraded);
            health["OpenLibrary"].SuccessRate.Should().Be(0.6);
        }

        [Test]
        public void should_report_remaining_seconds_from_the_active_deadline()
        {
            var registry = new MetadataProviderRegistry(new[] { new TestBookSearchProvider("Inventaire", 10, true) });
            var deadline = DateTime.UtcNow.AddMinutes(25);
            var stored = new ProviderHealthStatus
            {
                Health = ProviderHealth.Degraded,
                CooldownUntilUtc = deadline,
                RetryAfterRemainingSeconds = 1800
            };
            registry.UpdateProviderHealth("Inventaire", stored);

            var health = registry.GetProvidersHealthStatus()["Inventaire"];

            health.CooldownUntilUtc.Should().Be(deadline);
            health.RetryAfterRemainingSeconds.Should().BeInRange(1490, 1500);
            health.Health.Should().Be(ProviderHealth.Degraded);
            stored.RetryAfterRemainingSeconds.Should().Be(1800);
        }

        [TestCase("Inventaire", true)]
        [TestCase("GoogleBooks", true)]
        [TestCase("GoogleBooks", false)]
        public void should_clear_inactive_deadlines_and_remaining_seconds(string providerName, bool withPastDeadline)
        {
            var google = new GoogleBooksFallbackSearchProvider(Mock.Of<IConfigService>(), Mock.Of<IHttpClient>(), new CacheManager(), LogManager.GetCurrentClassLogger());
            var registry = new MetadataProviderRegistry(new IMetadataProvider[]
            {
                new TestBookSearchProvider("Inventaire", 10, true),
                google
            });
            var lastChecked = DateTime.UtcNow.AddMinutes(-1);
            registry.UpdateProviderHealth(providerName, new ProviderHealthStatus
            {
                Health = ProviderHealth.Degraded,
                LastChecked = lastChecked,
                CooldownUntilUtc = withPastDeadline ? lastChecked : null,
                RetryAfterRemainingSeconds = 27
            });

            var health = registry.GetProvidersHealthStatus()[providerName];

            health.CooldownUntilUtc.Should().BeNull();
            health.RetryAfterRemainingSeconds.Should().Be(0);
            health.LastChecked.Should().Be(lastChecked);
            health.Health.Should().Be(ProviderHealth.Degraded);
        }

        [Test]
        public void should_prefer_healthy_provider_over_degraded_provider_when_both_enabled()
        {
            var primary = new TestBookSearchProvider("Primary", 10, true);
            var fallback = new TestBookSearchProvider("Fallback", 20, true);
            var registry = new MetadataProviderRegistry(new IMetadataProvider[] { primary, fallback });

            registry.UpdateProviderHealth("Primary", new ProviderHealthStatus
            {
                Health = ProviderHealth.Degraded,
                LastChecked = DateTime.UtcNow
            });

            registry.UpdateProviderHealth("Fallback", new ProviderHealthStatus
            {
                Health = ProviderHealth.Healthy,
                LastChecked = DateTime.UtcNow
            });

            var ordered = registry.GetProviders().Select(x => x.ProviderName).ToList();

            ordered.Should().Equal("Fallback", "Primary");
        }

        [Test]
        public void should_skip_provider_in_active_cooldown_until_window_expires()
        {
            var first = new TestBookSearchProvider("First", 10, true);
            var second = new TestBookSearchProvider("Second", 20, true);
            var registry = new MetadataProviderRegistry(new IMetadataProvider[] { first, second });

            registry.UpdateProviderHealth("First", new ProviderHealthStatus
            {
                Health = ProviderHealth.Unhealthy,
                CooldownUntilUtc = DateTime.UtcNow.AddMinutes(5),
                LastChecked = DateTime.UtcNow
            });

            var duringCooldown = registry.GetProviders().Select(x => x.ProviderName).ToList();
            duringCooldown.Should().Equal("Second");

            registry.UpdateProviderHealth("First", new ProviderHealthStatus
            {
                Health = ProviderHealth.Healthy,
                CooldownUntilUtc = DateTime.UtcNow.AddMinutes(-1),
                LastChecked = DateTime.UtcNow
            });

            var afterCooldown = registry.GetProviders().Select(x => x.ProviderName).ToList();
            afterCooldown.Should().Contain("First");
            afterCooldown.Should().Contain("Second");
        }

        [Test]
        public void should_unregister_provider_and_clear_overrides()
        {
            var registry = new MetadataProviderRegistry(new IMetadataProvider[]
            {
                new TestBookSearchProvider("OpenLibrary", 10, true)
            });

            registry.DisableProvider("OpenLibrary");
            registry.SetProviderPriority("OpenLibrary", 99);
            registry.UpdateProviderHealth("OpenLibrary", new ProviderHealthStatus { Health = ProviderHealth.Unhealthy });
            registry.UnregisterProvider("OpenLibrary");

            registry.Count.Should().Be(0);
            registry.GetProvider("OpenLibrary").Should().BeNull();
            registry.GetProvidersHealthStatus().Should().BeEmpty();
        }

        private class TestBookSearchProvider : MetadataProviderBase, ISearchForNewBookV2
        {
            public TestBookSearchProvider(string providerName, int priority, bool isEnabled)
                : base(providerName, priority, isEnabled)
            {
            }

            public override bool SupportsBookSearch => true;

            public Task<List<Book>> SearchForNewBookAsync(string title, string author = null, BookSearchOptions options = null)
            {
                return Task.FromResult(new List<Book>());
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

        private class TestAuthorSearchProvider : MetadataProviderBase, ISearchForNewAuthorV2
        {
            public TestAuthorSearchProvider(string providerName, int priority, bool isEnabled)
                : base(providerName, priority, isEnabled)
            {
            }

            public override bool SupportsAuthorSearch => true;

            public Task<List<Author>> SearchForNewAuthorAsync(string name, AuthorSearchOptions options = null)
            {
                return Task.FromResult(new List<Author>());
            }

            public Task<Author> SearchByIdentifierAsync(string identifierType, string identifier, AuthorSearchOptions options = null)
            {
                return Task.FromResult<Author>(null);
            }

            public List<Author> SearchForNewAuthor(string name, AuthorSearchOptions options = null)
            {
                return new List<Author>();
            }
        }

        private class TestBookInfoProvider : MetadataProviderBase, IProvideBookInfoV2
        {
            public TestBookInfoProvider(string providerName, int priority, bool isEnabled)
                : base(providerName, priority, isEnabled)
            {
            }

            public override bool SupportsBookInfo => true;

            public Task<Tuple<string, Book, List<AuthorMetadata>>> GetBookInfoAsync(string providerId, BookInfoOptions options = null)
            {
                return Task.FromResult<Tuple<string, Book, List<AuthorMetadata>>>(null);
            }

            public Task<Tuple<string, Book, List<AuthorMetadata>>> GetBookInfoByIsbnAsync(string isbn, BookInfoOptions options = null)
            {
                return Task.FromResult<Tuple<string, Book, List<AuthorMetadata>>>(null);
            }

            public Task<Tuple<string, Book, List<AuthorMetadata>>> GetBookInfoByIdentifierAsync(string identifierType, string identifier, BookInfoOptions options = null)
            {
                return Task.FromResult<Tuple<string, Book, List<AuthorMetadata>>>(null);
            }

            public Tuple<string, Book, List<AuthorMetadata>> GetBookInfo(string providerId, BookInfoOptions options = null)
            {
                return null;
            }
        }

        private class TestAuthorInfoProvider : MetadataProviderBase, IProvideAuthorInfoV2
        {
            public TestAuthorInfoProvider(string providerName, int priority, bool isEnabled)
                : base(providerName, priority, isEnabled)
            {
            }

            public override bool SupportsAuthorInfo => true;

            public Task<Author> GetAuthorInfoAsync(string providerId, AuthorInfoOptions options = null)
            {
                return Task.FromResult<Author>(null);
            }

            public Task<Author> GetAuthorInfoByIdentifierAsync(string identifierType, string identifier, AuthorInfoOptions options = null)
            {
                return Task.FromResult<Author>(null);
            }

            public Task<HashSet<string>> GetChangedAuthorsAsync(DateTime startTime)
            {
                return Task.FromResult(new HashSet<string>());
            }

            public Author GetAuthorInfo(string providerId, AuthorInfoOptions options = null)
            {
                return null;
            }

            public HashSet<string> GetChangedAuthors(DateTime startTime)
            {
                return new HashSet<string>();
            }
        }

        private abstract class MetadataProviderBase : IMetadataProvider
        {
            protected MetadataProviderBase(string providerName, int priority, bool isEnabled)
            {
                ProviderName = providerName;
                Priority = priority;
                IsEnabled = isEnabled;
            }

            public string ProviderName { get; }
            public int Priority { get; }
            public bool IsEnabled { get; }

            public virtual bool SupportsBookSearch => false;
            public virtual bool SupportsAuthorSearch => false;
            public virtual bool SupportsIsbnLookup => false;
            public virtual bool SupportsAsinLookup => false;
            public virtual bool SupportsSeriesInfo => false;
            public virtual bool SupportsListInfo => false;
            public virtual bool SupportsCoverImages => false;
            public virtual bool SupportsBookInfo => false;
            public virtual bool SupportsAuthorInfo => false;

            public ProviderRateLimitInfo GetRateLimitInfo()
            {
                return new ProviderRateLimitInfo();
            }

            public ProviderHealthStatus GetHealthStatus()
            {
                return new ProviderHealthStatus();
            }
        }
    }
}
