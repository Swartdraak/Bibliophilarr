using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.Exceptions;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MusicTests
{
    [TestFixture]
    public class RefreshBookServiceFallbackIntegrationFixture : CoreTest<RefreshBookService>
    {
        [Test]
        public void get_skyhook_data_should_return_null_when_all_providers_miss_and_not_throw()
        {
            var orchestrator = BuildOrchestrator(new IMetadataProvider[]
            {
                new ThrowingBookInfoProvider(),
                new NullBookInfoProvider()
            });

            Mocker.SetConstant<IMetadataProviderOrchestrator>(orchestrator);

            var method = typeof(RefreshBookService).GetMethod("GetSkyhookData", BindingFlags.Instance | BindingFlags.NonPublic);
            var localBook = new Book { ForeignBookId = "openlibrary:work:OL9999999W", AuthorMetadataId = 1 };

            Action act = () => method.Invoke(Subject, new object[] { localBook });

            act.Should().NotThrow();
            var result = (Author)method.Invoke(Subject, new object[] { localBook });
            result.Should().BeNull();
        }

        [Test]
        public void get_skyhook_data_should_use_fallback_provider_for_prefixed_work_ids()
        {
            var orchestrator = BuildOrchestrator(new IMetadataProvider[]
            {
                new ThrowingBookInfoProvider(),
                new SuccessfulOpenLibraryBookInfoProvider()
            });

            Mocker.SetConstant<IMetadataProviderOrchestrator>(orchestrator);

            var method = typeof(RefreshBookService).GetMethod("GetSkyhookData", BindingFlags.Instance | BindingFlags.NonPublic);
            var localBook = new Book { ForeignBookId = "openlibrary:work:OL8547083W", AuthorMetadataId = 414 };

            var result = (Author)method.Invoke(Subject, new object[] { localBook });

            result.Should().NotBeNull();
            result.Metadata.Value.ForeignAuthorId.Should().Be("openlibrary:author:OL29303A");
            result.Books.Value.Should().ContainSingle();
            result.Books.Value.Single().ForeignBookId.Should().Be("openlibrary:work:OL8547083W");
        }

        private static IMetadataProviderOrchestrator BuildOrchestrator(IEnumerable<IMetadataProvider> providers)
        {
            var registry = new Mock<IMetadataProviderRegistry>();
            registry.Setup(x => x.GetProviders()).Returns(providers.ToList());
            var telemetry = new MetadataProviderTelemetryService();
            return new MetadataProviderOrchestrator(registry.Object, telemetry, NLog.LogManager.GetCurrentClassLogger());
        }

        private sealed class ThrowingBookInfoProvider : IMetadataProvider, IProvideBookInfo
        {
            public string ProviderName => "ThrowingBookInfo";
            public int Priority => 1;
            public bool IsEnabled => true;
            public bool SupportsAuthorSearch => true;
            public bool SupportsBookSearch => true;
            public bool SupportsIsbnLookup => true;
            public bool SupportsSeriesInfo => false;
            public bool SupportsCoverImages => false;

            public Tuple<string, Book, List<AuthorMetadata>> GetBookInfo(string foreignBookId)
            {
                throw new BookNotFoundException(foreignBookId);
            }
        }

        private sealed class NullBookInfoProvider : IMetadataProvider, IProvideBookInfo
        {
            public string ProviderName => "NullBookInfo";
            public int Priority => 2;
            public bool IsEnabled => true;
            public bool SupportsAuthorSearch => true;
            public bool SupportsBookSearch => true;
            public bool SupportsIsbnLookup => true;
            public bool SupportsSeriesInfo => false;
            public bool SupportsCoverImages => false;

            public Tuple<string, Book, List<AuthorMetadata>> GetBookInfo(string foreignBookId)
            {
                return null;
            }
        }

        private sealed class SuccessfulOpenLibraryBookInfoProvider : IMetadataProvider, IProvideBookInfo, IProvideAuthorInfo
        {
            public string ProviderName => "SuccessfulOpenLibrary";
            public int Priority => 2;
            public bool IsEnabled => true;
            public bool SupportsAuthorSearch => true;
            public bool SupportsBookSearch => true;
            public bool SupportsIsbnLookup => true;
            public bool SupportsSeriesInfo => false;
            public bool SupportsCoverImages => false;

            public Tuple<string, Book, List<AuthorMetadata>> GetBookInfo(string foreignBookId)
            {
                var metadata = new AuthorMetadata
                {
                    Id = 414,
                    ForeignAuthorId = "openlibrary:author:OL29303A",
                    OpenLibraryAuthorId = "OL29303A",
                    Name = "Dante Alighieri"
                };

                var remoteBook = new Book
                {
                    ForeignBookId = "openlibrary:work:OL8547083W",
                    OpenLibraryWorkId = "OL8547083W",
                    AuthorMetadata = metadata,
                    AuthorMetadataId = 414,
                    Title = "The Divine Comedy"
                };

                return Tuple.Create(metadata.ForeignAuthorId, remoteBook, new List<AuthorMetadata> { metadata });
            }

            public Author GetAuthorInfo(string bibliophilarrId, bool useCache = true)
            {
                return new Author
                {
                    Metadata = new AuthorMetadata
                    {
                        Id = 414,
                        ForeignAuthorId = "openlibrary:author:OL29303A",
                        OpenLibraryAuthorId = "OL29303A",
                        Name = "Dante Alighieri"
                    }
                };
            }

            public HashSet<string> GetChangedAuthors(DateTime startTime)
            {
                return null;
            }
        }
    }
}
