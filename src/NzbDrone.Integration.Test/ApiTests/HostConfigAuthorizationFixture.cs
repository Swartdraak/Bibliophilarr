using System.Net;
using System.Net.Http;
using NUnit.Framework;
using NzbDrone.Integration.Test.Client;

namespace NzbDrone.Integration.Test.ApiTests
{
    [TestFixture]
    public class HostConfigAuthorizationFixture : IntegrationTest
    {
        [Test]
        public void get_host_config_should_require_api_key()
        {
            var request = new SimpleRestRequest("config/host");
            var response = ExecuteAnonymousRequest(RootUrl + "api/v1/", request);

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
        }

        [Test]
        public void put_host_config_should_require_api_key()
        {
            var config = HostConfig.GetSingle();

            var request = new SimpleRestRequest("config/host/" + config.Id, HttpMethod.Put);
            request.AddJsonBody(config);

            var response = ExecuteAnonymousRequest(RootUrl + "api/v1/", request);

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
        }

        [Test]
        public void get_host_config_with_malformed_basic_auth_should_return_unauthorized()
        {
            var request = new SimpleRestRequest("config/host");
            request.AddHeader("Authorization", "Basic malformed-base64-value");

            var response = ExecuteAnonymousRequest(RootUrl + "api/v1/", request);

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
            Assert.That(response.StatusCode, Is.Not.EqualTo(HttpStatusCode.InternalServerError));
        }

        [Test]
        public void authorized_get_and_put_should_work_and_not_return_password_material()
        {
            var config = HostConfig.GetSingle();
            config.ConsoleLogLevel = "Info";

            HostConfig.Put(config, HttpStatusCode.Accepted);

            var updated = HostConfig.GetSingle();

            Assert.That(updated.ConsoleLogLevel, Is.EqualTo("Info"));
            Assert.That(updated.Password, Is.Empty);
            Assert.That(updated.PasswordConfirmation, Is.Empty);
        }

        [Test]
        public void first_run_none_authentication_mode_should_remain_valid_for_authorized_requests()
        {
            var config = HostConfig.GetSingle();
            config.AuthenticationMethod = NzbDrone.Core.Authentication.AuthenticationType.None;
            config.AuthenticationRequired = NzbDrone.Core.Authentication.AuthenticationRequiredType.Enabled;

            HostConfig.Put(config, HttpStatusCode.Accepted);

            var updated = HostConfig.GetSingle();

            Assert.That(updated.AuthenticationMethod, Is.EqualTo(NzbDrone.Core.Authentication.AuthenticationType.None));
            Assert.That(updated.Password, Is.Empty);
        }
    }
}
