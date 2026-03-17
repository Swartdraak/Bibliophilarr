using System;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Http;
using NzbDrone.Core.Configuration;
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
    }
}
