using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Configuration;

namespace NzbDrone.Api.Test.Authentication
{
    [TestFixture]
    public class BootstrapControllerIssue313Fixture
    {
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
            return (controller, authService);
        }
    }
}
