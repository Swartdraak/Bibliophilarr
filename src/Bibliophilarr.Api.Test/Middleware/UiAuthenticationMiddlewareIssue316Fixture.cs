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
    // Issue #316: the documented [AllowAnonymous] JSON liveness endpoint GET /ping was
    // shadowed to the SPA shell because UiAuthenticationMiddleware classified /ping as a
    // UI-shell GET (its exclusion list only covered /api/, /login, /content/). Under
    // AuthenticationRequired=Enabled the Forms challenge's LoginPath falls through to the
    // SPA catch-all, so anonymous GET /ping returned 200 + index.html instead of the
    // PingController's JSON {"Status":"OK"}.
    //
    // These fixtures are the durable regression guards: anonymous /ping must pass through
    // the middleware unchallenged so the [AllowAnonymous] controller response is served,
    // while the #313 auth-gate behavior (Enabled + anonymous UI -> Forms challenge, 401)
    // is preserved for real UI-shell paths.
    [TestFixture]
    public class UiAuthenticationMiddlewareIssue316Fixture
    {
        [Test]
        public void enabled_mode_anonymous_ping_get_should_pass_through_not_be_challenged()
        {
            var (context, auth, next, nextState, config) = CreatePipes(
                AuthenticationRequiredType.Enabled,
                remoteIp: "203.0.113.10",
                path: "/ping",
                isAuthenticated: false,
                trustCgnat: false);

            var mw = new Bibliophilarr.Http.Middleware.UiAuthenticationMiddleware(next, config);
            mw.InvokeAsync(context).GetAwaiter().GetResult();

            nextState.Called.Should().BeTrue(
                "GET /ping is [AllowAnonymous] (PingController) and must reach the pipeline so its JSON response is served");
            auth.ChallengedWith.Should().BeEmpty(
                "/ping must not be treated as a UI-shell page; a Forms challenge would shadow it to the SPA shell (issue #316)");
            auth.ForbiddenWith.Should().BeEmpty();
        }

        [Test]
        public void disabled_for_local_anonymous_ping_get_should_pass_through()
        {
            var (context, auth, next, nextState, config) = CreatePipes(
                AuthenticationRequiredType.DisabledForLocalAddresses,
                remoteIp: "203.0.113.10",
                path: "/ping",
                isAuthenticated: false,
                trustCgnat: false);

            var mw = new Bibliophilarr.Http.Middleware.UiAuthenticationMiddleware(next, config);
            mw.InvokeAsync(context).GetAwaiter().GetResult();

            nextState.Called.Should().BeTrue();
            auth.ChallengedWith.Should().BeEmpty();
        }

        [Test]
        public void enabled_mode_authed_ping_get_should_pass_through()
        {
            var (context, auth, next, nextState, config) = CreatePipes(
                AuthenticationRequiredType.Enabled,
                remoteIp: "203.0.113.10",
                path: "/ping",
                isAuthenticated: true,
                trustCgnat: false);

            var mw = new Bibliophilarr.Http.Middleware.UiAuthenticationMiddleware(next, config);
            mw.InvokeAsync(context).GetAwaiter().GetResult();

            nextState.Called.Should().BeTrue();
            auth.ChallengedWith.Should().BeEmpty();
        }

        [Test]
        public void enabled_mode_anonymous_ui_shell_paths_still_challenge_with_forms()
        {
            // #313/#315 auth-gate behavior must be unchanged by the /ping exclusion:
            // real UI-shell GETs under Enabled + anonymous are still challenged (401).
            foreach (var path in new[] { "/", "/library", "/calendar", "/settings", "/want" })
            {
                var (context, auth, next, nextState, config) = CreatePipes(
                    AuthenticationRequiredType.Enabled,
                    remoteIp: "203.0.113.10",
                    path: path,
                    isAuthenticated: false,
                    trustCgnat: false);

                var mw = new Bibliophilarr.Http.Middleware.UiAuthenticationMiddleware(next, config);
                mw.InvokeAsync(context).GetAwaiter().GetResult();

                nextState.Called.Should().BeFalse($"{path} (UI shell) must still be challenged before the pipeline");
                auth.ChallengedWith.Should().Contain("Forms", $"{path} must stay a Forms challenge -> 401");
                auth.ForbiddenWith.Should().BeEmpty($"{path} must not be Forbid'd (would yield 403)");
            }
        }

        [Test]
        public void ping_like_paths_and_methods_are_not_exempted()
        {
            // The exclusion is for the EXACT liveness path only: /ping/extra must still be a
            // UI-shell GET (SPA catch-all) and must not inherit the exemption.
            var (context, auth, next, nextState, config) = CreatePipes(
                AuthenticationRequiredType.Enabled,
                remoteIp: "203.0.113.10",
                path: "/ping/extra",
                isAuthenticated: false,
                trustCgnat: false);

            var mw = new Bibliophilarr.Http.Middleware.UiAuthenticationMiddleware(next, config);
            mw.InvokeAsync(context).GetAwaiter().GetResult();

            nextState.Called.Should().BeFalse("/ping/extra is not the liveness route; it must still be governed as a UI shell");
            auth.ChallengedWith.Should().Contain("Forms");
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
                context.User = new ClaimsPrincipal(new ClaimsIdentity(
                    new[] { new Claim("AuthType", "Forms") },
                    "Cookies",
                    "user",
                    "identifier"));
            }

            var config = new Mock<IConfigFileProvider>();
            config.Setup(c => c.AuthenticationRequired).Returns(authRequired);
            config.Setup(c => c.TrustCgnatIpAddresses).Returns(trustCgnat);

            var auth = new AuthRecorder();
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

            public Task<AuthenticateResult> AuthenticateAsync(HttpContext context, string scheme = null) =>
                Task.FromResult(AuthenticateResult.NoResult());

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

        private sealed class ServiceProviderRecorder : IServiceProvider
        {
            private readonly AuthRecorder _auth;

            public ServiceProviderRecorder(AuthRecorder auth)
            {
                _auth = auth;
            }

            public object GetService(Type serviceType)
            {
                return serviceType == typeof(IAuthenticationService) ? _auth : null;
            }
        }
    }
}
