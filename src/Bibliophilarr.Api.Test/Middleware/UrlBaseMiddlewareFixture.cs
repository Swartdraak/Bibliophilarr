using System.Threading.Tasks;
using Bibliophilarr.Http.Middleware;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace NzbDrone.Api.Test.Middleware
{
    [TestFixture]
    public class UrlBaseMiddlewareFixture
    {
        [Test]
        public async Task should_preserve_post_method_and_uri_encoding_in_local_redirect()
        {
            using var services = new ServiceCollection().AddLogging().BuildServiceProvider();
            var context = new DefaultHttpContext { RequestServices = services };
            context.Request.Method = HttpMethods.Post;
            context.Request.Path = PathString.FromUriComponent("/books/City%20Lights");
            context.Request.QueryString = QueryString.FromUriComponent("?term=City%20Lights&sort=title%2Bauthor");
            var middleware = new UrlBaseMiddleware(_ => throw new AssertionException("Unexpected request passthrough."), "/read/library");

            await middleware.InvokeAsync(context);

            context.Response.StatusCode.Should().Be(StatusCodes.Status307TemporaryRedirect);
            context.Response.Headers.Location.ToString().Should().Be("/read/library/books/City%20Lights?term=City%20Lights&sort=title%2Bauthor");
        }

        [TestCase("", "")]
        [TestCase("/read/library", "/read/library")]
        public async Task should_pass_through_when_url_base_is_empty_or_already_applied(string urlBase, string requestPathBase)
        {
            var context = new DefaultHttpContext();
            context.Request.PathBase = new PathString(requestPathBase);
            context.Request.Path = "/books";
            var invocations = 0;
            var middleware = new UrlBaseMiddleware(request =>
            {
                invocations++;
                request.Response.StatusCode = StatusCodes.Status202Accepted;
                return Task.CompletedTask;
            }, urlBase);

            await middleware.InvokeAsync(context);

            invocations.Should().Be(1);
            context.Response.StatusCode.Should().Be(StatusCodes.Status202Accepted);
            context.Response.Headers.ContainsKey("Location").Should().BeFalse();
        }

        [Test]
        public async Task should_reject_an_unrooted_configured_url_base_without_redirecting()
        {
            var context = new DefaultHttpContext();
            context.Request.Path = "/books";
            var middleware = new UrlBaseMiddleware(_ => throw new AssertionException("Unexpected request passthrough."), "library");

            await middleware.InvokeAsync(context);

            context.Response.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
            context.Response.Headers.ContainsKey("Location").Should().BeFalse();
        }
    }
}
