using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Http;
using NzbDrone.Core.Configuration;
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
