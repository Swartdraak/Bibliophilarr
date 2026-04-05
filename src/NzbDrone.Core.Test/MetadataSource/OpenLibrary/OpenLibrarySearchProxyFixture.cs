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
            books[0].TitleSlug.Should().Be("openlibrary:work:OL123W");
            books[0].Editions.Value.Should().ContainSingle();
            books[0].Editions.Value[0].TitleSlug.Should().Be("openlibrary:edition:OL123W");
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

        [Test]
        public void should_populate_open_library_author_id_on_author_mapping()
        {
            // After normalization, OpenLibraryAuthorId should carry the bare OL-format key (OL123A)
            // so cross-path ID lookups and BackfillService fast-path work correctly.
            var payload = "{" +
                          "\"key\":\"/authors/OL23919A\"," +
                          "\"name\":\"Frank Herbert\"" +
                          "}";

            Mocker.GetMock<IHttpClient>()
                .Setup(x => x.Get<OpenLibraryAuthorResource>(It.IsAny<HttpRequest>()))
                .Returns<HttpRequest>(request =>
                    new HttpResponse<OpenLibraryAuthorResource>(new HttpResponse(request, new HttpHeader { ContentType = "application/json" }, payload)));

            var author = Subject.LookupAuthorByKey("openlibrary:author:OL23919A");

            author.Should().NotBeNull();

            // ForeignAuthorId carries the full prefixed form
            author.Metadata.Value.ForeignAuthorId.Should().Be("openlibrary:author:OL23919A");

            // OpenLibraryAuthorId carries the bare OL form for backfill compatibility
            author.Metadata.Value.OpenLibraryAuthorId.Should().Be("OL23919A");
        }

        [Test]
        public void isbn_lookup_should_follow_open_library_redirect_to_edition_json()
        {
            // Open Library /isbn/{isbn}.json redirects to /books/OL{id}M.json.
            // With AllowAutoRedirect=true the HttpClient follows the redirect and we parse the JSON.
            var editionPayload = "{" +
                                 "\"key\":\"/books/OL9584111M\"," +
                                 "\"title\":\"The Godfather\"," +
                                 "\"works\":[{\"key\":\"/works/OL2748W\"}]," +
                                 "\"authors\":[{\"key\":\"/authors/OL31916A\"}]" +
                                 "}";

            HttpRequest capturedRequest = null;
            Mocker.GetMock<IHttpClient>()
                .Setup(x => x.Get<OpenLibraryEditionResource>(It.IsAny<HttpRequest>()))
                .Callback<HttpRequest>(req => capturedRequest = req)
                .Returns<HttpRequest>(request =>
                    new HttpResponse<OpenLibraryEditionResource>(new HttpResponse(request, new HttpHeader { ContentType = "application/json" }, editionPayload)));

            var result = Subject.LookupByIsbn("9780440243830");

            // Verify AllowAutoRedirect was set to true on the ISBN request
            capturedRequest.Should().NotBeNull();
            capturedRequest.AllowAutoRedirect.Should().BeTrue();

            // Edition data should be returned
            result.Should().NotBeNull();
            result.Title.Should().Be("The Godfather");
        }
    }
}
