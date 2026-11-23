using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Configuration;

namespace NzbDrone.Api.Test.Authentication
{
    [TestFixture]
    public class UiAuthorizationHandlerIssue313Fixture
    {
        // Issue #313 DEFECT-AUTH-POLICY-UNAUTH-200:
        // When AuthenticationRequired == Enabled, the UI policy must DENY anonymous
        // users. Before the fix, HandleRequirementAsync fell through silently when
        // Enabled, so the SPA shell was served (200) to unauthenticated users.
        [Test]
        public void enabled_mode_should_deny_anonymous_user()
        {
            var handler = CreateHandler(NzbDrone.Core.Authentication.AuthenticationRequiredType.Enabled, false);
            var context = CreateContext("203.0.113.10");
            handler.HandleAsync(context).GetAwaiter().GetResult();
            context.HasSucceeded.Should().BeFalse("Enabled mode must deny anonymous access (issue #313 A1/A2)");
        }

        [Test]
        public void enabled_mode_should_deny_even_from_local_address()
        {
            var handler = CreateHandler(NzbDrone.Core.Authentication.AuthenticationRequiredType.Enabled, true);
            var context = CreateContext("127.0.0.1");
            handler.HandleAsync(context).GetAwaiter().GetResult();
            context.HasSucceeded.Should().BeFalse("Enabled mode must deny even local addresses");
        }

        [Test]
        public void disabled_for_local_should_allow_local_address()
        {
            var handler = CreateHandler(NzbDrone.Core.Authentication.AuthenticationRequiredType.DisabledForLocalAddresses, false);
            var context = CreateContext("127.0.0.1");
            handler.HandleAsync(context).GetAwaiter().GetResult();
            context.HasSucceeded.Should().BeTrue("DisabledForLocalAddresses must allow local-IP access");
        }

        [Test]
        public void disabled_for_local_should_deny_remote_address()
        {
            var handler = CreateHandler(NzbDrone.Core.Authentication.AuthenticationRequiredType.DisabledForLocalAddresses, false);
            var context = CreateContext("203.0.113.10");
            handler.HandleAsync(context).GetAwaiter().GetResult();
            context.HasSucceeded.Should().BeFalse("DisabledForLocalAddresses must deny non-local addresses");
        }

        [Test]
        public void disabled_for_local_should_allow_cgnat_when_trusted()
        {
            var handler = CreateHandler(NzbDrone.Core.Authentication.AuthenticationRequiredType.DisabledForLocalAddresses, true);
            var context = CreateContext("100.64.0.1");
            handler.HandleAsync(context).GetAwaiter().GetResult();
            context.HasSucceeded.Should().BeTrue("CGNAT must be allowed when TrustCgnatIpAddresses is set");
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
    }
}
