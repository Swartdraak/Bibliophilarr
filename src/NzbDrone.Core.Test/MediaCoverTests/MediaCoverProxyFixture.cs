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

        [TestCase("https://covers.openlibrary.org/b/id/42-L.jpg?default=false", "42-L.jpg?default=false")]
        [TestCase("https://images.hardcover.app/fixture.jpg", "fixture.jpg")]
        [TestCase("http://books.google.com/fixture.png", "fixture.png")]
        [TestCase("https://inventaire.io/fixture.webp", "fixture.webp")]
        public void should_preserve_provider_artwork_mapping_and_http_fetch(string url, string fileName)
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
