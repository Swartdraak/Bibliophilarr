using System.Linq;
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
                          "\"data\":{\"search\":{\"error\":null,\"ids\":[1],\"results\":{\"found\":1,\"hits\":[{" +
                          "\"document\":{" +
                          "\"id\":\"book-1\"," +
                          "\"title\":\"The Stand\"," +
                          "\"description\":\"A classic horror novel\"," +
                          "\"author_names\":[\"Stephen King\"]," +
                          "\"contributions\":[{\"author\":{\"name\":\"Stephen King\"}}]," +
                          "\"isbns\":[\"9780307743688\"]," +
                          "\"release_year\":1978," +
                          "\"release_date\":\"1978-10-03\"," +
                          "\"language\":\"en\"," +
                          "\"has_ebook\":true," +
                          "\"has_audiobook\":false," +
                          "\"image\":{\"url\":\"https://covers.example/stand.jpg\"}," +
                          "\"ratings_count\":12," +
                          "\"rating\":4.5" +
                          "}}]}}}}";

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
            books[0].Editions.Value[0].ForeignEditionId.Should().Be("hardcover:edition:book-1");
            books[0].Editions.Value[0].TitleSlug.Should().Be("hardcover:edition:book-1");
            books[0].Editions.Value[0].Language.Should().Be("en");
            books[0].Editions.Value[0].Isbn13.Should().Be("9780307743688");
            books[0].Editions.Value[0].Images.Should().ContainSingle();
            books[0].Editions.Value[0].Images[0].Url.Should().Be("https://covers.example/stand.jpg");

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
                    return new HttpResponse<HardcoverGraphQlResponse>(new HttpResponse(request, new HttpHeader { ContentType = "application/json" }, "{\"data\":{\"search\":{\"error\":null,\"ids\":[],\"results\":{\"found\":0,\"hits\":[]}}}}"));
                });

            Subject.Search("Dune", "Frank Herbert").Should().BeEmpty();

            capturedRequest.Should().NotBeNull();
            capturedRequest.Headers.GetSingleValue("authorization").Should().Be("Bearer my-test-token");
            capturedRequest.RequestTimeout.Should().Be(System.TimeSpan.FromSeconds(25));
        }

        [Test]
        public void should_use_environment_token_before_config_token()
        {
            const string envVar = "BIBLIOPHILARR_HARDCOVER_API_TOKEN";
            var original = System.Environment.GetEnvironmentVariable(envVar);
            HttpRequest capturedRequest = null;

            try
            {
                System.Environment.SetEnvironmentVariable(envVar, "Bearer from-env-token");

                Mocker.GetMock<IConfigService>()
                    .SetupGet(x => x.HardcoverApiToken)
                    .Returns("config-token-should-not-be-used");

                Mocker.GetMock<IHttpClient>()
                    .Setup(x => x.Post<HardcoverGraphQlResponse>(It.IsAny<HttpRequest>()))
                    .Returns<HttpRequest>(request =>
                    {
                        capturedRequest = request;
                        return new HttpResponse<HardcoverGraphQlResponse>(new HttpResponse(request, new HttpHeader { ContentType = "application/json" }, "{\"data\":{\"search\":{\"error\":null,\"ids\":[],\"results\":{\"found\":0,\"hits\":[]}}}}"));
                    });

                Subject.Search("The Stand", "Stephen King").Should().BeEmpty();

                capturedRequest.Should().NotBeNull();
                capturedRequest.Headers.GetSingleValue("authorization").Should().Be("Bearer from-env-token");
            }
            finally
            {
                System.Environment.SetEnvironmentVariable(envVar, original);
            }
        }

        [Test]
        public void get_book_info_should_hydrate_author_book_edition_and_series_fields()
        {
            Mocker.GetMock<IHttpClient>()
                .Setup(x => x.Post<HardcoverGraphQlResponse>(It.IsAny<HttpRequest>()))
                .Returns<HttpRequest>(request =>
                    new HttpResponse<HardcoverGraphQlResponse>(new HttpResponse(request, new HttpHeader { ContentType = "application/json" }, BuildBookInfoPayload())));

            var result = Subject.GetBookInfo("hardcover:work:book-1");

            result.Should().NotBeNull();
            result.Item1.Should().Be("hardcover:author:Stephen%20King");
            result.Item3.Should().ContainSingle();

            var metadata = result.Item3[0];
            metadata.ForeignAuthorId.Should().Be("hardcover:author:Stephen%20King");
            metadata.Name.Should().Be("Stephen King");
            metadata.Overview.Should().Be("A classic horror novel");
            metadata.Ratings.Votes.Should().Be(12);
            metadata.Ratings.Value.Should().Be(4.5m);

            var book = result.Item2;
            book.Should().NotBeNull();
            book.ForeignBookId.Should().Be("hardcover:work:book-1");
            book.TitleSlug.Should().Be("hardcover:work:book-1");
            book.Title.Should().Be("The Stand");
            book.ReleaseDate.Should().Be(new System.DateTime(1978, 10, 3));
            book.Ratings.Votes.Should().Be(12);
            book.Ratings.Value.Should().Be(4.5m);
            book.AuthorMetadata.Value.Should().BeSameAs(metadata);

            book.SeriesLinks.Value.Should().ContainSingle();
            book.SeriesLinks.Value[0].Position.Should().Be("1");
            book.SeriesLinks.Value[0].SeriesPosition.Should().Be(1);
            book.SeriesLinks.Value[0].IsPrimary.Should().BeTrue();
            book.SeriesLinks.Value[0].Series.Value.ForeignSeriesId.Should().Be("hardcover:series:44");
            book.SeriesLinks.Value[0].Series.Value.Title.Should().Be("The Stand Saga");
            book.SeriesLinks.Value[0].Series.Value.WorkCount.Should().Be(6);
            book.SeriesLinks.Value[0].Series.Value.PrimaryWorkCount.Should().Be(6);

            book.Editions.Value.Should().ContainSingle();
            var edition = book.Editions.Value[0];
            edition.ForeignEditionId.Should().Be("hardcover:edition:book-1");
            edition.TitleSlug.Should().Be("hardcover:edition:book-1");
            edition.Title.Should().Be("The Stand");
            edition.Language.Should().Be("en");
            edition.Isbn13.Should().Be("9780307743688");
            edition.Format.Should().Be("Ebook");
            edition.IsEbook.Should().BeTrue();
            edition.PageCount.Should().Be(823);
            edition.Overview.Should().Be("A classic horror novel");
            edition.Ratings.Votes.Should().Be(12);
            edition.Ratings.Value.Should().Be(4.5m);
            edition.Images.Should().ContainSingle();
            edition.Images[0].Url.Should().Be("https://covers.example/stand.jpg");
        }

        [Test]
        public void get_author_info_should_hydrate_books_and_dedupe_series_links()
        {
            Mocker.GetMock<IHttpClient>()
                .Setup(x => x.Post<HardcoverGraphQlResponse>(It.IsAny<HttpRequest>()))
                .Returns<HttpRequest>(request =>
                    new HttpResponse<HardcoverGraphQlResponse>(new HttpResponse(request, new HttpHeader { ContentType = "application/json" }, BuildAuthorInfoPayload())));

            var author = Subject.GetAuthorInfo("hardcover:author:Stephen%20King");

            author.Should().NotBeNull();
            author.CleanName.Should().Be("stephenking");
            author.Metadata.Value.ForeignAuthorId.Should().Be("hardcover:author:Stephen%20King");
            author.Metadata.Value.Name.Should().Be("Stephen King");
            author.Metadata.Value.SortName.Should().Be("stephen king");
            author.Books.Value.Should().HaveCount(2);
            author.Books.Value.Should().OnlyHaveUniqueItems(b => b.ForeignBookId);

            author.Books.Value.All(x => x.AuthorMetadata.Value.ForeignAuthorId == author.Metadata.Value.ForeignAuthorId).Should().BeTrue();
            author.Books.Value.All(x => x.AuthorMetadata.Value.Name == author.Metadata.Value.Name).Should().BeTrue();

            author.Series.Value.Should().ContainSingle();
            var series = author.Series.Value[0];
            series.ForeignSeriesId.Should().Be("hardcover:series:10");
            series.Title.Should().Be("The Stand Saga");
            series.WorkCount.Should().Be(2);
            series.PrimaryWorkCount.Should().Be(2);
            series.Numbered.Should().BeTrue();
            series.LinkItems.Value.Should().HaveCount(2);
            series.LinkItems.Value.Should().OnlyHaveUniqueItems(x => x.Book.Value.ForeignBookId);
        }

        private static string BuildBookInfoPayload()
        {
            return "{" +
                   "\"data\":{\"search\":{\"error\":null,\"ids\":[1,2],\"results\":{\"found\":2,\"hits\":[{" +
                   "\"document\":{" +
                   "\"id\":\"book-1\"," +
                   "\"title\":\"The Stand\"," +
                   "\"description\":\"A classic horror novel\"," +
                   "\"author_names\":[\"Stephen King\"]," +
                   "\"contributions\":[{\"author\":{\"name\":\"Stephen King\"}}]," +
                   "\"isbns\":[\"9780307743688\"]," +
                   "\"release_year\":1978," +
                   "\"release_date\":\"1978-10-03\"," +
                   "\"language\":\"en\"," +
                   "\"has_ebook\":true," +
                   "\"has_audiobook\":false," +
                   "\"image\":{\"url\":\"https://covers.example/stand.jpg\"}," +
                   "\"ratings_count\":12," +
                   "\"rating\":4.5," +
                   "\"pages\":823," +
                   "\"featured_series\":{\"position\":1,\"series\":{\"id\":44,\"name\":\"The Stand Saga\",\"books_count\":6,\"primary_books_count\":6}}" +
                   "}},{" +
                   "\"document\":{" +
                   "\"id\":\"book-2\"," +
                   "\"title\":\"Doctor Sleep\"," +
                   "\"author_names\":[\"Stephen King\"]" +
                   "}}]}}}}";
        }

        private static string BuildAuthorInfoPayload()
        {
            return "{" +
                   "\"data\":{\"search\":{\"error\":null,\"ids\":[1,2,3],\"results\":{\"found\":3,\"hits\":[{" +
                   "\"document\":{" +
                   "\"id\":\"book-1\"," +
                   "\"title\":\"The Stand\"," +
                   "\"description\":\"A classic horror novel\"," +
                   "\"contributions\":[{\"author\":{\"name\":\"Stephen King\"}}]," +
                   "\"author_names\":[\"Stephen King\"]," +
                   "\"release_year\":1978," +
                   "\"has_ebook\":true," +
                   "\"ratings_count\":12," +
                   "\"rating\":4.5," +
                   "\"featured_series\":{\"position\":1,\"series\":{\"id\":10,\"name\":\"The Stand Saga\",\"books_count\":2,\"primary_books_count\":2}}" +
                   "}},{" +
                   "\"document\":{" +
                   "\"id\":\"book-2\"," +
                   "\"title\":\"Doctor Sleep\"," +
                   "\"description\":\"A sequel novel\"," +
                   "\"contributions\":[{\"author\":{\"name\":\"Stephen King\"}}]," +
                   "\"author_names\":[\"Stephen King\"]," +
                   "\"release_year\":2013," +
                   "\"has_ebook\":true," +
                   "\"ratings_count\":10," +
                   "\"rating\":4.0," +
                   "\"featured_series\":{\"position\":2,\"series\":{\"id\":10,\"name\":\"The Stand Saga\",\"books_count\":2,\"primary_books_count\":2}}" +
                   "}},{" +
                   "\"document\":{" +
                   "\"id\":\"book-2\"," +
                   "\"title\":\"Doctor Sleep\"," +
                   "\"description\":\"Duplicate hit\"," +
                   "\"author_names\":[\"Stephen King\"]" +
                   "}}]}}}}";
        }
    }
}
