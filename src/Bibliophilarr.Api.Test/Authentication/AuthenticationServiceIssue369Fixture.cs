using Bibliophilarr.Http.Authentication;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Authentication;
using NzbDrone.Core.Configuration;

namespace NzbDrone.Api.Test.Authentication
{
    // Issue #369 [AUTH/FORMS] Forms authentication flow broken: at first boot the app starts with
    // AuthenticationMethod=None, POST /bootstrap creates the first user, and the host-config flip to
    // Forms is persisted to config.xml. AuthenticationService used to cache AuthenticationMethod in a
    // process-lifetime static field set once in the constructor, so POST /login stayed rejected
    // (loginFailed=true, no cookie) even after the method was activated, until a restart. The fix
    // reads IConfigFileProvider.AuthenticationMethod per call. These fixtures pin that contract:
    // once the live config reports Forms, login succeeds for the bootstrapped user and still fails
    // for wrong credentials; while the live config reports None, login stays rejected.
    [TestFixture]
    public class AuthenticationServiceIssue369Fixture
    {
        private const string User = "admin";
        private const string Password = "MySecure123";

        [Test]
        public void login_should_return_bootstrapped_user_when_auth_method_is_forms()
        {
            // Auth method activated to Forms (as PUT /api/v1/config/host persists it) after boot.
            var service = CreateService(authMethod: AuthenticationType.Forms, userFound: true);

            var user = service.Login(null, User, Password);

            user.Should().NotBeNull();
            user.Username.Should().Be(User);
        }

        [Test]
        public void login_should_reject_wrong_password_when_auth_method_is_forms()
        {
            var service = CreateService(authMethod: AuthenticationType.Forms, userFound: false);

            service.Login(null, User, "WrongPassword1").Should().BeNull();
        }

        [Test]
        public void login_should_reject_when_auth_method_still_none()
        {
            // First-boot state (method not yet activated): login must stay rejected even for the
            // bootstrapped credentials. This is the negative case that must keep failing.
            var service = CreateService(authMethod: AuthenticationType.None, userFound: true);

            service.Login(null, User, Password).Should().BeNull();
        }

        [Test]
        public void login_should_reflect_live_config_change_after_first_boot_activation()
        {
            // The core #369 regression: a single service instance (constructed while config said
            // None, as on first boot) must observe a later in-process activation to Forms without a
            // restart. The static cache made the first call return null forever; the per-call read
            // must track the live provider.
            var (service, configProvider) = CreateServiceWithLiveConfig(initialMethod: AuthenticationType.None);

            service.Login(null, User, Password).Should().BeNull();

            // Host-config flip: config now reports Forms (SaveConfigDictionary clears the value cache).
            configProvider.SetupGet(c => c.AuthenticationMethod).Returns(AuthenticationType.Forms);

            var user = service.Login(null, User, Password);

            user.Should().NotBeNull();
            user.Username.Should().Be(User);
        }

        private static (AuthenticationService, Moq.Mock<IConfigFileProvider>) CreateServiceWithLiveConfig(
            AuthenticationType initialMethod, bool userFound = true)
        {
            var configProvider = new Moq.Mock<IConfigFileProvider>();
            configProvider.SetupGet(c => c.AuthenticationMethod).Returns(initialMethod);
            var userService = new Moq.Mock<IUserService>();
            userService.Setup(u => u.FindUser(User, Password))
                       .Returns(userFound ? new User { Username = User } : null);
            var service = new AuthenticationService(configProvider.Object, userService.Object);
            return (service, configProvider);
        }

        private static AuthenticationService CreateService(AuthenticationType authMethod, bool userFound)
        {
            var (service, _) = CreateServiceWithLiveConfig(authMethod, userFound);
            return service;
        }
    }
}
