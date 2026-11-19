using System;
using System.Collections.Generic;
using System.Net;
using System.Security.Claims;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Authentication;
using NzbDrone.Core.Configuration;

namespace NzbDrone.Api.Test.Middleware
{
    // Issue #313 acceptance A1/A2: unauth UI requests with AuthenticationRequired=Enabled
    // must be challenged (401), not ForbidAsync'd (403). This fixture exercises the new
    // UiAuthenticationMiddleware in isolation: it must call ChallengeAsync(Forms) for an
    // anonymous UI request in Enabled mode and NOT call Forbid (which would yield 403).
    [TestFixture]
    public class UiAuthenticationMiddlewareIssue313Fixture
    {
        [Test]
        public void enabled_mode_anonymous_ui_request_should_challenge_with_forms()
        {
            var (context, auth, next, nextState, config) = CreatePipes(
                AuthenticationRequiredType.Enabled,
                remoteIp: "203.0.113.10",
                path: "/library",
                isAuthenticated: false,
                trustCgnat: false);

            var mw = new Bibliophilarr.Http.Middleware.UiAuthenticationMiddleware(next, config);
            mw.InvokeAsync(context).GetAwaiter().GetResult();

            nextState.Called.Should().BeFalse("Enabled + anonymous UI must be challenged before reaching next");
            auth.ChallengedWith.Should().Contain("Forms", "UI denial must be a Forms challenge -> 401, not a 403 Forbid");
            auth.ForbiddenWith.Should().BeEmpty("Forbid would yield 403; A1/A2 require 401");
        }

        [Test]
        public void enabled_mode_anonymous_root_path_should_challenge_with_forms()
        {
            var (context, auth, next, nextState, config) = CreatePipes(
                AuthenticationRequiredType.Enabled,
                remoteIp: "203.0.113.10",
                path: "/",
                isAuthenticated: false,
                trustCgnat: false);

            var mw = new Bibliophilarr.Http.Middleware.UiAuthenticationMiddleware(next, config);
            mw.InvokeAsync(context).GetAwaiter().GetResult();

            nextState.Called.Should().BeFalse();
            auth.ChallengedWith.Should().Contain("Forms");
        }

        [Test]
        public void enabled_mode_authenticated_ui_request_should_pass_through()
        {
            var (context, auth, next, nextState, config) = CreatePipes(
                AuthenticationRequiredType.Enabled,
                remoteIp: "203.0.113.10",
                path: "/",
                isAuthenticated: true,
                trustCgnat: false);

            var mw = new Bibliophilarr.Http.Middleware.UiAuthenticationMiddleware(next, config);
            mw.InvokeAsync(context).GetAwaiter().GetResult();

            nextState.Called.Should().BeTrue("Authenticated UI requests must reach the pipeline (policy still applies)");
            auth.ChallengedWith.Should().BeEmpty("Authenticated users must not be challenged");
        }

        [Test]
        public void disabled_for_local_anonymous_local_address_should_pass_through()
        {
            var (context, auth, next, nextState, config) = CreatePipes(
                AuthenticationRequiredType.DisabledForLocalAddresses,
                remoteIp: "127.0.0.1",
                path: "/",
                isAuthenticated: false,
                trustCgnat: false);

            var mw = new Bibliophilarr.Http.Middleware.UiAuthenticationMiddleware(next, config);
            mw.InvokeAsync(context).GetAwaiter().GetResult();

            nextState.Called.Should().BeTrue("Local-address bypass must be preserved");
            auth.ChallengedWith.Should().BeEmpty("Local bypass must not challenge");
        }

        [Test]
        public void disabled_for_local_anonymous_remote_address_should_not_challenge()
        {
            // Remote address under DisabledForLocalAddresses is not a local bypass, but this is
            // also not an Enabled-mode UI denial — the middleware must not challenge here; the
            // UI policy (ForbidAsync) handles the denial in the authorization stage.
            var (context, auth, next, nextState, config) = CreatePipes(
                AuthenticationRequiredType.DisabledForLocalAddresses,
                remoteIp: "203.0.113.10",
                path: "/",
                isAuthenticated: false,
                trustCgnat: false);

            var mw = new Bibliophilarr.Http.Middleware.UiAuthenticationMiddleware(next, config);
            mw.InvokeAsync(context).GetAwaiter().GetResult();

            auth.ChallengedWith.Should().BeEmpty("DisabledForLocalAddresses is not an Enabled denial; no Forms challenge");
            auth.ForbiddenWith.Should().BeEmpty("Middleware must not Forbid either");
        }

        [Test]
        public void api_requests_should_not_be_challenged_by_ui_middleware()
        {
            var (context, auth, next, nextState, config) = CreatePipes(
                AuthenticationRequiredType.Enabled,
                remoteIp: "203.0.113.10",
                path: "/api/v1/books",
                isAuthenticated: false,
                trustCgnat: false);

            var mw = new Bibliophilarr.Http.Middleware.UiAuthenticationMiddleware(next, config);
            mw.InvokeAsync(context).GetAwaiter().GetResult();

            nextState.Called.Should().BeTrue("/api/* is out of scope for the UI middleware");
            auth.ChallengedWith.Should().BeEmpty("API scheme produces its own 401; UI middleware must not interfere");
        }

