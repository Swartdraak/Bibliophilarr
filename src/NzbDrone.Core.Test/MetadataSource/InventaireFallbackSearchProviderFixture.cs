using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Http;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.MetadataSource.Inventaire;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MetadataSource
{
    [TestFixture]
    public class InventaireFallbackSearchProviderFixture : CoreTest<InventaireFallbackSearchProvider>
    {
        [SetUp]
        public void SetUp()
        {
            Mocker.GetMock<IConfigService>()
                .SetupGet(x => x.EnableInventaireFallback)
                .Returns(true);
        }

        [Test]
        public void should_return_empty_when_disabled()
        {
            Mocker.GetMock<IConfigService>()
                .SetupGet(x => x.EnableInventaireFallback)
                .Returns(false);

            Subject.Search("Dune", "Frank Herbert").Should().BeEmpty();

            Mocker.GetMock<IHttpClient>()
                .Verify(x => x.Get<InventaireSearchResponse>(It.IsAny<HttpRequest>()), Times.Never());
        }

        [Test]
        public void should_map_search_results_with_cover_image()
        {
            HttpRequest capturedRequest = null;
            var payload = "{" +
                          "\"results\":[{" +
                          "\"uri\":\"https://inventaire.io/entity/wd:Q123\"," +
                          "\"label\":\"Dune\"," +
                          "\"author\":\"Frank Herbert\"," +
                          "\"description\":\"Epic science fiction novel\"," +
                          "\"cover\":\"https://inventaire.example/covers/dune.jpg\"," +
                          "\"isbn13\":\"9780441013593\"" +
                          "}]}";

            Mocker.GetMock<IHttpClient>()
                .Setup(x => x.Get<InventaireSearchResponse>(It.IsAny<HttpRequest>()))
                .Returns<HttpRequest>(request =>
                {
                    capturedRequest = request;
                    return new HttpResponse<InventaireSearchResponse>(new HttpResponse(request, new HttpHeader { ContentType = "application/json" }, payload));
                });

            var books = Subject.Search("Dune", "Frank Herbert");

            books.Should().ContainSingle();
            books[0].Title.Should().Be("Dune");
            books[0].ForeignBookId.Should().Be("inventaire:work:wd:Q123");
            books[0].TitleSlug.Should().Be("inventaire-work-wd-q123");
            books[0].AuthorMetadata.Value.Name.Should().Be("Frank Herbert");
            books[0].Editions.Value.Should().ContainSingle();
            books[0].Editions.Value[0].TitleSlug.Should().Be("inventaire-edition-wd-q123");
            books[0].Editions.Value[0].Images.Should().ContainSingle();
            books[0].Editions.Value[0].Images[0].Url.Should().Be("https://inventaire.example/covers/dune.jpg");

            capturedRequest.Should().NotBeNull();
            capturedRequest.RateLimitKey.Should().Be("Inventaire");
        }

        [TestCase("picture", null)]
        [TestCase("image", null)]
        [TestCase("picture", "https://inventaire.example/portraits/author.jpg")]
        public void should_keep_work_artwork_separate_from_the_author_portrait(string workImageProperty, string authorPortrait)
        {
            const string workImage = "https://inventaire.example/covers/work.jpg";
            var portraitJson = authorPortrait == null ? "null" : $"\"{authorPortrait}\"";
            var payload = $$"""
                {
                  "results": [{
                    "uri": "wd:Q123",
                    "label": "Fixture work",
                    "author": "Fixture author",
                    "{{workImageProperty}}": "{{workImage}}",
                    "authorPicture": {{portraitJson}}
                  }]
                }
                """;

            Mocker.GetMock<IHttpClient>()
                .Setup(x => x.Get<InventaireSearchResponse>(It.IsAny<HttpRequest>()))
                .Returns<HttpRequest>(request => new HttpResponse<InventaireSearchResponse>(
                    new HttpResponse(request, new HttpHeader { ContentType = "application/json" }, payload)));

            var book = Subject.Search("Fixture work", "Fixture author").Should().ContainSingle().Which;
            var edition = book.Editions.Value.Should().ContainSingle().Which;
            edition.Images.Should().ContainSingle().Which.Url.Should().Be(workImage);
            var portraits = book.AuthorMetadata.Value.Images;

            if (authorPortrait == null)
            {
                portraits.Should().BeEmpty();
            }
            else
            {
                portraits.Should().ContainSingle().Which.Url.Should().Be(authorPortrait);
            }
        }
    }
}
