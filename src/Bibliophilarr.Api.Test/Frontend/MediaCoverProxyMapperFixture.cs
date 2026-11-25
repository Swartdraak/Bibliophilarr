using System;
using System.Net;
using Bibliophilarr.Http.Frontend.Mappers;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Http;
using NzbDrone.Core.MediaCover;

namespace Bibliophilarr.Api.Test.Frontend
{
    [TestFixture]
    public class MediaCoverProxyMapperFixture
    {
        private const string ArtworkHash = "fixtureHash";
        private const string ArtworkPath = "/MediaCoverProxy/" + ArtworkHash + "/fixture.jpg";
        private Mock<IMediaCoverProxy> _proxy;
        private MediaCoverProxyMapper _mapper;

        [SetUp]
        public void Setup()
        {
            _proxy = new Mock<IMediaCoverProxy>(MockBehavior.Strict);
            _mapper = new MediaCoverProxyMapper(_proxy.Object);
        }

        [Test]
        public void should_return_an_empty_not_found_response_when_verified_artwork_is_missing()
        {
            _proxy.Setup(proxy => proxy.GetImage(ArtworkHash)).Throws(CreateFailure(HttpStatusCode.NotFound));

            var result = _mapper.GetResponse(ArtworkPath);

            result.Should().BeOfType<StatusCodeResult>().Which.StatusCode.Should().Be(404);
            _proxy.Verify(proxy => proxy.GetImage(ArtworkHash), Times.Once());
        }

        [TestCase(HttpStatusCode.Gone)]
        [TestCase(HttpStatusCode.ServiceUnavailable)]
        public void should_propagate_other_provider_failures(HttpStatusCode status)
        {
            var failure = CreateFailure(status);
            _proxy.Setup(proxy => proxy.GetImage(ArtworkHash)).Throws(failure);

            FluentActions.Invoking(() => _mapper.GetResponse(ArtworkPath))
                .Should().Throw<HttpException>().Which.Should().BeSameAs(failure);
        }

        [Test]
        public void should_propagate_cancellation()
        {
            var failure = new OperationCanceledException();
            _proxy.Setup(proxy => proxy.GetImage(ArtworkHash)).Throws(failure);

            FluentActions.Invoking(() => _mapper.GetResponse(ArtworkPath))
                .Should().Throw<OperationCanceledException>().Which.Should().BeSameAs(failure);
        }

        [Test]
        public void should_serve_bytes_for_existing_artwork()
        {
            var bytes = new byte[] { 1, 2, 3 };
            _proxy.Setup(proxy => proxy.GetImage(ArtworkHash)).Returns(bytes);

            var file = _mapper.GetResponse(ArtworkPath).Should().BeOfType<FileContentResult>().Which;

            file.ContentType.Should().Be("image/jpeg");
            file.FileContents.Should().Equal(bytes);
        }

        private static HttpException CreateFailure(HttpStatusCode status)
        {
            var request = new HttpRequest("https://covers.example/fixture.jpg");
            var response = new HttpResponse(request, new HttpHeader(), Array.Empty<byte>(), status);
            return new HttpException(response);
        }
    }
}
