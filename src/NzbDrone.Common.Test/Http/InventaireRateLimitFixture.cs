using System;
using System.Globalization;
using System.Net;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NLog;
using NUnit.Framework;
using NzbDrone.Common.Cache;
using NzbDrone.Common.Http;
using NzbDrone.Common.Http.Dispatchers;
using NzbDrone.Common.TPL;
using NzbDrone.Test.Common;

namespace NzbDrone.Common.Test.Http
{
    [TestFixture]
    public class InventaireRateLimitFixture : TestBase
    {
        [TestCase("/api/search", "/api/entities/serie-parts")]
        [TestCase("/api/entities/by-uris", "/api/search")]
        [TestCase("/api/entities/reverse-claims", "/api/entities/by-uris")]
        public void should_share_a_real_provider_hold_across_client_instances_and_every_supported_route(string first, string second)
        {
            var until = DateTimeOffset.UtcNow.AddHours(8);
            var cache = new CacheManager();
            var headers = new HttpHeader { ["Retry-After"] = until.ToString("R", CultureInfo.InvariantCulture) };
            Mocker.GetMock<IHttpDispatcher>()
                .Setup(dispatcher => dispatcher.GetResponseAsync(It.IsAny<HttpRequest>(), It.IsAny<CookieContainer>()))
                .ReturnsAsync((HttpRequest request, CookieContainer _) =>
                    new HttpResponse(request, headers, "Fixture body", HttpStatusCode.TooManyRequests));

            var upstream = Assert.ThrowsAsync<TooManyRequestsException>(() => Client(cache).GetAsync(Request(first)));
            var held = Assert.ThrowsAsync<TooManyRequestsException>(() => Client(cache).GetAsync(Request(second)));

            upstream.IsLocallySuppressed.Should().BeFalse();
            held.IsLocallySuppressed.Should().BeTrue();
            held.RetryAfter.TotalHours.Should().BeInRange(7.99, 8.01);
            Mocker.GetMock<IHttpDispatcher>().Verify(
                dispatcher => dispatcher.GetResponseAsync(It.IsAny<HttpRequest>(), It.IsAny<CookieContainer>()), Times.Once());
        }

        [Test]
        public async Task should_check_the_shared_hold_after_a_competing_series_request_finishes()
        {
            var cache = new CacheManager();
            var sent = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var reply = new TaskCompletionSource<HttpResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
            var first = Request("/api/entities/serie-parts");
            Mocker.GetMock<IHttpDispatcher>()
                .Setup(dispatcher => dispatcher.GetResponseAsync(It.IsAny<HttpRequest>(), It.IsAny<CookieContainer>()))
                .Returns(() =>
                {
                    sent.TrySetResult();
                    return reply.Task;
                });

            var upstream = Client(cache).GetAsync(first);
            await sent.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var queued = Client(cache).GetAsync(Request("/api/search"));
            var headers = new HttpHeader { ["Retry-After"] = "3600" };
            reply.SetResult(new HttpResponse(first, headers, "Fixture", HttpStatusCode.TooManyRequests));

            Assert.ThrowsAsync<TooManyRequestsException>(async () => await upstream).IsLocallySuppressed.Should().BeFalse();
            Assert.ThrowsAsync<TooManyRequestsException>(async () => await queued).IsLocallySuppressed.Should().BeTrue();
            Mocker.GetMock<IHttpDispatcher>().Verify(
                dispatcher => dispatcher.GetResponseAsync(It.IsAny<HttpRequest>(), It.IsAny<CookieContainer>()), Times.Once());
        }

        [Test]
        public async Task should_apply_one_actual_wire_budget_to_all_routes_without_counting_unrelated_origins()
        {
            var cache = new CacheManager();
            Mocker.GetMock<IHttpDispatcher>()
                .Setup(dispatcher => dispatcher.GetResponseAsync(It.IsAny<HttpRequest>(), It.IsAny<CookieContainer>()))
                .ReturnsAsync((HttpRequest request, CookieContainer _) => new HttpResponse(request, new HttpHeader(), "{}"));
            var client = Client(cache);
            for (var index = 0; index < 40; index++)
            {
                var path = index % 2 == 0 ? "/api/search" : "/api/entities/serie-parts";
                (await client.GetAsync(Request(path))).StatusCode.Should().Be(HttpStatusCode.OK);
            }

            Assert.ThrowsAsync<TooManyRequestsException>(() => client.GetAsync(Request("/api/entities/by-uris")))
                .IsLocallySuppressed.Should().BeTrue();
            (await client.GetAsync(new HttpRequest("https://elsewhere.invalid/api/search"))).StatusCode.Should().Be(HttpStatusCode.OK);
            Mocker.GetMock<IHttpDispatcher>().Verify(
                dispatcher => dispatcher.GetResponseAsync(It.IsAny<HttpRequest>(), It.IsAny<CookieContainer>()), Times.Exactly(41));
        }

        private HttpClient Client(ICacheManager cache)
        {
            var clock = Mocker.GetMock<IRateLimitService>();
            clock.Setup(service => service.WaitAndPulseAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeSpan>()))
                .Returns(Task.CompletedTask);
            return new HttpClient(Array.Empty<IHttpRequestInterceptor>(), cache, clock.Object, Mocker.GetMock<IHttpDispatcher>().Object, LogManager.GetCurrentClassLogger());
        }

        private static HttpRequest Request(string path) => new HttpRequest("https://inventaire.io" + path);
    }
}
