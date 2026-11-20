using System.Linq;
using System.Reflection;
using System.Security.Claims;
using System.Threading.Tasks;
using Bibliophilarr.Api.V1.System;
using Bibliophilarr.Http;
using Bibliophilarr.Http.Authentication;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using NUnit.Framework;
using AspNetAuthenticationService = Microsoft.AspNetCore.Authentication.IAuthenticationService;

namespace NzbDrone.Api.Test.Authentication
{
    [TestFixture]
    public class ApiWriteAuthenticationMiddlewareFixture
    {
        [TestCase("POST")]
        [TestCase("PUT")]
        [TestCase("PATCH")]
        [TestCase("DELETE")]
        public async Task should_preserve_a_write_with_a_valid_explicit_api_credential(string method)
        {
            var auth = new Mock<AspNetAuthenticationService>();
            var principal = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("ApiKey", "true") }, "API"));
            auth.Setup(service => service.AuthenticateAsync(It.IsAny<HttpContext>(), "API"))
                .ReturnsAsync(AuthenticateResult.Success(new AuthenticationTicket(principal, "API")));
            using var services = new ServiceCollection().AddSingleton(auth.Object).BuildServiceProvider();
            var context = new DefaultHttpContext { RequestServices = services };
            context.Request.Method = method;
            context.SetEndpoint(new Endpoint(_ => Task.CompletedTask,
                new EndpointMetadataCollection(new V1ApiControllerAttribute()),
                "API action"));
            var called = false;
            var middleware = new ApiWriteAuthenticationMiddleware(_ =>
            {
                called = true;
                return Task.CompletedTask;
            });

            await middleware.InvokeAsync(context);

            called.Should().BeTrue();
            auth.Verify(service => service.AuthenticateAsync(context, "API"), Times.Once());
        }

        [TestCase("GET", true)]
        [TestCase("HEAD", true)]
        [TestCase("OPTIONS", true)]
        [TestCase("POST", false)]
        public async Task should_leave_safe_api_methods_and_non_api_forms_in_the_existing_pipeline(string method, bool versionedApi)
        {
            var auth = new Mock<AspNetAuthenticationService>(MockBehavior.Strict);
            using var services = new ServiceCollection().AddSingleton(auth.Object).BuildServiceProvider();
            var context = new DefaultHttpContext { RequestServices = services };
            context.Request.Method = method;
            var metadata = versionedApi ? new EndpointMetadataCollection(new V1ApiControllerAttribute()) : EndpointMetadataCollection.Empty;
            context.SetEndpoint(new Endpoint(_ => Task.CompletedTask, metadata, "Request action"));
            var called = false;
            var middleware = new ApiWriteAuthenticationMiddleware(_ =>
            {
                called = true;
                return Task.CompletedTask;
            });

            await middleware.InvokeAsync(context);

            called.Should().BeTrue();
            auth.VerifyNoOtherCalls();
        }

        [TestCase(nameof(AuthenticationController.Login))]
        [TestCase(nameof(AuthenticationController.Logout))]
        [TestCase(nameof(AuthenticationController.Setup))]
        public void should_configure_antiforgery_for_forms_that_change_authentication_state(string method)
        {
            var endpoint = typeof(AuthenticationController).GetMethod(method);

            endpoint.GetCustomAttribute<HttpPostAttribute>().Should().NotBeNull();
            endpoint.GetCustomAttribute<ValidateAntiForgeryTokenAttribute>().Should().NotBeNull();
        }

        [Test]
        public async Task should_discover_the_version_marker_on_a_real_mutating_api_route()
        {
            var builder = WebApplication.CreateBuilder();
            builder.Services.AddControllers().AddApplicationPart(typeof(SystemController).Assembly);
            await using var app = builder.Build();
            app.MapControllers();

            var action = ((IEndpointRouteBuilder)app).DataSources.SelectMany(source => source.Endpoints)
                .Single(endpoint => endpoint.Metadata.GetMetadata<ControllerActionDescriptor>() is { } descriptor &&
                    descriptor.ControllerTypeInfo == typeof(SystemController) && descriptor.ActionName == nameof(SystemController.Shutdown));

            action.Metadata.GetMetadata<VersionedApiControllerAttribute>().Should().NotBeNull();
            action.Metadata.GetMetadata<HttpMethodMetadata>().HttpMethods.Should().ContainSingle().Which.Should().Be("POST");
        }
    }
}
