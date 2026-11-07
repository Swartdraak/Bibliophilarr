using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Configuration;

namespace NzbDrone.Api.Test.Authentication
{
    [TestFixture]
    public class UiAuthorizationHandlerIssue349Fixture
    {
        // Issue #349 DEFECT-FORMS-SESSION-POST-RESTART-NONACCEPT:
        // With AuthenticationRequired == Enabled, a user that already carries a valid Forms
        // session (valid BibliophilarrAuth cookie; Auth-Success logged) must SATISFY the UI
        // BypassableDenyAnonymous requirement so the in-session UI shell (GET / and the SPA
        // routes) returns 200 - both in the same process and after a SIGTERM restart. The
        // previous handler implementation called context.Fail() unconditionally in the Enabled
        // branch, so an authenticated in-session UI request failed the UI policy and returned
        // 401 while API-key auth in the same process returned 200.
        [Test]
        public void enabled_mode_should_allow_authenticated_forms_user()
        {
            var handler = CreateHandler(NzbDrone.Core.Authentication.AuthenticationRequiredType.Enabled, false);
            var context = CreateAuthenticatedContext("203.0.113.10");
            handler.HandleAsync(context).GetAwaiter().GetResult();
            context.HasSucceeded.Should().BeTrue(
                "Enabled mode must accept an authenticated Forms session (issue #349 AC1/AC2)");
        }

        [Test]
        public void enabled_mode_should_allow_authenticated_local_user()
        {
            var handler = CreateHandler(NzbDrone.Core.Authentication.AuthenticationRequiredType.Enabled, false);
            var context = CreateAuthenticatedContext("127.0.0.1");
            handler.HandleAsync(context).GetAwaiter().GetResult();
            context.HasSucceeded.Should().BeTrue(
                "Enabled mode must accept an authenticated local Forms session (issue #349)");
        }

        [Test]
        public void enabled_mode_still_denies_anonymous_user()
        {
            // #313 A1/A2 contract preserved: anonymous users are still denied.
            var handler = CreateHandler(NzbDrone.Core.Authentication.AuthenticationRequiredType.Enabled, false);
            var context = CreateContext("203.0.113.10");
            handler.HandleAsync(context).GetAwaiter().GetResult();
            context.HasSucceeded.Should().BeFalse("Enabled mode must still deny anonymous access (#313)");
        }

        [Test]
        public void disabled_for_local_still_allows_authenticated_user()
        {
            var handler = CreateHandler(NzbDrone.Core.Authentication.AuthenticationRequiredType.DisabledForLocalAddresses, false);
            var context = CreateAuthenticatedContext("203.0.113.10");
            handler.HandleAsync(context).GetAwaiter().GetResult();
            context.HasSucceeded.Should().BeTrue("Authenticated users are allowed regardless of mode");
        }

        private static NzbDrone.Http.Authentication.UiAuthorizationHandler CreateHandler(
            NzbDrone.Core.Authentication.AuthenticationRequiredType authRequired,
            bool trustCgnat)
        {
            var config = new Mock<IConfigFileProvider>();
            config.Setup(c => c.AuthenticationRequired).Returns(authRequired);
            config.Setup(c => c.TrustCgnatIpAddresses).Returns(trustCgnat);
            return new NzbDrone.Http.Authentication.UiAuthorizationHandler(config.Object);
        }

        private static AuthorizationHandlerContext CreateContext(string remoteIp)
        {
            var httpContext = new DefaultHttpContext();
            httpContext.Connection.RemoteIpAddress = System.Net.IPAddress.Parse(remoteIp);
            var requirement = new NzbDrone.Http.Authentication.BypassableDenyAnonymousAuthorizationRequirement();
            var policy = new AuthorizationPolicyBuilder().AddRequirements(requirement).Build();
            return new AuthorizationHandlerContext(policy.Requirements, null, httpContext);
        }

        private static AuthorizationHandlerContext CreateAuthenticatedContext(string remoteIp)
        {
            // AuthorizationHandlerContext.User is derived from its AuthorizationPolicy when the
            // resource is an HttpContext (the framework caches the principal via the policy's
            // AuthenticationSchemes / callback). Build the policy with an explicit auth scheme so
            // the context materializes the authenticated Forms principal, then supply the
            // ClaimsPrincipal as the Resource (Resource is the only settable slot).
            var httpContext = new DefaultHttpContext();
            httpContext.Connection.RemoteIpAddress = System.Net.IPAddress.Parse(remoteIp);
            var identity = new System.Security.Claims.ClaimsIdentity(
                new[] { new System.Security.Claims.Claim("name", "testuser") }, "Forms");
            var principal = new System.Security.Claims.ClaimsPrincipal(identity);
            var requirement = new NzbDrone.Http.Authentication.BypassableDenyAnonymousAuthorizationRequirement();
            var policy = new AuthorizationPolicyBuilder().AddRequirements(requirement).Build();
            return new AuthorizationHandlerContext(policy.Requirements, principal, httpContext);
        }
    }
}
