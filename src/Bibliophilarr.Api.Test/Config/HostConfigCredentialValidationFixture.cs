using System.Linq;
using Bibliophilarr.Api.V1.Config;
using Microsoft.AspNetCore.Mvc;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Core.Authentication;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Validation.Paths;

namespace NzbDrone.Api.Test.Config
{
    [TestFixture]
    public class HostConfigCredentialValidationFixture
    {
        [TestCase(AuthenticationType.Forms, "", "")]
        [TestCase(AuthenticationType.Basic, null, null)]
        [TestCase(AuthenticationType.Forms, " ", null)]
        public void should_save_unrelated_settings_without_replacing_the_existing_password(
            AuthenticationType method, string password, string confirmation)
        {
            var user = new User { Username = "configured", Password = "stored-test-hash" };
            var users = new Mock<IUserService>();
            users.Setup(service => service.FindUser()).Returns(user);
            var controller = new ValidationController(method, users.Object);
            var resource = BuildResource(method, "Configured", password, confirmation);

            Assert.That(controller.ValidationErrors(resource), Is.Empty);
            Assert.That(controller.SaveHostConfig(resource).Result, Is.TypeOf<AcceptedAtActionResult>());
            users.Verify(service => service.Upsert(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        }

        [TestCase(AuthenticationType.None, AuthenticationType.Forms, true, "configured")]
        [TestCase(AuthenticationType.Forms, AuthenticationType.Basic, true, "configured")]
        [TestCase(AuthenticationType.Forms, AuthenticationType.Forms, false, "configured")]
        [TestCase(AuthenticationType.Forms, AuthenticationType.Forms, true, "different")]
        public void should_require_password_for_new_credential_configuration(
            AuthenticationType previousMethod, AuthenticationType requestedMethod, bool hasUser, string username)
        {
            var users = new Mock<IUserService>();
            var user = hasUser ? new User { Username = "configured", Password = "stored-test-hash" } : null;
            users.Setup(service => service.FindUser()).Returns(user);
            var controller = new ValidationController(previousMethod, users.Object);
            var resource = BuildResource(requestedMethod, username, null, null);

            Assert.That(controller.ValidationErrors(resource), Does.Contain(nameof(HostConfigResource.Password)));
        }

        [TestCase("new-password", null, nameof(HostConfigResource.PasswordConfirmation))]
        [TestCase("new-password", "different", nameof(HostConfigResource.PasswordConfirmation))]
        [TestCase(null, "new-password", nameof(HostConfigResource.Password))]
        [TestCase("stored-test-hash", null, nameof(HostConfigResource.PasswordConfirmation))]
        public void should_require_a_new_password_and_matching_confirmation_if_either_is_entered(
            string password, string confirmation, string expectedError)
        {
            var users = new Mock<IUserService>();
            users.Setup(service => service.FindUser())
                .Returns(new User { Username = "configured", Password = "stored-test-hash" });
            var controller = new ValidationController(AuthenticationType.Forms, users.Object);
            var resource = BuildResource(AuthenticationType.Forms, "configured", password, confirmation);

            Assert.That(controller.ValidationErrors(resource), Does.Contain(expectedError));
        }

        [Test]
        public void should_save_matching_credentials_and_keep_password_fields_empty_on_read()
        {
            var users = new Mock<IUserService>();
            users.Setup(service => service.FindUser())
                .Returns(new User { Username = "configured", Password = "stored-test-hash" });
            var controller = new ValidationController(AuthenticationType.None, users.Object);
            var resource = BuildResource(AuthenticationType.Forms, "configured", "new-password", "new-password");

            Assert.That(controller.ValidationErrors(resource), Is.Empty);
            Assert.That(controller.SaveHostConfig(resource).Result, Is.TypeOf<AcceptedAtActionResult>());
            users.Verify(service => service.Upsert("configured", "new-password"), Times.Once);

            var read = controller.GetHostConfig();
            Assert.That(read.Username, Is.EqualTo("configured"));
            Assert.That(read.Password, Is.Empty);
            Assert.That(read.PasswordConfirmation, Is.Empty);
        }

        private static HostConfigResource BuildResource(
            AuthenticationType method, string username, string password, string confirmation)
        {
            return new HostConfigResource
            {
                Id = 1,
                BindAddress = "127.0.0.1",
                Port = 8787,
                UrlBase = string.Empty,
                InstanceName = "Bibliophilarr Fixture",
                Branch = "develop",
                BackupFolder = "Backups",
                BackupInterval = 7,
                BackupRetention = 28,
                AuthenticationMethod = method,
                Username = username,
                Password = password,
                PasswordConfirmation = confirmation
            };
        }

        private sealed class ValidationController : HostConfigController
        {
            public ValidationController(AuthenticationType method, IUserService users)
                : base(Mock.Of<IConfigFileProvider>(config => config.AuthenticationMethod == method),
                    Mock.Of<IConfigService>(),
                    users,
                    new FileExistsValidator(Mock.Of<IDiskProvider>()))
            {
            }

            public string[] ValidationErrors(HostConfigResource resource)
            {
                return SharedValidator.Validate(resource).Errors.Select(error => error.PropertyName).ToArray();
            }
        }
    }
}
