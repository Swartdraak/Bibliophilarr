using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.Json;
using FluentAssertions;
using Moq;
using NLog;
using NLog.Config;
using NLog.Targets;
using NUnit.Framework;
using NzbDrone.Common.Cache;
using NzbDrone.Common.Http;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Exceptions;
using NzbDrone.Core.MetadataSource.GoogleBooks;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MetadataSource
{
    [TestFixture]
    public class GoogleBooksFallbackSearchProviderFixture : CoreTest<GoogleBooksFallbackSearchProvider>
    {
        [SetUp]
        public void SetUp()
        {
            Mocker.SetConstant<TimeProvider>(TimeProvider.System);
            Mocker.GetMock<IConfigService>()
                .SetupGet(x => x.EnableGoogleBooksFallback)
                .Returns(true);
        }

        [TestCase("text/html; charset=utf-8")]
        [TestCase("Text/Html; charset=utf-8")]
        public void should_log_html_fallback_failures_without_private_request_or_response_data(string contentType)
        {
            const string title = "private-fixture-title";
            const string author = "private-fixture-author";
            const string key = "private-fixture-api-key";
            const string bodyMarker = "private-fixture-response";
            var config = Mocker.GetMock<IConfigService>();
            config.SetupGet(value => value.GoogleBooksApiKey).Returns(key);
            var http = Mocker.GetMock<IHttpClient>();
            http.Setup(client => client.Get(It.IsAny<HttpRequest>()))
                .Returns<HttpRequest>(request => new HttpResponse(
                    request,
                    new HttpHeader { ContentType = contentType },
                    $"<html>{bodyMarker}</html>",
                    HttpStatusCode.OK));
            var messages = new MemoryTarget { Layout = "${message}|${exception:format=ToString}" };
            var configuration = new LoggingConfiguration();
            configuration.AddRule(LogLevel.Warn, LogLevel.Fatal, messages);
            using var logFactory = new LogFactory { Configuration = configuration };
            var provider = new GoogleBooksFallbackSearchProvider(
                config.Object,
                http.Object,
                Mocker.Resolve<ICacheManager>(),
                logFactory.GetLogger(nameof(GoogleBooksFallbackSearchProvider)));

            provider.Search(title, author).Should().BeEmpty();

            var log = messages.Logs.Should().ContainSingle().Which;
            log.Should().StartWith("Google Books search failed (GoogleBooksUnexpectedHtmlException).");
            log.Should().NotContain(title).And.NotContain(author).And.NotContain(key).And.NotContain(bodyMarker);
            http.Verify(client => client.Get(It.IsAny<HttpRequest>()), Times.Once());
        }

        [Test]
        public void author_hydration_should_continue_after_short_pages_and_beyond_400_volumes()
        {
            const int total = 413;
            var offsets = new List<int>();
            Mocker.GetMock<IHttpClient>()
                .Setup(client => client.Get(It.IsAny<HttpRequest>()))
                .Returns<HttpRequest>(request =>
                {
                    var offset = GetOffset(request);
                    offsets.Add(offset);
                    request.Url.Query.Should().NotContain("langRestrict");
                    return CreatePage(request, total, offset, Math.Min(27, total - offset));
                });

            var author = Subject.GetAuthorInfo("googlebooks:author:Author%20Name");

            author.Metadata.Value.Overview.Should().BeNull();
            author.Books.Value.Should().HaveCount(total);
            author.Books.Value.First().Editions.Value.Single().Overview.Should().Be("Volume synopsis");
            author.Books.Value.Should().Contain(book => book.ForeignBookId == "googlebooks:work:volume-412");
            offsets.Should().Contain(405);
            offsets.Last().Should().Be(total);
        }

        [Test]
        public void ordinary_search_should_keep_the_existing_volume_cap_and_language_default()
        {
            Mocker.GetMock<IHttpClient>()
                .Setup(client => client.Get(It.IsAny<HttpRequest>()))
                .Returns<HttpRequest>(request =>
                {
                    request.Url.Query.Should().Contain("langRestrict=en");
                    return CreatePage(request, 500, GetOffset(request), 40);
                });

            Subject.Search("Book", "Author Name").Should().HaveCount(200);
        }

        [TestCase("http")]
        [TestCase("repeated")]
        [TestCase("unexpected empty")]
        public void author_hydration_should_reject_later_incomplete_pages(string failure)
        {
            Mocker.GetMock<IHttpClient>()
                .Setup(client => client.Get(It.IsAny<HttpRequest>()))
                .Returns<HttpRequest>(request =>
                {
                    if (GetOffset(request) == 0)
                    {
                        return CreatePage(request, 2, 0, 1);
                    }

                    return failure switch
                    {
                        "http" => CreatePage(request, 0, 0, 0, HttpStatusCode.ServiceUnavailable),
                        "repeated" => CreatePage(request, 2, 0, 1),
                        _ => CreatePage(request, 2, 0, 0)
                    };
                });

            var lookup = () => Subject.GetAuthorInfo("googlebooks:author:Author%20Name");

            lookup.Should().Throw<MetadataProviderUnavailableException>();
        }

        [Test]
        public void author_hydration_should_reject_a_total_that_shrinks_on_the_empty_page()
        {
            var offsets = new List<int>();
            Mocker.GetMock<IHttpClient>()
                .Setup(client => client.Get(It.IsAny<HttpRequest>()))
                .Returns<HttpRequest>(request =>
                {
                    var offset = GetOffset(request);
                    offsets.Add(offset);
                    return offset switch
                    {
                        0 => CreatePage(request, 4, 0, 2),
                        2 => CreatePage(request, 4, 2, 1),
                        _ => CreatePage(request, 3, 0, 0)
                    };
                });

            var lookup = () => Subject.GetAuthorInfo("googlebooks:author:Author%20Name");

            lookup.Should().Throw<MetadataProviderUnavailableException>();
            offsets.Should().Equal(0, 2, 3);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void author_hydration_should_require_all_distinct_volumes_when_pages_overlap(bool complete)
        {
            var offsets = new List<int>();
            Mocker.GetMock<IHttpClient>()
                .Setup(client => client.Get(It.IsAny<HttpRequest>()))
                .Returns<HttpRequest>(request =>
                {
                    var offset = GetOffset(request);
                    offsets.Add(offset);
                    return offset switch
                    {
                        0 => CreatePage(request, 4, new[] { 0, 1 }),
                        2 => CreatePage(request, 4, new[] { 1, 2 }),
                        4 when complete => CreatePage(request, 4, new[] { 3 }),
                        _ => CreatePage(request, 4, 0, 0)
                    };
                });

            if (complete)
            {
                var author = Subject.GetAuthorInfo("googlebooks:author:Author%20Name");

                author.Books.Value.Select(book => book.ForeignBookId)
                    .Should().Equal("googlebooks:work:volume-0", "googlebooks:work:volume-1", "googlebooks:work:volume-2", "googlebooks:work:volume-3");
                offsets.Should().Equal(0, 2, 4, 5);
            }
            else
            {
                var lookup = () => Subject.GetAuthorInfo("googlebooks:author:Author%20Name");

                lookup.Should().Throw<MetadataProviderUnavailableException>();
                offsets.Should().Equal(0, 2, 4);
            }
        }

        private static int GetOffset(HttpRequest request)
        {
            return int.Parse(request.Url.Query.TrimStart('?').Split('&').Single(part => part.StartsWith("startIndex=", StringComparison.Ordinal)).Substring("startIndex=".Length));
        }

        private static HttpResponse CreatePage(
            HttpRequest request, int total, int offset, int count, HttpStatusCode status = HttpStatusCode.OK)
        {
            return CreatePage(request, total, Enumerable.Range(offset, count), status);
        }

        private static HttpResponse CreatePage(
            HttpRequest request, int total, IEnumerable<int> volumeIndices, HttpStatusCode status = HttpStatusCode.OK)
        {
            var payload = JsonSerializer.Serialize(new
            {
                totalItems = total,
                items = volumeIndices.Select(index => new
                {
                    id = $"volume-{index}",
                    volumeInfo = new { title = $"Book {index}", authors = new[] { "Author Name" }, description = "Volume synopsis" }
                })
            });
            return new HttpResponse(request, new HttpHeader { ContentType = "application/json" }, payload, status);
        }

        [Test]
        public void should_keep_definitive_volume_not_found_separate_from_a_shared_google_cooldown()
        {
            const string bookId = "googlebooks:work:fixture-volume";
            var http = Mocker.GetMock<IHttpClient>();
            var headers = new HttpHeader { ContentType = "text/html", ["Retry-After"] = "180" };
            http.Setup(client => client.Get(It.IsAny<HttpRequest>()))
                .Returns<HttpRequest>(request => new HttpResponse(request, headers, "<html>missing</html>", HttpStatusCode.NotFound));

            Action initial = () => Subject.GetBookInfo(bookId);
            initial.Should().Throw<BookNotFoundException>();
            http.Verify(client => client.Get(It.IsAny<HttpRequest>()), Times.Once());

            http.Setup(client => client.Get(It.IsAny<HttpRequest>()))
                .Returns<HttpRequest>(request => new HttpResponse(request, headers, "<html>rate limited</html>", HttpStatusCode.TooManyRequests));
            initial.Should().Throw<MetadataProviderUnavailableException>().WithInnerException<TooManyRequestsException>();

            var secondInstance = new GoogleBooksFallbackSearchProvider(
                Mocker.GetMock<IConfigService>().Object,
                http.Object,
                Mocker.Resolve<ICacheManager>(),
                LogManager.GetCurrentClassLogger());
            Action held = () => secondInstance.GetBookInfo(bookId);
            held.Should().Throw<MetadataProviderUnavailableException>();
            http.Verify(client => client.Get(It.IsAny<HttpRequest>()), Times.Exactly(2));
        }

        [Test]
        public void should_admit_a_google_request_exactly_when_the_shared_cooldown_expires()
        {
            const string bookId = "googlebooks:work:fixture-volume";
            var clock = new FixtureTimeProvider();
            var http = Mocker.GetMock<IHttpClient>();
            var request = new HttpRequest("https://example.test");
            var rateLimitHeaders = new HttpHeader { ContentType = "application/json", ["Retry-After"] = "180" };
            http.SetupSequence(client => client.Get(It.IsAny<HttpRequest>()))
                .Returns(new HttpResponse(request, rateLimitHeaders, "{}", HttpStatusCode.TooManyRequests))
                .Returns(new HttpResponse(
                    request,
                    new HttpHeader { ContentType = "application/json" },
                    """{"id":"fixture-volume","volumeInfo":{"title":"Fixture book","authors":["Fixture Author"]}}"""));
            var provider = new GoogleBooksFallbackSearchProvider(
                Mocker.GetMock<IConfigService>().Object,
                http.Object,
                Mocker.Resolve<ICacheManager>(),
                LogManager.GetCurrentClassLogger(),
                clock);

            Action lookup = () => provider.GetBookInfo(bookId);
            lookup.Should().Throw<MetadataProviderUnavailableException>().WithInnerException<TooManyRequestsException>();

            clock.UtcNow = clock.UtcNow.AddSeconds(179);
            lookup.Should().Throw<MetadataProviderUnavailableException>();
            http.Verify(client => client.Get(It.IsAny<HttpRequest>()), Times.Once());

            clock.UtcNow = clock.UtcNow.AddSeconds(1);
            provider.GetBookInfo(bookId).Item2.ForeignBookId.Should().Be(bookId);
            http.Verify(client => client.Get(It.IsAny<HttpRequest>()), Times.Exactly(2));
        }

        [Test]
        public void should_reject_mixed_case_html_without_retaining_google_request_or_body()
        {
            const string apiKey = "fixture-private-api-key";
            const string author = "Fixture%20Private%20Name";
            const string body = "<html>private-body</html>";
            Mocker.GetMock<IConfigService>().SetupGet(x => x.GoogleBooksApiKey).Returns(apiKey);
            Mocker.GetMock<IHttpClient>().Setup(client => client.Get(It.IsAny<HttpRequest>()))
                .Returns<HttpRequest>(request => new HttpResponse(request, new HttpHeader { ContentType = "TeXt/HtMl; charset=utf-8" }, body));

            Action lookup = () => Subject.GetAuthorInfo("googlebooks:author:" + author);
            var failure = lookup.Should().Throw<MetadataProviderUnavailableException>().Which.InnerException;
            failure.Message.Should().Be("Google Books returned unexpected HTML content.");
            failure.ToString().Should().NotContain(apiKey).And.NotContain("private-body").And.NotContain("www.googleapis.com");
        }

        [Test]
        public void should_map_cover_images_from_image_links()
        {
            var payload = "{" +
                          "\"items\":[{" +
                          "\"id\":\"gb-123\"," +
                          "\"volumeInfo\":{" +
                          "\"title\":\"Dune\"," +
                          "\"authors\":[\"Frank Herbert\"]," +
                          "\"publishedDate\":\"1965-01-01\"," +
                          "\"printType\":\"BOOK\"," +
                          "\"industryIdentifiers\":[{" +
                          "\"type\":\"ISBN_13\",\"identifier\":\"9780441013593\"}]," +
                          "\"imageLinks\":{" +
                          "\"thumbnail\":\"https://books.google.example/cover-thumb.jpg\"}}}]}";

            Mocker.GetMock<IHttpClient>()
                .Setup(x => x.Get(It.IsAny<HttpRequest>()))
                .Returns<HttpRequest>(request =>
                    new HttpResponse(request, new HttpHeader { ContentType = "application/json" }, payload));

            var books = Subject.Search("Dune", "Frank Herbert");

            books.Should().ContainSingle();
            books[0].TitleSlug.Should().Be("googlebooks-work-gb-123");
            books[0].Editions.Value.Should().ContainSingle();
            books[0].Editions.Value[0].TitleSlug.Should().Be("googlebooks-edition-gb-123");
            books[0].Editions.Value[0].Images.Should().ContainSingle();
            books[0].Editions.Value[0].Images[0].Url.Should().Be("https://books.google.example/cover-thumb.jpg");
        }

        private sealed class FixtureTimeProvider : TimeProvider
        {
            public DateTimeOffset UtcNow { get; set; } = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

            public override DateTimeOffset GetUtcNow() => UtcNow;
        }
    }
}
