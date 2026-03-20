using System.Net;
using FluentAssertions;
using NUnit.Framework;
using RestSharp;

namespace NzbDrone.Integration.Test.ApiTests
{
    [TestFixture]
    public class HostConfigAuthorizationFixture : IntegrationTest
    {
        [Test]
        public void get_host_config_should_require_api_key()
        {
            var anonymousClient = new RestClient(RootUrl + "api/v1/");
            var request = new RestRequest("config/host", Method.GET);

            var response = anonymousClient.Execute(request);

            response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }

        [Test]
        public void put_host_config_should_require_api_key()
        {
            var config = HostConfig.GetSingle();

            var anonymousClient = new RestClient(RootUrl + "api/v1/");
            var request = new RestRequest("config/host/{id}", Method.PUT);
            request.AddUrlSegment("id", config.Id);
            request.AddJsonBody(config);

            var response = anonymousClient.Execute(request);

            response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }

        [Test]
        public void authorized_get_and_put_should_work_and_not_return_password_material()
        {
            var config = HostConfig.GetSingle();
            config.ConsoleLogLevel = "Info";

            HostConfig.Put(config, HttpStatusCode.Accepted);

            var updated = HostConfig.GetSingle();

            updated.ConsoleLogLevel.Should().Be("Info");
            updated.Password.Should().BeEmpty();
            updated.PasswordConfirmation.Should().BeEmpty();
        }

        [Test]
        public void first_run_none_authentication_mode_should_remain_valid_for_authorized_requests()
        {
            var config = HostConfig.GetSingle();
            config.AuthenticationMethod = NzbDrone.Core.Authentication.AuthenticationType.None;
            config.AuthenticationRequired = NzbDrone.Core.Authentication.AuthenticationRequiredType.Enabled;

            HostConfig.Put(config, HttpStatusCode.Accepted);

            var updated = HostConfig.GetSingle();

            updated.AuthenticationMethod.Should().Be(NzbDrone.Core.Authentication.AuthenticationType.None);
            updated.Password.Should().BeEmpty();
        }
    }
}
