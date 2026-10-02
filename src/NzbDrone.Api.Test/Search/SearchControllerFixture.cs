using System;
using System.Collections.Generic;
using System.Linq;
using Bibliophilarr.Api.V1.Search;
using FluentAssertions;
using Moq;
using NLog;
using NLog.Config;
using NLog.Targets;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.MediaCover;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.Organizer;

namespace NzbDrone.Api.Test.Search
{
    [TestFixture]
    public class SearchControllerFixture
    {
        private SearchTelemetryService _telemetry;
        private MemoryTarget _memoryTarget;
        private LoggingConfiguration _originalConfiguration;

        [SetUp]
        public void SetUp()
        {
            _telemetry = new SearchTelemetryService();
            _originalConfiguration = LogManager.Configuration;
            _memoryTarget = new MemoryTarget("search-messages") { Layout = "${level}|${message}" };

            var configuration = new LoggingConfiguration();
            configuration.AddTarget(_memoryTarget);
            configuration.LoggingRules.Add(new LoggingRule("Search", LogLevel.Warn, _memoryTarget));
            LogManager.Configuration = configuration;
        }

        [TearDown]
        public void TearDown()
        {
            LogManager.Configuration = _originalConfiguration;
        }

        [Test]
        public void should_skip_unsupported_search_entities_and_record_observability()
        {
            var author = new Author
            {
                Metadata = new AuthorMetadata
                {
                    Name = "Anne Shirley",
                    ForeignAuthorId = "OL123A",
                    Images = new List<MediaCover>()
                }
            };

            var searchProxy = new Mock<IMetadataProviderOrchestrator>();
            searchProxy.Setup(v => v.SearchForNewEntity("anne"))
                .Returns(new List<object> { author, new Version(1, 0) });

            var fileNameBuilder = new Mock<IBuildFileNames>();
            fileNameBuilder.Setup(v => v.GetAuthorFolder(It.IsAny<Author>(), null))
                .Returns("Anne Shirley");

            var coverMapper = new Mock<IMapCoversToLocal>();

            var controller = new SearchController(searchProxy.Object, fileNameBuilder.Object, coverMapper.Object, _telemetry);

            var result = controller.Search("anne");

            result.Should().BeOfType<List<SearchResource>>();
            var resources = (List<SearchResource>)result;
            resources.Should().HaveCount(1);
            resources[0].Author.Should().NotBeNull();

            var snapshot = _telemetry.GetSnapshot();
            snapshot.UnsupportedEntityCount.Should().Be(1);
            snapshot.UnsupportedEntityTypes[typeof(Version).FullName].Should().Be(1);
            snapshot.Terms["anne"].Should().Be(1);
            _memoryTarget.Logs.Should().ContainSingle(log => log.Contains("unsupported search entity type") && log.Contains("anne"));
        }

        // ── Issue #248 regression: null-safe mapping of search results ──────────
        //
        // The primary search endpoint GET /api/v1/search returned 500
        // System.NullReferenceException from SearchController.MapToResource when a
        // real metadata-provider fan-out produced a Book whose Author.Metadata was
        // an unset LazyLoaded (Metadata._value == null).  BookResource.ToResource
        // dereferenced model.Author?.Value?.Metadata?.Value?.SortNameLastFirst and
        // threw because Metadata.Value returned null.
        //
        // These regression tests cover the three null shapes that triggered the NRE:
        //   1. Book.Author is a non-null LazyLoaded<Author> but Author.Metadata has
        //      _value == null  (the primary crash — real provider fan-out shape)
        //   2. Book.Author is null  (the LazyLoaded<Author> itself is unset)
        //   3. Book.Editions is null (the LazyLoaded<List<Edition>> is unset)
        //
        // Each test asserts 200-equivalent (a non-throwing List<SearchResource>)
        // with the expected resource content, matching acceptance criterion 1-3.
        [Test]
        public void should_not_throw_when_book_author_metadata_is_unset()
        {
            // Shape 1 (primary crash): Author is loaded but its Metadata LazyLoaded
            // was never set (new LazyLoaded<AuthorMetadata>() with _value == null).
            var author = new Author
            {
                Metadata = new LazyLoaded<AuthorMetadata>() // _value == null — unset
            };

            var book = new Book
            {
                Title = "The Hobbit",
                ForeignBookId = "OL123",
                Author = new LazyLoaded<Author>(author),
                Editions = new LazyLoaded<List<Edition>>(new List<Edition>
                {
                    new Edition
                    {
                        Title = "The Hobbit: There and Back Again",
                        Overview = "A quest begins.",
                        Images = new List<MediaCover>(),
                        Monitored = false
                    }
                })
            };

            var resources = SearchWithResults(book);

            resources.Should().HaveCount(1);
            var resource = resources[0];
            resource.Book.Should().NotBeNull();

            // BookResource.ToResource uses the selected edition's title when available.
            resource.Book.Title.Should().Be("The Hobbit: There and Back Again");
            resource.Book.ForeignBookId.Should().Be("OL123");
            resource.ForeignId.Should().Be("OL123");

            // Author was mapped (non-null) but Metadata was null — the mapper must
            // have produced an AuthorResource with null-safe fields, not thrown.
            resource.Book.Author.Should().NotBeNull();
        }

