using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using FluentAssertions;
using Moq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using NzbDrone.Common.Http;
using NzbDrone.Core.Books;
using NzbDrone.Core.Books.Commands;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Exceptions;
using NzbDrone.Core.HealthCheck;
using NzbDrone.Core.HealthCheck.Checks;
using NzbDrone.Core.Localization;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.MetadataSource.Hardcover;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.MetadataSource
{
    [TestFixture]
    public class HardcoverFallbackSearchProviderFixture : CoreTest<HardcoverFallbackSearchProvider>
    {
        private const string EnvVar = "BIBLIOPHILARR_HARDCOVER_API_TOKEN";

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

        [TestCase("Author", "Narrator", true)]
        [TestCase("Author", "Author", false)]
        [TestCase("Narrator", "Author", false)]
        public void author_work_witness_requires_the_expected_sole_author_role(string firstRole, string secondRole, bool expected)
        {
            var payload = JObject.Parse("""
                {"data":{"books":[{"id":124036,"title":"Fixture","editions":[{"id":7287,"isbn_13":"9781442482890"}],
                "contributions":[{"author_id":196108,"contributor_role":{"name":"first"}},{"author_id":22,"contributor_role":{"name":"second"}}]}]}}
                """);
            payload.SelectToken("data.books[0].contributions[0].contributor_role")["name"] = firstRole;
            payload.SelectToken("data.books[0].contributions[1].contributor_role")["name"] = secondRole;
            Mocker.GetMock<IHttpClient>().Setup(client => client.Post<JObject>(It.IsAny<HttpRequest>()))
                .Returns<HttpRequest>(request => new HttpResponse<JObject>(new HttpResponse(
                    request,
                    new HttpHeader { ContentType = "application/json" },
                    payload.ToString())));

            Subject.GetVerifiedAuthorWorkTitle("hardcover:author:196108", "hardcover:work:124036", "9781442482890")
                .Should().Be(expected ? "Fixture" : null);
        }

        [Test]
        public void should_preserve_timestamp_offset_in_stringified_search_results()
        {
            var payload = JObject.Parse(BuildBookInfoPayload());
            var search = payload.SelectToken("data.search");
            var results = search["results"];
            results.SelectToken("hits[0].document")["release_date"] = "1978-10-03T01:00:00+02:00";
            search["results"] = results.ToString(Newtonsoft.Json.Formatting.None);

            Mocker.GetMock<IHttpClient>()
                .Setup(x => x.Post<HardcoverGraphQlResponse>(It.IsAny<HttpRequest>()))
                .Returns<HttpRequest>(request => new HttpResponse<HardcoverGraphQlResponse>(
                    new HttpResponse(request, new HttpHeader { ContentType = "application/json" }, payload.ToString())));

            var books = Subject.Search("The Stand", "Stephen King");
            var book = books.Single(x => x.ForeignBookId == "hardcover:work:book-1");

            book.ReleaseDate.Should().Be(new System.DateTime(1978, 10, 2, 23, 0, 0, System.DateTimeKind.Utc));
            book.ReleaseDate.Value.Kind.Should().Be(System.DateTimeKind.Utc);
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
            books[0].TitleSlug.Should().Be("hardcover-work-book-1");
            books[0].AuthorMetadata.Should().NotBeNull();
            books[0].AuthorMetadata.Value.Name.Should().Be("Stephen King");
            books[0].Editions.Value.Should().ContainSingle();
            books[0].Editions.Value[0].ForeignEditionId.Should().Be("hardcover:edition:book-1");
            books[0].Editions.Value[0].TitleSlug.Should().Be("hardcover-edition-book-1");
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
            JObject.Parse(Encoding.UTF8.GetString(capturedRequest.ContentData)).SelectToken("variables.fields").Should().BeNull();
        }

        [Test]
        public void should_return_empty_when_graphql_error_payload_has_no_data()
        {
            var payload = "{\"data\":{\"search\":{\"error\":\"invalid query\",\"ids\":[],\"results\":{\"found\":0,\"hits\":[]}}}}";

            Mocker.GetMock<IHttpClient>()
                .Setup(x => x.Post<HardcoverGraphQlResponse>(It.IsAny<HttpRequest>()))
                .Returns<HttpRequest>(request =>
                    new HttpResponse<HardcoverGraphQlResponse>(new HttpResponse(request, new HttpHeader { ContentType = "application/json" }, payload)));

            var books = Subject.Search("The Stand", "Stephen King");

            books.Should().BeEmpty();
        }

        [Test]
        public void should_enter_cooldown_after_repeated_deterministic_provider_errors()
        {
            Mocker.GetMock<IHttpClient>()
                .Setup(x => x.Post<HardcoverGraphQlResponse>(It.IsAny<HttpRequest>()))
                .Returns<HttpRequest>(request =>
                    new HttpResponse<HardcoverGraphQlResponse>(new HttpResponse(request, new HttpHeader { ContentType = "application/json" }, "{\"data\":{\"search\":{\"error\":\"Number of weights in query_by_weights does not match number of query_by fields.\",\"ids\":[],\"results\":{\"found\":0,\"hits\":[]}}}}")));

            Subject.Search("Dune", "Frank Herbert").Should().BeEmpty();
            Subject.Search("Dune", "Frank Herbert").Should().BeEmpty();
            Subject.Search("Dune", "Frank Herbert").Should().BeEmpty();
            Subject.Search("Dune", "Frank Herbert").Should().BeEmpty();

            Mocker.GetMock<IHttpClient>()
                .Verify(x => x.Post<HardcoverGraphQlResponse>(It.IsAny<HttpRequest>()), Times.Exactly(3));

            ExceptionVerification.ExpectedWarns(1);
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
            var original = System.Environment.GetEnvironmentVariable(EnvVar);
            HttpRequest capturedRequest = null;

            try
            {
                System.Environment.SetEnvironmentVariable(EnvVar, "Bearer from-env-token");

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
                System.Environment.SetEnvironmentVariable(EnvVar, original);
            }
        }

        [Test]
        public void should_report_enabled_when_environment_token_is_present()
        {
            var original = System.Environment.GetEnvironmentVariable(EnvVar);

            try
            {
                System.Environment.SetEnvironmentVariable(EnvVar, "Bearer from-env-token");

                Mocker.GetMock<IConfigService>()
                    .SetupGet(x => x.HardcoverApiToken)
                    .Returns(string.Empty);

                Subject.IsEnabled.Should().BeTrue();
            }
            finally
            {
                System.Environment.SetEnvironmentVariable(EnvVar, original);
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
            metadata.Overview.Should().BeNull();
            metadata.Ratings.Votes.Should().Be(0);
            metadata.Ratings.Value.Should().Be(0m);

            var book = result.Item2;
            book.Should().NotBeNull();
            book.ForeignBookId.Should().Be("hardcover:work:book-1");
            book.TitleSlug.Should().Be("hardcover-work-book-1");
            book.Title.Should().Be("The Stand");
            var expectedReleaseDate = new DateTime(1978, 10, 3, 0, 0, 0, DateTimeKind.Utc);
            book.ReleaseDate.Should().Be(expectedReleaseDate);
            book.ReleaseDate.Value.Kind.Should().Be(DateTimeKind.Utc);
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
            edition.TitleSlug.Should().Be("hardcover-edition-book-1");
            edition.Title.Should().Be("The Stand");
            edition.Language.Should().Be("en");
            edition.Isbn13.Should().Be("9780307743688");
            edition.Format.Should().Be("Ebook");
            edition.IsEbook.Should().BeTrue();
            edition.PageCount.Should().Be(823);
            edition.ReleaseDate.Should().Be(expectedReleaseDate);
            edition.ReleaseDate.Value.Kind.Should().Be(DateTimeKind.Utc);
            edition.Overview.Should().Be("A classic horror novel");
            edition.Ratings.Votes.Should().Be(12);
            edition.Ratings.Value.Should().Be(4.5m);
            edition.Images.Should().ContainSingle();
            edition.Images[0].Url.Should().Be("https://covers.example/stand.jpg");
        }

        [TestCase("\"1978-10-03T01:00:00+02:00\"", 10, 2, 23)]
        [TestCase("null", 1, 1, 0)]
        [TestCase("\"invalid date\"", 1, 1, 0)]
        public void get_book_info_should_normalize_timestamps_and_year_fallback_to_utc(string releaseDateJson, int month, int day, int hour)
        {
            var payload = BuildBookInfoPayload().Replace("\"1978-10-03\"", releaseDateJson);
            Mocker.GetMock<IHttpClient>()
                .Setup(x => x.Post<HardcoverGraphQlResponse>(It.IsAny<HttpRequest>()))
                .Returns<HttpRequest>(request =>
                    new HttpResponse<HardcoverGraphQlResponse>(new HttpResponse(request, new HttpHeader { ContentType = "application/json" }, payload)));

            var book = Subject.GetBookInfo("hardcover:work:book-1").Item2;

            book.ReleaseDate.Should().Be(new DateTime(1978, month, day, hour, 0, 0, DateTimeKind.Utc));
            book.ReleaseDate.Value.Kind.Should().Be(DateTimeKind.Utc);
        }

        [TestCase("hardcover:author:123", 200, 100, new[] { 0, 100, 200 })]
        [TestCase("hardcover:author:Complete%20Author", 101, 100, new[] { 0, 100, 101 })]
        [TestCase("hardcover:author:123", 141, 65, new[] { 0, 65, 130, 141 })]
        public void get_author_info_should_fetch_the_complete_ordered_bibliography(
            string authorId, int contributionCount, int providerCap, int[] expectedOffsets)
        {
            var offsets = new List<int>();
            var searchedByName = false;
            Mocker.GetMock<IHttpClient>()
                .Setup(client => client.Post<JObject>(It.IsAny<HttpRequest>()))
                .Returns<HttpRequest>(request =>
                {
                    var body = JObject.Parse(Encoding.UTF8.GetString(request.ContentData));
                    var query = body.Value<string>("query");
                    if (query.StartsWith("query SearchAuthors", StringComparison.Ordinal))
                    {
                        searchedByName = true;
                        return CreateJsonResponse(request, JObject.Parse("{\"data\":{\"search\":{\"ids\":[123]}}}"));
                    }

                    if (query.StartsWith("query GetAuthorSeries", StringComparison.Ordinal))
                    {
                        return CreateJsonResponse(request, JObject.Parse("{\"data\":{\"book_series\":[]}}"));
                    }

                    query.Should().Contain("order_by: {id: asc}");
                    var offset = body.SelectToken("variables.offset").Value<int>();
                    var limit = body.SelectToken("variables.limit").Value<int>();
                    offsets.Add(offset);

                    var contributionIds = Enumerable.Range(offset + 1, Math.Min(Math.Min(limit, providerCap), contributionCount - offset));
                    return CreateJsonResponse(request, BuildAuthorPage(contributionIds));
                });

            var author = Subject.GetAuthorInfo(authorId);

            searchedByName.Should().Be(authorId != "hardcover:author:123");
            offsets.Should().Equal(expectedOffsets);
            author.Metadata.Value.ForeignAuthorId.Should().Be("hardcover:author:123");
            author.Books.Value.Should().HaveCount(contributionCount - 1);
            author.Books.Value.Should().OnlyHaveUniqueItems(book => book.ForeignBookId);
            author.Books.Value.Should().Contain(book => book.ForeignBookId == $"hardcover:work:{contributionCount}");
            author.Books.Value.Should().Contain(book => book.Title == "Book Collection: Volume 1");
            author.Books.Value.Should().Contain(book => book.Title == "Book Collection: Volume 2");
            author.Books.Value.Should().OnlyContain(book => book.AuthorMetadata.Value.Name == "Complete Author");
            author.Series.Value.Should().ContainSingle();
            author.Series.Value.Single().LinkItems.Value.Should().HaveCount(contributionCount - 1);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void get_author_info_should_treat_empty_graphql_results_as_inconclusive(bool authorExists)
        {
            Mocker.GetMock<IHttpClient>()
                .Setup(client => client.Post<JObject>(It.IsAny<HttpRequest>()))
                .Returns<HttpRequest>(request => CreateJsonResponse(
                    request,
                    authorExists ? BuildAuthorPage(Array.Empty<int>()) : JObject.Parse("{\"data\":{\"authors\":[]}}")));

            Mocker.GetMock<IHttpClient>()
                .Setup(client => client.Post<HardcoverGraphQlResponse>(It.IsAny<HttpRequest>()))
                .Returns<HttpRequest>(request => new HttpResponse<HardcoverGraphQlResponse>(
                    new HttpResponse(request, new HttpHeader(), "{\"data\":{\"search\":{\"results\":{\"found\":0,\"hits\":[]}}}}")));

            var lookup = () => Subject.GetAuthorInfo("hardcover:author:123");
            lookup.Should().Throw<AuthorNotFoundException>().Which.IsConfirmedMissing.Should().BeFalse();
        }

        [TestCase("hardcover:author:123", "transport")]
        [TestCase("hardcover:author:Complete%20Author", "partial")]
        [TestCase("hardcover:author:123", "missing")]
        [TestCase("hardcover:author:123", "repeated")]
        public void get_author_info_should_report_failed_later_pages_as_unavailable(string authorId, string failure)
        {
            Mocker.GetMock<IHttpClient>()
                .Setup(client => client.Post<JObject>(It.IsAny<HttpRequest>()))
                .Returns<HttpRequest>(request =>
                {
                    var body = JObject.Parse(Encoding.UTF8.GetString(request.ContentData));
                    var query = body.Value<string>("query");
                    if (query.StartsWith("query SearchAuthors", StringComparison.Ordinal))
                    {
                        return CreateJsonResponse(request, JObject.Parse("{\"data\":{\"search\":{\"ids\":[123]}}}"));
                    }

                    var offset = body.SelectToken("variables.offset").Value<int>();
                    if (offset == 0)
                    {
                        return CreateJsonResponse(request, BuildAuthorPage(Enumerable.Range(1, 100)));
                    }

                    offset.Should().Be(100);
                    if (failure == "transport")
                    {
                        throw new HttpRequestException("Provider connection closed.");
                    }

                    if (failure == "missing")
                    {
                        return CreateJsonResponse(request, JObject.Parse("{\"data\":{\"authors\":[]}}"));
                    }

                    var payload = BuildAuthorPage(new[] { failure == "repeated" ? 100 : 101 });
                    if (failure == "partial")
                    {
                        payload["errors"] = JArray.Parse("[{\"message\":\"Bibliography query incomplete\"}]");
                    }

                    return CreateJsonResponse(request, payload);
                });

            var registry = Mocker.GetMock<IMetadataProviderRegistry>();
            registry.Setup(service => service.GetProviders()).Returns(new IMetadataProvider[] { Subject });
            var orchestrator = Mocker.Resolve<MetadataProviderOrchestrator>();

            var act = () => orchestrator.GetAuthorInfo(authorId);

            act.Should().Throw<MetadataProviderUnavailableException>()
                .Which.EntityId.Should().Be("hardcover:author:123");
            ExceptionVerification.ExpectedWarns(failure == "transport" ? 2 : 1);
            Mocker.GetMock<IHttpClient>()
                .Verify(client => client.Post<HardcoverGraphQlResponse>(It.IsAny<HttpRequest>()), Times.Never);
        }

        [Test]
        public void should_keep_existing_author_when_bibliography_transport_fails()
        {
            const string authorId = "hardcover:author:123";
            const string biography = "Existing biography";
            var http = Mocker.GetMock<IHttpClient>();
            http.Setup(client => client.Post<JObject>(It.IsAny<HttpRequest>()))
                .Throws(new HttpRequestException("No route to host."));
            Mocker.GetMock<IMetadataProviderRegistry>()
                .Setup(registry => registry.GetProviders()).Returns(new IMetadataProvider[] { Subject });

            var telemetry = new MetadataProviderTelemetryService();
            Mocker.SetConstant<IMetadataProviderTelemetryService>(telemetry);
            Mocker.SetConstant<IMetadataProviderOrchestrator>(Mocker.Resolve<MetadataProviderOrchestrator>());

            var stored = new Author { Id = 12, Metadata = new AuthorMetadata { ForeignAuthorId = authorId, Overview = biography } };
            var authors = Mocker.GetMock<IAuthorService>();
            authors.Setup(service => service.GetAuthors(new[] { stored.Id })).Returns(new List<Author> { stored });

            Mocker.Resolve<RefreshAuthorService>().Execute(new RefreshAuthorCommand(stored.Id));

            http.Verify(client => client.Post<JObject>(It.IsAny<HttpRequest>()), Times.Exactly(2));
            http.VerifyNoOtherCalls();
            authors.Verify(service => service.GetAuthors(new[] { stored.Id }), Times.Once);
            authors.VerifyNoOtherCalls();
            Mocker.GetMock<IAuthorMetadataService>().VerifyNoOtherCalls();
            Mocker.GetMock<IRefreshSeriesService>().VerifyNoOtherCalls();
            stored.Metadata.Value.Overview.Should().Be(biography);
            Mocker.GetMock<ICommandResultReporter>()
                .Verify(reporter => reporter.Report(CommandResult.Unsuccessful), Times.Once);

            var localization = Mock.Of<ILocalizationService>(service =>
                service.GetLocalizedString("MetadataProviderUnreachableHealthCheckMessage") == "Metadata provider unreachable");
            var result = new MetadataProviderConnectivityCheck(telemetry, localization).Check();
            result.Type.Should().Be(HealthCheckResult.Warning);
            result.Message.Should().Be("Metadata provider unreachable");
            ExceptionVerification.ExpectedWarns(3);
            ExceptionVerification.ExpectedErrors(1);
        }

        [Test]
        public void should_keep_existing_author_metadata_when_series_is_throttled_after_bibliography()
        {
            const string authorId = "hardcover:author:123";
            var requests = new List<string>();
            Mocker.GetMock<IHttpClient>()
                .Setup(client => client.Post<JObject>(It.IsAny<HttpRequest>()))
                .Returns<HttpRequest>(request =>
                {
                    var body = JObject.Parse(Encoding.UTF8.GetString(request.ContentData));
                    if (body.Value<string>("query").StartsWith("query GetAuthorSeries", StringComparison.Ordinal))
                    {
                        requests.Add("series");
                        var headers = new HttpHeader { ["Retry-After"] = "72096" };
                        var response = new HttpResponse(request, headers, string.Empty, HttpStatusCode.TooManyRequests);
                        throw new TooManyRequestsException(request, response);
                    }

                    var offset = body.SelectToken("variables.offset").Value<int>();
                    requests.Add($"works:{offset}");
                    return CreateJsonResponse(request, BuildAuthorPage(offset == 0 ? new[] { 1 } : Array.Empty<int>()));
                });

            Mocker.GetMock<IMetadataProviderRegistry>()
                .Setup(registry => registry.GetProviders()).Returns(new IMetadataProvider[] { Subject });
            var orchestrator = Mocker.Resolve<MetadataProviderOrchestrator>();
            Mocker.SetConstant<IMetadataProviderOrchestrator>(orchestrator);

            var lookup = () => orchestrator.GetAuthorInfo(authorId);
            var unavailable = lookup.Should().Throw<MetadataProviderUnavailableException>().Which;
            unavailable.EntityId.Should().Be(authorId);
            unavailable.InnerException.Should().BeOfType<TooManyRequestsException>()
                .Which.RetryAfter.Should().Be(TimeSpan.FromSeconds(72096));
            requests.Should().Equal("works:0", "works:1", "series");
            requests.Clear();

            var stored = new Author { Id = 12, Metadata = new AuthorMetadata { ForeignAuthorId = authorId, Overview = "Existing biography" } };
            var authorService = Mocker.GetMock<IAuthorService>();
            authorService.Setup(service => service.GetAuthors(new[] { stored.Id })).Returns(new List<Author> { stored });

            Mocker.Resolve<RefreshAuthorService>().Execute(new RefreshAuthorCommand(stored.Id));

            requests.Should().Equal("works:0", "works:1", "series");
            Mocker.GetMock<ICommandResultReporter>()
                .Verify(reporter => reporter.Report(CommandResult.Unsuccessful), Times.Once);
            authorService.Verify(service => service.GetAuthors(new[] { stored.Id }), Times.Once);
            authorService.VerifyNoOtherCalls();
            Mocker.GetMock<IAuthorMetadataService>().VerifyNoOtherCalls();
            Mocker.GetMock<IRefreshSeriesService>().VerifyNoOtherCalls();
            stored.Metadata.Value.Overview.Should().Be("Existing biography");
            ExceptionVerification.ExpectedWarns(3);
            ExceptionVerification.ExpectedErrors(1);
        }

        [TestCase(true)]
        [TestCase(false)]
        public void should_keep_the_legacy_edition_and_add_typed_provider_editions(bool authorHydration)
        {
            var source = InstallEditionResponse(JArray.Parse("""[{"id":50,"reading_format_id":1,"edition_format":"Paperback","isbn_13":"9780306406157","pages":307,"language":{"language":"English"}}]"""));

            var book = authorHydration
                ? Subject.GetAuthorInfo("hardcover:author:123").Books.Value.Single()
                : Subject.GetBookInfo("hardcover:work:543").Item2;

            book.SeriesLinks.Value.Single().Series.Value.Title.Should().Be("Variant Series");
            book.Editions.Value.Select(edition => edition.ForeignEditionId)
                .Should().Equal("hardcover:edition:543", "hardcover:source-edition:60", "hardcover:source-edition:80");
            var primary = book.Editions.Value[0];
            primary.Isbn13.Should().Be("9780306406157");
            primary.PageCount.Should().Be(302);
            primary.Format.Should().Be("Print");
            primary.MatchesFormat(FormatType.Audiobook).Should().BeFalse();
            book.GetPreferredEdition(FormatType.Ebook).Asin.Should().Be("EBOOKASIN");
            book.GetPreferredEdition(FormatType.Audiobook).Asin.Should().Be("AUDIOASIN");
            book.GetPreferredEdition(FormatType.Audiobook).Format.Should().Be("Audiobook");
            source.SelectToken("ebooks[0].id").Value<int>().Should().Be(60);
        }

        [TestCase(true)]
        [TestCase(false)]
        public void should_prefer_edition_artwork_and_keep_work_artwork_as_fallback(bool authorHydration)
        {
            var legacyEditions = JArray.Parse("""[{"id":50,"reading_format_id":1}]""");
            var authorSeries = JArray.Parse("""
                [{"book_id":543,"position":1,"series":{"id":789,"name":"Variant Series","description":"Full series description"}}]
                """);
            var source = InstallEditionResponse(legacyEditions, authorSeries);
            source["cached_image"] = new JObject { ["url"] = "https://covers.example/work.jpg" };
            ((JObject)source.SelectToken("ebooks[0]"))["cached_image"] = new JObject { ["url"] = "https://covers.example/ebook.jpg" };
            ((JObject)source.SelectToken("book_series[0].series"))["description"] = "Published series description";

            var author = authorHydration ? Subject.GetAuthorInfo("hardcover:author:123") : null;
            var book = author?.Books.Value.Single() ?? Subject.GetBookInfo("hardcover:work:543").Item2;

            book.GetPreferredEdition(FormatType.Ebook).Images.Should().ContainSingle()
                .Which.Url.Should().Be("https://covers.example/ebook.jpg");
            book.GetPreferredEdition(FormatType.Audiobook).Images.Should().ContainSingle()
                .Which.Url.Should().Be("https://covers.example/work.jpg");
            book.SeriesLinks.Value.Single().Series.Value.Description.Should().Be("Published series description");

            if (authorHydration)
            {
                author.Series.Value.Single().Description.Should().Be("Full series description");
            }
        }

        [Test]
        public void should_not_duplicate_the_legacy_edition_if_it_is_the_preferred_source_edition()
        {
            InstallEditionResponse(JArray.Parse("""[{"id":60,"reading_format_id":4,"edition_format":"Kindle","asin":"EBOOKASIN"}]"""));

            var editions = Subject.GetAuthorInfo("hardcover:author:123").Books.Value.Single().Editions.Value;

            editions.Select(edition => edition.ForeignEditionId)
                .Should().Equal("hardcover:edition:543", "hardcover:source-edition:80");
            editions[0].IsEbook.Should().BeTrue();
        }

        [TestCase(true)]
        [TestCase(false)]
        public void should_map_edition_publisher_and_publication_date_without_changing_work_release_date(bool authorHydration)
        {
            var legacyEditions = JArray.Parse("""[{"id":50,"reading_format_id":1}]""");
            var source = InstallEditionResponse(legacyEditions);
            source["release_date"] = "1990-05-04";
            var ebook = (JObject)source.SelectToken("ebooks[0]");
            ebook["publisher"] = new JObject { ["name"] = "Fixture Ebook Publisher" };
            ebook["release_date"] = "2001-02-03";
            var audiobook = (JObject)source.SelectToken("audiobooks[0]");
            audiobook["publisher"] = new JObject { ["name"] = "Fixture Audiobook Publisher" };
            audiobook["release_date"] = "2003-11-12";

            var author = authorHydration ? Subject.GetAuthorInfo("hardcover:author:123") : null;
            var book = author?.Books.Value.Single() ?? Subject.GetBookInfo("hardcover:work:543").Item2;

            book.ReleaseDate.Should().Be(new DateTime(1990, 5, 4));
            book.GetPreferredEdition(FormatType.Ebook).Publisher.Should().Be("Fixture Ebook Publisher");
            book.GetPreferredEdition(FormatType.Ebook).ReleaseDate.Should().Be(new DateTime(2001, 2, 3));
            book.GetPreferredEdition(FormatType.Audiobook).Publisher.Should().Be("Fixture Audiobook Publisher");
            book.GetPreferredEdition(FormatType.Audiobook).ReleaseDate.Should().Be(new DateTime(2003, 11, 12));
        }

        [Test]
        public void should_not_advertise_an_audiobook_from_print_or_a_wrongly_typed_alias()
        {
            var source = InstallEditionResponse(JArray.Parse("""[{"id":50,"reading_format_id":1,"edition_format":"Paperback"}]"""));
            source["ebooks"] = new JArray();
            source["audiobooks"] = JArray.Parse("""[{"id":50,"reading_format_id":1,"edition_format":"Paperback"},{"id":0,"reading_format_id":2}]""");

            var book = Subject.GetAuthorInfo("hardcover:author:123").Books.Value.Single();

            book.Editions.Value.Should().ContainSingle();
            book.GetPreferredEdition(FormatType.Ebook).Should().BeNull();
            book.GetPreferredEdition(FormatType.Audiobook).Should().BeNull();
        }

        [TestCase("""{"id":50,"reading_format_id":1}""")]
        [TestCase("""{"id":50,"reading_format_id":3}""")]
        [TestCase("""{"id":50,"reading_format_id":2,"edition_format":"Paperback"}""")]
        [TestCase("""{"id":50,"reading_format_id":4,"edition_format":"Kindle","physical_format":"Paperback"}""")]
        public void should_leave_ambiguous_or_conflicting_edition_format_unclassified(string editionJson)
        {
            var source = InstallEditionResponse(new JArray(JObject.Parse(editionJson)));
            source["ebooks"] = new JArray();
            source["audiobooks"] = new JArray();

            var book = Subject.GetAuthorInfo("hardcover:author:123").Books.Value.Single();

            book.Editions.Value.Single().Format.Should().Be("Unclassified");
            book.GetPreferredEdition(FormatType.Ebook).Should().BeNull();
            book.GetPreferredEdition(FormatType.Audiobook).Should().BeNull();
        }

        private JObject InstallEditionResponse(JArray legacyEditions, JArray authorSeries = null)
        {
            var book = JObject.Parse("""
                {
                  "id": 543,
                  "title": "Print, ebook, and audio",
                  "pages": 302,
                  "cached_contributors": [{"author":{"id":123,"name":"Complete Author"}}],
                  "book_series": [{"position":1,"series":{"id":789,"name":"Variant Series"}}],
                  "ebooks": [{"id":60,"reading_format_id":4,"edition_format":"Kindle","asin":"EBOOKASIN","pages":288}],
                  "audiobooks": [{"id":80,"reading_format_id":2,"edition_format":"Audiobook","asin":"AUDIOASIN","language":{"language":"English"}}]
                }
                """);
            book["editions"] = legacyEditions;

            Mocker.GetMock<IHttpClient>()
                .Setup(client => client.Post<JObject>(It.IsAny<HttpRequest>()))
                .Returns<HttpRequest>(request =>
                {
                    var body = JObject.Parse(Encoding.UTF8.GetString(request.ContentData));
                    var query = body.Value<string>("query");
                    if (query.StartsWith("query GetAuthorSeries", StringComparison.Ordinal))
                    {
                        return CreateJsonResponse(request, new JObject
                        {
                            ["data"] = new JObject { ["book_series"] = authorSeries ?? new JArray() }
                        });
                    }

                    query.Should().Contain("ebooks: editions(where: {reading_format_id: {_eq: 4}}");
                    query.Should().Contain("audiobooks: editions(where: {reading_format_id: {_eq: 2}}");

                    if (query.StartsWith("query GetBook", StringComparison.Ordinal))
                    {
                        return CreateJsonResponse(request, new JObject { ["data"] = new JObject { ["books"] = new JArray(book) } });
                    }

                    var payload = BuildAuthorPage(body.SelectToken("variables.offset").Value<int>() == 0 ? new[] { 1 } : Array.Empty<int>());
                    if (payload.SelectToken("data.authors[0].contributions[0]") is JObject contribution)
                    {
                        contribution["book"] = book;
                    }

                    return CreateJsonResponse(request, payload);
                });

            return book;
        }

        [TestCase("Published author biography")]
        [TestCase(null)]
        public void author_hydration_should_map_the_queried_author_bio_without_using_coauthor_or_book_content(string biography)
        {
            Mocker.GetMock<IHttpClient>()
                .Setup(client => client.Post<JObject>(It.IsAny<HttpRequest>()))
                .Returns<HttpRequest>(request =>
                {
                    var body = JObject.Parse(Encoding.UTF8.GetString(request.ContentData));
                    var query = body.Value<string>("query");
                    if (query.StartsWith("query GetAuthorSeries", StringComparison.Ordinal))
                    {
                        return CreateJsonResponse(request, JObject.Parse("""{"data":{"book_series":[]}}"""));
                    }

                    query.Should().Contain("bio");
                    var offset = body.SelectToken("variables.offset").Value<int>();
                    var payload = BuildAuthorPage(offset == 0 ? new[] { 1 } : Array.Empty<int>());
                    payload.SelectToken("data.authors[0]")["bio"] = biography;
                    if (offset == 0)
                    {
                        var book = payload.SelectToken("data.authors[0].contributions[0].book");
                        book["description"] = "Synopsis of a coauthored book";
                        book["ratings_count"] = 71;
                        book["rating"] = 4.2;
                    }

                    return CreateJsonResponse(request, payload);
                });

            var author = Subject.GetAuthorInfo("hardcover:author:123");
            var book = author.Books.Value.Single();

            author.Metadata.Value.Name.Should().Be("Complete Author");
            author.Metadata.Value.Overview.Should().Be(biography);
            author.Metadata.Value.Ratings.Votes.Should().Be(0);
            book.AuthorMetadata.Value.Should().BeSameAs(author.Metadata.Value);
            book.Editions.Value.Single().Overview.Should().Be("Synopsis of a coauthored book");
            book.Ratings.Votes.Should().Be(71);
        }

        [TestCase(123, "Author", true)]
        [TestCase(123, "Narrator", false)]
        [TestCase(456, "Author", false)]
        public void author_bibliography_should_project_only_positive_author_credit(int creditedAuthor, string role, bool creditExpected)
        {
            Mocker.GetMock<IHttpClient>().Setup(client => client.Post<JObject>(It.IsAny<HttpRequest>()))
                .Returns<HttpRequest>(request =>
                {
                    var body = JObject.Parse(Encoding.UTF8.GetString(request.ContentData));
                    var query = body.Value<string>("query");
                    if (query.StartsWith("query GetAuthorSeries", StringComparison.Ordinal))
                    {
                        return CreateJsonResponse(request, JObject.Parse("""{"data":{"book_series":[]}}"""));
                    }

                    query.Should().Contain("author_id").And.Contain("contributor_role");
                    var payload = BuildAuthorPage(body.SelectToken("variables.offset").Value<int>() == 0 ? new[] { 1 } : Array.Empty<int>());
                    if (payload.SelectToken("data.authors[0].contributions[0]") is JObject credit)
                    {
                        credit["author_id"] = creditedAuthor;
                        credit["contributor_role"] = new JObject { ["name"] = role };
                    }

                    return CreateJsonResponse(request, payload);
                });

            var work = Subject.GetAuthorInfo("hardcover:author:123").Books.Value.Single();

            work.CreditedHardcoverAuthorId.Should().Be(creditExpected ? 123 : null);
        }

        private static HttpResponse<JObject> CreateJsonResponse(HttpRequest request, JObject body)
        {
            return new HttpResponse<JObject>(
                new HttpResponse(request, new HttpHeader { ContentType = "application/json" }, body.ToString()));
        }

        [TestCase("supported", true)]
        [TestCase("competing-owner", false)]
        [TestCase("wrong-edition-owner", false)]
        [TestCase("missing-edition-owner", false)]
        [TestCase("wrong-original-author", false)]
        [TestCase("different-title", false)]
        [TestCase("unsupported", false)]
        [TestCase("truncated", false)]
        public void author_bibliography_should_only_stage_supported_original_work_mappings(string scenario, bool expectMapping)
        {
            var original = JObject.Parse("""
                {"id":101,"title":"Fixture Work","canonical_id":444,"cached_contributors":[{"author":{"id":123}}]}
                """);
            var target = JObject.Parse("""
                {"id":202,"title":"Fixture Work","cached_contributors":[{"author":{"id":123}}],
                 "book_mappings":[
                    {"original_book_id":101,"book_id":202,"edition_id":987,"edition":{"id":987,"book_id":202},"state":"normalized"},
                    {"original_book_id":101,"book_id":202,"edition_id":987,"edition":{"id":987,"book_id":202},"state":"cannot_load"},
                    {"original_book_id":202,"book_id":202,"state":"normalized"}]}
                """);
            var mapping = (JObject)target.SelectToken("book_mappings[0]");
            var competitor = JObject.Parse("""{"id":303,"title":"Fixture Work"}""");

            switch (scenario)
            {
                case "competing-owner":
                    competitor["book_mappings"] = JArray.Parse("""[{"original_book_id":101,"book_id":303,"state":"normalized"}]""");
                    break;
                case "wrong-edition-owner":
                    ((JObject)mapping["edition"])["book_id"] = 303;
                    break;
                case "missing-edition-owner":
                    mapping["edition"] = JValue.CreateNull();
                    break;
                case "wrong-original-author":
                    original["cached_contributors"] = JArray.Parse("""[{"author":{"id":456}}]""");
                    break;
                case "different-title":
                    original["title"] = "Different work";
                    break;
                case "unsupported":
                    mapping["state"] = "cannot_load";
                    break;
                case "truncated":
                    target["book_mappings"] = new JArray(Enumerable.Range(0, 21).Select(_ => mapping.DeepClone()));
                    break;
            }

            var pages = new[] { target, original, competitor };
            Mocker.GetMock<IHttpClient>().Setup(client => client.Post<JObject>(It.IsAny<HttpRequest>()))
                .Returns<HttpRequest>(request =>
                {
                    var body = JObject.Parse(Encoding.UTF8.GetString(request.ContentData));
                    var query = body.Value<string>("query");
                    if (query.StartsWith("query GetAuthorSeries", StringComparison.Ordinal))
                    {
                        return CreateJsonResponse(request, JObject.Parse("""{"data":{"book_series":[]}}"""));
                    }

                    query.Should().Contain("book_mappings").And.Contain("edition { id book_id }");
                    body.SelectToken("variables.mappingLimit").Value<int>().Should().Be(21);
                    var offset = body.SelectToken("variables.offset").Value<int>();
                    var payload = BuildAuthorPage(offset < pages.Length ? new[] { offset + 1 } : Array.Empty<int>());
                    if (payload.SelectToken("data.authors[0].contributions[0]") is JObject contribution)
                    {
                        contribution["book"] = pages[offset];
                    }

                    return CreateJsonResponse(request, payload);
                });

            var books = Subject.GetAuthorInfo("hardcover:author:123").Books.Value;

            books.Select(book => book.ForeignBookId).Should().Equal("hardcover:work:202", "hardcover:work:101", "hardcover:work:303");
            var pending = books.SelectMany(book => book.PendingSourceAssociations).ToList();
            if (expectMapping)
            {
                var alias = books.First().PendingSourceAssociations.Should().ContainSingle().Which;
                alias.Provider.Should().Be("Hardcover");
                alias.ProviderId.Should().Be("101");
                alias.Evidence.Should().Be(CatalogueIdentityRepository.HardcoverWorkMappingEvidence);
                pending.Should().ContainSingle();
            }
            else
            {
                pending.Should().BeEmpty();
            }
        }

        private static JObject BuildAuthorPage(IEnumerable<int> contributionIds)
        {
            return JObject.FromObject(new
            {
                data = new
                {
                    authors = new[]
                    {
                        new
                        {
                            id = 123,
                            name = "Complete Author",
                            slug = "complete-author",
                            contributions = contributionIds.Select(contributionId =>
                            {
                                // Duplicate work IDs are emitted with distinct contribution IDs.
                                var bookId = contributionId == 100 ? 1 : contributionId;

                                return new
                                {
                                    id = contributionId,
                                    book = new
                                    {
                                        id = bookId,
                                        title = bookId is 1 or 2 ? $"Book Collection: Volume {bookId}" : $"Book {bookId}",
                                        cached_contributors = new[] { new { author = new { id = 456, name = "Coauthor" } } },
                                        book_series = new[] { new { position = bookId, series = new { id = 789, name = "Complete Series" } } }
                                    }
                                };
                            })
                        }
                    }
                }
            });
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
