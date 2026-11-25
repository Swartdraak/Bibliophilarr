using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Bibliophilarr.Api.V1.Search;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using NLog;
using NLog.Config;
using NLog.Targets;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.MediaCover;
using NzbDrone.Core.MediaFiles;
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

            var controller = new SearchController(searchProxy.Object, fileNameBuilder.Object, coverMapper.Object, _telemetry, Mock.Of<ICatalogueIdentityRepository>(), Mock.Of<ISeriesRepository>(), Mock.Of<IAuthorRepository>(), Mock.Of<IBookRepository>());

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

        [Test]
        public void should_return_one_result_for_each_normalized_provider_identity_and_keep_namesakes()
        {
            var results = SearchWithResults(
                SearchAuthor("hardcover:author:41"),
                SearchAuthor("hardcover:author:041"),
                SearchAuthor("hardcover:author:42"),
                new Book { ForeignBookId = "hardcover:work:41", Title = "Same title" },
                new Book { ForeignBookId = "hardcover:work:041", Title = "Same title" },
                new Book { ForeignBookId = "hardcover:work:42", Title = "Same title" },
                new SeriesSearchSummary { ForeignSeriesId = "hardcover:series:41", Title = "Same title" },
                new SeriesSearchSummary { ForeignSeriesId = "hardcover:series:041", Title = "Same title" },
                new SeriesSearchSummary { ForeignSeriesId = "hardcover:series:42", Title = "Same title" });

            results.Select(result => result.ForeignId).Should().Equal(
                "hardcover:author:41",
                "hardcover:author:42",
                "hardcover:work:41",
                "hardcover:work:42",
                "hardcover:series:41",
                "hardcover:series:42");
        }

        [Test]
        public void should_report_only_verified_existing_author_metadata_for_book_search_results()
        {
            const string storedCanonicalAuthor = "a0e693a449884a6981ff7a7f9f855a00";
            var authorAlias = SearchAuthor("openlibrary:author:OL300A");
            var namesake = SearchAuthor("openlibrary:author:OL301A");
            var disputed = SearchAuthor("openlibrary:author:OL302A");
            disputed.Metadata.Value.CanonicalId = storedCanonicalAuthor;

            var identity = new Mock<ICatalogueIdentityRepository>();
            identity.Setup(repository => repository.FindByProviderId(CatalogueEntityKind.Author, "OpenLibrary", "OL300A")).Returns(17);
            identity.Setup(repository => repository.FindByProviderId(CatalogueEntityKind.Author, "OpenLibrary", "OL302A")).Returns(19);
            identity.Setup(repository => repository.GetSources(CatalogueEntityKind.Author, 17))
                .Returns(new[] { new CatalogueSourceAssociation { Provider = "OpenLibrary", ProviderId = "OL300A", Evidence = "primary-provider-id" } });
            identity.Setup(repository => repository.FindByRoute(CatalogueEntityKind.Author, storedCanonicalAuthor)).Returns(17);
            identity.Setup(repository => repository.FindByProviderId(CatalogueEntityKind.Work, "OpenLibrary", "OL402W")).Returns(82);

            var proxy = new Mock<IMetadataProviderOrchestrator>(MockBehavior.Strict);
            proxy.Setup(provider => provider.SearchForNewBook("Same title", null, false)).Returns(new List<Book>
            {
                new Book { ForeignBookId = "openlibrary:work:OL401W", Title = "Same title", Author = authorAlias },
                new Book { ForeignBookId = "openlibrary:work:OL402W", Title = "Same title", Author = namesake },
                new Book { ForeignBookId = "openlibrary:work:OL403W", Title = "Same title", Author = disputed }
            });
            var controller = new SearchController(
                proxy.Object,
                Mock.Of<IBuildFileNames>(),
                Mock.Of<IMapCoversToLocal>(),
                _telemetry,
                identity.Object,
                Mock.Of<ISeriesRepository>(),
                Mock.Of<IAuthorRepository>(),
                Mock.Of<IBookRepository>());

            var results = (List<SearchResource>)controller.Search("Same title", "book");

            results.Should().HaveCount(3);
            results[0].CatalogueOwnerId.Should().BeNull();
            results[0].Book.Author.Id.Should().Be(0);
            results[0].Book.Author.ForeignAuthorId.Should().Be("openlibrary:author:OL300A");
            results[0].Book.Author.AuthorMetadataId.Should().Be(17);
            results[1].CatalogueOwnerId.Should().Be(82);
            results[1].Book.Author.AuthorMetadataId.Should().Be(0);
            results[2].Book.Author.AuthorMetadataId.Should().Be(0);
            proxy.Verify(provider => provider.SearchForNewBook("Same title", null, false), Times.Once);
            proxy.VerifyNoOtherCalls();
        }

        private static Author SearchAuthor(string providerId)
        {
            return new Author
            {
                Metadata = new AuthorMetadata
                {
                    Name = "Jeff Fixture",
                    ForeignAuthorId = providerId
                }
            };
        }

        private static void GivenVerifiedCrossProviderAuthor(Mock<ICatalogueIdentityRepository> identity)
        {
            identity.Setup(repository => repository.FindByProviderId(CatalogueEntityKind.Author, "Hardcover", "41")).Returns(11);
            identity.Setup(repository => repository.FindByProviderId(CatalogueEntityKind.Author, "OpenLibrary", "OL300A")).Returns(11);
            identity.Setup(repository => repository.GetSources(CatalogueEntityKind.Author, 11))
                .Returns(new[]
                {
                    new CatalogueSourceAssociation { Provider = "Hardcover", ProviderId = "41", Evidence = "primary-provider-id" },
                    new CatalogueSourceAssociation { Provider = "OpenLibrary", ProviderId = "OL300A", Evidence = "two-isbn-title-author-work-witnesses" }
                });
        }

        [Test]
        public void should_group_verified_cross_provider_aliases_and_keep_the_first_payload()
        {
            const string canonicalId = "a0e693a449884a6981ff7a7f9f855a00";
            var identity = new Mock<ICatalogueIdentityRepository>();
            GivenVerifiedCrossProviderAuthor(identity);
            identity.Setup(repository => repository.FindByRoute(CatalogueEntityKind.Work, canonicalId)).Returns(12);
            identity.Setup(repository => repository.FindByProviderId(CatalogueEntityKind.Work, "Hardcover", "41")).Returns(12);
            identity.Setup(repository => repository.FindByProviderId(CatalogueEntityKind.Work, "OpenLibrary", "OL300W")).Returns(12);
            var firstEdition = new Edition { ForeignEditionId = "hardcover:source-edition:15", Title = "First edition" };

            var results = SearchWithCatalogueResults(identity.Object,
                SearchAuthor("hardcover:author:41"),
                SearchAuthor("openlibrary:author:OL300A"),
                SearchAuthor("hardcover:author:42"),
                new Book
                {
                    ForeignBookId = "hardcover:work:41",
                    Title = "First result",
                    CanonicalId = canonicalId,
                    Editions = new List<Edition> { firstEdition }
                },
                new Book { ForeignBookId = "openlibrary:work:OL300W", Title = "Alternate payload" },
                new SeriesSearchSummary { ForeignSeriesId = "hardcover:series:41", Title = "Other entity type" });

            results.Select(result => result.ForeignId).Should().Equal(
                "hardcover:author:41",
                "hardcover:author:42",
                "hardcover:work:41",
                "hardcover:series:41");
            results.Select(result => result.CatalogueOwnerId).Should().Equal((int?)11, null, (int?)12, null);
            results[2].Book.Editions.Should().ContainSingle().Which.ForeignEditionId.Should().Be(firstEdition.ForeignEditionId);
        }

        [Test]
        public void should_use_only_exact_stored_author_and_work_art_when_the_provider_result_has_none()
        {
            const string storedPosterUrl = "https://covers.example/stored-author.jpg";
            const string storedCoverUrl = "https://covers.example/stored-book.jpg";
            const string remoteCoverUrl = "https://covers.example/provider-book.jpg";
            var authorHit = SearchAuthor("hardcover:author:41");
            var namesake = SearchAuthor("hardcover:author:42");
            var bookHit = new Book
            {
                Title = "Same title",
                ForeignBookId = "hardcover:work:41",
                Editions = new List<Edition> { new Edition { ForeignEditionId = "hardcover:source-edition:15", Title = "Selected edition" } }
            };
            var bookWithArt = new Book
            {
                Title = bookHit.Title,
                ForeignBookId = "hardcover:work:42",
                Editions = new List<Edition>
                {
                    new Edition { Overview = "Provider synopsis", Images = new List<MediaCover> { new MediaCover(MediaCoverTypes.Cover, remoteCoverUrl) } }
                }
            };
            var disputedBook = new Book
            {
                Title = bookHit.Title,
                ForeignBookId = "hardcover:work:43",
                CanonicalId = "b302caf165b64272ad2d196d4b1b203c"
            };
            var canonicalOnlyBook = new Book
            {
                Title = bookHit.Title,
                ForeignBookId = "hardcover:work:44",
                CanonicalId = "2f0a12f18c6846929959e858933da1fd"
            };

            var identity = new Mock<ICatalogueIdentityRepository>();
            identity.Setup(r => r.FindByProviderId(CatalogueEntityKind.Author, "Hardcover", "41")).Returns(17);
            identity.Setup(repository => repository.GetSources(CatalogueEntityKind.Author, 17))
                .Returns(new[] { new CatalogueSourceAssociation { Provider = "Hardcover", ProviderId = "41", Evidence = "two-isbn-title-author-work-witnesses" } });
            identity.Setup(r => r.FindByProviderId(CatalogueEntityKind.Work, "Hardcover", "41")).Returns(23);
            identity.Setup(r => r.FindByProviderId(CatalogueEntityKind.Work, "Hardcover", "42")).Returns(24);
            identity.Setup(r => r.FindByProviderId(CatalogueEntityKind.Work, "Hardcover", "43")).Returns(25);
            identity.Setup(r => r.FindByRoute(CatalogueEntityKind.Work, disputedBook.CanonicalId)).Returns(23);
            identity.Setup(r => r.FindByRoute(CatalogueEntityKind.Work, canonicalOnlyBook.CanonicalId)).Returns(26);

            var storedAuthor = SearchAuthor("openlibrary:author:OL800A");
            storedAuthor.Id = 91;
            storedAuthor.AuthorMetadataId = 17;
            var storedPoster = new MediaCover(MediaCoverTypes.Poster, storedPosterUrl);
            storedAuthor.Metadata.Value.Images = new List<MediaCover> { storedPoster };
            var authors = new Mock<IAuthorRepository>(MockBehavior.Strict);
            authors.Setup(r => r.GetAuthorByMetadataId(17)).Returns(storedAuthor);

            var storedCover = new MediaCover(MediaCoverTypes.Cover, storedCoverUrl);
            var books = new Mock<IBookRepository>(MockBehavior.Strict);
            books.Setup(r => r.Find(23)).Returns(new Book
            {
                Id = 23,
                ForeignBookId = "openlibrary:work:OL800W",
                Editions = new List<Edition> { new Edition { Images = new List<MediaCover> { storedCover } } }
            });

            var proxy = new Mock<IMetadataProviderOrchestrator>(MockBehavior.Strict);
            proxy.Setup(p => p.SearchForNewEntity("Fixture"))
                .Returns(new List<object> { authorHit, namesake, bookHit, bookWithArt, disputedBook, canonicalOnlyBook });
            var covers = new Mock<IMapCoversToLocal>();
            covers.Setup(mapper => mapper.ConvertToLocalUrls(It.IsAny<int>(), It.IsAny<MediaCoverEntity>(), It.IsAny<IEnumerable<MediaCover>>()))
                .Callback<int, MediaCoverEntity, IEnumerable<MediaCover>>((id, entity, images) =>
                {
                    foreach (var image in images)
                    {
                        image.RemoteUrl = image.Url;
                        image.Url = $"/{entity}/{id}/fixture.jpg";
                    }
                });

            var controller = new SearchController(proxy.Object,
                Mock.Of<IBuildFileNames>(),
                covers.Object,
                _telemetry,
                identity.Object,
                Mock.Of<ISeriesRepository>(),
                authors.Object,
                books.Object);

            var results = (List<SearchResource>)controller.Search("Fixture");

            results.Select(result => result.ForeignId).Should().Equal(
                authorHit.ForeignAuthorId,
                namesake.ForeignAuthorId,
                bookHit.ForeignBookId,
                bookWithArt.ForeignBookId,
                disputedBook.ForeignBookId,
                canonicalOnlyBook.ForeignBookId);
            results[0].Author.Id.Should().Be(0);
            results[0].Author.RemotePoster.Should().Be(storedPosterUrl);
            results[0].Author.Images.Should().ContainSingle().Which.Url.Should().Be("/Author/91/fixture.jpg");
            results[1].Author.Images.Should().BeEmpty();
            results[2].Book.Id.Should().Be(0);
            results[2].Book.RemoteCover.Should().Be(storedCoverUrl);
            results[2].Book.Images.Should().ContainSingle().Which.Url.Should().Be("/Book/23/fixture.jpg");
            results[2].Book.Editions.Should().ContainSingle().Which.ForeignEditionId.Should().Be("hardcover:source-edition:15");
            results[3].Book.RemoteCover.Should().Be(remoteCoverUrl);
            results[4].Book.Images.Should().BeEmpty();
            results[5].Book.Images.Should().BeEmpty();
            storedPoster.Url.Should().Be(storedPosterUrl);
            storedCover.Url.Should().Be(storedCoverUrl);
            authors.Verify(r => r.GetAuthorByMetadataId(17), Times.Once);
            books.Verify(r => r.Find(23), Times.Once);
            proxy.Verify(p => p.SearchForNewEntity("Fixture"), Times.Once);
        }

        [Test]
        public void should_map_verified_fallback_art_without_replacing_existing_art_or_selected_editions()
        {
            var firstAuthor = SearchAuthor("hardcover:author:41");
            var alternateAuthor = SearchAuthor("openlibrary:author:OL300A");
            var poster = new MediaCover(MediaCoverTypes.Poster, "https://covers.example/fallback-author.jpg");
            alternateAuthor.Metadata.Value.Images = new List<MediaCover>
            {
                new MediaCover(MediaCoverTypes.Banner, "https://covers.example/other-art.jpg"),
                new MediaCover(MediaCoverTypes.Poster, "relative-placeholder.jpg"),
                poster
            };

            var firstBook = new Book
            {
                ForeignBookId = "hardcover:work:41",
                Editions = new List<Edition> { new Edition { ForeignEditionId = "hardcover:source-edition:15", Title = "Selected edition" } }
            };
            var alternateBook = new Book
            {
                ForeignBookId = "hardcover:work:041",
                Editions = new List<Edition>
                {
                    new Edition
                    {
                        Title = "Alternate edition",
                        Images = new List<MediaCover> { new MediaCover(MediaCoverTypes.Cover, "https://covers.example/fallback-book.jpg") }
                    }
                }
            };
            var bookWithArt = new Book
            {
                ForeignBookId = "hardcover:work:42",
                Editions = new List<Edition>
                {
                    new Edition
                    {
                        Title = "Book with a cover",
                        Images = new List<MediaCover> { new MediaCover(MediaCoverTypes.Cover, "https://covers.example/representative.jpg") }
                    }
                }
            };
            var unusedAlternative = new Book { ForeignBookId = "hardcover:work:042", Editions = alternateBook.Editions };

            var identity = new Mock<ICatalogueIdentityRepository>();
            GivenVerifiedCrossProviderAuthor(identity);
            var proxy = new Mock<IMetadataProviderOrchestrator>();
            proxy.Setup(search => search.SearchForNewEntity("Fixture"))
                .Returns(new List<object> { firstAuthor, firstBook, bookWithArt, alternateAuthor, alternateBook, unusedAlternative });

            var covers = new Mock<IMapCoversToLocal>();
            covers.Setup(mapper => mapper.ConvertToLocalUrls(0, It.IsAny<MediaCoverEntity>(), It.IsAny<IEnumerable<MediaCover>>()))
                .Callback<int, MediaCoverEntity, IEnumerable<MediaCover>>((_, _, images) =>
                {
                    foreach (var image in images)
                    {
                        image.RemoteUrl = image.Url;
                        image.Url = "/MediaCoverProxy/fixture.jpg";
                    }
                });

            var controller = new SearchController(proxy.Object, Mock.Of<IBuildFileNames>(), covers.Object, _telemetry, identity.Object, Mock.Of<ISeriesRepository>(), Mock.Of<IAuthorRepository>(), Mock.Of<IBookRepository>());
            var results = (List<SearchResource>)controller.Search("Fixture");

            results.Select(result => result.ForeignId).Should().Equal("hardcover:author:41", "hardcover:work:41", "hardcover:work:42");
            results[0].Author.RemotePoster.Should().Be("https://covers.example/fallback-author.jpg");
            results[0].Author.Images.Should().ContainSingle().Which.Url.Should().Be("/MediaCoverProxy/fixture.jpg");
            results[1].Book.RemoteCover.Should().Be("https://covers.example/fallback-book.jpg");
            results[1].Book.Editions.Should().ContainSingle().Which.ForeignEditionId.Should().Be("hardcover:source-edition:15");
            results[2].Book.Images.Should().ContainSingle().Which.RemoteUrl.Should().Be("https://covers.example/representative.jpg");
            poster.Url.Should().Be("https://covers.example/fallback-author.jpg");
            covers.Verify(mapper => mapper.ConvertToLocalUrls(0, MediaCoverEntity.Author, It.IsAny<IEnumerable<MediaCover>>()), Times.Once);
            covers.Verify(mapper => mapper.ConvertToLocalUrls(0, MediaCoverEntity.Book, It.IsAny<IEnumerable<MediaCover>>()), Times.Exactly(2));
        }

        [TestCase("exact-source", null, "Stored exact synopsis")]
        [TestCase("exact-source", "  ", "Stored exact synopsis")]
        [TestCase("exact-source", "Provider synopsis", "Provider synopsis")]
        [TestCase("different-source", null, null)]
        [TestCase("different-owner", null, null)]
        public void should_fill_only_a_missing_synopsis_from_the_same_stored_primary_work(
            string storedMatch, string providerSynopsis, string expected)
        {
            const string title = "Fixture Work";
            const string coverUrl = "https://covers.example/search.jpg";
            var selectedEdition = new Edition
            {
                Title = title,
                ForeignEditionId = "hardcover:source-edition:15",
                Overview = providerSynopsis,
                Images = new List<MediaCover> { new MediaCover(MediaCoverTypes.Cover, coverUrl) }
            };
            var first = new Book { Title = title, ForeignBookId = "hardcover:work:041", Editions = new List<Edition> { selectedEdition } };
            var repeated = new Book { Title = title, ForeignBookId = "hardcover:work:41" };
            var namesake = new Book { Title = title, ForeignBookId = "hardcover:work:99" };
            var stored = new Book
            {
                Id = storedMatch == "different-owner" ? 25 : 23,
                ForeignBookId = storedMatch == "different-source" ? "openlibrary:work:OL41W" : repeated.ForeignBookId,
                Editions = new List<Edition> { new Edition { ForeignEditionId = "hardcover:source-edition:16", Overview = "Stored exact synopsis" } }
            };

            var identity = new Mock<ICatalogueIdentityRepository>();
            identity.Setup(repository => repository.FindByProviderId(CatalogueEntityKind.Work, "Hardcover", "41")).Returns(23);
            var books = new Mock<IBookRepository>(MockBehavior.Strict);
            books.Setup(repository => repository.Find(23)).Returns(stored);
            var proxy = new Mock<IMetadataProviderOrchestrator>(MockBehavior.Strict);
            proxy.Setup(search => search.SearchForNewBook(title, null, false)).Returns(new List<Book> { first, namesake, repeated });
            var controller = new SearchController(proxy.Object,
                Mock.Of<IBuildFileNames>(),
                Mock.Of<IMapCoversToLocal>(),
                _telemetry,
                identity.Object,
                Mock.Of<ISeriesRepository>(),
                Mock.Of<IAuthorRepository>(),
                books.Object);

            var results = (List<SearchResource>)controller.Search(title, "book");

            results.Select(result => result.ForeignId).Should().Equal(first.ForeignBookId, namesake.ForeignBookId);
            results[0].CatalogueOwnerId.Should().Be(23);
            results[0].Book.Overview.Should().Be(expected);
            results[0].Book.Editions.Should().ContainSingle().Which.ForeignEditionId.Should().Be(selectedEdition.ForeignEditionId);
            results[0].Book.Images.Should().ContainSingle().Which.Url.Should().Be(coverUrl);
            results[1].Book.Overview.Should().BeNull();
            results[1].CatalogueOwnerId.Should().BeNull();
            selectedEdition.Overview.Should().Be(providerSynopsis);
            books.Verify(repository => repository.Find(23), string.IsNullOrWhiteSpace(providerSynopsis) ? Times.Once() : Times.Never());
            books.VerifyNoOtherCalls();
            proxy.Verify(search => search.SearchForNewBook(title, null, false), Times.Once());
            proxy.VerifyNoOtherCalls();
        }

        [Test]
        public void should_use_the_typed_source_when_canonical_id_has_no_catalogue_owner()
        {
            const string unpersistedCanonicalId = "a0e693a449884a6981ff7a7f9f855a00";

            var results = SearchWithResults(
                new Book { ForeignBookId = "hardcover:work:41", Title = "First result", CanonicalId = unpersistedCanonicalId },
                new Book { ForeignBookId = "hardcover:work:41", Title = "Repeated source" },
                new Book { ForeignBookId = "hardcover:work:42", Title = "Other work", CanonicalId = unpersistedCanonicalId });

            results.Select(result => result.ForeignId).Should().Equal("hardcover:work:41", "hardcover:work:42");
            results[0].Book.Title.Should().Be("First result");
        }

        [Test]
        public void should_keep_results_when_identity_is_unknown_or_verified_sources_disagree()
        {
            const string canonicalId = "a0e693a449884a6981ff7a7f9f855a00";
            var identity = new Mock<ICatalogueIdentityRepository>();
            identity.Setup(repository => repository.FindByRoute(CatalogueEntityKind.Work, canonicalId)).Returns(11);
            identity.Setup(repository => repository.FindByProviderId(CatalogueEntityKind.Work, "Hardcover", "41")).Returns(12);

            var results = SearchWithCatalogueResults(identity.Object,
                SearchAuthor(null),
                SearchAuthor(null),
                new Book { ForeignBookId = "hardcover:work:41", CanonicalId = canonicalId },
                new Book { ForeignBookId = "hardcover:work:41" },
                new Book { ForeignBookId = "googlebooks:work:Ab13" },
                new Book { ForeignBookId = "googlebooks:work:ab13" });

            results.Should().HaveCount(6);
        }

        [Test]
        public void should_use_stored_member_art_only_for_the_verified_series_namesake()
        {
            var proxy = new Mock<IMetadataProviderOrchestrator>(MockBehavior.Strict);
            proxy.Setup(search => search.SearchForNewSeries("Fixture"))
                .Returns(new List<SeriesSearchSummary>
                {
                    new SeriesSearchSummary { ForeignSeriesId = "hardcover:series:40", Title = "Fixture" },
                    new SeriesSearchSummary { ForeignSeriesId = "hardcover:series:41", Title = "Fixture" }
                });

            var identity = new Mock<ICatalogueIdentityRepository>();
            identity.Setup(repository => repository.FindByProviderId(CatalogueEntityKind.Series, "Hardcover", "41")).Returns(11);
            var book = new Book
            {
                Id = 9,
                Title = "Verified member",
                Editions = new List<Edition>
                {
                    new Edition
                    {
                        Images = new List<MediaCover> { new MediaCover(MediaCoverTypes.Cover, "https://covers.example/source.jpg") }
                    }
                }
            };
            var series = new Mock<ISeriesRepository>(MockBehavior.Strict);
            series.Setup(repository => repository.Find(11))
                .Returns(new Series
                {
                    Id = 11,
                    LinkItems = new List<SeriesBookLink>
                    {
                        new SeriesBookLink { SeriesId = 11, BookId = book.Id, Book = book, IsPrimary = true, SeriesPosition = 1 }
                    }
                });

            var covers = new Mock<IMapCoversToLocal>();
            covers.Setup(mapper => mapper.ConvertToLocalUrls(9, MediaCoverEntity.Book, It.IsAny<IEnumerable<MediaCover>>()))
                .Callback<int, MediaCoverEntity, IEnumerable<MediaCover>>((_, _, images) =>
                    images.Single().Url = "/MediaCoverProxy/verified-member.jpg");

            var controller = new SearchController(proxy.Object, Mock.Of<IBuildFileNames>(), covers.Object, _telemetry, identity.Object, series.Object, Mock.Of<IAuthorRepository>(), Mock.Of<IBookRepository>());
            var results = (List<SearchResource>)controller.Search("Fixture", "series");

            results.Select(result => result.ForeignId).Should().Equal("hardcover:series:40", "hardcover:series:41");
            results[0].SeriesCovers.Should().BeNullOrEmpty();
            var cover = results[1].SeriesCovers.Should().ContainSingle().Which;
            cover.BookTitle.Should().Be(book.Title);
            cover.Url.Should().Be("/MediaCoverProxy/verified-member.jpg");
            book.Editions.Value.Single().Images.Single().Url.Should().Be("https://covers.example/source.jpg");
            proxy.Verify(search => search.SearchForNewSeries("Fixture"), Times.Once);
            proxy.VerifyNoOtherCalls();
        }

        [Test]
        public void should_issue_only_the_requested_search_and_keep_series_provider_identity()
        {
            var proxy = new Mock<IMetadataProviderOrchestrator>(MockBehavior.Strict);
            proxy.Setup(search => search.SearchForNewSeries("Fixture"))
                .Returns(new List<SeriesSearchSummary>
                {
                    new SeriesSearchSummary { ForeignSeriesId = "hardcover:series:10", Title = "Fixture", Slug = "fixture" },
                    new SeriesSearchSummary { ForeignSeriesId = "hardcover:series:11", Title = "Fixture", Slug = "another-fixture" }
                });
            var controller = new SearchController(proxy.Object, Mock.Of<IBuildFileNames>(), Mock.Of<IMapCoversToLocal>(), _telemetry, Mock.Of<ICatalogueIdentityRepository>(), Mock.Of<ISeriesRepository>(), Mock.Of<IAuthorRepository>(), Mock.Of<IBookRepository>());

            var results = (List<SearchResource>)controller.Search(" Fixture ", "series");

            results.Select(result => result.ForeignId).Should().Equal("hardcover:series:10", "hardcover:series:11");
            results.Should().OnlyContain(result => result.Series.Title == "Fixture" && result.Book == null && result.Author == null);
            proxy.Verify(search => search.SearchForNewSeries("Fixture"), Times.Once());
            proxy.VerifyNoOtherCalls();
        }

        [TestCase("Fixture Author", null)]
        [TestCase("openlibrary:author:OL302A", "author")]
        public void should_require_verified_existing_author_identity_for_all_search_scopes(string term, string scope)
        {
            const string weakId = "openlibrary:author:OL302A";
            var result = SearchAuthor(weakId);
            var identity = new Mock<ICatalogueIdentityRepository>();
            identity.Setup(repository => repository.FindByProviderId(CatalogueEntityKind.Author, "OpenLibrary", "OL302A")).Returns(11);
            identity.Setup(repository => repository.GetSources(CatalogueEntityKind.Author, 11))
                .Returns(new[] { new CatalogueSourceAssociation { Provider = "OpenLibrary", ProviderId = "OL302A", Evidence = "legacy-linked-id" } });
            var search = new Mock<IMetadataProviderOrchestrator>();
            search.Setup(proxy => proxy.SearchForNewEntity(term)).Returns(new List<object> { result });
            search.Setup(proxy => proxy.SearchForInteractiveAuthors(term, It.IsAny<CancellationToken>())).Returns(new List<Author> { result });
            var controller = new SearchController(
                search.Object,
                Mock.Of<IBuildFileNames>(),
                Mock.Of<IMapCoversToLocal>(),
                _telemetry,
                identity.Object,
                Mock.Of<ISeriesRepository>(),
                Mock.Of<IAuthorRepository>(),
                Mock.Of<IBookRepository>());

            var card = ((List<SearchResource>)controller.Search(term, scope)).Should().ContainSingle().Which;

            card.ForeignId.Should().Be(weakId);
            card.CatalogueOwnerId.Should().BeNull();
        }

        [Test]
        public void should_supplement_interactive_author_results_using_only_exact_name_and_corroborated_identity()
        {
            const string term = "Jeff Fixture";
            const string primaryId = "hardcover:author:41";
            const string portrait = "https://covers.example/verified-alternate-portrait.jpg";
            var primary = SearchAuthor(primaryId);
            var verifiedAlias = SearchAuthor("openlibrary:author:OL300A");
            verifiedAlias.Metadata.Value.Images = new List<MediaCover> { new (MediaCoverTypes.Poster, portrait) };
            var namesake = SearchAuthor("openlibrary:author:OL301A");
            var weakAlias = SearchAuthor("openlibrary:author:OL302A");
            weakAlias.Metadata.Value.Images = new List<MediaCover> { new (MediaCoverTypes.Poster, "https://covers.example/unverified-portrait.jpg") };
            var fuzzy = SearchAuthor("openlibrary:author:OL303A");
            fuzzy.Metadata.Value.Name = "Jeff Other";

            var identity = new Mock<ICatalogueIdentityRepository>();
            identity.Setup(repository => repository.FindByProviderId(CatalogueEntityKind.Author, "Hardcover", "41")).Returns(11);
            identity.Setup(repository => repository.FindByProviderId(CatalogueEntityKind.Author, "OpenLibrary", "OL300A")).Returns(11);
            identity.Setup(repository => repository.FindByProviderId(CatalogueEntityKind.Author, "OpenLibrary", "OL302A")).Returns(11);
            identity.Setup(repository => repository.GetSources(CatalogueEntityKind.Author, 11))
                .Returns(new[]
                {
                    new CatalogueSourceAssociation { Provider = "Hardcover", ProviderId = "41", Evidence = "primary-provider-id" },
                    new CatalogueSourceAssociation { Provider = "OpenLibrary", ProviderId = "OL300A", Evidence = "two-isbn-title-author-work-witnesses" },
                    new CatalogueSourceAssociation { Provider = "OpenLibrary", ProviderId = "OL302A", Evidence = "legacy-linked-id" }
                });

            var hardcover = AuthorSearchAdapter("Hardcover", term, () => new List<Author> { primary });
            var openLibrary = AuthorSearchAdapter("OpenLibrary", term, () => new List<Author> { verifiedAlias, namesake, weakAlias, fuzzy });
            var controller = InteractiveAuthorSearchController(identity.Object, hardcover.Provider, openLibrary.Provider);

            var results = (List<SearchResource>)controller.Search(term, "author");

            hardcover.Search.Verify(search => search.SearchForNewAuthor(term), Times.Once());
            openLibrary.Supplement.Verify(search => search.SearchForSupplementalAuthor(term), Times.Once());
            results.Select(result => result.ForeignId).Should().Equal(primaryId, namesake.ForeignAuthorId, weakAlias.ForeignAuthorId);
            results[0].CatalogueOwnerId.Should().Be(11);
            results[0].Author.Images.Should().ContainSingle(image => image.CoverType == MediaCoverTypes.Poster && image.Url == portrait);
            results.Skip(1).Should().OnlyContain(result => result.CatalogueOwnerId == null);
        }

        [TestCase("Jeff Fixture", false, true)]
        [TestCase("Jeff Fixture", true, false)]
        [TestCase("openlibrary:author:OL300A", false, false)]
        [TestCase("OL300A", false, false)]
        [TestCase("300", false, false)]
        public void should_bound_an_optional_author_provider_attempt_and_skip_identifiers_or_cancelled_requests(
            string term, bool cancelAfterPrimary, bool supplementExpected)
        {
            using var cancellation = new CancellationTokenSource();
            var google = AuthorSearchAdapter("GoogleBooks", term, () =>
            {
                if (cancelAfterPrimary)
                {
                    cancellation.Cancel();
                }

                return new List<Author> { SearchAuthor("googlebooks:author:Jeff%20Fixture") };
            });
            var openLibrary = AuthorSearchAdapter("OpenLibrary", term, () => new List<Author>());
            var hardcover = AuthorSearchAdapter("Hardcover", term, () => new List<Author>());
            var controller = InteractiveAuthorSearchController(Mock.Of<ICatalogueIdentityRepository>(), google.Provider, openLibrary.Provider, hardcover.Provider);
            controller.ControllerContext.HttpContext = new DefaultHttpContext { RequestAborted = cancellation.Token };

            if (cancelAfterPrimary)
            {
                try
                {
                    controller.Search(term, "author");
                }
                catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
                {
                    // An aborted caller may end the action before returning its first-provider result.
                }
            }
            else
            {
                ((List<SearchResource>)controller.Search(term, "author")).Should().ContainSingle();
            }

            google.Search.Verify(search => search.SearchForNewAuthor(term), Times.Once());
            openLibrary.Supplement.Verify(search => search.SearchForSupplementalAuthor(term), supplementExpected ? Times.Once() : Times.Never());
            openLibrary.Search.Verify(search => search.SearchForNewAuthor(It.IsAny<string>()), Times.Never());
            hardcover.Search.Verify(search => search.SearchForNewAuthor(It.IsAny<string>()), Times.Never());
        }

        [TestCase(false)]
        [TestCase(true)]
        public void should_keep_the_primary_author_results_when_the_optional_provider_is_unavailable(bool coolingDown)
        {
            const string term = "Jeff Fixture";
            var primaryResult = SearchAuthor("hardcover:author:41");
            var hardcover = AuthorSearchAdapter("Hardcover", term, () => new List<Author> { primaryResult });
            var openLibrary = AuthorSearchAdapter("OpenLibrary", term, () => new List<Author>());
            var registry = new Mock<IMetadataProviderRegistry>();
            registry.Setup(service => service.GetProviders()).Returns(new[] { hardcover.Provider, openLibrary.Provider });
            registry.Setup(service => service.GetProvidersHealthStatus()).Returns(new Dictionary<string, ProviderHealthStatus>
            {
                ["Hardcover"] = new ProviderHealthStatus { Health = ProviderHealth.Healthy },
                ["OpenLibrary"] = new ProviderHealthStatus
                {
                    Health = coolingDown ? ProviderHealth.Healthy : ProviderHealth.Unhealthy,
                    CooldownUntilUtc = coolingDown ? DateTime.UtcNow.AddDays(1) : null
                }
            });
            var providerTelemetry = new MetadataProviderTelemetryService();
            var orchestrator = new MetadataProviderOrchestrator(registry.Object, providerTelemetry, LogManager.GetLogger("FixtureProviderSearch"));
            var controller = new SearchController(
                orchestrator,
                Mock.Of<IBuildFileNames>(),
                Mock.Of<IMapCoversToLocal>(),
                _telemetry,
                Mock.Of<ICatalogueIdentityRepository>(),
                Mock.Of<ISeriesRepository>(),
                Mock.Of<IAuthorRepository>(),
                Mock.Of<IBookRepository>());

            var result = ((List<SearchResource>)controller.Search(term, "author")).Should().ContainSingle().Which;

            result.ForeignId.Should().Be(primaryResult.ForeignAuthorId);
            openLibrary.Supplement.Verify(search => search.SearchForSupplementalAuthor(It.IsAny<string>()), Times.Never());
            var providerResult = providerTelemetry.GetSnapshots().Should().ContainSingle().Which;
            providerResult.ProviderName.Should().Be("Hardcover");
            providerResult.Calls.Should().Be(1);
            providerResult.TotalResultCount.Should().Be(1);
        }

        private static (IMetadataProvider Provider, Mock<ISearchForNewAuthor> Search, Mock<ISearchForSupplementalAuthor> Supplement) AuthorSearchAdapter(
            string name, string term, Func<List<Author>> reply)
        {
            var provider = new Mock<IMetadataProvider>();
            provider.SetupGet(adapter => adapter.ProviderName).Returns(name);
            provider.SetupGet(adapter => adapter.IsEnabled).Returns(true);
            provider.SetupGet(adapter => adapter.SupportsAuthorSearch).Returns(true);
            var search = provider.As<ISearchForNewAuthor>();
            search.Setup(adapter => adapter.SearchForNewAuthor(term)).Returns(reply);
            var supplement = provider.As<ISearchForSupplementalAuthor>();
            supplement.Setup(adapter => adapter.SearchForSupplementalAuthor(term)).Returns(reply);
            return (provider.Object, search, supplement);
        }

        private SearchController InteractiveAuthorSearchController(ICatalogueIdentityRepository identity, params IMetadataProvider[] providers)
        {
            var registry = new Mock<IMetadataProviderRegistry>();
            registry.Setup(adapter => adapter.GetProviders()).Returns(providers);
            registry.Setup(adapter => adapter.GetProvidersHealthStatus()).Returns(providers.ToDictionary(
                provider => provider.ProviderName,
                _ => new ProviderHealthStatus { Health = ProviderHealth.Healthy }));
            var orchestrator = new MetadataProviderOrchestrator(registry.Object, new MetadataProviderTelemetryService(), LogManager.GetLogger("FixtureProviderSearch"));
            return new SearchController(
                orchestrator,
                Mock.Of<IBuildFileNames>(),
                Mock.Of<IMapCoversToLocal>(),
                _telemetry,
                identity,
                Mock.Of<ISeriesRepository>(),
                Mock.Of<IAuthorRepository>(),
                Mock.Of<IBookRepository>());
        }

        [TestCase("author")]
        [TestCase("book")]
        public void should_route_scoped_search_without_combined_fan_out(string scope)
        {
            var proxy = new Mock<IMetadataProviderOrchestrator>(MockBehavior.Strict);
            if (scope == "author")
            {
                proxy.Setup(search => search.SearchForInteractiveAuthors("Fixture", CancellationToken.None)).Returns(new List<Author>());
            }
            else
            {
                proxy.Setup(search => search.SearchForNewBook("Fixture", null, false)).Returns(new List<Book>());
            }

            var controller = new SearchController(proxy.Object, Mock.Of<IBuildFileNames>(), Mock.Of<IMapCoversToLocal>(), _telemetry, Mock.Of<ICatalogueIdentityRepository>(), Mock.Of<ISeriesRepository>(), Mock.Of<IAuthorRepository>(), Mock.Of<IBookRepository>());

            ((List<SearchResource>)controller.Search("Fixture", scope)).Should().BeEmpty();
            proxy.Invocations.Should().ContainSingle();
        }

        [Test]
        public void should_distinguish_unavailable_series_from_successful_empty_results()
        {
            var proxy = new Mock<IMetadataProviderOrchestrator>(MockBehavior.Strict);
            proxy.Setup(search => search.SearchForNewSeries("Unavailable")).Returns((List<SeriesSearchSummary>)null);
            proxy.Setup(search => search.SearchForNewSeries("Empty")).Returns(new List<SeriesSearchSummary>());
            var controller = new SearchController(proxy.Object, Mock.Of<IBuildFileNames>(), Mock.Of<IMapCoversToLocal>(), _telemetry, Mock.Of<ICatalogueIdentityRepository>(), Mock.Of<ISeriesRepository>(), Mock.Of<IAuthorRepository>(), Mock.Of<IBookRepository>());

            controller.Search("Unavailable", "series").Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(503);
            ((List<SearchResource>)controller.Search("Empty", "series")).Should().BeEmpty();
            controller.Search("Fixture", "unknown").Should().BeOfType<BadRequestObjectResult>();
            ((List<SearchResource>)controller.Search("ab", "series")).Should().BeEmpty();
        }

        [Test]
        public void should_return_addable_book_results_in_verified_series_order()
        {
            var proxy = new Mock<IMetadataProviderOrchestrator>(MockBehavior.Strict);
            proxy.Setup(search => search.GetSeriesBooks("hardcover:series:24")).Returns(new List<Book>
            {
                new Book { Title = "Second alphabetically", ForeignBookId = "hardcover:work:8" },
                new Book { Title = "First alphabetically", ForeignBookId = "hardcover:work:9" }
            });
            var controller = new SearchController(proxy.Object, Mock.Of<IBuildFileNames>(), Mock.Of<IMapCoversToLocal>(), _telemetry, Mock.Of<ICatalogueIdentityRepository>(), Mock.Of<ISeriesRepository>(), Mock.Of<IAuthorRepository>(), Mock.Of<IBookRepository>());

            var results = (List<SearchResource>)controller.Search("hardcover:series:24", "series-books");

            results.Select(result => result.Book.ForeignBookId).Should().Equal("hardcover:work:8", "hardcover:work:9");
            controller.Search("A series title", "series-books").Should().BeOfType<BadRequestObjectResult>();
            controller.Search("hardcover:series:0", "series-books").Should().BeOfType<BadRequestObjectResult>();
            proxy.Verify(search => search.GetSeriesBooks("hardcover:series:24"), Times.Once());
            proxy.VerifyNoOtherCalls();
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
            // An unresolved author must not prevent valid edition artwork from being mapped.
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
                    new MediaCover { CoverType = MediaCoverTypes.Cover, Url = "https://covers.example/cover.jpg" }
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
        public void should_keep_same_work_fallback_metadata_in_search_without_modifying_provider_covers()
        {
            const string preferredUrl = "https://covers.example/preferred.jpg";
            const string fallbackUrl = "https://covers.example/fallback.jpg";
            var preferredCover = new MediaCover(MediaCoverTypes.Cover, preferredUrl);
            var fallbackCover = new MediaCover(MediaCoverTypes.Cover, fallbackUrl);
            var book = new Book
            {
                Title = "Fixture Work",
                ForeignBookId = "OL250W",
                Editions = new List<Edition>
                {
                    new Edition { Title = "Fixture Work", Monitored = true, Images = new List<MediaCover> { preferredCover } },
                    new Edition { Title = "Fixture Work", Overview = "A verified edition synopsis.", Images = new List<MediaCover> { fallbackCover } }
                }
            };
            var searchProxy = new Mock<IMetadataProviderOrchestrator>();
            searchProxy.Setup(proxy => proxy.SearchForNewEntity("fixture")).Returns(new List<object> { book });
            var coverMapper = new Mock<IMapCoversToLocal>();
            coverMapper.Setup(mapper => mapper.ConvertToLocalUrls(0, MediaCoverEntity.Book, It.IsAny<IEnumerable<MediaCover>>()))
                .Callback<int, MediaCoverEntity, IEnumerable<MediaCover>>((_, _, images) =>
                {
                    foreach (var image in images)
                    {
                        image.RemoteUrl = image.Url;
                        image.Url = "/artwork/" + System.IO.Path.GetFileName(image.Url);
                    }
                });
            var controller = new SearchController(searchProxy.Object, Mock.Of<IBuildFileNames>(), coverMapper.Object, _telemetry, Mock.Of<ICatalogueIdentityRepository>(), Mock.Of<ISeriesRepository>(), Mock.Of<IAuthorRepository>(), Mock.Of<IBookRepository>());

            var result = ((List<SearchResource>)controller.Search("fixture")).Should().ContainSingle().Which.Book;

            result.Overview.Should().Be("A verified edition synopsis.");
            result.Images.Select(image => image.RemoteUrl).Should().Equal(preferredUrl, fallbackUrl);
            result.RemoteCover.Should().Be(preferredUrl);
            preferredCover.Url.Should().Be(preferredUrl);
            fallbackCover.Url.Should().Be(fallbackUrl);
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

        // ── Issue #250 regression: book-branch nested LazyLoaded nulls ─────────
        //
        // PR #249 hardened the AUTHOR branch (AuthorResourceMapper.ToResource) and
        // the book-author folder mapping in SearchController.MapToResource, but
        // BookResource.ToResource still dereferenced nested LazyLoaded values that
        // a provider partial result can leave null:
        //   (a) Editions.Value null list            (LazyLoaded<List<Edition>>(null))
        //   (b) Author.Metadata.Value null          (Author resolved, metadata row absent)
        //   (c) SeriesBookLink.Series.Value null    (series row unresolved)
        //   (d) Edition.BookFiles.Value null list   (edition files unresolved)
        //
        // These shapes made GET /api/v1/search return 500 NRE at
        // SearchController.MapToResource+MoveNext (SearchController.cs:48) for any
        // term whose provider fan-out returned book results.  The #249 fixture
        // tests above cover shapes 2/3 from that run but NOT these nested book
        // shapes — which is why CI was green while the runtime 500 was live.
        //
        // Each test asserts the non-throwing List<SearchResource> result with the
        // expected mapping (null entities skipped/mapped safely, not dropped with
        // a 500).
        [Test]
        public void should_not_throw_when_book_editions_value_is_null_list()
        {
            // Shape (a): Editions LazyLoaded set but its Value is a null list.
            var author = new Author
            {
                Metadata = new AuthorMetadata
                {
                    Name = "J.R.R. Tolkien",
                    SortNameLastFirst = "Tolkien, J.R.R.",
                    Images = new List<MediaCover>()
                }
            };

            var book = new Book
            {
                Title = "The Hobbit",
                ForeignBookId = "OL250A",
                Author = new LazyLoaded<Author>(author),
                Editions = new LazyLoaded<List<Edition>>(null) // _value == null
            };

            var resources = SearchWithResults(book);

            resources.Should().HaveCount(1);
            var resource = resources[0];
            resource.Book.Should().NotBeNull();
            resource.Book.Title.Should().Be("The Hobbit");
            resource.Book.Editions.Should().BeEmpty();
            resource.Book.Author.Should().NotBeNull();
        }

        [Test]
        public void should_not_throw_when_book_author_metadata_unresolved_and_editions_present()
        {
            // Shape (b): Author resolved (non-null) but its Metadata LazyLoaded is
            // unset (the SAME pattern #249 fixed for AuthorResource but not for the
            // book branch).  Exercises BookResource.ToResource's authorTitle path
            // where the book's own Author deref previously threw before the
            // controller's own bookAuthor?.ToResource() override could run.
            var author = new Author
            {
                Metadata = new LazyLoaded<AuthorMetadata>() // unset
            };

            var book = new Book
            {
                Title = "The Hobbit",
                ForeignBookId = "OL250B",
                Author = new LazyLoaded<Author>(author),
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

            // AuthorResource must still be produced (controller overrides it),
            // mapped safely without the metadata row.
            resource.Book.Author.Should().NotBeNull();
            resource.Book.Author.AuthorName.Should().BeNull();
        }

        [Test]
        public void should_not_throw_when_book_series_link_series_is_unresolved()
        {
            // Shape (c): SeriesLinks set with a non-empty list, but each
            // SeriesBookLink's Series LazyLoaded Value is null (series row
            // unresolved by the provider fan-out).
            var book = new Book
            {
                Title = "The Hobbit",
                ForeignBookId = "OL250C",
                Author = null,
                Editions = null,
                SeriesLinks = new LazyLoaded<List<SeriesBookLink>>(new List<SeriesBookLink>
                {
                    new SeriesBookLink
                    {
                        SeriesId = 1,
                        SeriesPosition = 1,
                        Position = "1",
                        Series = new LazyLoaded<Series>() // _value == null
                    }
                })
            };

            var resources = SearchWithResults(book);

            resources.Should().HaveCount(1);
            var resource = resources[0];
            resource.Book.Should().NotBeNull();
            resource.Book.Title.Should().Be("The Hobbit");

            // Unresolved series title maps safely to empty; the position suffix is
            // preserved (existing behavior for a present Position string) — not a 500.
            resource.Book.SeriesTitle.Should().NotBeNull();
            resource.Book.SeriesTitle.Should().NotStartWith("null");
            resource.Book.SeriesTitle.Should().Contain("#1");
        }

        [Test]
        public void should_not_throw_when_edition_bookfiles_value_is_null_list()
        {
            // Shape (d): Edition.BookFiles LazyLoaded set but Value is a null list
            // (format-status derivation must skip it without throwing).
            var book = new Book
            {
                Title = "The Hobbit",
                ForeignBookId = "OL250D",
                Author = null,
                Editions = new LazyLoaded<List<Edition>>(new List<Edition>
                {
                    new Edition
                    {
                        Title = "The Hobbit",
                        Overview = "A quest.",
                        Images = new List<MediaCover>(),
                        BookFiles = new LazyLoaded<List<BookFile>>(null), // _value == null
                        IsEbook = true,
                        Monitored = true
                    }
                })
            };

            var resources = SearchWithResults(book);

            resources.Should().HaveCount(1);
            var resource = resources[0];
            resource.Book.Should().NotBeNull();
            resource.Book.Title.Should().Be("The Hobbit");

            // Edition-level classification still drives format statuses.
            resource.Book.FormatStatuses.Should().Contain(fs => fs.FormatType == FormatType.Ebook && fs.Monitored);
        }

        [Test]
        public void should_map_book_with_all_nested_lazy_values_null()
        {
            // Combined shape: every nested LazyLoaded on the book branch is null —
            // the maximal provider partial result.  Must map to a valid (if empty)
            // resource instead of 500.
            var book = new Book
            {
                Title = "The Hobbit",
                ForeignBookId = "OL250E",
                Author = new LazyLoaded<Author>(new Author
                {
                    Metadata = new LazyLoaded<AuthorMetadata>() // unset
                }),
                Editions = new LazyLoaded<List<Edition>>(null),
                SeriesLinks = new LazyLoaded<List<SeriesBookLink>>(new List<SeriesBookLink>
                {
                    new SeriesBookLink
                    {
                        Series = new LazyLoaded<Series>() // _value == null
                    }
                })
            };

            var resources = SearchWithResults(book);

            resources.Should().HaveCount(1);
            var resource = resources[0];
            resource.Book.Should().NotBeNull();
            resource.Book.Title.Should().Be("The Hobbit");
            resource.Book.ForeignBookId.Should().Be("OL250E");
            resource.Book.Editions.Should().BeEmpty();
            resource.Book.Author.Should().NotBeNull();
        }

        [Test]
        public void should_return_empty_list_when_search_proxy_returns_null()
        {
            // Issue #250 root cause: MetadataProviderOrchestrator.ExecuteFirst returns
            // null when no provider yields a result (every provider failed/returned
            // null, or none supported the search). The original controller enumerated
            // the null result directly, throwing an NRE at the foreach — the live 500
            // on /api/v1/search. It must degrade to an empty list instead.
            var searchProxy = new Mock<IMetadataProviderOrchestrator>();
            searchProxy.Setup(v => v.SearchForNewEntity(It.IsAny<string>()))
                .Returns((List<object>)null);

            var fileNameBuilder = new Mock<IBuildFileNames>();
            var coverMapper = new Mock<IMapCoversToLocal>();

            var controller = new SearchController(
                searchProxy.Object,
                fileNameBuilder.Object,
                coverMapper.Object,
                new SearchTelemetryService(),
                Mock.Of<ICatalogueIdentityRepository>(),
                Mock.Of<ISeriesRepository>(),
                Mock.Of<IAuthorRepository>(),
                Mock.Of<IBookRepository>());

            var result = controller.Search("hobbit");

            result.Should().BeOfType<List<SearchResource>>();
            ((List<SearchResource>)result).Should().BeEmpty();
        }

        // Helper: build a SearchController wired with a stub provider that returns
        // the given entities, then invoke Search and return the result list.
        private static List<SearchResource> SearchWithResults(params object[] entities)
        {
            return SearchWithCatalogueResults(Mock.Of<ICatalogueIdentityRepository>(), entities);
        }

        private static List<SearchResource> SearchWithCatalogueResults(ICatalogueIdentityRepository catalogueIdentity, params object[] entities)
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
                new SearchTelemetryService(),
                catalogueIdentity,
                Mock.Of<ISeriesRepository>(),
                Mock.Of<IAuthorRepository>(),
                Mock.Of<IBookRepository>());

            var result = controller.Search("hobbit");
            return (List<SearchResource>)result;
        }
    }
}
