using System.Net;
using FluentAssertions;
using NUnit.Framework;
using RestSharp;

namespace NzbDrone.Integration.Test.ApiTests
{
    [TestFixture]
    public class ControllerNonThrowingContractFixture : IntegrationTest
    {
        [Test]
        public void queue_item_lookup_should_return_not_found_instead_of_server_error()
        {
            var request = new RestRequest("queue/999999", Method.GET);
            var response = RestClient.Execute(request);

            response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        }

        [Test]
        public void queue_status_endpoint_should_return_ok()
        {
            var request = new RestRequest("queue/status", Method.GET);
            var response = RestClient.Execute(request);

            response.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        [Test]
        public void queue_details_lookup_should_return_not_found_instead_of_server_error()
        {
            var request = new RestRequest("queue/details/999999", Method.GET);
            var response = RestClient.Execute(request);

            response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        }

        [Test]
        public void health_lookup_should_return_not_found_instead_of_server_error()
        {
            var request = new RestRequest("health/999999", Method.GET);
            var response = RestClient.Execute(request);

            response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        }

        [Test]
        public void metadata_bulk_update_should_not_throw_not_implemented()
        {
            var request = new RestRequest("metadata/bulk", Method.PUT);
            request.AddJsonBody(new { ids = new int[0] });

            var response = RestClient.Execute(request);

            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        }

        [Test]
        public void notification_bulk_update_should_not_throw_not_implemented()
        {
            var request = new RestRequest("notification/bulk", Method.PUT);
            request.AddJsonBody(new { ids = new int[0] });

            var response = RestClient.Execute(request);

            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        }
    }
}
