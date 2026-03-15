using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.MetadataSource;

namespace NzbDrone.Core.Test.MetadataSource
{
    /// <summary>
    /// Integration tests for MetadataProviderRegistry with real multi-provider compositions.
    /// Tests primary/fallback flows, degraded-provider skipping, and capability routing.
    /// </summary>
    [TestFixture]
    public class MetadataProviderRegistryIntegrationFixture
    {
        // ── helpers ───────────────────────────────────────────────────────────
        private static Book MakeBook(string title)
        {
            return new Book { Title = title };
        }

        // ── primary-only flow ─────────────────────────────────────────────────
        [Test]
        public async Task should_use_primary_provider_when_it_returns_results()
        {
            var registry = new MetadataProviderRegistry(new IMetadataProvider[]
            {
                new FakeBookSearchProvider("Primary", 10, true, _ => new List<Book> { MakeBook("Primary Result") }),
                new FakeBookSearchProvider("Fallback", 20, true, _ => new List<Book> { MakeBook("Fallback Result") }),
            });

            var result = await FirstNonEmptyBookSearchAsync(registry, "Foundation");

            result.Single().Title.Should().Be("Primary Result");
        }

        [Test]
        public async Task should_fall_back_to_secondary_when_primary_returns_empty()
        {
            var registry = new MetadataProviderRegistry(new IMetadataProvider[]
            {
                new FakeBookSearchProvider("Primary", 10, true, _ => new List<Book>()),
                new FakeBookSearchProvider("Secondary", 20, true, _ => new List<Book> { MakeBook("Secondary Result") }),
            });

            var result = await FirstNonEmptyBookSearchAsync(registry, "Foundation");

            result.Single().Title.Should().Be("Secondary Result");
        }

        [Test]
        public async Task should_fall_back_when_primary_throws()
        {
            var registry = new MetadataProviderRegistry(new IMetadataProvider[]
            {
                new FakeBookSearchProvider("Primary", 10, true, _ => throw new InvalidOperationException("transient error")),
                new FakeBookSearchProvider("Fallback", 20, true, _ => new List<Book> { MakeBook("Fallback Result") }),
            });

            var result = await FirstNonEmptyBookSearchWithFallbackOnExceptionAsync(registry, "Foundation");

            result.Single().Title.Should().Be("Fallback Result");
        }

        // ── health-based skipping ─────────────────────────────────────────────
        [Test]
        public async Task should_skip_unhealthy_provider_and_use_healthy_fallback()
        {
            var unhealthyStatus = new ProviderHealthStatus
            {
                Health = ProviderHealth.Unhealthy,
                SuccessRate = 0.0,
                ConsecutiveFailures = 10
            };

            var registry = new MetadataProviderRegistry(new IMetadataProvider[]
            {
                new FakeBookSearchProvider("Primary", 10, true, _ => new List<Book> { MakeBook("Should Not Appear") }),
                new FakeBookSearchProvider("Fallback", 20, true, _ => new List<Book> { MakeBook("Healthy Result") }),
            });

            registry.UpdateProviderHealth("Primary", unhealthyStatus);

            var result = await FirstNonEmptyBookSearchSkippingUnhealthyAsync(registry, "Foundation");

            result.Single().Title.Should().Be("Healthy Result");
        }

        [Test]
        public void should_use_degraded_provider_but_mark_in_health_status()
        {
            var degradedStatus = new ProviderHealthStatus
            {
                Health = ProviderHealth.Degraded,
                SuccessRate = 0.5
            };

            var registry = new MetadataProviderRegistry(new IMetadataProvider[]
            {
                new FakeBookSearchProvider("Degraded", 10, true, _ => new List<Book> { MakeBook("Degraded Result") }),
            });

            registry.UpdateProviderHealth("Degraded", degradedStatus);

            var health = registry.GetProvidersHealthStatus();

            registry.GetBookSearchProviders().Should().ContainSingle(p => p.ProviderName == "Degraded");
            health["Degraded"].Health.Should().Be(ProviderHealth.Degraded);
        }

