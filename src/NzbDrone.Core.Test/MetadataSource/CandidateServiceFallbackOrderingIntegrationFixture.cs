using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Http;
using NzbDrone.Core.Books;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.MediaFiles.BookImport.Identification;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.MetadataSource.GoogleBooks;
using NzbDrone.Core.MetadataSource.Hardcover;
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

            var googleBooks = new GoogleBooksFallbackSearchProvider(Mocker.GetMock<IConfigService>().Object, Mocker.GetMock<IHttpClient>().Object);
            var hardcover = new HardcoverFallbackSearchProvider(Mocker.GetMock<IConfigService>().Object, Mocker.GetMock<IHttpClient>().Object);

            var emptySecondary = new Mock<IBookSearchFallbackProvider>();
            emptySecondary.SetupGet(x => x.ProviderName).Returns("EmptyFallback");
            emptySecondary.SetupGet(x => x.RateLimitInfo).Returns(new ProviderRateLimitInfo());
            emptySecondary.Setup(x => x.Search(It.IsAny<string>(), It.IsAny<string>())).Returns(new List<Book>());

            Mocker.SetConstant<IEnumerable<IBookSearchFallbackProvider>>(new IBookSearchFallbackProvider[]
            {
                emptySecondary.Object,
                googleBooks,
                hardcover
            });

            Mocker.GetMock<IBookSearchFallbackExecutionService>()
                .Setup(x => x.Search(It.IsAny<IBookSearchFallbackProvider>(), It.IsAny<string>(), It.IsAny<string>()))
                .Returns((IBookSearchFallbackProvider provider, string title, string author) => provider.Search(title, author));

            Mocker.GetMock<ISearchForNewBook>()
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
                .Setup(x => x.Get<GoogleBooksSearchResponse>(It.IsAny<HttpRequest>()))
                .Returns<HttpRequest>(request => new HttpResponse<GoogleBooksSearchResponse>(new HttpResponse(request, new HttpHeader { ContentType = "application/json" }, payload)));
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

            Mocker.GetMock<ISearchForNewBook>()
                .Verify(x => x.SearchForNewBook("The Cuckoo's Calling", "Robert Galbraith", It.IsAny<bool>()), Times.Once());

            Mocker.GetMock<IHttpClient>()
                .Verify(x => x.Get<GoogleBooksSearchResponse>(It.IsAny<HttpRequest>()), Times.AtLeastOnce());
        }

        [Test]
        public void should_try_hardcover_after_google_books_returns_no_candidates()
        {
            Mocker.GetMock<IHttpClient>()
                .Setup(x => x.Get<GoogleBooksSearchResponse>(It.IsAny<HttpRequest>()))
                .Returns<HttpRequest>(request =>
                {
                    var emptyPayload = "{\"items\":[]}";
                    return new HttpResponse<GoogleBooksSearchResponse>(new HttpResponse(request, new HttpHeader { ContentType = "application/json" }, emptyPayload));
                });

            Mocker.GetMock<IHttpClient>()
                .Setup(x => x.Post<HardcoverGraphQlResponse>(It.IsAny<HttpRequest>()))
                .Returns<HttpRequest>(request =>
                {
                    var payload = "{" +
                                  "\"data\":{\"search\":{\"results\":[{" +
                                  "\"id\":\"hc-1\"," +
                                  "\"title\":\"The Cuckoo's Calling\"," +
                                  "\"releaseYear\":2013," +
                                  "\"contributors\":[{\"author\":{\"name\":\"Robert Galbraith\"}}]," +
                                  "\"editions\":[{\"id\":\"hc-ed-1\",\"title\":\"The Cuckoo's Calling\",\"readingFormat\":\"Ebook\"}]" +
                                  "}]}}}";

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
                .Verify(x => x.Get<GoogleBooksSearchResponse>(It.IsAny<HttpRequest>()), Times.AtLeastOnce());

            Mocker.GetMock<IHttpClient>()
                .Verify(x => x.Post<HardcoverGraphQlResponse>(It.IsAny<HttpRequest>()), Times.AtLeastOnce());
        }
    }
}
