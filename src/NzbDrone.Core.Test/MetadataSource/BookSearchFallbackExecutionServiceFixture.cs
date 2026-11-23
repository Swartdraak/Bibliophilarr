using System;
using System.Collections.Generic;
using System.Net;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Http;
using NzbDrone.Common.TPL;
using NzbDrone.Core.Books;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Exceptions;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.MetadataSource.GoogleBooks;
using NzbDrone.Core.MetadataSource.Hardcover;
using NzbDrone.Core.MetadataSource.Inventaire;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.MetadataSource
{
    [TestFixture]
    public class BookSearchFallbackExecutionServiceFixture : CoreTest<BookSearchFallbackExecutionService>
    {
        private MetadataProviderTelemetryService _telemetry;

        [SetUp]
        public void SetUp()
        {
            Mocker.SetConstant<TimeProvider>(TimeProvider.System);
            Mocker.SetConstant<IMetadataProviderRegistry>(new MetadataProviderRegistry(Array.Empty<IMetadataProvider>()));
            _telemetry = new MetadataProviderTelemetryService();
            Mocker.SetConstant<IMetadataProviderTelemetryService>(_telemetry);
        }

        [TestCase("GoogleBooks")]
        [TestCase("Hardcover")]
        [TestCase("Inventaire")]
        public void should_not_reserve_quota_or_issue_requests_when_fallback_is_disabled(string providerName)
        {
            Mocker.GetMock<IConfigService>()
                .SetupGet(x => x.EnableGoogleBooksProvider)
                .Returns(true);

            IBookSearchFallbackProvider provider = providerName switch
            {
                "GoogleBooks" => Mocker.Resolve<GoogleBooksFallbackSearchProvider>(),
                "Hardcover" => Mocker.Resolve<HardcoverFallbackSearchProvider>(),
                _ => Mocker.Resolve<InventaireFallbackSearchProvider>()
            };

            Subject.Search(provider, "Title", "Author").Should().BeEmpty();

            Mocker.GetMock<IRateLimitService>()
                .Verify(x => x.WaitAndPulse("metadata-fallback", providerName, It.IsAny<TimeSpan>()), Times.Never());
            Mocker.GetMock<IHttpClient>().VerifyNoOtherCalls();
            Mocker.GetMock<IProviderTelemetryService>()
                .Verify(x => x.RecordSuccess(providerName, "fallback-search", It.IsAny<double>(), It.IsAny<int>()), Times.Never());
            _telemetry.GetOperationSnapshots().Should().BeEmpty();
        }

        [TestCase(true)]
        [TestCase(false)]
        public void should_publish_actual_inventaire_fallback_results_to_operation_telemetry(bool hasMatch)
        {
            var books = hasMatch ? new List<Book> { new Book { Title = "Fixture Work" } } : new List<Book>();
            var provider = new Mock<IBookSearchFallbackProvider>();
            provider.SetupGet(x => x.IsFallbackEnabled).Returns(true);
            provider.SetupGet(x => x.ProviderName).Returns("Inventaire");
            provider.SetupGet(x => x.RateLimitInfo).Returns(new ProviderRateLimitInfo());
            provider.Setup(x => x.Search("Title", "Author")).Returns(books);

            Subject.Search(provider.Object, "Title", "Author").Should().BeSameAs(books);

            var snapshot = _telemetry.GetOperationSnapshots().Should().ContainSingle().Which;
            snapshot.ProviderName.Should().Be("Inventaire");
            snapshot.OperationName.Should().Be("fallback-search");
            snapshot.Calls.Should().Be(1);
            snapshot.Successes.Should().Be(1);
            snapshot.Failures.Should().Be(0);
            snapshot.TotalResultCount.Should().Be(books.Count);
            snapshot.FallbackHits.Should().Be(books.Count);
            _telemetry.GetSnapshots().Should().ContainSingle().Which.Operations.Should().ContainSingle();
        }

        [Test]
        public void should_distinguish_null_fallback_response_from_empty_results()
        {
            var provider = new Mock<IBookSearchFallbackProvider>();
            provider.SetupGet(x => x.IsFallbackEnabled).Returns(true);
            provider.SetupGet(x => x.ProviderName).Returns("Inventaire");
            provider.SetupGet(x => x.RateLimitInfo).Returns(new ProviderRateLimitInfo());
            provider.Setup(x => x.Search("Title", "Author")).Returns((List<Book>)null);

            Subject.Search(provider.Object, "Title", "Author").Should().BeEmpty();

            var snapshot = _telemetry.GetOperationSnapshots().Should().ContainSingle().Which;
            snapshot.Calls.Should().Be(1);
            snapshot.NullResults.Should().Be(1);
            snapshot.Successes.Should().Be(0);
            snapshot.Failures.Should().Be(0);
            snapshot.FallbackHits.Should().Be(0);
        }

        [Test]
        public void should_run_google_fallback_with_the_primary_provider_disabled()
        {
            Mocker.GetMock<IConfigService>()
                .SetupGet(x => x.EnableGoogleBooksFallback)
                .Returns(true);
            Mocker.GetMock<IHttpClient>()
                .Setup(x => x.Get(It.IsAny<HttpRequest>()))
                .Returns<HttpRequest>(request =>
                    new HttpResponse(request, new HttpHeader(), "{\"items\":[]}"));

            var provider = Mocker.Resolve<GoogleBooksFallbackSearchProvider>();

            Subject.Search(provider, "Title", "Author").Should().BeEmpty();

            Mocker.GetMock<IRateLimitService>()
                .Verify(x => x.WaitAndPulse("metadata-fallback", "GoogleBooks", It.IsAny<TimeSpan>()), Times.Once());
            Mocker.GetMock<IHttpClient>()
                .Verify(x => x.Get(It.IsAny<HttpRequest>()), Times.Once());
        }

        [TestCase(false, "180", 180)]
        [TestCase(true, "180", 180)]
        [TestCase(false, null, 300)]
        [TestCase(true, null, 300)]
        public void should_enforce_google_fallback_rate_limit_before_decoding_the_response(bool html, string retryAfter, int cooldownSeconds)
        {
            const string providerName = "GoogleBooks";
            const string apiKey = "synthetic-private-api-key";
            const string firstTitle = "private-first-candidate";
            const string responseMarker = "private-response-marker";

            var config = Mocker.GetMock<IConfigService>();
            config.SetupGet(x => x.EnableGoogleBooksFallback).Returns(true);
            config.SetupGet(x => x.GoogleBooksApiKey).Returns(apiKey);
            var http = Mocker.GetMock<IHttpClient>();

            HttpResponse RateLimit(HttpRequest request)
            {
                var headers = new HttpHeader { ContentType = html ? "text/html" : "application/json" };
                if (retryAfter != null)
                {
                    headers["Retry-After"] = retryAfter;
                }

                var content = html ? $"<html>{responseMarker}</html>" : $"{{\"error\":{{\"message\":\"{responseMarker}\"}}}}";
                request.SuppressHttpError.Should().BeTrue();
                return new HttpResponse(request, headers, content, HttpStatusCode.TooManyRequests);
            }

            http.Setup(x => x.Get(It.IsAny<HttpRequest>())).Returns<HttpRequest>(RateLimit);
            http.Setup(x => x.Get<GoogleBooksSearchResponse>(It.IsAny<HttpRequest>()))
                .Returns<HttpRequest>(request =>
                {
                    var response = RateLimit(request);
                    if (html)
                    {
                        throw new UnexpectedHtmlContentException(response);
                    }

                    return new HttpResponse<GoogleBooksSearchResponse>(response);
                });

            var provider = Mocker.Resolve<GoogleBooksFallbackSearchProvider>();
            var registry = Mocker.Resolve<IMetadataProviderRegistry>();
            var started = DateTime.UtcNow;

            Subject.Search(provider, firstTitle, "Author").Should().BeEmpty();
            var deadline = registry.GetProvidersHealthStatus()[providerName].CooldownUntilUtc;
            Subject.Search(provider, "second candidate", "Another Author").Should().BeEmpty();

            http.Invocations.Should().ContainSingle("Google must not receive another fallback request during cooldown");
            deadline.Should().BeOnOrAfter(started.AddSeconds(cooldownSeconds));
            deadline.Should().BeOnOrBefore(DateTime.UtcNow.AddSeconds(cooldownSeconds));
            registry.GetProvidersHealthStatus()[providerName].CooldownUntilUtc.Should().Be(deadline);
            http.Verify(x => x.Get(It.IsAny<HttpRequest>()), Times.Once());
            http.VerifyNoOtherCalls();
            var telemetry = _telemetry.GetOperationSnapshots().Should().ContainSingle().Which;
            telemetry.Calls.Should().Be(1);
            telemetry.Failures.Should().Be(1);
            telemetry.Successes.Should().Be(0);
            Mocker.GetMock<IProviderTelemetryService>()
                .Verify(x => x.RecordFailure(providerName, "fallback-search", It.Is<TooManyRequestsException>(failure =>
                    failure.Request == null && failure.Response == null &&
                    !failure.ToString().Contains(apiKey) && !failure.ToString().Contains(firstTitle) &&
                    !failure.ToString().Contains(responseMarker))), Times.Once());
            ExceptionVerification.ExpectedWarns(1);
        }

        [TestCase(true, true, 180)]
        [TestCase(false, false, 180)]
        [TestCase(true, true, 72096)]
        public void should_share_google_cooldown_across_detail_search_and_bibliography(bool throttleFromDetail, bool html, int cooldownSeconds)
        {
            const string bookId = "googlebooks:work:fixture-volume";
            const string authorId = "googlebooks:author:Fixture%20Author";
            var settings = Mocker.GetMock<IConfigService>();
            settings.SetupGet(x => x.EnableGoogleBooksFallback).Returns(true);
            settings.SetupGet(x => x.EnableGoogleBooksProvider).Returns(true);
            var provider = Mocker.Resolve<GoogleBooksFallbackSearchProvider>();
            var registry = Mocker.Resolve<IMetadataProviderRegistry>();
            var otherProvider = new Mock<IMetadataProvider>();
            otherProvider.SetupGet(x => x.ProviderName).Returns("FixtureAlternative");
            otherProvider.SetupGet(x => x.IsEnabled).Returns(true);
            registry.RegisterProvider(provider);
            registry.RegisterProvider(otherProvider.Object);
            var http = Mocker.GetMock<IHttpClient>();
            var headers = new HttpHeader { ContentType = html ? "text/html" : "application/json", ["Retry-After"] = cooldownSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture) };
            var body = html ? "<html>upstream private data</html>" : "{\"error\":{\"message\":\"upstream private data\"}}";

            HttpResponse RateLimit(HttpRequest request) => new HttpResponse(request, headers, body, HttpStatusCode.TooManyRequests);
            http.Setup(x => x.Get(It.IsAny<HttpRequest>())).Returns<HttpRequest>(RateLimit);
            http.Setup(x => x.Get<GoogleBooksVolumeResource>(It.IsAny<HttpRequest>()))
                .Returns<HttpRequest>(request => new HttpResponse<GoogleBooksVolumeResource>(RateLimit(request)));
            http.Setup(x => x.Get<GoogleBooksSearchResponse>(It.IsAny<HttpRequest>()))
                .Returns<HttpRequest>(request => new HttpResponse<GoogleBooksSearchResponse>(RateLimit(request)));

            var started = DateTime.UtcNow;
            if (throttleFromDetail)
            {
                Action initial = () => provider.GetBookInfo(bookId);
                initial.Should().Throw<MetadataProviderUnavailableException>().WithInnerException<TooManyRequestsException>();
                Action heldSearch = () => provider.SearchForNewBook("Held search", "Fixture Author");
                heldSearch.Should().Throw<Exception>();
            }
            else
            {
                Action initial = () => provider.SearchForNewBook("Initial search", "Fixture Author");
                initial.Should().Throw<TooManyRequestsException>();
            }

            var health = registry.GetProvidersHealthStatus()[provider.ProviderName];
            var deadline = health.CooldownUntilUtc;
            deadline.Should().BeOnOrAfter(started.AddSeconds(cooldownSeconds));
            deadline.Should().BeOnOrBefore(DateTime.UtcNow.AddSeconds(cooldownSeconds));
            health.RetryAfterRemainingSeconds.Should().BeInRange(1, cooldownSeconds);
            Action lookupBook = () => provider.GetBookInfo(bookId);
            Action lookupAuthor = () => provider.GetAuthorInfo(authorId);
            lookupBook.Should().Throw<MetadataProviderUnavailableException>();
            lookupAuthor.Should().Throw<MetadataProviderUnavailableException>();
            Action heldIsbn = () => provider.SearchByIsbn("9780441013593");
            heldIsbn.Should().Throw<Exception>();
            Subject.Search(provider, "Held fallback", "Fixture Author").Should().BeEmpty();

            http.Invocations.Should().ContainSingle();
            registry.GetProvidersHealthStatus()[provider.ProviderName].CooldownUntilUtc.Should().Be(deadline);
            registry.GetProviders().Should().Contain(provider).And.Contain(otherProvider.Object);
            Mocker.GetMock<IRateLimitService>()
                .Verify(x => x.WaitAndPulse("metadata-fallback", "GoogleBooks", It.IsAny<TimeSpan>()), Times.Never());
            _telemetry.GetOperationSnapshots().Should().BeEmpty();
        }

        [Test]
        public void should_recheck_the_google_cooldown_after_waiting_for_fallback_admission()
        {
            const string providerName = "GoogleBooks";
            const string bookId = "googlebooks:work:fixture-volume";
            Mocker.GetMock<IConfigService>().SetupGet(x => x.EnableGoogleBooksFallback).Returns(true);
            var provider = Mocker.Resolve<GoogleBooksFallbackSearchProvider>();
            var http = Mocker.GetMock<IHttpClient>();
            var headers = new HttpHeader { ContentType = "application/json", ["Retry-After"] = "180" };
            http.Setup(x => x.Get(It.IsAny<HttpRequest>()))
                .Returns<HttpRequest>(request => new HttpResponse(request, headers, "{}", HttpStatusCode.TooManyRequests));
            http.Setup(x => x.Get<GoogleBooksVolumeResource>(It.IsAny<HttpRequest>()))
                .Returns<HttpRequest>(request => new HttpResponse<GoogleBooksVolumeResource>(new HttpResponse(
                    request, headers, "{}", HttpStatusCode.TooManyRequests)));
            Mocker.GetMock<IRateLimitService>()
                .Setup(x => x.WaitAndPulse("metadata-fallback", providerName, It.IsAny<TimeSpan>()))
                .Callback(() =>
                {
                    Action lookup = () => provider.GetBookInfo(bookId);
                    lookup.Should().Throw<MetadataProviderUnavailableException>();
                });

            Subject.Search(provider, "Waiting fallback", "Fixture Author").Should().BeEmpty();

            http.Invocations.Should().ContainSingle();
            _telemetry.GetOperationSnapshots().Should().BeEmpty();
        }

        [Test]
        public void should_fail_a_google_bibliography_throttled_after_a_page_without_dispatching_followup()
        {
            const string authorId = "googlebooks:author:Fixture%20Author";
            const string bookId = "googlebooks:work:fixture-volume";
            var provider = Mocker.Resolve<GoogleBooksFallbackSearchProvider>();
            var http = Mocker.GetMock<IHttpClient>();
            var headers = new HttpHeader { ContentType = "application/json", ["Retry-After"] = "180" };

            HttpResponse Page(HttpRequest request) => request.Url.Query.Contains("startIndex=0")
                ? new HttpResponse(request, headers, "{\"totalItems\":2,\"items\":[{\"id\":\"first\"}]}")
                : new HttpResponse(request, headers, "{}", HttpStatusCode.TooManyRequests);
            http.Setup(x => x.Get(It.IsAny<HttpRequest>())).Returns<HttpRequest>(Page);
            http.Setup(x => x.Get<GoogleBooksSearchResponse>(It.IsAny<HttpRequest>()))
                .Returns<HttpRequest>(request => new HttpResponse<GoogleBooksSearchResponse>(Page(request)));

            Action lookup = () => provider.GetAuthorInfo(authorId);
            lookup.Should().Throw<MetadataProviderUnavailableException>().WithInnerException<TooManyRequestsException>();
            Action lookupBook = () => provider.GetBookInfo(bookId);
            lookupBook.Should().Throw<MetadataProviderUnavailableException>();
            http.Invocations.Should().HaveCount(2);
        }

        [TestCase("search-for-new-book")]
        [TestCase("search-by-isbn")]
        [TestCase("search-by-asin")]
        [TestCase("search-by-external-id")]
        public void should_record_only_the_actual_google_rate_limit_and_try_an_alternative(string operation)
        {
            var google = Mocker.Resolve<GoogleBooksFallbackSearchProvider>();
            Mocker.GetMock<IConfigService>().SetupGet(x => x.EnableGoogleBooksProvider).Returns(true);
            var other = new Mock<IMetadataProvider>();
            other.SetupGet(x => x.ProviderName).Returns("FixtureAlternative");
            other.SetupGet(x => x.Priority).Returns(99);
            other.SetupGet(x => x.IsEnabled).Returns(true);
            other.SetupGet(x => x.SupportsIsbnLookup).Returns(true);
            other.SetupGet(x => x.SupportsBookSearch).Returns(true);
            var search = other.As<ISearchForNewBook>();
            var result = new List<Book> { new Book { Title = "Alternative result" } };
            search.Setup(x => x.SearchForNewBook(It.IsAny<string>(), null, true)).Returns(result);
            search.Setup(x => x.SearchByIsbn(It.IsAny<string>())).Returns(result);
            search.Setup(x => x.SearchByAsin(It.IsAny<string>())).Returns(result);
            search.Setup(x => x.SearchByExternalId("googlebooks", It.IsAny<string>())).Returns(result);

            var registry = Mocker.Resolve<IMetadataProviderRegistry>();
            registry.RegisterProvider(google);
            registry.RegisterProvider(other.Object);
            var http = Mocker.GetMock<IHttpClient>();
            var headers = new HttpHeader { ContentType = "application/json", ["Retry-After"] = "180" };
            HttpResponse RateLimit(HttpRequest request) => new HttpResponse(request, headers, """{"error":{"message":"private body"}}""", HttpStatusCode.TooManyRequests);
            http.Setup(x => x.Get(It.IsAny<HttpRequest>())).Returns<HttpRequest>(RateLimit);
            http.Setup(x => x.Get<GoogleBooksSearchResponse>(It.IsAny<HttpRequest>()))
                .Returns<HttpRequest>(request => new HttpResponse<GoogleBooksSearchResponse>(RateLimit(request)));
            http.Setup(x => x.Get<GoogleBooksVolumeResource>(It.IsAny<HttpRequest>()))
                .Returns<HttpRequest>(request => new HttpResponse<GoogleBooksVolumeResource>(RateLimit(request)));

            var orchestrator = Mocker.Resolve<MetadataProviderOrchestrator>();
            List<Book> Lookup(string query) => operation switch
            {
                "search-for-new-book" => orchestrator.SearchForNewBook(query, null),
                "search-by-isbn" => orchestrator.SearchByIsbn(query),
                "search-by-asin" => orchestrator.SearchByAsin(query),
                _ => orchestrator.SearchByExternalId("googlebooks", query)
            };

            Lookup("first candidate").Should().BeSameAs(result);
            var deadline = registry.GetProvidersHealthStatus()[google.ProviderName].CooldownUntilUtc;
            Lookup("distinct candidate").Should().BeSameAs(result);

            var googleOperation = _telemetry.GetOperationSnapshots().Should()
                .ContainSingle(x => x.ProviderName == google.ProviderName).Which;
            googleOperation.OperationName.Should().Be(operation);
            googleOperation.Calls.Should().Be(1);
            googleOperation.Failures.Should().Be(1);
            googleOperation.Successes.Should().Be(0);
            googleOperation.NullResults.Should().Be(0);
            _telemetry.GetOperationSnapshots().Should().ContainSingle(x => x.ProviderName == "FixtureAlternative" && x.Calls == 2);
            http.Invocations.Should().ContainSingle();
            deadline.Should().BeAfter(DateTime.UtcNow);
            registry.GetProvidersHealthStatus()[google.ProviderName].CooldownUntilUtc.Should().Be(deadline);

            Action book = () => orchestrator.GetBookInfo("googlebooks:work:held-volume");
            Action author = () => orchestrator.GetAuthorInfo("googlebooks:author:Held%20Author");
            book.Should().Throw<MetadataProviderUnavailableException>();
            author.Should().Throw<MetadataProviderUnavailableException>();
            http.Invocations.Should().ContainSingle();
            _telemetry.GetOperationSnapshots().Should().ContainSingle(x => x.ProviderName == google.ProviderName);
            ExceptionVerification.ExpectedWarns(1);
        }

        [TestCase("application/json", "{\"error\":{\"message\":\"private-5xx-body\"}}")]
        [TestCase("TeXt/HtMl", "<html>private-5xx-body</html>")]
        public void should_apply_the_existing_server_error_hold_to_google_fallback(string contentType, string body)
        {
            const string providerName = "GoogleBooks";
            const string apiKey = "fixture-private-api-key";
            const string firstTitle = "first-private-candidate";
            var config = Mocker.GetMock<IConfigService>();
            config.SetupGet(x => x.EnableGoogleBooksFallback).Returns(true);
            config.SetupGet(x => x.GoogleBooksApiKey).Returns(apiKey);
            var http = Mocker.GetMock<IHttpClient>();
            var headers = new HttpHeader { ContentType = contentType, ["Retry-After"] = "3600" };
            http.Setup(x => x.Get(It.IsAny<HttpRequest>()))
                .Returns<HttpRequest>(request => new HttpResponse(request, headers, body, HttpStatusCode.BadGateway));

            var provider = Mocker.Resolve<GoogleBooksFallbackSearchProvider>();
            var registry = Mocker.Resolve<IMetadataProviderRegistry>();
            var started = DateTime.UtcNow;
            Subject.Search(provider, firstTitle, "First fixture").Should().BeEmpty();
            var deadline = registry.GetProvidersHealthStatus()[providerName].CooldownUntilUtc;
            Subject.Search(provider, "second candidate", "Second fixture").Should().BeEmpty();
            Subject.Search(provider, "third candidate", "Third fixture").Should().BeEmpty();

            http.Invocations.Should().ContainSingle();
            deadline.Should().BeOnOrAfter(started.AddMinutes(2));
            deadline.Should().BeOnOrBefore(DateTime.UtcNow.AddMinutes(2));
            registry.GetProvidersHealthStatus()[providerName].CooldownUntilUtc.Should().Be(deadline);
            var telemetry = _telemetry.GetOperationSnapshots().Should().ContainSingle().Which;
            telemetry.Calls.Should().Be(1);
            telemetry.Failures.Should().Be(1);
            telemetry.Successes.Should().Be(0);
            Mocker.GetMock<IProviderTelemetryService>()
                .Verify(x => x.RecordFailure(providerName, "fallback-search", It.Is<HttpException>(failure =>
                    failure.Request == null && failure.Response == null && failure.Message.Contains("502") &&
                    !failure.ToString().Contains(apiKey) && !failure.ToString().Contains(firstTitle) &&
                    !failure.ToString().Contains("private-5xx-body"))), Times.Once());

            http.Setup(x => x.Get(It.IsAny<HttpRequest>()))
                .Returns<HttpRequest>(request => new HttpResponse(request, headers, "{}", HttpStatusCode.NotFound));
            Action genuineMissingVolume = () => provider.GetBookInfo("googlebooks:work:missing-volume");
            genuineMissingVolume.Should().Throw<BookNotFoundException>();
            http.Verify(x => x.Get(It.IsAny<HttpRequest>()), Times.Exactly(2));
        }

        [Test]
        public void should_report_google_server_failure_and_continue_an_interactive_search()
        {
            var config = Mocker.GetMock<IConfigService>();
            config.SetupGet(x => x.EnableGoogleBooksProvider).Returns(true);
            var google = Mocker.Resolve<GoogleBooksFallbackSearchProvider>();
            var alternative = new Mock<IMetadataProvider>();
            alternative.SetupGet(x => x.ProviderName).Returns("FixtureAlternative");
            alternative.SetupGet(x => x.Priority).Returns(99);
            alternative.SetupGet(x => x.IsEnabled).Returns(true);
            alternative.SetupGet(x => x.SupportsBookSearch).Returns(true);
            var result = new List<Book> { new Book { Title = "Alternative match" } };
            alternative.As<ISearchForNewBook>().Setup(x => x.SearchForNewBook("fixture", null, true)).Returns(result);

            var registry = Mocker.Resolve<IMetadataProviderRegistry>();
            registry.RegisterProvider(google);
            registry.RegisterProvider(alternative.Object);
            var http = Mocker.GetMock<IHttpClient>();
            var headers = new HttpHeader { ContentType = "text/html" };
            http.Setup(x => x.Get(It.IsAny<HttpRequest>()))
                .Returns<HttpRequest>(request => new HttpResponse(request, headers, "<html>private body</html>", HttpStatusCode.ServiceUnavailable));

            var orchestrator = Mocker.Resolve<MetadataProviderOrchestrator>();
            orchestrator.SearchForNewBook("fixture", null).Should().BeSameAs(result);

            var failure = _telemetry.GetOperationSnapshots().Should().ContainSingle(x => x.ProviderName == google.ProviderName).Which;
            failure.OperationName.Should().Be("search-for-new-book");
            failure.Calls.Should().Be(1);
            failure.Failures.Should().Be(1);
            failure.Successes.Should().Be(0);
            _telemetry.GetOperationSnapshots().Should().ContainSingle(x => x.ProviderName == "FixtureAlternative" && x.Successes == 1);
            http.Invocations.Should().ContainSingle();
            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void should_apply_retry_after_cooldown_on_rate_limit()
        {
            var provider = new Mock<IBookSearchFallbackProvider>();
            provider.SetupGet(x => x.IsFallbackEnabled).Returns(true);
            provider.SetupGet(x => x.ProviderName).Returns("GoogleBooks");
            provider.SetupGet(x => x.RateLimitInfo).Returns(new ProviderRateLimitInfo());

            var request = new HttpRequest("https://example.test");
            var headers = new HttpHeader();
            headers["Retry-After"] = "120";
            var response = new HttpResponse(request, headers, string.Empty, (HttpStatusCode)429);

            provider.Setup(x => x.Search(It.IsAny<string>(), It.IsAny<string>()))
                .Throws(new TooManyRequestsException(request, response));

            Subject.Search(provider.Object, "Title", "Author").Should().BeEmpty();

            ExceptionVerification.IgnoreWarns();
            Subject.Search(provider.Object, "Title", "Author").Should().BeEmpty();

            provider.Verify(x => x.Search(It.IsAny<string>(), It.IsAny<string>()), Times.Once());

            var telemetry = _telemetry.GetOperationSnapshots().Should().ContainSingle().Which;
            telemetry.Calls.Should().Be(1);
            telemetry.Failures.Should().Be(1);
            telemetry.Successes.Should().Be(0);
            telemetry.FallbackHits.Should().Be(0);
        }

        [Test]
        public void should_not_attempt_a_request_when_cooldown_starts_during_the_rate_wait()
        {
            const string providerName = "Inventaire";
            var provider = new Mock<IBookSearchFallbackProvider>();
            provider.SetupGet(x => x.IsFallbackEnabled).Returns(true);
            provider.SetupGet(x => x.ProviderName).Returns(providerName);
            provider.SetupGet(x => x.RateLimitInfo)
                .Returns(new ProviderRateLimitInfo { MaxRequests = 40, TimeWindow = TimeSpan.FromMinutes(1) });

            var request = new HttpRequest("https://example.test");
            var headers = new HttpHeader { ["Retry-After"] = "120" };
            var response = new HttpResponse(request, headers, string.Empty, HttpStatusCode.TooManyRequests);
            provider.Setup(x => x.Search("Throttled Title", "Author")).Throws(new TooManyRequestsException(request, response));
            provider.Setup(x => x.Search("Waiting Title", "Author")).Returns(new List<Book>());

            var waits = 0;
            var limiter = Mocker.GetMock<IRateLimitService>();
            limiter.Setup(x => x.WaitAndPulse("metadata-fallback", providerName, It.IsAny<TimeSpan>()))
                .Callback(() =>
                {
                    // Deterministically complete the other search while this one is still in its rate wait.
                    if (++waits == 1)
                    {
                        Subject.Search(provider.Object, "Throttled Title", "Author").Should().BeEmpty();
                    }
                });

            Subject.Search(provider.Object, "Waiting Title", "Author").Should().BeEmpty();

            ExceptionVerification.IgnoreWarns();
            limiter.Verify(x => x.WaitAndPulse("metadata-fallback", providerName, It.IsAny<TimeSpan>()), Times.Exactly(2));
            provider.Verify(x => x.Search("Throttled Title", "Author"), Times.Once());
            provider.Verify(x => x.Search("Waiting Title", "Author"), Times.Never());
            var health = Mocker.Resolve<IMetadataProviderRegistry>().GetProvidersHealthStatus()[providerName];
            health.RateLimitWindowRequests.Should().Be(1);
            health.CooldownUntilUtc.Should().BeAfter(DateTime.UtcNow);
            var operation = _telemetry.GetOperationSnapshots().Should().ContainSingle().Which;
            operation.Calls.Should().Be(1);
            operation.Failures.Should().Be(1);
        }

        [Test]
        public void should_keep_a_cooldown_set_while_a_successful_request_was_in_flight()
        {
            const string providerName = "Inventaire";
            var provider = new Mock<IBookSearchFallbackProvider>();
            provider.SetupGet(x => x.IsFallbackEnabled).Returns(true);
            provider.SetupGet(x => x.ProviderName).Returns(providerName);
            provider.SetupGet(x => x.RateLimitInfo)
                .Returns(new ProviderRateLimitInfo { MaxRequests = 40, TimeWindow = TimeSpan.FromMinutes(1) });

            var request = new HttpRequest("https://example.test");
            var headers = new HttpHeader { ["Retry-After"] = "120" };
            var response = new HttpResponse(request, headers, string.Empty, HttpStatusCode.TooManyRequests);
            provider.Setup(x => x.Search("Throttled Title", "Author")).Throws(new TooManyRequestsException(request, response));

            var registry = Mocker.Resolve<IMetadataProviderRegistry>();
            var candidate = new Book { Title = "In Flight Title" };
            DateTime? deadline = null;
            provider.Setup(x => x.Search("In Flight Title", "Author")).Returns(() =>
            {
                // The second request completes while the first is already executing.
                Subject.Search(provider.Object, "Throttled Title", "Author").Should().BeEmpty();
                deadline = registry.GetProvidersHealthStatus()[providerName].CooldownUntilUtc;
                return new List<Book> { candidate };
            });

            var results = Subject.Search(provider.Object, "In Flight Title", "Author");
            var healthAfterSuccess = registry.GetProvidersHealthStatus()[providerName];
            var deadlineAfterSuccess = healthAfterSuccess.CooldownUntilUtc;
            var requestsAfterSuccess = healthAfterSuccess.RateLimitWindowRequests;
            var remainingAfterSuccess = healthAfterSuccess.RateLimitRemaining;
            Subject.Search(provider.Object, "Later Title", "Author").Should().BeEmpty();

            ExceptionVerification.ExpectedWarns(1);
            results.Should().ContainSingle().Which.Should().BeSameAs(candidate);
            deadline.Should().BeAfter(DateTime.UtcNow);
            provider.Verify(x => x.Search("Later Title", "Author"), Times.Never());
            deadlineAfterSuccess.Should().Be(deadline);
            requestsAfterSuccess.Should().Be(2);
            remainingAfterSuccess.Should().Be(38);
            var telemetry = _telemetry.GetOperationSnapshots().Should().ContainSingle().Which;
            telemetry.Calls.Should().Be(2);
            telemetry.Successes.Should().Be(1);
            telemetry.Failures.Should().Be(1);
            telemetry.FallbackHits.Should().Be(1);
        }

        [Test]
        public void should_back_off_when_provider_health_is_degraded()
        {
            var registry = new MetadataProviderRegistry(Array.Empty<IMetadataProvider>());
            registry.UpdateProviderHealth("GoogleBooks", new ProviderHealthStatus
            {
                Health = ProviderHealth.Degraded,
                LastFailure = DateTime.UtcNow,
                LastChecked = DateTime.UtcNow
            });

            Mocker.SetConstant<IMetadataProviderRegistry>(registry);

            var provider = new Mock<IBookSearchFallbackProvider>();
            provider.SetupGet(x => x.IsFallbackEnabled).Returns(true);
            provider.SetupGet(x => x.ProviderName).Returns("GoogleBooks");
            provider.SetupGet(x => x.RateLimitInfo).Returns(new ProviderRateLimitInfo { MaxRequests = 60, TimeWindow = TimeSpan.FromMinutes(1) });
            provider.Setup(x => x.Search(It.IsAny<string>(), It.IsAny<string>())).Returns(new List<Book>());

            Subject.Search(provider.Object, "Title", "Author");

            Mocker.GetMock<IRateLimitService>()
                .Verify(x => x.WaitAndPulse("metadata-fallback", "GoogleBooks", It.Is<TimeSpan>(s => s >= TimeSpan.FromSeconds(15))), Times.Once());
        }

        [Test]
        public void should_apply_server_error_cooldown_on_hardcover_request_timeout()
        {
            var provider = new Mock<IBookSearchFallbackProvider>();
            provider.SetupGet(x => x.IsFallbackEnabled).Returns(true);
            provider.SetupGet(x => x.ProviderName).Returns("Hardcover");
            provider.SetupGet(x => x.RateLimitInfo).Returns(new ProviderRateLimitInfo());

            var request = new HttpRequest("https://api.hardcover.app/v1/graphql");
            var response = new HttpResponse(request, new HttpHeader(), string.Empty, HttpStatusCode.RequestTimeout);
            provider.Setup(x => x.Search(It.IsAny<string>(), It.IsAny<string>()))
                .Throws(new HttpException(request, response));

            Subject.Search(provider.Object, "Dune", "Herbert").Should().BeEmpty();
            Subject.Search(provider.Object, "Dune", "Herbert").Should().BeEmpty();

            provider.Verify(x => x.Search(It.IsAny<string>(), It.IsAny<string>()), Times.Once());
            Mocker.GetMock<IProviderTelemetryService>()
                .Verify(x => x.RecordTimeout("Hardcover", "fallback-search"), Times.Once());
        }

        [Test]
        public void should_apply_server_error_cooldown_on_hardcover_5xx_error()
        {
            var provider = new Mock<IBookSearchFallbackProvider>();
            provider.SetupGet(x => x.IsFallbackEnabled).Returns(true);
            provider.SetupGet(x => x.ProviderName).Returns("Hardcover");
            provider.SetupGet(x => x.RateLimitInfo).Returns(new ProviderRateLimitInfo());

            var request = new HttpRequest("https://api.hardcover.app/v1/graphql");
            var response = new HttpResponse(request, new HttpHeader(), string.Empty, HttpStatusCode.ServiceUnavailable);
            provider.Setup(x => x.Search(It.IsAny<string>(), It.IsAny<string>()))
                .Throws(new HttpException(request, response));

            Subject.Search(provider.Object, "Dune", "Herbert").Should().BeEmpty();
            Subject.Search(provider.Object, "Dune", "Herbert").Should().BeEmpty();

            provider.Verify(x => x.Search(It.IsAny<string>(), It.IsAny<string>()), Times.Once());
            Mocker.GetMock<IProviderTelemetryService>()
                .Verify(x => x.RecordFailure("Hardcover", "fallback-search", It.IsAny<Exception>()), Times.Once());
        }

        [Test]
        public void should_record_telemetry_on_hardcover_rate_limit_429()
        {
            var provider = new Mock<IBookSearchFallbackProvider>();
            provider.SetupGet(x => x.IsFallbackEnabled).Returns(true);
            provider.SetupGet(x => x.ProviderName).Returns("Hardcover");
            provider.SetupGet(x => x.RateLimitInfo).Returns(new ProviderRateLimitInfo());

            var request = new HttpRequest("https://api.hardcover.app/v1/graphql");
            var headers = new HttpHeader();
            headers["Retry-After"] = "60";
            var response = new HttpResponse(request, headers, string.Empty, (HttpStatusCode)429);
            provider.Setup(x => x.Search(It.IsAny<string>(), It.IsAny<string>()))
                .Throws(new TooManyRequestsException(request, response));

            Subject.Search(provider.Object, "Dune", "Herbert").Should().BeEmpty();

            ExceptionVerification.IgnoreWarns();

            Mocker.GetMock<IProviderTelemetryService>()
                .Verify(x => x.RecordFailure("Hardcover", "fallback-search", It.IsAny<Exception>()), Times.Once());
        }

        [Test]
        public void should_record_empty_result_telemetry_on_hardcover_no_matches()
        {
            var provider = new Mock<IBookSearchFallbackProvider>();
            provider.SetupGet(x => x.IsFallbackEnabled).Returns(true);
            provider.SetupGet(x => x.ProviderName).Returns("Hardcover");
            provider.SetupGet(x => x.RateLimitInfo).Returns(new ProviderRateLimitInfo());
            provider.Setup(x => x.Search(It.IsAny<string>(), It.IsAny<string>())).Returns(new List<Book>());

            Subject.Search(provider.Object, "Obscure Title", "Unknown Author").Should().BeEmpty();

            Mocker.GetMock<IProviderTelemetryService>()
                .Verify(x => x.RecordSuccess("Hardcover", "fallback-search", It.IsAny<double>(), 0), Times.Once());
        }

        [Test]
        public void should_expose_retry_after_remaining_when_provider_is_rate_limited()
        {
            var provider = new Mock<IBookSearchFallbackProvider>();
            provider.SetupGet(x => x.IsFallbackEnabled).Returns(true);
            provider.SetupGet(x => x.ProviderName).Returns("Hardcover");
            provider.SetupGet(x => x.RateLimitInfo).Returns(new ProviderRateLimitInfo { MaxRequests = 10, TimeWindow = TimeSpan.FromMinutes(1) });

            var request = new HttpRequest("https://api.hardcover.app/v1/graphql");
            var headers = new HttpHeader();
            headers["Retry-After"] = "75";
            var response = new HttpResponse(request, headers, string.Empty, (HttpStatusCode)429);
            provider.Setup(x => x.Search(It.IsAny<string>(), It.IsAny<string>()))
                .Throws(new TooManyRequestsException(request, response));

            Subject.Search(provider.Object, "Dune", "Herbert").Should().BeEmpty();

            ExceptionVerification.IgnoreWarns();

            var health = Mocker.Resolve<IMetadataProviderRegistry>().GetProvidersHealthStatus()["Hardcover"];
            health.RetryAfterRemainingSeconds.Should().BeGreaterThan(0);
            health.CooldownUntilUtc.Should().NotBeNull();
            health.RateLimitWindowLimit.Should().Be(10);
        }

        [Test]
        public void should_mark_rate_limit_as_near_ceiling_when_usage_crosses_threshold()
        {
            var provider = new Mock<IBookSearchFallbackProvider>();
            provider.SetupGet(x => x.IsFallbackEnabled).Returns(true);
            provider.SetupGet(x => x.ProviderName).Returns("Hardcover");
            provider.SetupGet(x => x.RateLimitInfo).Returns(new ProviderRateLimitInfo { MaxRequests = 10, TimeWindow = TimeSpan.FromMinutes(1) });
            provider.Setup(x => x.Search(It.IsAny<string>(), It.IsAny<string>())).Returns(new List<Book>());

            for (var i = 0; i < 9; i++)
            {
                Subject.Search(provider.Object, "Dune", "Herbert").Should().BeEmpty();
            }

            var health = Mocker.Resolve<IMetadataProviderRegistry>().GetProvidersHealthStatus()["Hardcover"];
            health.RateLimitWindowRequests.Should().BeGreaterThanOrEqualTo(9);
            health.RateLimitWindowLimit.Should().Be(10);
            health.IsRateLimitNearCeiling.Should().BeTrue();
            health.RateLimitUsageRatio.Should().BeGreaterThanOrEqualTo(0.85);

            health.RateLimitRemaining.Should().BeLessThanOrEqualTo(1);
        }

        [TestCase("Hardcover", "72096", 72096)]
        [TestCase("GoogleBooks", "72096", 72096)]
        [TestCase("Inventaire", "72096", 72096)]
        [TestCase("Inventaire", "2147483647", int.MaxValue)]
        [TestCase("Inventaire", "Sat, 01 Jan 2400 00:00:00 GMT", int.MaxValue)]
        [TestCase("Inventaire", "Sat, 01 Jan 2000 00:00:00 GMT", 300)]
        [TestCase("Inventaire", null, 300)]
        [TestCase("Inventaire", "invalid", 300)]
        [TestCase("Inventaire", "0", 300)]
        [TestCase("Inventaire", "1", 30)]
        public void should_report_and_skip_until_the_same_retry_after_deadline(string providerName, string retryAfter, int expectedCooldownSeconds)
        {
            var provider = new Mock<IBookSearchFallbackProvider>();
            provider.SetupGet(x => x.IsFallbackEnabled).Returns(true);
            provider.SetupGet(x => x.ProviderName).Returns(providerName);

            var request = new HttpRequest("https://example.test");
            var headers = new HttpHeader();
            if (retryAfter != null)
            {
                headers["Retry-After"] = retryAfter;
            }

            var response = new HttpResponse(request, headers, string.Empty, HttpStatusCode.TooManyRequests);
            provider.Setup(x => x.Search(It.IsAny<string>(), It.IsAny<string>()))
                .Throws(new TooManyRequestsException(request, response));

            var registry = Mocker.Resolve<IMetadataProviderRegistry>();
            var started = DateTime.UtcNow;
            Subject.Search(provider.Object, "Title", "Author").Should().BeEmpty();
            ExceptionVerification.IgnoreWarns();

            var health = registry.GetProvidersHealthStatus()[providerName];
            health.RetryAfterRemainingSeconds.Should().BeInRange(1, expectedCooldownSeconds);
            var deadline = health.CooldownUntilUtc;
            deadline.Should().BeOnOrAfter(started.AddSeconds(expectedCooldownSeconds));
            deadline.Should().BeOnOrBefore(DateTime.UtcNow.AddSeconds(expectedCooldownSeconds));

            Subject.Search(provider.Object, "Title", "Author").Should().BeEmpty();

            registry.GetProvidersHealthStatus()[providerName].CooldownUntilUtc.Should().Be(deadline);
            provider.Verify(x => x.Search(It.IsAny<string>(), It.IsAny<string>()), Times.Once());
            Mocker.GetMock<IRateLimitService>()
                .Verify(x => x.WaitAndPulse("metadata-fallback", providerName, It.IsAny<TimeSpan>()), Times.Once());
        }
    }
}