        // ── capability routing ────────────────────────────────────────────────
        [Test]
        public void should_route_book_search_to_book_providers_only()
        {
            var registry = new MetadataProviderRegistry(new IMetadataProvider[]
            {
                new FakeBookSearchProvider("BookSearch", 10, true),
                new FakeAuthorSearchProvider("AuthorSearch", 10, true),
                new FakeBookInfoProvider("BookInfo", 10, true),
            });

            registry.GetBookSearchProviders().Select(p => p.ProviderName).Should().ContainSingle("BookSearch");
            registry.GetAuthorSearchProviders().Select(p => p.ProviderName).Should().ContainSingle("AuthorSearch");
            registry.GetBookInfoProviders().Select(p => p.ProviderName).Should().ContainSingle("BookInfo");
        }

        [Test]
        public void should_respect_priority_ordering_for_multi_capability_provider()
        {
            var registry = new MetadataProviderRegistry(new IMetadataProvider[]
            {
                new FakeFullProvider("AllCapabilities", 5, true),
                new FakeBookSearchProvider("SpecialistSearch", 1, true),
            });

            registry.GetBookSearchProviders().First().ProviderName.Should().Be("SpecialistSearch");
        }

        [Test]
        public void should_not_include_disabled_providers_in_capability_lists()
        {
            var registry = new MetadataProviderRegistry(new IMetadataProvider[]
            {
                new FakeBookSearchProvider("Enabled", 10, true),
                new FakeBookSearchProvider("Disabled", 5, true),
            });

            registry.DisableProvider("Disabled");

            registry.GetBookSearchProviders().Select(p => p.ProviderName).Should().ContainSingle("Enabled");
        }

        // ── ordering and override ─────────────────────────────────────────────
        [Test]
        public void should_compose_three_providers_in_correct_priority_order()
        {
            var registry = new MetadataProviderRegistry(new IMetadataProvider[]
            {
                new FakeBookSearchProvider("GoogleBooks", 30, true),
                new FakeBookSearchProvider("OpenLibrary", 10, true),
                new FakeBookSearchProvider("Inventaire", 20, true),
            });

            registry.GetBookSearchProviders().Select(p => p.ProviderName).Should().Equal("OpenLibrary", "Inventaire", "GoogleBooks");
        }

        [Test]
        public void should_reorder_after_priority_override()
        {
            var registry = new MetadataProviderRegistry(new IMetadataProvider[]
            {
                new FakeBookSearchProvider("OpenLibrary", 10, true),
                new FakeBookSearchProvider("Inventaire", 20, true),
                new FakeBookSearchProvider("GoogleBooks", 30, true),
            });

            registry.SetProviderPriority("Inventaire", 5);

            registry.GetBookSearchProviders().First().ProviderName.Should().Be("Inventaire");
        }

        [Test]
        public void should_handle_all_providers_disabled_gracefully()
        {
            var registry = new MetadataProviderRegistry(new IMetadataProvider[]
            {
                new FakeBookSearchProvider("P1", 10, false),
                new FakeBookSearchProvider("P2", 20, false),
            });

            registry.GetEnabledProviders().Should().BeEmpty();
            registry.GetBookSearchProviders().Should().BeEmpty();
        }

        // ── fallback-aware search helpers ─────────────────────────────────────
        private static async Task<List<Book>> FirstNonEmptyBookSearchAsync(
            IMetadataProviderRegistry registry,
            string query)
        {
            foreach (var provider in registry.GetBookSearchProviders())
            {
                var results = await provider.SearchForNewBookAsync(query);
                if (results.Any())
                {
                    return results;
                }
            }

            return new List<Book>();
        }

        private static async Task<List<Book>> FirstNonEmptyBookSearchWithFallbackOnExceptionAsync(
            IMetadataProviderRegistry registry,
            string query)
        {
            foreach (var provider in registry.GetBookSearchProviders())
            {
                try
                {
                    var results = await provider.SearchForNewBookAsync(query);
                    if (results.Any())
                    {
                        return results;
                    }
                }
                catch (Exception)
                {
                    // swallow and continue to next provider
                }
            }

            return new List<Book>();
        }

