using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.Json;
using FluentAssertions;
using Moq;
using NUnit.Framework;
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
            Mocker.GetMock<IConfigService>()
                .SetupGet(x => x.EnableGoogleBooksFallback)
                .Returns(true);
        }

        [Test]
        public void author_hydration_should_continue_after_short_pages_and_beyond_400_volumes()
        {
            const int total = 413;
            var offsets = new List<int>();
            Mocker.GetMock<IHttpClient>()
                .Setup(client => client.Get<GoogleBooksSearchResponse>(It.IsAny<HttpRequest>()))
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
                .Setup(client => client.Get<GoogleBooksSearchResponse>(It.IsAny<HttpRequest>()))
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
                .Setup(client => client.Get<GoogleBooksSearchResponse>(It.IsAny<HttpRequest>()))
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

        private static int GetOffset(HttpRequest request)
        {
            return int.Parse(request.Url.Query.TrimStart('?').Split('&').Single(part => part.StartsWith("startIndex=", StringComparison.Ordinal)).Substring("startIndex=".Length));
        }

        private static HttpResponse<GoogleBooksSearchResponse> CreatePage(
            HttpRequest request, int total, int offset, int count, HttpStatusCode status = HttpStatusCode.OK)
        {
            var payload = JsonSerializer.Serialize(new
            {
                totalItems = total,
                items = Enumerable.Range(offset, count).Select(index => new
                {
                    id = $"volume-{index}",
                    volumeInfo = new { title = $"Book {index}", authors = new[] { "Author Name" }, description = "Volume synopsis" }
                })
            });
            return new HttpResponse<GoogleBooksSearchResponse>(new HttpResponse(
                request, new HttpHeader { ContentType = "application/json" }, payload, status));
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
                .Setup(x => x.Get<GoogleBooksSearchResponse>(It.IsAny<HttpRequest>()))
                .Returns<HttpRequest>(request =>
                    new HttpResponse<GoogleBooksSearchResponse>(new HttpResponse(request, new HttpHeader { ContentType = "application/json" }, payload)));

            var books = Subject.Search("Dune", "Frank Herbert");

            books.Should().ContainSingle();
            books[0].TitleSlug.Should().Be("googlebooks-work-gb-123");
            books[0].Editions.Value.Should().ContainSingle();
            books[0].Editions.Value[0].TitleSlug.Should().Be("googlebooks-edition-gb-123");
            books[0].Editions.Value[0].Images.Should().ContainSingle();
            books[0].Editions.Value[0].Images[0].Url.Should().Be("https://books.google.example/cover-thumb.jpg");
        }
    }
}
