using System;
using System.Collections.Generic;
using System.Net;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Http;
using NzbDrone.Common.TPL;
using NzbDrone.Core.Books;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.MetadataSource
{
    [TestFixture]
    public class BookSearchFallbackExecutionServiceFixture : CoreTest<BookSearchFallbackExecutionService>
    {
        [SetUp]
        public void SetUp()
        {
            Mocker.SetConstant<IMetadataProviderRegistry>(new MetadataProviderRegistry(Array.Empty<IMetadataProvider>()));
        }

        [Test]
        public void should_apply_retry_after_cooldown_on_rate_limit()
        {
            var provider = new Mock<IBookSearchFallbackProvider>();
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
    }
}
