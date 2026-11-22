using System;
using System.Text.Json;
using Bibliophilarr.Api.V1.Author;
using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Serializer;
using NzbDrone.Core.Books;
using NzbDrone.Core.Exceptions;
using NzbDrone.Test.Common.AutoMoq;

namespace NzbDrone.Api.Test.Authors
{
    [TestFixture]
    public class AuthorControllerFixture
    {
        private const string AuthorId = "openlibrary:author:OL123A";
        private static AuthorResource Resource => new AuthorResource { ForeignAuthorId = AuthorId, AuthorName = "Fixture Author" };

        [Test]
        public void should_return_retryable_sanitized_error_for_unavailable_author_metadata()
        {
            var failure = new MetadataProviderUnavailableException(
                "get-author-info",
                AuthorId,
                new InvalidOperationException("private-provider-diagnostic"));
            var controller = CreateController(failure);

            var result = controller.AddAuthor(Resource).Result as ObjectResult;

            Assert.That(result, Is.Not.Null);
            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.Status503ServiceUnavailable));
            var payload = STJson.ToJson(result.Value);
            using var json = JsonDocument.Parse(payload);
            Assert.That(
                json.RootElement.GetProperty("message").GetString(),
                Is.EqualTo("Could not verify the author's bibliography with the configured metadata providers. Try adding the author again later."));
            Assert.That(payload, Does.Not.Contain(AuthorId).And.Not.Contain("private-provider-diagnostic"));
        }

        [Test]
        public void should_preserve_author_validation_errors()
        {
            var failure = new ValidationException("Author could not be found");
            var controller = CreateController(failure);

            Assert.That(Assert.Throws<ValidationException>(() => controller.AddAuthor(Resource)), Is.SameAs(failure));
        }

        private static AuthorController CreateController(Exception failure)
        {
            var mocker = new AutoMoqer();
            mocker.GetMock<IAddAuthorService>()
                .Setup(service => service.AddAuthor(It.Is<NzbDrone.Core.Books.Author>(author => author.ForeignAuthorId == AuthorId), true))
                .Throws(failure);
            return mocker.Resolve<AuthorController>();
        }
    }
}
