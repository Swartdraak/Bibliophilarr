using System;
using System.Net;
using System.Reflection;
using Bibliophilarr.Http.Authentication;
using FluentAssertions;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Configuration;

namespace NzbDrone.Api.Test.Authentication
{
    [TestFixture]
    public class BootstrapControllerIssue313Fixture
    {
        [Test]
        public void bootstrap_should_issue_tokens_for_a_direct_terminal_client_and_validate_the_submission()
        {
            var (controller, _) = CreateController(existingUser: null);
            var antiforgery = new Mock<IAntiforgery>();
            antiforgery.Setup(service => service.GetAndStoreTokens(controller.HttpContext))
                .Returns(new AntiforgeryTokenSet("request-token", "cookie-token", "__RequestVerificationToken", "RequestVerificationToken"));

            var tokenAction = typeof(AuthenticationController).GetMethod(nameof(AuthenticationController.GetBootstrapToken));
            tokenAction.GetCustomAttribute<HttpGetAttribute>().Template.Should().Be("bootstrap");
            var result = controller.GetBootstrapToken(antiforgery.Object);

            result.Should().BeOfType<OkObjectResult>().Which.Value.Should().BeEquivalentTo(new
            {
                headerName = "RequestVerificationToken",
                requestToken = "request-token"
            });
            antiforgery.Verify(service => service.GetAndStoreTokens(controller.HttpContext), Times.Once);
            var submission = typeof(AuthenticationController).GetMethod(nameof(AuthenticationController.Bootstrap));
            submission.GetCustomAttribute<HttpPostAttribute>().Template.Should().Be("bootstrap");
            var validation = submission.GetCustomAttribute<ValidateAntiForgeryTokenAttribute>();
            tokenAction.GetCustomAttribute<DirectTerminalBootstrapAttribute>().Should().NotBeNull();
            submission.GetCustomAttribute<DirectTerminalBootstrapAttribute>().Order.Should().BeLessThan(validation.Order);
        }

        // Issue #313 DEFECT-AUTH-BOOTSTRAP-DEADLOCK:
        // An Enabled install with an empty Users table can never create its first
        // user because PUT /config/host is auth-gated. POST /bootstrap provides the
        // one-time first-user provisioning path.
        [Test]
        public void bootstrap_should_create_first_user_when_table_empty()
        {
            var (controller, authService) = CreateController(existingUser: null);
            var result = controller.Bootstrap(
                new Bibliophilarr.Http.Authentication.BootstrapResource { Username = "admin", Password = "MySecure123" });
            result.Should().BeOfType<OkObjectResult>();
            authService.Verify(a => a.AddUser("admin", "MySecure123"), Times.Once);
        }

        [Test]
        public void bootstrap_should_reject_when_user_already_exists()
        {
            var (controller, authService) = CreateController(existingUser: new NzbDrone.Core.Authentication.User { Username = "admin" });
            var result = controller.Bootstrap(
                new Bibliophilarr.Http.Authentication.BootstrapResource { Username = "attacker", Password = "AnotherSecret99" });
            result.Should().BeOfType<ConflictResult>();
            authService.Verify(a => a.AddUser(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        }

        [Test]
        public void bootstrap_should_reject_short_password()
        {
            var (controller, authService) = CreateController(existingUser: null);
            var result = controller.Bootstrap(
                new Bibliophilarr.Http.Authentication.BootstrapResource { Username = "admin", Password = "short" });
            result.Should().BeOfType<BadRequestObjectResult>();
            authService.Verify(a => a.AddUser(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        }

        [Test]
        public void bootstrap_should_reject_short_username()
        {
            var (controller, authService) = CreateController(existingUser: null);
            var result = controller.Bootstrap(
                new Bibliophilarr.Http.Authentication.BootstrapResource { Username = "ab", Password = "ValidPassword123" });
            result.Should().BeOfType<BadRequestObjectResult>();
            authService.Verify(a => a.AddUser(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        }

        [Test]
        public void bootstrap_should_reject_whitespace_username()
        {
            var (controller, authService) = CreateController(existingUser: null);
            var result = controller.Bootstrap(
                new Bibliophilarr.Http.Authentication.BootstrapResource { Username = "   ", Password = "ValidPassword123" });
            result.Should().BeOfType<BadRequestObjectResult>();
            authService.Verify(a => a.AddUser(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        }

        [TestCase(null, true)]
        [TestCase("X-Forwarded-For", false)]
        [TestCase("Forwarded", false)]
        [TestCase("Origin", false)]
        [TestCase("Sec-Fetch-Site", false)]
        public void terminal_bootstrap_should_only_accept_direct_terminal_requests(string header, bool allowed)
        {
            var context = new DefaultHttpContext();
            context.Connection.RemoteIpAddress = IPAddress.Loopback;
            context.Request.Host = new HostString("localhost");
            if (header != null)
            {
                context.Request.Headers[header] = "fixture-value";
            }

            FirstAccountSetup.CaptureRequest(context);
            var filterContext = new AuthorizationFilterContext(
                new ActionContext(context, new RouteData(), new ActionDescriptor()), Array.Empty<IFilterMetadata>());
            new DirectTerminalBootstrapAttribute().OnAuthorization(filterContext);

            if (allowed)
            {
                filterContext.Result.Should().BeNull();
            }
            else
            {
                filterContext.Result.Should().BeOfType<StatusCodeResult>().Which.StatusCode.Should().Be(403);
            }
        }

        private static (Bibliophilarr.Http.Authentication.AuthenticationController, Moq.Mock<Bibliophilarr.Http.Authentication.IAuthenticationService>) CreateController(
            NzbDrone.Core.Authentication.User existingUser)
        {
            var authService = new Mock<Bibliophilarr.Http.Authentication.IAuthenticationService>();
            authService.Setup(a => a.FindUser()).Returns(existingUser);
            authService.Setup(a => a.AddUser(It.IsAny<string>(), It.IsAny<string>()))
                .Returns<string, string>((u, p) => new NzbDrone.Core.Authentication.User { Username = u });
            var configProvider = new Mock<IConfigFileProvider>();
            var appFolderInfo = new Mock<NzbDrone.Common.EnvironmentInfo.IAppFolderInfo>();
            var controller = new Bibliophilarr.Http.Authentication.AuthenticationController(
                authService.Object, configProvider.Object, appFolderInfo.Object, null);
            var context = new DefaultHttpContext();
            context.Connection.RemoteIpAddress = IPAddress.Loopback;
            context.Request.Host = new HostString("localhost");
            FirstAccountSetup.CaptureRequest(context);
            controller.ControllerContext = new ControllerContext { HttpContext = context };
            return (controller, authService);
        }
    }
}
