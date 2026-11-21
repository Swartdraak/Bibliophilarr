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
                .Setup(x => x.Get<GoogleBooksSearchResponse>(It.IsAny<HttpRequest>()))
                .Returns<HttpRequest>(request =>
                    new HttpResponse<GoogleBooksSearchResponse>(new HttpResponse(request, new HttpHeader(), "{\"items\":[]}")));

            var provider = Mocker.Resolve<GoogleBooksFallbackSearchProvider>();

            Subject.Search(provider, "Title", "Author").Should().BeEmpty();

            Mocker.GetMock<IRateLimitService>()
                .Verify(x => x.WaitAndPulse("metadata-fallback", "GoogleBooks", It.IsAny<TimeSpan>()), Times.Once());
            Mocker.GetMock<IHttpClient>()
                .Verify(x => x.Get<GoogleBooksSearchResponse>(It.IsAny<HttpRequest>()), Times.Once());
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

        [TestCase("Hardcover", 72096)]
        [TestCase("GoogleBooks", 900)]
        public void should_report_and_skip_until_the_same_retry_after_deadline(string providerName, int expectedCooldownSeconds)
        {
            var provider = new Mock<IBookSearchFallbackProvider>();
            provider.SetupGet(x => x.IsFallbackEnabled).Returns(true);
            provider.SetupGet(x => x.ProviderName).Returns(providerName);

            var request = new HttpRequest("https://example.test");
            var headers = new HttpHeader { ["Retry-After"] = "72096" };
            var response = new HttpResponse(request, headers, string.Empty, HttpStatusCode.TooManyRequests);
            provider.Setup(x => x.Search(It.IsAny<string>(), It.IsAny<string>()))
                .Throws(new TooManyRequestsException(request, response));

            var registry = Mocker.Resolve<IMetadataProviderRegistry>();
            var started = DateTime.UtcNow;
            Subject.Search(provider.Object, "Title", "Author").Should().BeEmpty();
            ExceptionVerification.IgnoreWarns();

            var deadline = registry.GetProvidersHealthStatus()[providerName].CooldownUntilUtc;
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
