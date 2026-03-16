using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Http;
using NzbDrone.Core.MetadataSource.OpenLibrary;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MetadataSource.OpenLibrary
{
    [TestFixture]
    public class OpenLibrarySearchProxyFixture : CoreTest<OpenLibrarySearchProxy>
    {
        [Test]
        public void should_map_cover_image_from_search_results()
        {
            var payload = "{" +
                          "\"docs\":[{" +
                          "\"key\":\"/works/OL123W\"," +
                          "\"title\":\"Dune\"," +
                          "\"author_name\":[\"Frank Herbert\"]," +
                          "\"author_key\":[\"OL23919A\"]," +
                          "\"cover_i\":987654," +
                          "\"first_publish_year\":1965" +
                          "}]}";

            Mocker.GetMock<IHttpClient>()
                .Setup(x => x.Get<OpenLibrarySearchResponse>(It.IsAny<HttpRequest>()))
                .Returns<HttpRequest>(request =>
                    new HttpResponse<OpenLibrarySearchResponse>(new HttpResponse(request, new HttpHeader { ContentType = "application/json" }, payload)));

            var books = Subject.Search("Dune");

            books.Should().ContainSingle();
            books[0].Editions.Value.Should().ContainSingle();
            books[0].Editions.Value[0].Images.Should().ContainSingle();
            books[0].Editions.Value[0].Images[0].Url.Should().Be("https://covers.openlibrary.org/b/id/987654-L.jpg");
        }

        [Test]
        public void should_lookup_author_details_by_open_library_author_id()
        {
            var payload = "{" +
                          "\"key\":\"/authors/OL23919A\"," +
                          "\"name\":\"Frank Herbert\"," +
                          "\"bio\":\"American science fiction author\"," +
                          "\"photos\":[12345]" +
                          "}";

            Mocker.GetMock<IHttpClient>()
                .Setup(x => x.Get<OpenLibraryAuthorResource>(It.IsAny<HttpRequest>()))
                .Returns<HttpRequest>(request =>
                    new HttpResponse<OpenLibraryAuthorResource>(new HttpResponse(request, new HttpHeader { ContentType = "application/json" }, payload)));

            var author = Subject.LookupAuthorByKey("openlibrary:author:OL23919A");

            author.Should().NotBeNull();
            author.ForeignAuthorId.Should().Be("openlibrary:author:OL23919A");
            author.Metadata.Value.Name.Should().Be("Frank Herbert");
            author.Metadata.Value.Overview.Should().Be("American science fiction author");
            author.Metadata.Value.Images.Should().ContainSingle();
            author.Metadata.Value.Images[0].Url.Should().Be("https://covers.openlibrary.org/a/id/12345-L.jpg");
        }
    }
}
