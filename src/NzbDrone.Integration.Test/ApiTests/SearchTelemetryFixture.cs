using System.Net;
using System.Net.Http;
using Bibliophilarr.Api.V1.Search;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Integration.Test.Client;

namespace NzbDrone.Integration.Test.ApiTests
{
    [TestFixture]
    public class SearchTelemetryFixture : IntegrationTest
    {
        private ClientBase _telemetry;

        protected override void InitRestClients()
        {
            base.InitRestClients();

            _telemetry = new ClientBase(RestClient, ApiKey, "diagnostics/search/telemetry");
        }

        [Test]
        public void should_expose_search_telemetry_snapshot_over_http()
        {
            var request = _telemetry.BuildRequest();
            request.Method = HttpMethod.Get;

            var response = _telemetry.Execute<SearchTelemetryResource>(request, HttpStatusCode.OK);

            response.Should().NotBeNull();
            response.UnsupportedEntityCount.Should().BeGreaterOrEqualTo(0);
            response.UnsupportedEntityTypes.Should().NotBeNull();
            response.Terms.Should().NotBeNull();
        }
    }
}
