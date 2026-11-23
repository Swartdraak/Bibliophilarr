using System;
using System.Linq;
using System.Reflection;
using System.Security.Claims;
using System.Threading.Tasks;
using Bibliophilarr.Api.V1.Books;
using Bibliophilarr.Api.V1.Bookshelf;
using Bibliophilarr.Api.V1.History;
using Bibliophilarr.Api.V1.Indexers;
using Bibliophilarr.Api.V1.ManualImport;
using Bibliophilarr.Api.V1.Queue;
using Bibliophilarr.Api.V1.System;
using Bibliophilarr.Api.V1.System.Backup;
using Bibliophilarr.Api.V1.Update;
using Bibliophilarr.Http;
using Bibliophilarr.Http.Authentication;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Configuration;
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

        [TestCase("X-Api-Key")]
        [TestCase("Authorization")]
        [TestCase("apikey")]
        public async Task should_preserve_supported_credentials_on_a_versioned_write(string credentialSource)
        {
            const string key = "local-fixture-key";
            var registrations = new ServiceCollection();
            registrations.AddLogging();
            registrations.AddSingleton(Mock.Of<IConfigFileProvider>(config => config.ApiKey == key));
            registrations.AddAuthentication().AddApiKey("API", options =>
            {
                options.HeaderName = "X-Api-Key";
                options.QueryName = "apikey";
            });

            using var services = registrations.BuildServiceProvider();
            using var scope = services.CreateScope();
            var context = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
            context.Request.Method = HttpMethods.Post;
            context.SetEndpoint(new Endpoint(_ => Task.CompletedTask,
                new EndpointMetadataCollection(new V1ApiControllerAttribute()),
                "API action"));
            if (credentialSource == "apikey")
            {
                context.Request.QueryString = QueryString.Create(credentialSource, key);
            }
            else
            {
                context.Request.Headers[credentialSource] = credentialSource == "Authorization" ? $"Bearer {key}" : key;
            }

            var called = false;
            var middleware = new ApiWriteAuthenticationMiddleware(_ =>
            {
                called = true;
                return Task.CompletedTask;
            });

            await middleware.InvokeAsync(context);

            called.Should().BeTrue();
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

        [TestCase("POST")]
        [TestCase("PUT")]
        [TestCase("PATCH")]
        [TestCase("DELETE")]
        public async Task should_require_an_explicit_api_key_with_an_authenticated_forms_session(string method)
        {
            var registrations = new ServiceCollection();
            registrations.AddLogging();
            registrations.AddSingleton(Mock.Of<IConfigFileProvider>(config => config.ApiKey == "local-fixture-key"));
            registrations.AddAuthentication().AddApiKey("API", options =>
            {
                options.HeaderName = "X-Api-Key";
                options.QueryName = "apikey";
            });

            using var services = registrations.BuildServiceProvider();
            using var scope = services.CreateScope();
            var context = new DefaultHttpContext
            {
                RequestServices = scope.ServiceProvider,
                User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("user", "fixture") }, "Forms"))
            };
            context.Request.Method = method;
            context.SetEndpoint(new Endpoint(_ => Task.CompletedTask, new EndpointMetadataCollection(new V1ApiControllerAttribute()), "API action"));
            var called = false;
            var middleware = new ApiWriteAuthenticationMiddleware(_ =>
            {
                called = true;
                return Task.CompletedTask;
            });

            await middleware.InvokeAsync(context);

            called.Should().BeFalse();
            context.Response.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
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
        public async Task should_require_an_explicit_credential_on_each_reviewed_post_route()
        {
            var reviewedActions = new (Type Controller, string Method, int Posts)[]
            {
                (typeof(BookshelfController), nameof(BookshelfController.UpdateAll), 1),
                (typeof(RenameBookController), nameof(RenameBookController.RenameFiles), 1),
                (typeof(RenameBookController), nameof(RenameBookController.RenameAuthor), 1),
                (typeof(HistoryController), nameof(HistoryController.MarkAsFailed), 1),
                (typeof(ReleaseController), nameof(ReleaseController.DownloadRelease), 1),
                (typeof(ReleasePushController), nameof(ReleasePushController.Create), 1),
                (typeof(ManualImportController), nameof(ManualImportController.UpdateItems), 1),
                (typeof(QueueActionController), nameof(QueueActionController.Grab), 2),
                (typeof(BackupController), nameof(BackupController.Restore), 1),
                (typeof(BackupController), nameof(BackupController.UploadAndRestore), 1),
                (typeof(SystemController), nameof(SystemController.Shutdown), 1),
                (typeof(SystemController), nameof(SystemController.Restart), 1),
                (typeof(UpdateController), nameof(UpdateController.CheckPublicRelease), 1)
            };
            var builder = WebApplication.CreateBuilder();
            builder.Services.AddSingleton(Mock.Of<IConfigFileProvider>(config => config.ApiKey == "local-fixture-key"));
            builder.Services.AddAuthentication().AddApiKey("API", options =>
            {
                options.HeaderName = "X-Api-Key";
                options.QueryName = "apikey";
            });
            builder.Services.AddControllers().AddApplicationPart(typeof(SystemController).Assembly);
            await using var app = builder.Build();
            app.MapControllers();

            var endpoints = ((IEndpointRouteBuilder)app).DataSources.SelectMany(source => source.Endpoints).ToArray();
            foreach (var (controller, method, posts) in reviewedActions)
            {
                var actions = endpoints.Where(endpoint =>
                    endpoint.Metadata.GetMetadata<ControllerActionDescriptor>() is { } descriptor &&
                    descriptor.ControllerTypeInfo == controller && descriptor.MethodInfo.Name == method);
                actions.Should().HaveCount(posts, "{0}.{1} has this many reviewed POST routes", controller.Name, method);
                foreach (var action in actions)
                {
                    action.Metadata.GetMetadata<VersionedApiControllerAttribute>().Should().NotBeNull(
                        "{0} requires the explicit API credential", action.DisplayName);
                    action.Metadata.GetMetadata<HttpMethodMetadata>().HttpMethods.Should().Equal(HttpMethods.Post);

                    using var scope = app.Services.CreateScope();
                    var context = new DefaultHttpContext
                    {
                        RequestServices = scope.ServiceProvider,
                        User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("user", "fixture") }, "Forms"))
                    };
                    context.Request.Method = HttpMethods.Post;
                    context.SetEndpoint(action);

                    var reachedDownstream = false;
                    var middleware = new ApiWriteAuthenticationMiddleware(_ =>
                    {
                        reachedDownstream = true;
                        return Task.CompletedTask;
                    });

                    await middleware.InvokeAsync(context);

                    reachedDownstream.Should().BeFalse("{0} requires an explicit API credential", action.DisplayName);
                    context.Response.StatusCode.Should().Be(StatusCodes.Status401Unauthorized, "{0} refuses a Forms session alone", action.DisplayName);

                    await AssertRegisteredRequestForgeryFilter(action, app.Services);
                }
            }
        }

        private static async Task AssertRegisteredRequestForgeryFilter(Endpoint endpoint, IServiceProvider services)
        {
            const string key = "local-fixture-key";
            var descriptor = endpoint.Metadata.GetMetadata<ControllerActionDescriptor>();
            var filters = descriptor.FilterDescriptors.Select(item => item.Filter).ToList();
            var filter = filters.OfType<ValidateApiWriteAntiforgeryAttribute>()
                .Should().ContainSingle("{0} must enforce request-forgery protection in MVC", endpoint.DisplayName).Which;

            foreach (var credential in new[] { null, "X-Api-Key", "Authorization", "apikey" })
            {
                using var scope = services.CreateScope();
                var context = new DefaultHttpContext
                {
                    RequestServices = scope.ServiceProvider,
                    User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("user", "fixture") }, "Forms"))
                };
                context.Request.Method = HttpMethods.Post;
                context.SetEndpoint(endpoint);
                if (credential == "apikey")
                {
                    context.Request.QueryString = QueryString.Create(credential, key);
                }
                else if (credential != null)
                {
                    context.Request.Headers[credential] = credential == "Authorization" ? $"Bearer {key}" : key;
                }

                var actionContext = new ActionContext(context, new RouteData(), descriptor);
                var filterContext = new AuthorizationFilterContext(actionContext, filters);
                await filter.OnAuthorizationAsync(filterContext);

                if (credential == null)
                {
                    var challenge = filterContext.Result.Should().BeOfType<ChallengeResult>().Which;
                    challenge.AuthenticationSchemes.Should().Equal("API");
                    await challenge.ExecuteResultAsync(actionContext);
                    context.Response.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
                }
                else
                {
                    filterContext.Result.Should().BeNull("{0} remains accepted by {1}", credential, endpoint.DisplayName);
                }
            }
        }
    }
}
