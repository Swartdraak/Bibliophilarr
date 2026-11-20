using System;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Cache;
using NzbDrone.Common.Http;
using NzbDrone.Common.Http.Dispatchers;
using NzbDrone.Common.TPL;
using NzbDrone.Test.Common;
using HttpClient = NzbDrone.Common.Http.HttpClient;

namespace NzbDrone.Common.Test.Http
{
    [TestFixture]
    public class HardcoverRateLimitFixture : TestBase
    {
        private const string Endpoint = "https://api.hardcover.app/v1/graphql";
        private const string LongDelayBody = """{"error":"Rate limit exceeded. Please try again in 72096 seconds."}""";

        [TestCase(null, LongDelayBody, 72096)]
        [TestCase("30", LongDelayBody, 72096)]
        [TestCase("73000", LongDelayBody, 73000)]
        [TestCase(null, "{\"retry_after\":72096}", 72096)]
        [TestCase("invalid", "{\"error\":\"request 72096 failed\"}", 300)]
        public void should_honor_the_longest_valid_hardcover_delay_across_requests_and_clients(string retryAfter, string body, int seconds)
        {
            var cache = new CacheManager();
            GiveThrottledResponse(retryAfter, body);
            var first = Assert.ThrowsAsync<TooManyRequestsException>(() => CreateClient(cache).ExecuteAsync(PrepareRequest(Endpoint, "search")));
            var second = Assert.ThrowsAsync<TooManyRequestsException>(() => CreateClient(cache).ExecuteAsync(PrepareRequest(Endpoint, "work lookup")));
            var third = Assert.ThrowsAsync<TooManyRequestsException>(() => CreateClient(cache).ExecuteAsync(PrepareRequest(Endpoint, "author bibliography")));

            foreach (var failure in new[] { first, second, third })
            {
                failure.RetryAfter.TotalSeconds.Should().BeInRange(seconds - 10, seconds);
            }

            Mocker.GetMock<IHttpDispatcher>().Verify(
                dispatcher => dispatcher.GetResponseAsync(It.IsAny<HttpRequest>(), It.IsAny<CookieContainer>()), Times.Once());
        }

        [Test]
        public void should_honor_a_standard_http_date()
        {
            var cache = new CacheManager();
            var retryAfter = DateTimeOffset.UtcNow.AddHours(20).ToString("R", CultureInfo.InvariantCulture);
            GiveThrottledResponse(retryAfter, "{}");

            var first = Assert.ThrowsAsync<TooManyRequestsException>(() => CreateClient(cache).ExecuteAsync(PrepareRequest(Endpoint, "search")));
            var second = Assert.ThrowsAsync<TooManyRequestsException>(() => CreateClient(cache).ExecuteAsync(PrepareRequest(Endpoint, "author bibliography")));

            first.RetryAfter.TotalHours.Should().BeInRange(19.99, 20.0);
            second.RetryAfter.TotalHours.Should().BeInRange(19.99, 20.0);
            Mocker.GetMock<IHttpDispatcher>().Verify(
                dispatcher => dispatcher.GetResponseAsync(It.IsAny<HttpRequest>(), It.IsAny<CookieContainer>()), Times.Once());
        }

        [Test]
        public async Task should_keep_queued_hardcover_requests_off_the_network_when_the_first_request_is_throttled()
        {
            var response = new TaskCompletionSource<HttpResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var initial = PrepareRequest(Endpoint, "search");
            initial.RateLimit = TimeSpan.FromSeconds(1);
            var client = CreateClient(new CacheManager());
            Mocker.GetMock<IHttpDispatcher>()
                .Setup(dispatcher => dispatcher.GetResponseAsync(It.IsAny<HttpRequest>(), It.IsAny<CookieContainer>()))
                .Returns(() =>
                {
                    entered.TrySetResult();
                    return response.Task;
                });

            var first = client.ExecuteAsync(initial);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var next = PrepareRequest(Endpoint, "lookup");
            next.RateLimit = initial.RateLimit;
            var second = client.ExecuteAsync(next);
            response.SetResult(new HttpResponse(initial, new HttpHeader(), LongDelayBody, HttpStatusCode.TooManyRequests));

            Assert.ThrowsAsync<TooManyRequestsException>(async () => await first);
            Assert.ThrowsAsync<TooManyRequestsException>(async () => await second);
            Mocker.GetMock<IHttpDispatcher>().Verify(
                dispatcher => dispatcher.GetResponseAsync(It.IsAny<HttpRequest>(), It.IsAny<CookieContainer>()), Times.Once());
            Mocker.GetMock<IRateLimitService>().Verify(service => service.WaitAndPulseAsync("api.hardcover.app", It.IsAny<string>(), initial.RateLimit),
                Times.Once());
        }

        [Test]
        public async Task should_leave_other_endpoints_untouched_and_keep_the_hold_when_http_errors_are_suppressed()
        {
            var client = CreateClient(new CacheManager());
            GiveThrottledResponse(null, LongDelayBody);
            var request = PrepareRequest(Endpoint, "search");
            request.SuppressHttpError = true;

            var first = await client.ExecuteAsync(request);
            var held = await client.ExecuteAsync(request);
            await client.ExecuteAsync(PrepareRequest("https://api.hardcover.app/unrelated", "outside graph"));
            await client.ExecuteAsync(PrepareRequest("https://example.invalid/v1/graphql", "unrelated host"));

            first.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
            held.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
            Mocker.GetMock<IHttpDispatcher>().Verify(
                dispatcher => dispatcher.GetResponseAsync(It.IsAny<HttpRequest>(), It.IsAny<CookieContainer>()), Times.Exactly(3));
        }

        private HttpClient CreateClient(ICacheManager cache)
        {
            return new HttpClient(Array.Empty<IHttpRequestInterceptor>(),
                cache,
                Mocker.GetMock<IRateLimitService>().Object,
                Mocker.GetMock<IHttpDispatcher>().Object,
                TestLogger);
        }

        private void GiveThrottledResponse(string retryAfter, string body)
        {
            Mocker.GetMock<IHttpDispatcher>()
                .Setup(dispatcher => dispatcher.GetResponseAsync(It.IsAny<HttpRequest>(), It.IsAny<CookieContainer>()))
                .ReturnsAsync((HttpRequest request, CookieContainer _) =>
                {
                    var headers = new HttpHeader();
                    if (retryAfter != null)
                    {
                        headers["Retry-After"] = retryAfter;
                    }

                    return new HttpResponse(request, headers, body, request.Url.ToString() == Endpoint ? HttpStatusCode.TooManyRequests : HttpStatusCode.OK);
                });
        }

        private static HttpRequest PrepareRequest(string path, string operation)
        {
            return new HttpRequest(path) { Method = HttpMethod.Post, ContentSummary = operation, LogHttpError = false };
        }
    }
}
