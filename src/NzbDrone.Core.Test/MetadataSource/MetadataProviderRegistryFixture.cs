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
    [TestFixture]
    public class MetadataProviderRegistryFixture
    {
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
