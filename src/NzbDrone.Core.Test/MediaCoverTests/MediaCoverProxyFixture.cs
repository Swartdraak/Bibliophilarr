using System.Collections.Generic;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Http;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.MediaCover;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MediaCoverTests
{
    [TestFixture]
    public class MediaCoverProxyFixture : CoreTest<MediaCoverProxy>
    {
        [TestCase("file:///synthetic-cover.jpg")]
        [TestCase("ftp://covers.example/synthetic-cover.jpg")]
        [TestCase("data:image/png;base64,AA==")]
        [TestCase("images/synthetic-cover.jpg")]
        [TestCase("https://")]
        public void should_reject_non_http_artwork_without_caching_it(string url)
        {
            Subject.RegisterUrl(url).Should().BeNull();
            FluentActions.Invoking(() => Subject.GetUrl(url.SHA256Hash())).Should().Throw<KeyNotFoundException>();
            Mocker.GetMock<IHttpClient>().VerifyNoOtherCalls();
        }

        [TestCase("https://covers.openlibrary.org/b/id/42-L.jpg?default=false", "image.jpg")]
        [TestCase("https://images.hardcover.app/fixture.jpg", "image.jpg")]
        [TestCase("http://books.google.com/fixture.png", "image.png")]
        [TestCase("https://inventaire.io/fixture.webp", "image.webp")]
        public void should_map_to_a_neutral_artwork_name_and_fetch_the_original_url(string url, string fileName)
        {
            Mocker.GetMock<IConfigFileProvider>().SetupGet(config => config.UrlBase).Returns("/library");
            var bytes = new byte[] { 1, 2, 3 };
            var http = Mocker.GetMock<IHttpClient>();
            http.Setup(client => client.Get(It.Is<HttpRequest>(request => request.Url.ToString() == url)))
                .Returns((HttpRequest request) => new HttpResponse(request, new HttpHeader(), bytes));

            var hash = url.SHA256Hash();
            Subject.RegisterUrl(url).Should().Be($"/library/MediaCoverProxy/{hash}/{fileName}");
            Subject.GetUrl(hash).Should().Be(url);
            Subject.GetImage(hash).Should().Equal(bytes);
            http.Verify(client => client.Get(It.IsAny<HttpRequest>()), Times.Once());
        }
    }
}
