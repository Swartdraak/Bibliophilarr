using System;
using System.Collections.Generic;
using System.Net;
using FluentAssertions;
using Moq;
using NLog;
using NLog.Config;
using NLog.Targets;
using NUnit.Framework;
using NzbDrone.Common.Http;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Exceptions;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.MetadataSource.OpenLibrary;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.MetadataSource.OpenLibrary
{
    [TestFixture]
    public class OpenLibraryClientResilienceFixture : CoreTest<OpenLibraryClient>
    {
        [SetUp]
        public void SetUp()
        {
            Mocker.GetMock<IConfigService>().SetupGet(x => x.MetadataProviderTimeoutSeconds).Returns(5);
            Mocker.GetMock<IConfigService>().SetupGet(x => x.MetadataProviderRetryBudget).Returns(1);
            Mocker.GetMock<IConfigService>().SetupGet(x => x.MetadataProviderCircuitBreakerThreshold).Returns(3);
            Mocker.GetMock<IConfigService>().SetupGet(x => x.MetadataProviderCircuitBreakerDurationSeconds).Returns(30);
            Mocker.GetMock<IConfigService>().SetupGet(x => x.OpenLibrarySearchRetryBudget).Returns(-1);
            Mocker.GetMock<IConfigService>().SetupGet(x => x.OpenLibraryIsbnRetryBudget).Returns(-1);
            Mocker.GetMock<IConfigService>().SetupGet(x => x.OpenLibraryWorkRetryBudget).Returns(-1);
        }

        [TestCase("Test Book", false, HttpStatusCode.OK, 1, 0)]
        [TestCase("Test Book", false, HttpStatusCode.ServiceUnavailable, 0, 1)]
        [TestCase("Test Author", true, HttpStatusCode.OK, 1, 0)]
        [TestCase("Test Author", true, HttpStatusCode.ServiceUnavailable, 0, 1)]
        [TestCase("isbn:9780140328720", false, HttpStatusCode.OK, 0, 0)]
        [TestCase("isbn:9780140328721", false, HttpStatusCode.NotFound, 1, 0)]
        [TestCase("isbn:9780140328721", false, HttpStatusCode.ServiceUnavailable, 0, 1)]
        [TestCase("isbn:9780140328721", false, HttpStatusCode.TooManyRequests, 0, 1)]
        [TestCase("asin:B000000001", false, HttpStatusCode.ServiceUnavailable, 0, 1)]
        public void search_telemetry_should_distinguish_no_results_from_http_failures(
            string term, bool searchAuthors, HttpStatusCode status, int expectedSuccesses, int expectedFailures)
        {
            var config = Mocker.GetMock<IConfigService>();
            config.SetupGet(provider => provider.EnableOpenLibraryProvider).Returns(true);
            var isIsbn = term.StartsWith("isbn:", StringComparison.Ordinal);
            var path = isIsbn ? $"/isbn/{term[5..]}.json" : "/search.json";
            var request = new HttpRequest($"https://openlibrary.org{path}");
            var headers = new HttpHeader();
            if (status == HttpStatusCode.TooManyRequests)
            {
                headers.Add("Retry-After", "0");
            }

            var response = new HttpResponse(request, headers, "{\"docs\":[]}", status);
            Mocker.GetMock<IHttpClient>().Setup(client => client.Get(It.IsAny<HttpRequest>())).Returns(response);

            var logger = LogManager.GetCurrentClassLogger();
            var provider = new OpenLibraryProvider(Subject, config.Object, logger);
            var registry = new MetadataProviderRegistry(new[] { provider });
            var telemetry = new MetadataProviderTelemetryService();
            var orchestrator = new MetadataProviderOrchestrator(registry, telemetry, logger);
            if (searchAuthors)
            {
                orchestrator.SearchForNewAuthor(term);
            }
            else
            {
                orchestrator.SearchForNewBook(term, null);
            }

            if (expectedSuccesses + expectedFailures == 0)
            {
                telemetry.GetOperationSnapshots().Should().BeEmpty();
                Mocker.GetMock<IHttpClient>().Verify(client => client.Get(It.IsAny<HttpRequest>()), Times.Never());
                return;
            }

            var entry = telemetry.GetOperationSnapshots().Should().ContainSingle().Which;
            entry.ProviderName.Should().Be("OpenLibrary");
            var expectedOperation = isIsbn ? "search-by-isbn" : searchAuthors ? "search-for-new-author" : "search-for-new-book";
            entry.OperationName.Should().Be(expectedOperation);
            entry.Calls.Should().Be(1);
            entry.Successes.Should().Be(expectedSuccesses);
            entry.Failures.Should().Be(expectedFailures);
            entry.NullResults.Should().Be(0);
            entry.TotalResultCount.Should().Be(0);
            var expectedRequests = status == HttpStatusCode.TooManyRequests ? 2 : 1;
            Mocker.GetMock<IHttpClient>().Verify(client => client.Get(It.Is<HttpRequest>(value => value.Url.Path == path)), Times.Exactly(expectedRequests));
            if (status is HttpStatusCode.ServiceUnavailable or HttpStatusCode.TooManyRequests)
            {
                var providerWarning = status == HttpStatusCode.TooManyRequests
                    ? "OpenLibrary rate-limit (429) exceeded retry budget"
                    : "OpenLibrary returned HTTP 503";
                ExceptionVerification.ExpectedWarns(
                    providerWarning,
                    $"Metadata provider 'OpenLibrary' failed during {expectedOperation}",
                    $"All providers failed for operation {expectedOperation}");
            }
            else
            {
                ExceptionVerification.ExpectedWarns(0);
            }
        }

        [Test]
        public void supplemental_author_search_honors_retry_after_without_retrying_or_following_redirects()
        {
            Mocker.GetMock<IConfigService>().SetupGet(config => config.OpenLibrarySearchRetryBudget).Returns(3);
            Mocker.GetMock<IConfigService>().SetupGet(config => config.MetadataProviderCircuitBreakerThreshold).Returns(5);
            Mocker.GetMock<IConfigService>().SetupGet(config => config.EnableOpenLibraryProvider).Returns(true);
            var http = Mocker.GetMock<IHttpClient>();
            http.Setup(client => client.Get(It.IsAny<HttpRequest>()))
                .Returns<HttpRequest>(request => new HttpResponse(
                    request, new HttpHeader { { "Retry-After", "120" } }, Array.Empty<byte>(), HttpStatusCode.TooManyRequests));

            Subject.SearchOnePageWithoutRetry("Fixture Author", 10).RequestUnavailable.Should().BeTrue();
            Subject.SearchOnePageWithoutRetry("Different Fixture Author", 10).RequestUnavailable.Should().BeTrue();

            http.Verify(client => client.Get(It.Is<HttpRequest>(request =>
                request.Url.Path == "/search.json" && !request.AllowAutoRedirect)), Times.Once());
            http.VerifyNoOtherCalls();
            ExceptionVerification.ExpectedWarns("OpenLibrary rate-limit (429) exceeded retry budget");
        }

        [Test]
        public void should_stop_bounded_author_profiles_after_the_first_rate_limit_response()
        {
            var config = Mocker.GetMock<IConfigService>();
            config.SetupGet(settings => settings.EnableOpenLibraryProvider).Returns(true);
            config.SetupGet(settings => settings.OpenLibraryWorkRetryBudget).Returns(1);
            config.SetupGet(settings => settings.OpenLibraryWorkTimeoutSeconds).Returns(45);
            var http = Mocker.GetMock<IHttpClient>();
            var requests = new List<HttpRequest>();
            http.Setup(client => client.Get(It.IsAny<HttpRequest>()))
                .Returns<HttpRequest>(request =>
                {
                    requests.Add(request);
                    return requests.Count == 1
                        ? new HttpResponse(request, new HttpHeader { ["Retry-After"] = "0" }, Array.Empty<byte>(), HttpStatusCode.TooManyRequests)
                        : new HttpResponse(request, new HttpHeader(), """{"key":"/authors/OL42A","name":"Fixture Author"}""");
                });

            var provider = new OpenLibraryProvider(Subject, config.Object, TestLogger);
            provider.GetAuthorProfileMetadata("OL42A").Should().BeNull();
            provider.GetAuthorProfileMetadata("OL84A").Should().BeNull();

            var sent = requests.Should().ContainSingle().Which;
            sent.Url.Path.Should().Be("/authors/OL42A.json");
            sent.AllowAutoRedirect.Should().BeFalse();
            sent.RateLimit.Should().Be(TimeSpan.FromSeconds(1));
            sent.RateLimitKey.Should().Be("OpenLibrary");
            sent.RequestTimeout.Should().Be(TimeSpan.FromSeconds(10));
            ExceptionVerification.ExpectedWarns("OpenLibrary rate-limit (429) exceeded retry budget for openlibrary.author-profile");
        }

        [Test]
        public void should_keep_the_configured_retry_for_the_normal_author_fetch()
        {
            Mocker.GetMock<IConfigService>().SetupGet(settings => settings.OpenLibraryWorkRetryBudget).Returns(1);
            var endpoint = new HttpRequest("https://openlibrary.org/authors/OL42A.json");
            var http = Mocker.GetMock<IHttpClient>();
            http.SetupSequence(client => client.Get(It.IsAny<HttpRequest>()))
                .Returns(new HttpResponse(endpoint, new HttpHeader { ["Retry-After"] = "0" }, Array.Empty<byte>(), HttpStatusCode.TooManyRequests))
                .Returns(new HttpResponse(endpoint, new HttpHeader(), """{"key":"/authors/OL42A","name":"Fixture Author"}"""));

            Subject.GetAuthor("OL42A", confirmNotFound: true).Key.Should().Be("/authors/OL42A");

            http.Verify(client => client.Get(It.Is<HttpRequest>(request => request.Url.Path == "/authors/OL42A.json")), Times.Exactly(2));
        }

        [Test]
        public void search_should_retry_after_rate_limit_and_succeed()
        {
            var first = new HttpResponse(new HttpRequest("https://openlibrary.org/search.json"), new HttpHeader { { "Retry-After", "0" } }, Array.Empty<byte>(), System.Net.HttpStatusCode.TooManyRequests);
            var second = new HttpResponse(new HttpRequest("https://openlibrary.org/search.json"), new HttpHeader(), "{\"docs\":[{\"key\":\"/works/OL1W\",\"title\":\"Retry Hit\"}]}");

            Mocker.GetMock<IHttpClient>()
                .SetupSequence(x => x.Get(It.IsAny<HttpRequest>()))
                .Returns(first)
                .Returns(second);

            var result = Subject.Search("retry", 1);

            result.Should().NotBeNull();
            result.Docs.Should().HaveCount(1);
            Mocker.GetMock<IHttpClient>().Verify(x => x.Get(It.IsAny<HttpRequest>()), Times.Exactly(2));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void isbn_diagnostic_should_report_operation_without_the_lookup_value(bool exact)
        {
            const string isbn = "9781442482890";
            var http = Mocker.GetMock<IHttpClient>();
            http.Setup(client => client.Get(It.IsAny<HttpRequest>())).Returns<HttpRequest>(request =>
                new HttpResponse(request, new HttpHeader(), string.Empty, HttpStatusCode.ServiceUnavailable));
            var messages = new MemoryTarget { Layout = "${message} ${exception}" };
            var configuration = new LoggingConfiguration();
            configuration.AddRuleForAllLevels(messages);
            using var logFactory = new LogFactory { Configuration = configuration };
            var provider = new OpenLibraryClient(http.Object, Mocker.GetMock<IConfigService>().Object, logFactory.GetLogger(nameof(OpenLibraryClient)));

            var result = exact ? provider.GetExactEditionByIsbn(isbn) : provider.GetEditionByIsbn(isbn);

            result.RequestUnavailable.Should().BeTrue();
            messages.Logs.Should().Contain(message => message.Contains("ISBN lookup"));
            messages.Logs.Should().Contain(message => message.Contains("HTTP 503") && message.Contains("openlibrary.isbn"));
            messages.Logs.Should().NotContain(message => message.Contains(isbn));
            http.Verify(client => client.Get(It.Is<HttpRequest>(request => request.Url.Path == $"/isbn/{isbn}.json")), Times.Once());
        }

        [TestCase("/books/OL33516062M.json")]
        [TestCase("https://openlibrary.org/books/OL33516062M.json")]
        [TestCase("https://openlibrary.org:443/books/OL33516062M.json")]
        public void isbn_lookup_follows_one_validated_edition_redirect(string location)
        {
            const string isbnUrl = "https://openlibrary.org/isbn/9781442482890.json";
            const string editionUrl = "https://openlibrary.org/books/OL33516062M.json";
            var urls = new List<string>();
            var redirects = new List<bool>();
            Mocker.GetMock<IHttpClient>().Setup(client => client.Get(It.IsAny<HttpRequest>()))
                .Returns<HttpRequest>(request =>
                {
                    urls.Add(request.Url.ToString());
                    redirects.Add(request.AllowAutoRedirect);
                    return urls.Count == 1
                        ? new HttpResponse(request, new HttpHeader { { "Location", location } }, "", HttpStatusCode.Found)
                        : new HttpResponse(request, new HttpHeader(), """{"key":"/books/OL33516062M","isbn_13":["9781442482890"]}""");
                });

            var edition = Subject.GetEditionByIsbn("9781442482890");

            edition.Should().NotBeNull();
            edition.Key.Should().Be("/books/OL33516062M");
            urls.Should().Equal(isbnUrl, editionUrl);
            redirects.Should().Equal(false, false);
        }

        [TestCase(true, true, true)]
        [TestCase(true, false, false)]
        [TestCase(false, false, true)]
        public void isbn_corroboration_requires_the_body_to_match_the_redirected_edition(
            bool corroboration, bool matchingBody, bool accepted)
        {
            const string isbn = "9781442482890";
            const string redirectedEdition = "/books/OL33516062M";
            var bodyEdition = matchingBody ? redirectedEdition : "/books/OL2M";
            var client = Mocker.GetMock<IHttpClient>();
            client.Setup(http => http.Get(It.IsAny<HttpRequest>())).Returns<HttpRequest>(request =>
                request.Url.ToString().Contains("/isbn/")
                    ? new HttpResponse(request, new HttpHeader { { "Location", redirectedEdition + ".json" } }, string.Empty, HttpStatusCode.Found)
                    : new HttpResponse(request, new HttpHeader(), $$"""{"key":"{{bodyEdition}}","isbn_13":["{{isbn}}"]}"""));

            var edition = corroboration ? Subject.GetExactEditionByIsbn(isbn) : Subject.GetEditionByIsbn(isbn);

            if (accepted)
            {
                edition.Should().NotBeNull();
                edition.Key.Should().Be(bodyEdition);
            }
            else
            {
                edition.Should().BeNull();
            }

            client.Verify(http => http.Get(It.IsAny<HttpRequest>()), Times.Exactly(2));
        }

        [TestCase("https://example.invalid/books/OL33516062M.json")]
        [TestCase("http://openlibrary.org/books/OL33516062M.json")]
        [TestCase("https://openlibrary.org:444/books/OL33516062M.json")]
        [TestCase("https://user@openlibrary.org/books/OL33516062M.json")]
        [TestCase("https://@openlibrary.org/books/OL33516062M.json")]
        [TestCase("/books/OL33516062M.json?next=1")]
        [TestCase("/books/OL33516062M.json#other")]
        [TestCase("/works/OL33516062W.json")]
        [TestCase("/books/../books/OL33516062M.json")]
        [TestCase("/books/OL33516062%4D.json")]
        [TestCase("//openlibrary.org/books/OL33516062M.json")]
        [TestCase(null)]
        public void isbn_lookup_rejects_unverified_destination(string location)
        {
            var headers = new HttpHeader();
            if (location != null)
            {
                headers.Add("Location", location);
            }

            Mocker.GetMock<IHttpClient>().Setup(client => client.Get(It.IsAny<HttpRequest>()))
                .Returns<HttpRequest>(request => new HttpResponse(request, headers, "", HttpStatusCode.Found));

            Subject.GetEditionByIsbn("9781442482890").Should().BeNull();

            Mocker.GetMock<IHttpClient>().Verify(client => client.Get(It.IsAny<HttpRequest>()), Times.Once);
        }

        [Test]
        public void isbn_lookup_does_not_follow_a_second_redirect()
        {
            var urls = new List<string>();
            Mocker.GetMock<IHttpClient>().Setup(client => client.Get(It.IsAny<HttpRequest>()))
                .Returns<HttpRequest>(request =>
                {
                    urls.Add(request.Url.ToString());
                    var location = urls.Count == 1 ? "/books/OL33516062M.json" : "/books/OL2M.json";
                    return new HttpResponse(request, new HttpHeader { { "Location", location } }, """{"key":"/books/OL2M"}""", HttpStatusCode.Found);
                });

            Subject.GetEditionByIsbn("9781442482890").Should().BeNull();

            urls.Should().Equal("https://openlibrary.org/isbn/9781442482890.json", "https://openlibrary.org/books/OL33516062M.json");
        }

        [Test]
        public void isbn_lookup_rejects_multiple_location_headers()
        {
            var headers = new HttpHeader
            {
                { "Location", "/books/OL33516062M.json" },
                { "Location", "/books/OL2M.json" }
            };
            Mocker.GetMock<IHttpClient>().Setup(client => client.Get(It.IsAny<HttpRequest>()))
                .Returns<HttpRequest>(request => new HttpResponse(request, headers, "", HttpStatusCode.Found));

            Subject.GetEditionByIsbn("9781442482890").Should().BeNull();

            Mocker.GetMock<IHttpClient>().Verify(client => client.Get(It.IsAny<HttpRequest>()), Times.Once);
        }

        [Test]
        public void edition_lookup_keeps_automatic_redirects_disabled()
        {
            var headers = new HttpHeader { { "Location", "/books/OL2M.json" } };
            var client = Mocker.GetMock<IHttpClient>();
            client.Setup(http => http.Get(It.IsAny<HttpRequest>()))
                .Returns<HttpRequest>(request => new HttpResponse(request, headers, "", HttpStatusCode.Found));

            Subject.GetEdition("OL33516062M").Should().BeNull();

            client.Verify(http => http.Get(It.Is<HttpRequest>(request => !request.AllowAutoRedirect)), Times.Once);
            client.Verify(http => http.Get(It.IsAny<HttpRequest>()), Times.Once);
        }

        [Test]
        public void isbn_lookup_preserves_the_retry_budget_on_the_validated_edition()
        {
            const string editionUrl = "https://openlibrary.org/books/OL33516062M.json";
            var edition = new HttpResponse(new HttpRequest(editionUrl), new HttpHeader(), """{"key":"/books/OL33516062M"}""");
            var urls = new List<string>();
            Mocker.GetMock<IHttpClient>().Setup(client => client.Get(It.IsAny<HttpRequest>()))
                .Returns<HttpRequest>(request =>
                {
                    urls.Add(request.Url.ToString());
                    return urls.Count switch
                    {
                        1 => new HttpResponse(request, new HttpHeader { { "Location", "/books/OL33516062M.json" } }, "", HttpStatusCode.Found),
                        2 => new HttpResponse(request, new HttpHeader { { "Retry-After", "0" } }, "", HttpStatusCode.TooManyRequests),
                        _ => edition
                    };
                });

            Subject.GetEditionByIsbn("9781442482890").Should().NotBeNull();

            urls.Should().Equal("https://openlibrary.org/isbn/9781442482890.json", editionUrl, editionUrl);
        }

        [Test]
        public void search_should_open_circuit_after_threshold_and_short_circuit_followup_calls()
        {
            Mocker.GetMock<IConfigService>().SetupGet(x => x.MetadataProviderRetryBudget).Returns(0);
            Mocker.GetMock<IConfigService>().SetupGet(x => x.MetadataProviderCircuitBreakerThreshold).Returns(1);
            Mocker.GetMock<IConfigService>().SetupGet(x => x.MetadataProviderCircuitBreakerDurationSeconds).Returns(60);

            var error = new HttpResponse(new HttpRequest("https://openlibrary.org/search.json"), new HttpHeader(), Array.Empty<byte>(), System.Net.HttpStatusCode.InternalServerError);
            Mocker.GetMock<IHttpClient>()
                .Setup(x => x.Get(It.IsAny<HttpRequest>()))
                .Returns(error);

            var first = Subject.Search("circuit", 1);
            var second = Subject.Search("circuit", 1);

            first.Docs.Should().BeEmpty();
            second.Docs.Should().BeEmpty();
            Mocker.GetMock<IHttpClient>().Verify(x => x.Get(It.IsAny<HttpRequest>()), Times.Once);
            ExceptionVerification.IgnoreWarns();
        }

        [TestCase(true, System.Net.HttpStatusCode.TooManyRequests)]
        [TestCase(false, System.Net.HttpStatusCode.TooManyRequests)]
        [TestCase(true, System.Net.HttpStatusCode.OK)]
        [TestCase(false, System.Net.HttpStatusCode.OK)]
        public void author_pagination_should_distinguish_request_errors_and_malformed_data_from_empty_pages(
            bool useWorksEndpoint, System.Net.HttpStatusCode status)
        {
            Mocker.GetMock<IConfigService>().SetupGet(config => config.MetadataProviderRetryBudget).Returns(0);
            var error = new HttpResponse(new HttpRequest("https://openlibrary.org"), new HttpHeader(), "{}", status);
            Mocker.GetMock<IHttpClient>().Setup(client => client.Get(It.IsAny<HttpRequest>())).Returns(error);

            var result = useWorksEndpoint
                ? (object)Subject.GetAuthorWorks("OL1A", 100, 100)
                : Subject.Search("author_key:OL1A", 100, 100);

            result.Should().BeNull();
            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void author_lookup_marks_only_an_explicit_404_as_confirmed_missing()
        {
            var request = new HttpRequest("https://openlibrary.org/authors/OL1A.json");
            Mocker.GetMock<IHttpClient>()
                .SetupSequence(client => client.Get(It.IsAny<HttpRequest>()))
                .Returns(new HttpResponse(request, new HttpHeader(), "", System.Net.HttpStatusCode.OK))
                .Returns(new HttpResponse(request, new HttpHeader(), "", System.Net.HttpStatusCode.ServiceUnavailable))
                .Returns(new HttpResponse(request, new HttpHeader(), "", System.Net.HttpStatusCode.NotFound));

            Subject.GetAuthor("OL1A", true).Should().BeNull();
            Subject.GetAuthor("OL1A", true).Should().BeNull();
            var lookup = () => Subject.GetAuthor("OL1A", true);
            lookup.Should().Throw<AuthorNotFoundException>().Which.IsConfirmedMissing.Should().BeTrue();
            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void get_work_should_deserialize_author_role_when_type_is_string()
        {
            var payload = "{\"key\":\"/works/OL1W\",\"title\":\"Work\",\"authors\":[{\"author\":{\"key\":\"/authors/OL1A\"},\"type\":\"/type/author_role\"}]}";
            var response = new HttpResponse(new HttpRequest("https://openlibrary.org/works/OL1W.json"), new HttpHeader(), payload);

            Mocker.GetMock<IHttpClient>()
                .Setup(x => x.Get(It.IsAny<HttpRequest>()))
                .Returns(response);

            var work = Subject.GetWork("OL1W");

            work.Should().NotBeNull();
            work.Authors.Should().HaveCount(1);
            work.Authors[0].Author.Key.Should().Be("/authors/OL1A");
            work.Authors[0].Type.Key.Should().Be("/type/author_role");
        }

        [Test]
        public void get_work_should_tolerate_malformed_mixed_author_array_entries()
        {
            var payload = "{\"key\":\"/works/OL2W\",\"title\":\"Work\",\"authors\":[{\"author\":{\"key\":\"/authors/OL1A\"},\"type\":\"/type/author_role\"},{\"author\":42,\"type\":[\"/type/author_role\"]},{\"author\":\"/authors/OL2A\",\"type\":{\"key\":\"/type/author_role\"}}]}";
            var response = new HttpResponse(new HttpRequest("https://openlibrary.org/works/OL2W.json"), new HttpHeader(), payload);

            Mocker.GetMock<IHttpClient>()
                .Setup(x => x.Get(It.IsAny<HttpRequest>()))
                .Returns(response);

            var work = Subject.GetWork("OL2W");

            work.Should().NotBeNull();
            work.Authors.Should().HaveCount(3);
            work.Authors[0].Author.Key.Should().Be("/authors/OL1A");
            work.Authors[1].Author.Should().BeNull();
            work.Authors[1].Type.Should().BeNull();
            work.Authors[2].Author.Key.Should().Be("/authors/OL2A");
            work.Authors[2].Type.Key.Should().Be("/type/author_role");
        }

        [Test]
        public void search_should_request_series_enrichment_fields()
        {
            HttpRequest captured = null;

            var response = new HttpResponse(new HttpRequest("https://openlibrary.org/search.json"), new HttpHeader(), "{\"docs\":[]}");
            Mocker.GetMock<IHttpClient>()
                .Setup(x => x.Get(It.IsAny<HttpRequest>()))
                .Callback<HttpRequest>(r => captured = r)
                .Returns(response);

            Subject.Search("tolkien", 5);

            captured.Should().NotBeNull();
            captured.Url.ToString().Should().Contain("series%2Cseries_with_number");
        }

        [Test]
        public void search_should_use_operation_specific_retry_budget_when_configured()
        {
            Mocker.GetMock<IConfigService>().SetupGet(x => x.MetadataProviderRetryBudget).Returns(3);
            Mocker.GetMock<IConfigService>().SetupGet(x => x.OpenLibrarySearchRetryBudget).Returns(0);

            var first = new HttpResponse(new HttpRequest("https://openlibrary.org/search.json"), new HttpHeader { { "Retry-After", "0" } }, Array.Empty<byte>(), System.Net.HttpStatusCode.TooManyRequests);

            Mocker.GetMock<IHttpClient>()
                .Setup(x => x.Get(It.IsAny<HttpRequest>()))
                .Returns(first);

            var result = Subject.Search("retry", 1);

            result.Should().NotBeNull();
            result.Docs.Should().BeEmpty();
            Mocker.GetMock<IHttpClient>().Verify(x => x.Get(It.IsAny<HttpRequest>()), Times.Once());
            ExceptionVerification.IgnoreWarns();
        }
    }
}
