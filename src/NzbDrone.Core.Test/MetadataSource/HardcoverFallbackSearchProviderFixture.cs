using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Http;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.MetadataSource.Hardcover;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MetadataSource
{
    [TestFixture]
    public class HardcoverFallbackSearchProviderFixture : CoreTest<HardcoverFallbackSearchProvider>
    {
        [SetUp]
        public void SetUp()
        {
            Mocker.GetMock<IConfigService>()
                .SetupGet(x => x.EnableHardcoverFallback)
                .Returns(true);

            Mocker.GetMock<IConfigService>()
                .SetupGet(x => x.HardcoverApiToken)
                .Returns("hardcover-token");

            Mocker.GetMock<IConfigService>()
                .SetupGet(x => x.HardcoverRequestTimeoutSeconds)
                .Returns(0);
        }

        [Test]
        public void should_return_empty_when_disabled()
        {
            Mocker.GetMock<IConfigService>()
                .SetupGet(x => x.EnableHardcoverFallback)
                .Returns(false);

            Subject.Search("The Stand", "Stephen King").Should().BeEmpty();

            Mocker.GetMock<IHttpClient>()
                .Verify(x => x.Post<HardcoverGraphQlResponse>(It.IsAny<HttpRequest>()), Times.Never());
        }

        [Test]
        public void should_return_empty_when_token_is_missing()
        {
            Mocker.GetMock<IConfigService>()
                .SetupGet(x => x.HardcoverApiToken)
                .Returns(string.Empty);

            Subject.Search("The Stand", "Stephen King").Should().BeEmpty();

            Mocker.GetMock<IHttpClient>()
                .Verify(x => x.Post<HardcoverGraphQlResponse>(It.IsAny<HttpRequest>()), Times.Never());
        }

        [Test]
        public void should_map_search_results_from_graphql_response()
        {
            HttpRequest capturedRequest = null;
            var payload = "{" +
                          "\"data\":{\"search\":{\"results\":[{" +
                          "\"id\":\"book-1\"," +
                          "\"title\":\"The Stand\"," +
                          "\"releaseYear\":1978," +
                          "\"releaseDate\":\"1978-10-03\"," +
                          "\"contributors\":[{\"author\":{\"name\":\"Stephen King\"}}]," +
                          "\"editions\":[{" +
                          "\"id\":\"edition-1\"," +
                          "\"title\":\"The Stand (Collector Edition)\"," +
                          "\"isbn13\":\"9780307743688\"," +
                          "\"asin\":\"B001C4HPNW\"," +
                          "\"readingFormat\":\"Ebook\"," +
                          "\"audioSeconds\":0," +
                          "\"releaseDate\":\"2011-01-01\"," +
                          "\"publisher\":{\"name\":\"Anchor\"}," +
                          "\"language\":{\"code\":\"en\"}" +
                          "}]" +
                          "}]}}}";

            Mocker.GetMock<IHttpClient>()
                .Setup(x => x.Post<HardcoverGraphQlResponse>(It.IsAny<HttpRequest>()))
                .Returns<HttpRequest>(request =>
                {
                    capturedRequest = request;
                    return new HttpResponse<HardcoverGraphQlResponse>(new HttpResponse(request, new HttpHeader { ContentType = "application/json" }, payload));
                });

            var books = Subject.Search("The Stand", "Stephen King");

            books.Should().ContainSingle();
            books[0].Title.Should().Be("The Stand");
            books[0].ForeignBookId.Should().Be("hardcover:work:book-1");
            books[0].TitleSlug.Should().Be("hardcover:work:book-1");
            books[0].AuthorMetadata.Should().NotBeNull();
            books[0].AuthorMetadata.Value.Name.Should().Be("Stephen King");
            books[0].Editions.Value.Should().ContainSingle();
            books[0].Editions.Value[0].ForeignEditionId.Should().Be("hardcover:edition:edition-1");
            books[0].Editions.Value[0].TitleSlug.Should().Be("hardcover:edition:edition-1");
            books[0].Editions.Value[0].Publisher.Should().Be("Anchor");
            books[0].Editions.Value[0].Language.Should().Be("en");
            books[0].Editions.Value[0].Isbn13.Should().Be("9780307743688");

            capturedRequest.Should().NotBeNull();
            capturedRequest.Method.Should().Be(System.Net.Http.HttpMethod.Post);
            capturedRequest.RateLimitKey.Should().Be("Hardcover");
            capturedRequest.Headers.ContentType.Should().Contain("application/json");
            capturedRequest.Headers.GetSingleValue("authorization").Should().Be("Bearer hardcover-token");
            capturedRequest.ContentData.Should().NotBeNull();
        }

        [Test]
        public void should_return_empty_when_graphql_error_payload_has_no_data()
        {
            var payload = "{\"errors\":[{\"message\":\"invalid query\"}]}";

            Mocker.GetMock<IHttpClient>()
                .Setup(x => x.Post<HardcoverGraphQlResponse>(It.IsAny<HttpRequest>()))
                .Returns<HttpRequest>(request =>
                    new HttpResponse<HardcoverGraphQlResponse>(new HttpResponse(request, new HttpHeader { ContentType = "application/json" }, payload)));

            var books = Subject.Search("The Stand", "Stephen King");

            books.Should().BeEmpty();
        }

        [Test]
        public void should_normalize_bearer_prefix_and_apply_request_timeout_override()
        {
            HttpRequest capturedRequest = null;

            Mocker.GetMock<IConfigService>()
                .SetupGet(x => x.HardcoverApiToken)
                .Returns("Bearer my-test-token");

            Mocker.GetMock<IConfigService>()
                .SetupGet(x => x.HardcoverRequestTimeoutSeconds)
                .Returns(25);

            Mocker.GetMock<IHttpClient>()
                .Setup(x => x.Post<HardcoverGraphQlResponse>(It.IsAny<HttpRequest>()))
                .Returns<HttpRequest>(request =>
                {
                    capturedRequest = request;
                    return new HttpResponse<HardcoverGraphQlResponse>(new HttpResponse(request, new HttpHeader { ContentType = "application/json" }, "{\"data\":{\"search\":{\"results\":[]}}}"));
                });

            Subject.Search("Dune", "Frank Herbert").Should().BeEmpty();

            capturedRequest.Should().NotBeNull();
            capturedRequest.Headers.GetSingleValue("authorization").Should().Be("Bearer my-test-token");
            capturedRequest.RequestTimeout.Should().Be(System.TimeSpan.FromSeconds(25));
        }
    }
}
