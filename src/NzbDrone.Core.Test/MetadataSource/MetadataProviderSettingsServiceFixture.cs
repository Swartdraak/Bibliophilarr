using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Moq;
using NLog;
using NUnit.Framework;
using NzbDrone.Core.MetadataSource;

namespace NzbDrone.Core.Test.MetadataSource
{
    [TestFixture]
    public class MetadataProviderSettingsServiceFixture
    {
        [Test]
        public void should_persist_priority_and_apply_it_after_registry_recreation()
        {
            var stored = new List<MetadataProviderSettings>();
            var repository = BuildRepository(stored);
            var service = new MetadataProviderSettingsService(repository.Object, LogManager.GetCurrentClassLogger());

            service.SaveProviderPriority("Inventaire", 5);
            service.SaveProviderEnabled("Inventaire", true);
            service.SaveProviderPriority("GoogleBooks", 35);
            service.SaveProviderEnabled("GoogleBooks", false);

            var firstRegistry = BuildRegistry();
            service.ApplyPersistedSettings(firstRegistry);

            firstRegistry.GetBookSearchProviders().Select(x => x.ProviderName)
                .Should().ContainInOrder("Inventaire", "OpenLibrary");
            firstRegistry.GetBookSearchProviders().Should().NotContain(x => x.ProviderName == "GoogleBooks");

            // Simulate restart by recreating provider registry and reapplying persisted settings.
            var secondRegistry = BuildRegistry();
            service.ApplyPersistedSettings(secondRegistry);

            secondRegistry.GetBookSearchProviders().Select(x => x.ProviderName)
                .Should().ContainInOrder("Inventaire", "OpenLibrary");
            secondRegistry.GetBookSearchProviders().Should().NotContain(x => x.ProviderName == "GoogleBooks");
        }

        [Test]
        public void should_update_existing_priority_without_duplicate_records()
        {
            var stored = new List<MetadataProviderSettings>();
            var repository = BuildRepository(stored);
            var service = new MetadataProviderSettingsService(repository.Object, LogManager.GetCurrentClassLogger());

            service.SaveProviderPriority("OpenLibrary", 15);
            service.SaveProviderPriority("OpenLibrary", 7);

            stored.Should().HaveCount(1);
            stored[0].ProviderName.Should().Be("OpenLibrary");
            stored[0].Priority.Should().Be(7);
        }

        private static Mock<IMetadataProviderSettingsRepository> BuildRepository(List<MetadataProviderSettings> stored)
        {
            var repository = new Mock<IMetadataProviderSettingsRepository>();

            repository.Setup(x => x.All())
                .Returns(() => stored.ToList());

            repository.Setup(x => x.FindByProviderName(It.IsAny<string>()))
                .Returns((string providerName) => stored.SingleOrDefault(x => x.ProviderName == providerName));

            repository.Setup(x => x.Insert(It.IsAny<MetadataProviderSettings>()))
                .Returns((MetadataProviderSettings settings) =>
                {
                    settings.Id = stored.Count + 1;
                    stored.Add(settings);
                    return settings;
                });

            repository.Setup(x => x.Update(It.IsAny<MetadataProviderSettings>()))
                .Returns((MetadataProviderSettings settings) => settings);

            return repository;
        }

        private static MetadataProviderRegistry BuildRegistry()
        {
            return new MetadataProviderRegistry(new IMetadataProvider[]
            {
                new TestBookSearchProvider("OpenLibrary", 10, true),
                new TestBookSearchProvider("Inventaire", 20, true),
                new TestBookSearchProvider("GoogleBooks", 30, true)
            });
        }

        private class TestBookSearchProvider : ISearchForNewBookV2
        {
            public TestBookSearchProvider(string providerName, int priority, bool isEnabled)
            {
                ProviderName = providerName;
                Priority = priority;
                IsEnabled = isEnabled;
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

            public System.Threading.Tasks.Task<List<NzbDrone.Core.Books.Book>> SearchForNewBookAsync(string title, string author = null, BookSearchOptions options = null)
            {
                return System.Threading.Tasks.Task.FromResult(new List<NzbDrone.Core.Books.Book>());
            }

            public System.Threading.Tasks.Task<List<NzbDrone.Core.Books.Book>> SearchByIsbnAsync(string isbn, BookSearchOptions options = null)
            {
                return System.Threading.Tasks.Task.FromResult(new List<NzbDrone.Core.Books.Book>());
            }

            public System.Threading.Tasks.Task<List<NzbDrone.Core.Books.Book>> SearchByAsinAsync(string asin, BookSearchOptions options = null)
            {
                return System.Threading.Tasks.Task.FromResult(new List<NzbDrone.Core.Books.Book>());
            }

            public System.Threading.Tasks.Task<List<NzbDrone.Core.Books.Book>> SearchByIdentifierAsync(string identifierType, string identifier, BookSearchOptions options = null)
            {
                return System.Threading.Tasks.Task.FromResult(new List<NzbDrone.Core.Books.Book>());
            }

            public List<NzbDrone.Core.Books.Book> SearchForNewBook(string title, string author = null, BookSearchOptions options = null)
            {
                return new List<NzbDrone.Core.Books.Book>();
            }

            public List<NzbDrone.Core.Books.Book> SearchByIsbn(string isbn, BookSearchOptions options = null)
            {
                return new List<NzbDrone.Core.Books.Book>();
            }

            public List<NzbDrone.Core.Books.Book> SearchByAsin(string asin, BookSearchOptions options = null)
            {
                return new List<NzbDrone.Core.Books.Book>();
            }
        }
    }
}
