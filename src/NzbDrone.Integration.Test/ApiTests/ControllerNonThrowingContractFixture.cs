using System.Net;
using System.Net.Http;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Integration.Test.Client;

namespace NzbDrone.Integration.Test.ApiTests
{
    [TestFixture]
    public class ControllerNonThrowingContractFixture : IntegrationTest
    {
        [Test]
        public void queue_item_lookup_should_return_not_found_instead_of_server_error()
        {
            var request = new SimpleRestRequest("queue/999999");
            var response = ExecuteRequest(request);

            response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        }

        [Test]
        public void queue_status_endpoint_should_return_ok()
        {
            var request = new SimpleRestRequest("queue/status");
            var response = ExecuteRequest(request);

            response.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        [Test]
        public void queue_details_lookup_should_return_not_found_instead_of_server_error()
        {
            var request = new SimpleRestRequest("queue/details/999999");
            var response = ExecuteRequest(request);

            response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        }

        [Test]
        public void health_lookup_should_return_not_found_instead_of_server_error()
        {
            var request = new SimpleRestRequest("health/999999");
            var response = ExecuteRequest(request);

            response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        }

        [Test]
        public void metadata_bulk_update_should_not_throw_not_implemented()
        {
            var request = new SimpleRestRequest("metadata/bulk", HttpMethod.Put);
            request.AddJsonBody(new { ids = new int[0] });

            var response = ExecuteRequest(request);

            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        }

        [Test]
        public void notification_bulk_update_should_not_throw_not_implemented()
        {
            var request = new SimpleRestRequest("notification/bulk", HttpMethod.Put);
            request.AddJsonBody(new { ids = new int[0] });

            var response = ExecuteRequest(request);

            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        }
    }
}