        [Test]
        public void should_not_throw_when_book_author_is_null()
        {
            // Shape 2: Book.Author (the LazyLoaded<Author>) itself is null.
            var book = new Book
            {
                Title = "The Hobbit",
                ForeignBookId = "OL456",
                Author = null, // LazyLoaded<Author> is null
                Editions = new LazyLoaded<List<Edition>>(new List<Edition>
                {
                    new Edition
                    {
                        Title = "The Hobbit",
                        Overview = "A quest.",
                        Images = new List<MediaCover>(),
                        Monitored = false
                    }
                })
            };

            var resources = SearchWithResults(book);

            resources.Should().HaveCount(1);
            var resource = resources[0];
            resource.Book.Should().NotBeNull();
            resource.Book.Title.Should().Be("The Hobbit");
            resource.Book.Author.Should().BeNull();
        }

        [Test]
        public void should_not_throw_when_book_editions_are_null()
        {
            // Shape 3: Book.Editions (the LazyLoaded<List<Edition>>) itself is null.
            var author = new Author
            {
                Metadata = new AuthorMetadata
                {
                    Name = "J.R.R. Tolkien",
                    ForeignAuthorId = "OL789A",
                    NameLastFirst = "Tolkien, J.R.R.",
                    SortNameLastFirst = "Tolkien, J.R.R.",
                    Images = new List<MediaCover>()
                }
            };

            var book = new Book
            {
                Title = "The Hobbit",
                ForeignBookId = "OL789",
                Author = new LazyLoaded<Author>(author),
                Editions = null // LazyLoaded<List<Edition>> is null
            };

            var resources = SearchWithResults(book);

            resources.Should().HaveCount(1);
            var resource = resources[0];
            resource.Book.Should().NotBeNull();
            resource.Book.Title.Should().Be("The Hobbit");
            resource.Book.Editions.Should().BeEmpty();
            resource.Book.Author.Should().NotBeNull();
            resource.Book.Author.AuthorName.Should().Be("J.R.R. Tolkien");
        }

        [Test]
        public void should_not_throw_when_book_author_metadata_is_null_and_editions_have_images()
        {
            // Combined shape: Author.Metadata unset AND editions carry images that
            // must still be mapped into the book resource (preserves the
            // selectedEdition.Images override behavior from the original code).
            var author = new Author
            {
                Metadata = new LazyLoaded<AuthorMetadata>() // unset
            };

            var edition = new Edition
            {
                Title = "The Hobbit",
                Overview = "A quest.",
                Images = new List<MediaCover>
                {
                    new MediaCover { CoverType = MediaCoverTypes.Cover, RemoteUrl = "https://covers.example/cover.jpg" }
                },
                Monitored = false
            };

            var book = new Book
            {
                Title = "The Hobbit",
                ForeignBookId = "OL100",
                Author = new LazyLoaded<Author>(author),
                Editions = new LazyLoaded<List<Edition>>(new List<Edition> { edition })
            };

            var resources = SearchWithResults(book);

            resources.Should().HaveCount(1);
            var resource = resources[0];
            resource.Book.Should().NotBeNull();
            resource.Book.Author.Should().NotBeNull();
            resource.Book.Images.Should().NotBeNull();
            resource.Book.Images.Should().Contain(c => c.CoverType == MediaCoverTypes.Cover);
        }

        [Test]
        public void should_still_return_author_resource_when_author_result_has_metadata()
        {
            // Regression guard for the Author entity path (unchanged behavior):
            // an Author result with populated Metadata must still map correctly.
            var author = new Author
            {
                Metadata = new AuthorMetadata
                {
                    Name = "Anne Shirley",
                    NameLastFirst = "Shirley, Anne",
                    SortNameLastFirst = "Shirley, Anne",
                    ForeignAuthorId = "OL123A",
                    Images = new List<MediaCover>()
                }
            };

            var resources = SearchWithResults(author);

            resources.Should().HaveCount(1);
            var resource = resources[0];
            resource.Author.Should().NotBeNull();
            resource.Author.AuthorName.Should().Be("Anne Shirley");
            resource.Author.SortNameLastFirst.Should().Be("Shirley, Anne");
            resource.ForeignId.Should().Be("OL123A");
        }

        // Helper: build a SearchController wired with a stub provider that returns
        // the given entities, then invoke Search and return the result list.
        private static List<SearchResource> SearchWithResults(params object[] entities)
        {
            var searchProxy = new Mock<IMetadataProviderOrchestrator>();
            searchProxy.Setup(v => v.SearchForNewEntity(It.IsAny<string>()))
                .Returns((List<object>)entities.ToList());

            var fileNameBuilder = new Mock<IBuildFileNames>();
            fileNameBuilder.Setup(v => v.GetAuthorFolder(It.IsAny<Author>(), null))
                .Returns("AuthorFolder");

            var coverMapper = new Mock<IMapCoversToLocal>();

            var controller = new SearchController(
                searchProxy.Object,
                fileNameBuilder.Object,
                coverMapper.Object,
                new SearchTelemetryService());

            var result = controller.Search("hobbit");
            return (List<SearchResource>)result;
        }
    }
}
