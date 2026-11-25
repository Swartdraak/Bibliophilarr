using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using FluentAssertions;
using Moq;
using NLog;
using NUnit.Framework;
using NzbDrone.Common.Cache;
using NzbDrone.Common.Http;
using NzbDrone.Core.Books;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.MediaFiles.BookImport;
using NzbDrone.Core.MediaFiles.BookImport.Identification;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.MetadataSource.GoogleBooks;
using NzbDrone.Core.MetadataSource.Hardcover;
using NzbDrone.Core.MetadataSource.Inventaire;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MetadataSource
{
    [TestFixture]
    public class CandidateServiceFallbackOrderingIntegrationFixture : CoreTest<CandidateService>
    {
        [SetUp]
        public void SetUp()
        {
            var normalization = Mocker.Resolve<MetadataQueryNormalizationService>();
            Mocker.SetConstant<IMetadataQueryNormalizationService>(normalization);

            Mocker.GetMock<IConfigService>()
                .SetupGet(x => x.EnableInventaireFallback)
                .Returns(true);

            Mocker.GetMock<IConfigService>()
                .SetupGet(x => x.EnableGoogleBooksFallback)
                .Returns(true);

            Mocker.GetMock<IConfigService>()
                .SetupGet(x => x.EnableHardcoverFallback)
                .Returns(true);

            Mocker.GetMock<IConfigService>()
                .SetupGet(x => x.HardcoverApiToken)
                .Returns("hardcover-token");

            Mocker.GetMock<IConfigService>()
                .SetupGet(x => x.MetadataAuthorAliases)
                .Returns("{\"J.K. Rowling\":[\"Robert Galbraith\"]}");

            Mocker.GetMock<IConfigService>()
                .SetupGet(x => x.MetadataTitleStripPatterns)
                .Returns("[\":\\\\s*Book\\\\s*\\\\d+$\"]");

            var inventaire = new InventaireFallbackSearchProvider(Mocker.GetMock<IConfigService>().Object, Mocker.GetMock<IHttpClient>().Object, LogManager.GetCurrentClassLogger());
            var googleBooks = new GoogleBooksFallbackSearchProvider(
                Mocker.GetMock<IConfigService>().Object,
                Mocker.GetMock<IHttpClient>().Object,
                Mocker.Resolve<ICacheManager>(),
                LogManager.GetCurrentClassLogger());
            var hardcover = new HardcoverFallbackSearchProvider(Mocker.GetMock<IConfigService>().Object, Mocker.GetMock<IHttpClient>().Object, LogManager.GetCurrentClassLogger());

            var emptySecondary = new Mock<IBookSearchFallbackProvider>();
            emptySecondary.SetupGet(x => x.ProviderName).Returns("EmptyFallback");
            emptySecondary.SetupGet(x => x.RateLimitInfo).Returns(new ProviderRateLimitInfo());
            emptySecondary.Setup(x => x.Search(It.IsAny<string>(), It.IsAny<string>())).Returns(new List<Book>());

            Mocker.SetConstant<IEnumerable<IBookSearchFallbackProvider>>(new IBookSearchFallbackProvider[]
            {
                emptySecondary.Object,
                googleBooks,
                hardcover,
                inventaire
            });

            Mocker.GetMock<IBookSearchFallbackExecutionService>()
                .Setup(x => x.Search(It.IsAny<IBookSearchFallbackProvider>(), It.IsAny<string>(), It.IsAny<string>()))
                .Returns((IBookSearchFallbackProvider provider, string title, string author) => provider.Search(title, author));

            Mocker.GetMock<IMetadataProviderOrchestrator>()
                .Setup(x => x.SearchForNewBook(It.IsAny<string>(), It.IsAny<string>(), true))
                .Returns(new List<Book>());

            var payload = "{" +
                          "\"items\":[{" +
                          "\"id\":\"gb-1\"," +
                          "\"volumeInfo\":{" +
                          "\"title\":\"The Cuckoo's Calling\"," +
                          "\"authors\":[\"Robert Galbraith\"]," +
                          "\"publishedDate\":\"2013-01-01\"," +
                          "\"publisher\":\"Sphere\"," +
                          "\"language\":\"en\"," +
                          "\"printType\":\"BOOK\"," +
                          "\"industryIdentifiers\":[{\"type\":\"ISBN_13\",\"identifier\":\"9780316206846\"}]" +
                          "}}]}";

            Mocker.GetMock<IHttpClient>()
                .Setup(x => x.Get<InventaireSearchResponse>(It.IsAny<HttpRequest>()))
                .Returns<HttpRequest>(request =>
                {
                    var emptyPayload = "{\"results\":[]}";
                    return new HttpResponse<InventaireSearchResponse>(new HttpResponse(request, new HttpHeader { ContentType = "application/json" }, emptyPayload));
                });

            Mocker.GetMock<IHttpClient>()
                .Setup(x => x.Get(It.IsAny<HttpRequest>()))
                .Returns<HttpRequest>(request => new HttpResponse(request, new HttpHeader { ContentType = "application/json" }, payload));
        }

        [TestCase("/library/Asin Route Fixture.epub", "Ebook", true, "B0171WIS9Y", true)]
        [TestCase("/library/Asin Route Fixture.m4b", "Audiobook", true, "B0171WIS9Y", true)]
        [TestCase("/imports/Asin Route Fixture.epub", "Ebook", false, "B0171WIS9Y", false)]
        [TestCase("/library/Asin Route Fixture.epub", "Ebook", true, "0306406152", false)]
        public void should_limit_b0_asin_routing_to_scoped_providers_in_library_scans(
            string path, string format, bool libraryScan, string asin, bool skipGoogle)
        {
            const string title = "Asin Route Fixture";
            Mocker.GetMock<IConfigService>().SetupGet(config => config.EnableGoogleBooksProvider).Returns(true);
            var http = Mocker.GetMock<IHttpClient>();
            var payload = """
                {"items":[{"id":"asin-route-fixture","volumeInfo":{"title":"Asin Route Fixture",
                "authors":["Route Writer"],"printType":"BOOK",
                "industryIdentifiers":[{"type":"OTHER","identifier":"B0171WIS9Y"}]}}]}
                """;
            var headers = new HttpHeader { ContentType = "application/json" };
            http.Setup(client => client.Get(It.IsAny<HttpRequest>()))
                .Returns<HttpRequest>(request => new HttpResponse(request, headers, payload));
            var google = new GoogleBooksFallbackSearchProvider(Mocker.GetMock<IConfigService>().Object, http.Object, Mocker.Resolve<ICacheManager>(), LogManager.GetCurrentClassLogger());
            var hardcover = new HardcoverFallbackSearchProvider(Mocker.GetMock<IConfigService>().Object, http.Object, LogManager.GetCurrentClassLogger());
            var ebook = format == "Ebook";
            var hardcoverPayload = """
                {"data":{"search":{"error":null,"ids":[42],"results":{"found":1,"hits":[{"document":{
                "id":"42","title":"Asin Route Fixture","author_names":["Route Writer"],
                "has_ebook":EBOOK_SUPPORTED,"has_audiobook":AUDIO_SUPPORTED}}]}}}}
                """.Replace("EBOOK_SUPPORTED", ebook ? "true" : "false").Replace("AUDIO_SUPPORTED", ebook ? "false" : "true");
            HttpRequest hardcoverRequest = null;
            http.Setup(client => client.Post<HardcoverGraphQlResponse>(It.IsAny<HttpRequest>()))
                .Returns<HttpRequest>(request =>
                {
                    hardcoverRequest = request;
                    return new HttpResponse<HardcoverGraphQlResponse>(new HttpResponse(request, headers, hardcoverPayload));
                });
            var registry = new Mock<IMetadataProviderRegistry>();
            registry.Setup(service => service.GetProviders()).Returns(new IMetadataProvider[] { google, hardcover });
            var telemetry = new MetadataProviderTelemetryService();
            var orchestrator = new MetadataProviderOrchestrator(registry.Object, telemetry, LogManager.GetCurrentClassLogger());
            Mocker.SetConstant<IMetadataProviderOrchestrator>(orchestrator);
            var release = new LocalEdition
            {
                LocalBooks = new List<LocalBook>
                {
                    new LocalBook
                    {
                        Path = path,
                        FileTrackInfo = new ParsedTrackInfo { BookTitle = title, Authors = new List<string> { "Route Writer" }, Asin = asin }
                    }
                }
            };

            // Enumerate through the direct identifier route. The later title lookup is a separate operation.
            var candidate = Subject.GetRemoteCandidates(release, null, forLibraryScan: libraryScan).First();
            var operations = telemetry.GetOperationSnapshots();
            if (skipGoogle)
            {
                candidate.Edition.ForeignEditionId.Should().Be("hardcover:edition:42");
                candidate.Edition.Format.Should().Be(format);
                candidate.Edition.Asin.Should().BeNull();
                http.Verify(client => client.Get(It.IsAny<HttpRequest>()), Times.Never());
                http.Verify(client => client.Post<HardcoverGraphQlResponse>(It.IsAny<HttpRequest>()), Times.Once());
                using var request = JsonDocument.Parse(hardcoverRequest.ContentData);
                request.RootElement.GetProperty("variables").GetProperty("query").GetString().Should().Be(asin);
                operations.Should().NotContain(operation => operation.ProviderName == "GoogleBooks");
                operations.Should().ContainSingle(operation => operation.ProviderName == "Hardcover" &&
                    operation.OperationName == "search-by-asin" && operation.Calls == 1 && operation.Successes == 1);
            }
            else
            {
                candidate.Edition.ForeignEditionId.Should().Be("googlebooks:edition:asin-route-fixture");
                candidate.Edition.Asin.Should().Be("B0171WIS9Y");
                http.Verify(client => client.Get(It.Is<HttpRequest>(request => request.Url.Query.Contains("q=" + asin + "&"))), Times.Once());
                http.Verify(client => client.Post<HardcoverGraphQlResponse>(It.IsAny<HttpRequest>()), Times.Never());
                operations.Should().ContainSingle(operation => operation.ProviderName == "GoogleBooks" &&
                    operation.OperationName == "search-by-asin" && operation.Calls == 1 && operation.Successes == 1);
            }
        }

        [TestCase(".m4b", "verified-owner", true)]
        [TestCase(".epub", "verified-owner", true)]
        [TestCase(".m4b", "two-distinct-works", false)]
        [TestCase(".m4b", "unverified-owner", false)]
        [TestCase(".epub", "unverified-owner", true)]
        [TestCase(".m4b", "weak-work-alias", false)]
        [TestCase(".m4b", "conflicting-provider-title", false)]
        [TestCase(".m4b", "conflicting-filename", false)]
        [TestCase(".m4b", "missing-contributor", false)]
        [TestCase(".m4b", "provider-disabled", false)]
        [TestCase(".m4b", "forced-other-work", false)]
        public void should_only_admit_inventaire_work_summary_for_a_verified_existing_library_work(string extension, string scenario, bool accepted)
        {
            const string authorName = "Mira Fixture";
            const string title = "Observatory Archive";
            const string sourceWorkId = "wd:Q912001";
            const string source = "inventaire:work:" + sourceWorkId;
            const string legacyEditionId = "inventaire:edition:" + sourceWorkId;
            const string canonicalWorkId = "705b2b5da2a943319bf107448959fe9c";
            var owner = new Author
            {
                Id = 21,
                AuthorMetadataId = 22,
                Metadata = new AuthorMetadata
                {
                    Id = 22,
                    ForeignAuthorId = "hardcover:author:912101",
                    CanonicalId = "185cbb81d132400fb235a95b1a264f58",
                    Name = authorName
                }
            };
            var work = new Book
            {
                Id = 31,
                CanonicalId = canonicalWorkId,
                ForeignBookId = source,
                Title = title,
                Author = owner,
                AuthorMetadata = owner.Metadata.Value,
                AuthorMetadataId = owner.AuthorMetadataId
            };
            var legacy = new Edition { Id = 41, BookId = work.Id, Book = work, ForeignEditionId = legacyEditionId, Title = title, Format = "Ebook", IsEbook = true };
            work.Editions = new List<Edition> { legacy };
            if (scenario != "unverified-owner")
            {
                var books = Mocker.GetMock<IBookService>();
                books.Setup(service => service.FindById(source)).Returns(work);
                books.Setup(service => service.GetBook(work.Id)).Returns(work);
                var identity = Mocker.GetMock<ICatalogueIdentityRepository>();
                identity.Setup(repository => repository.FindByProviderId(CatalogueEntityKind.Work, "Inventaire", sourceWorkId)).Returns(work.Id);
                identity.Setup(repository => repository.GetSources(CatalogueEntityKind.Work, work.Id))
                    .Returns(new[] { new CatalogueSourceAssociation { Provider = "Inventaire", ProviderId = sourceWorkId, Evidence = scenario == "weak-work-alias" ? "legacy-linked-id" : "primary-provider-id" } });
            }

            var captured = new List<HttpRequest>();
            if (scenario == "provider-disabled")
            {
                Mocker.GetMock<IConfigService>().SetupGet(config => config.EnableInventaireFallback).Returns(false);
            }

            var provider = new InventaireFallbackSearchProvider(Mocker.GetMock<IConfigService>().Object, Mocker.GetMock<IHttpClient>().Object, LogManager.GetCurrentClassLogger());
            Mocker.SetConstant<IEnumerable<IBookSearchFallbackProvider>>(new[] { provider });
            var row = new
            {
                uri = sourceWorkId,
                label = title,
                author = scenario == "missing-contributor" ? null : authorName,
                isbn13 = "9780140328721"
            };
            var results = scenario == "two-distinct-works"
                ? new[] { row, new { uri = "wd:Q912002", label = title, author = authorName, isbn13 = "9780140328721" } }
                : scenario == "conflicting-provider-title"
                    ? new[] { row, new { uri = sourceWorkId, label = "Different Work", author = authorName, isbn13 = "9780140328721" } }
                    : new[] { row };
            var payload = JsonSerializer.Serialize(new { results });
            Mocker.GetMock<IHttpClient>().Setup(client => client.Get<InventaireSearchResponse>(It.IsAny<HttpRequest>()))
                .Returns<HttpRequest>(request =>
                {
                    captured.Add(request);
                    var response = new HttpResponse(request, new HttpHeader { ContentType = "application/json" }, payload);
                    return new HttpResponse<InventaireSearchResponse>(response);
                });

            var filenameTitle = scenario == "conflicting-filename" ? "Different Work" : title;
            var release = new LocalEdition(new List<LocalBook>
            {
                new LocalBook
                {
                    Path = $"/library/{authorName}/{title}/{authorName} - {filenameTitle}{extension}",
                    ReliableTaggedBookTitle = title.ToUpperInvariant(),
                    FileTrackInfo = new ParsedTrackInfo
                    {
                        BookTitle = title,
                        BookTitleConfidence = 1,
                        Authors = new List<string> { authorName },
                        AuthorConfidence = 1
                    }
                }
            });
            var overrides = scenario == "forced-other-work"
                ? new IdentificationOverrides { Book = new Book { ForeignBookId = "hardcover:work:912002" } }
                : null;
            var candidates = Subject.GetRemoteCandidates(release, overrides, forLibraryScan: true).ToList();

            if (scenario is "conflicting-filename" or "provider-disabled" or "forced-other-work")
            {
                captured.Should().BeEmpty();
                candidates.Should().BeEmpty();
                return;
            }

            captured.Should().NotBeEmpty().And.OnlyContain(request =>
                request.Url.Host == "inventaire.io" && request.Url.Path == "/api/search" && request.Url.Query.Contains("types=works"));
            if (!accepted)
            {
                candidates.Should().BeEmpty();
                return;
            }

            var summary = candidates.Should().ContainSingle().Which.Edition;
            if (scenario == "unverified-owner")
            {
                summary.ForeignEditionId.Should().Be(legacyEditionId);
                summary.IsEbook.Should().BeTrue();
                return;
            }

            captured.Should().ContainSingle();
            summary.Book.Value.Id.Should().Be(work.Id);
            summary.Book.Value.CanonicalId.Should().Be(canonicalWorkId);
            summary.Book.Value.Author.Value.Id.Should().Be(owner.Id);
            ImportFormat.ForEdition(summary).Should().BeNull();
            summary.ForeignEditionId.Should().NotBe(legacyEditionId);
            summary.Isbn13.Should().BeNull();
            legacy.IsEbook.Should().BeTrue();
            legacy.Format.Should().Be("Ebook");
        }

        [Test]
        public void should_exhaust_primary_and_secondary_before_tertiary_provider_returns_candidate()
        {
            var edition = new LocalEdition
            {
                LocalBooks = new List<LocalBook>
                {
                    new LocalBook
                    {
                        FileTrackInfo = new ParsedTrackInfo
                        {
                            Authors = new List<string> { "J.K. Rowling" },
                            BookTitle = "The Cuckoo's Calling: Book 1"
                        }
                    }
                }
            };

            var candidates = Subject.GetRemoteCandidates(edition, null).ToList();

            candidates.Should().ContainSingle();
            candidates[0].Edition.Title.Should().Be("The Cuckoo's Calling");
            candidates[0].Edition.Book.Value.AuthorMetadata.Value.Name.Should().Be("Robert Galbraith");

            Mocker.GetMock<IMetadataProviderOrchestrator>()
                .Verify(x => x.SearchForNewBook("The Cuckoo's Calling", "Robert Galbraith", It.IsAny<bool>()), Times.Once());

            Mocker.GetMock<IHttpClient>()
                .Verify(x => x.Get(It.IsAny<HttpRequest>()), Times.AtLeastOnce());
        }

        [Test]
        public void should_prefer_inventaire_before_google_books_when_inventaire_returns_candidates()
        {
            Mocker.GetMock<IHttpClient>()
                .Setup(x => x.Get<InventaireSearchResponse>(It.IsAny<HttpRequest>()))
                .Returns<HttpRequest>(request =>
                {
                    var payload = "{" +
                                  "\"results\":[{" +
                                  "\"uri\":\"inv:work:1\"," +
                                  "\"label\":\"The Cuckoo's Calling\"," +
                                  "\"author\":\"Robert Galbraith\"," +
                                  "\"cover\":\"https://inventaire.example/cover.jpg\"" +
                                  "}]}";

                    return new HttpResponse<InventaireSearchResponse>(new HttpResponse(request, new HttpHeader { ContentType = "application/json" }, payload));
                });

            Mocker.GetMock<IHttpClient>()
                .Setup(x => x.Get(It.IsAny<HttpRequest>()))
                .Returns<HttpRequest>(request =>
                {
                    var payload = "{" +
                                  "\"items\":[{" +
                                  "\"id\":\"gb-ignored\"," +
                                  "\"volumeInfo\":{" +
                                  "\"title\":\"The Cuckoo's Calling\"," +
                                  "\"authors\":[\"Robert Galbraith\"]," +
                                  "\"printType\":\"BOOK\"," +
                                  "\"imageLinks\":{\"thumbnail\":\"https://google.example/cover.jpg\"}}}]}";

                    return new HttpResponse(request, new HttpHeader { ContentType = "application/json" }, payload);
                });

            var edition = new LocalEdition
            {
                LocalBooks = new List<LocalBook>
                {
                    new LocalBook
                    {
                        FileTrackInfo = new ParsedTrackInfo
                        {
                            Authors = new List<string> { "J.K. Rowling" },
                            BookTitle = "The Cuckoo's Calling: Book 1"
                        }
                    }
                }
            };

            var candidates = Subject.GetRemoteCandidates(edition, null).ToList();

            candidates.Should().ContainSingle();
            candidates[0].Edition.ForeignEditionId.Should().StartWith("inventaire:edition:");
            candidates[0].Edition.Images.Should().ContainSingle();
            candidates[0].Edition.Images[0].Url.Should().Be("https://inventaire.example/cover.jpg");

            Mocker.GetMock<IHttpClient>()
                .Verify(x => x.Get(It.IsAny<HttpRequest>()), Times.Never());

            Mocker.GetMock<IHttpClient>()
                .Verify(x => x.Post<HardcoverGraphQlResponse>(It.IsAny<HttpRequest>()), Times.Never());
        }

        [Test]
        public void should_try_hardcover_after_google_books_returns_no_candidates()
        {
            Mocker.GetMock<IHttpClient>()
                .Setup(x => x.Get(It.IsAny<HttpRequest>()))
                .Returns<HttpRequest>(request =>
                {
                    var emptyPayload = "{\"items\":[]}";
                    return new HttpResponse(request, new HttpHeader { ContentType = "application/json" }, emptyPayload);
                });

            Mocker.GetMock<IHttpClient>()
                .Setup(x => x.Post<HardcoverGraphQlResponse>(It.IsAny<HttpRequest>()))
                .Returns<HttpRequest>(request =>
                {
                    var payload = "{" +
                                  "\"data\":{\"search\":{\"error\":null,\"ids\":[1],\"results\":{\"found\":1,\"hits\":[{" +
                                  "\"document\":{" +
                                  "\"id\":\"hc-1\"," +
                                  "\"title\":\"The Cuckoo's Calling\"," +
                                  "\"author_names\":[\"Robert Galbraith\"]," +
                                  "\"contributions\":[{\"author\":{\"name\":\"Robert Galbraith\"}}]," +
                                  "\"release_year\":2013," +
                                  "\"has_ebook\":true," +
                                  "\"has_audiobook\":false" +
                                  "}}]}}}}";

                    return new HttpResponse<HardcoverGraphQlResponse>(new HttpResponse(request, new HttpHeader { ContentType = "application/json" }, payload));
                });

            var edition = new LocalEdition
            {
                LocalBooks = new List<LocalBook>
                {
                    new LocalBook
                    {
                        FileTrackInfo = new ParsedTrackInfo
                        {
                            Authors = new List<string> { "J.K. Rowling" },
                            BookTitle = "The Cuckoo's Calling: Book 1"
                        }
                    }
                }
            };

            var candidates = Subject.GetRemoteCandidates(edition, null).ToList();

            candidates.Should().ContainSingle();
            candidates[0].Edition.Title.Should().Be("The Cuckoo's Calling");
            candidates[0].Edition.Book.Value.AuthorMetadata.Value.Name.Should().Be("Robert Galbraith");

            Mocker.GetMock<IHttpClient>()
                .Verify(x => x.Get(It.IsAny<HttpRequest>()), Times.AtLeastOnce());

            Mocker.GetMock<IHttpClient>()
                .Verify(x => x.Post<HardcoverGraphQlResponse>(It.IsAny<HttpRequest>()), Times.AtLeastOnce());
        }
    }
}