        [Test]
        public void login_and_content_should_pass_through()
        {
            foreach (var path in new[] { "/login", "/content/foo.js" })
            {
                var (context, auth, next, nextState, config) = CreatePipes(
                    AuthenticationRequiredType.Enabled,
                    remoteIp: "203.0.113.10",
                    path: path,
                    isAuthenticated: false,
                    trustCgnat: false);

                var mw = new Bibliophilarr.Http.Middleware.UiAuthenticationMiddleware(next, config);
                mw.InvokeAsync(context).GetAwaiter().GetResult();

                nextState.Called.Should().BeTrue($"{path} is [AllowAnonymous] and must pass through");
                auth.ChallengedWith.Should().BeEmpty($"{path} must not be challenged");
            }
        }

        [Test]
        public void bootstrap_post_should_pass_through()
        {
            // POST /bootstrap is [AllowAnonymous] (one-time first-user provisioning). The UI
            // middleware must NOT challenge it — only GET SPA-shell requests are governed.
            var (context, auth, next, nextState, config) = CreatePipes(
                AuthenticationRequiredType.Enabled,
                remoteIp: "203.0.113.10",
                path: "/bootstrap",
                method: "POST",
                isAuthenticated: false,
                trustCgnat: false);

            var mw = new Bibliophilarr.Http.Middleware.UiAuthenticationMiddleware(next, config);
            mw.InvokeAsync(context).GetAwaiter().GetResult();

            nextState.Called.Should().BeTrue("POST /bootstrap must reach the pipeline (AllowAnonymous)");
            auth.ChallengedWith.Should().BeEmpty("POST /bootstrap must not be challenged by the UI middleware");
        }

        [Test]
        public void login_post_should_pass_through()
        {
            var (context, auth, next, nextState, config) = CreatePipes(
                AuthenticationRequiredType.Enabled,
                remoteIp: "203.0.113.10",
                path: "/login",
                method: "POST",
                isAuthenticated: false,
                trustCgnat: false);

            var mw = new Bibliophilarr.Http.Middleware.UiAuthenticationMiddleware(next, config);
            mw.InvokeAsync(context).GetAwaiter().GetResult();

            nextState.Called.Should().BeTrue("POST /login must reach the pipeline (AllowAnonymous)");
            auth.ChallengedWith.Should().BeEmpty("POST /login must not be challenged by the UI middleware");
        }

        private static (HttpContext, AuthRecorder, RequestDelegate, NextState, IConfigFileProvider) CreatePipes(
            AuthenticationRequiredType authRequired,
            string remoteIp,
            string path,
            string method = "GET",
            bool isAuthenticated = false,
            bool trustCgnat = false)
        {
            var context = new DefaultHttpContext();
            context.Request.Path = new PathString(path);
            context.Request.Method = method;
            context.Connection.RemoteIpAddress = IPAddress.Parse(remoteIp);

            if (isAuthenticated)
            {
                var identity = new ClaimsIdentity(
                    new[] { new Claim("AuthType", "Forms") },
                    "Cookies",
                    "user",
                    "identifier");
                context.User = new ClaimsPrincipal(identity);
            }

            var config = new Mock<IConfigFileProvider>();
            config.Setup(c => c.AuthenticationRequired).Returns(authRequired);
            config.Setup(c => c.TrustCgnatIpAddresses).Returns(trustCgnat);

            var auth = new AuthRecorder();

            // ChallengeAsync/ForbidAsync resolve IAuthenticationService from HttpContext.RequestServices,
            // so we must provide a service provider that returns our recorder.
            context.RequestServices = new ServiceProviderRecorder(auth);

            var nextState = new NextState();
            RequestDelegate next = ctx =>
            {
                nextState.Called = true;
                return Task.CompletedTask;
            };

            return (context, auth, next, nextState, config.Object);
        }

        private sealed class NextState
        {
            public bool Called { get; set; }
        }

        private sealed class AuthRecorder : IAuthenticationService
        {
            public List<string> ChallengedWith { get; } = new List<string>();
            public List<string> ForbiddenWith { get; } = new List<string>();

            public Task<AuthenticateResult> AuthenticateAsync(HttpContext context, string scheme = null)
            {
                return Task.FromResult<AuthenticateResult>(AuthenticateResult.NoResult());
            }

            public Task ChallengeAsync(HttpContext context, string scheme, AuthenticationProperties properties = null)
            {
                ChallengedWith.Add(scheme ?? "(default)");
                return Task.CompletedTask;
            }

            public Task ForbidAsync(HttpContext context, string scheme, AuthenticationProperties properties = null)
            {
                ForbiddenWith.Add(scheme ?? "(default)");
                return Task.CompletedTask;
            }

            public Task SignInAsync(HttpContext context, string scheme, ClaimsPrincipal principal, AuthenticationProperties properties = null) =>
                Task.CompletedTask;

            public Task SignOutAsync(HttpContext context, string scheme, AuthenticationProperties properties = null) =>
                Task.CompletedTask;
        }

        // Minimal IServiceProvider that hands back the AuthRecorder for IAuthenticationService,
        // so the framework's ChallengeAsync/ForbidAsync extensions (which resolve the service
        // from HttpContext.RequestServices) hit our recorder instead of throwing.
        private sealed class ServiceProviderRecorder : IServiceProvider
        {
            private readonly AuthRecorder _auth;

            public ServiceProviderRecorder(AuthRecorder auth)
            {
                _auth = auth;
            }

            public object GetService(Type serviceType)
            {
                if (serviceType == typeof(IAuthenticationService))
                {
                    return _auth;
                }

                return null;
            }
        }
    }
}