        private static async Task<List<Book>> FirstNonEmptyBookSearchSkippingUnhealthyAsync(
            IMetadataProviderRegistry registry,
            string query)
        {
            var healthMap = registry.GetProvidersHealthStatus();

            foreach (var provider in registry.GetBookSearchProviders())
            {
                if (healthMap.TryGetValue(provider.ProviderName, out var health) &&
                    health.Health == ProviderHealth.Unhealthy)
                {
                    continue;
                }

                var results = await provider.SearchForNewBookAsync(query);
                if (results.Any())
                {
                    return results;
                }
            }

            return new List<Book>();
        }

        // ── fakes ─────────────────────────────────────────────────────────────
        private class FakeBookSearchProvider : FakeProviderBase, ISearchForNewBookV2
        {
            private readonly Func<string, List<Book>> _searchImpl;

            public FakeBookSearchProvider(
                string name,
                int priority,
                bool enabled,
                Func<string, List<Book>> searchImpl = null)
                : base(name, priority, enabled)
            {
                _searchImpl = searchImpl ?? (_ => new List<Book>());
            }

            public override bool SupportsBookSearch
            {
                get { return true; }
            }

            public Task<List<Book>> SearchForNewBookAsync(string title, string author = null, BookSearchOptions options = null)
            {
                return Task.FromResult(_searchImpl(title));
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
                return _searchImpl(title);
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

        private class FakeAuthorSearchProvider : FakeProviderBase, ISearchForNewAuthorV2
        {
            public FakeAuthorSearchProvider(string name, int priority, bool enabled)
                : base(name, priority, enabled)
            {
            }

            public override bool SupportsAuthorSearch
            {
                get { return true; }
            }

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

        private class FakeBookInfoProvider : FakeProviderBase, IProvideBookInfoV2
        {
            public FakeBookInfoProvider(string name, int priority, bool enabled)
                : base(name, priority, enabled)
            {
            }

            public override bool SupportsBookInfo
            {
                get { return true; }
            }

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

        private class FakeFullProvider : FakeProviderBase, ISearchForNewBookV2, IProvideBookInfoV2
        {
            public FakeFullProvider(string name, int priority, bool enabled)
                : base(name, priority, enabled)
            {
            }

            public override bool SupportsBookSearch
            {
                get { return true; }
            }

            public override bool SupportsBookInfo
            {
                get { return true; }
            }

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

        private abstract class FakeProviderBase : IMetadataProvider
        {
            protected FakeProviderBase(string name, int priority, bool enabled)
            {
                ProviderName = name;
                Priority = priority;
                IsEnabled = enabled;
            }

            public string ProviderName { get; }
            public int Priority { get; }
            public bool IsEnabled { get; }

            public virtual bool SupportsBookSearch
            {
                get { return false; }
            }

            public virtual bool SupportsAuthorSearch
            {
                get { return false; }
            }

            public virtual bool SupportsIsbnLookup
            {
                get { return false; }
            }

            public virtual bool SupportsAsinLookup
            {
                get { return false; }
            }

            public virtual bool SupportsSeriesInfo
            {
                get { return false; }
            }

            public virtual bool SupportsListInfo
            {
                get { return false; }
            }

            public virtual bool SupportsCoverImages
            {
                get { return false; }
            }

            public virtual bool SupportsBookInfo
            {
                get { return false; }
            }

            public virtual bool SupportsAuthorInfo
            {
                get { return false; }
            }

            public ProviderRateLimitInfo GetRateLimitInfo()
            {
                return new ProviderRateLimitInfo();
            }

            public ProviderHealthStatus GetHealthStatus()
            {
                return new ProviderHealthStatus { Health = ProviderHealth.Healthy };
            }
        }
    }
}
