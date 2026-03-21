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
    }
}
