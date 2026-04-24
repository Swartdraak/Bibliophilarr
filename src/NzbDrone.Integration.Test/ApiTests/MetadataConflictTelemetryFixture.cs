using System.Net;
using System.Net.Http;
using Bibliophilarr.Api.V1.Metadata;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Integration.Test.Client;

namespace NzbDrone.Integration.Test.ApiTests
{
    [TestFixture]
    public class MetadataConflictTelemetryFixture : IntegrationTest
    {
        private ClientBase _telemetry;

        protected override void InitRestClients()
        {
            base.InitRestClients();

            _telemetry = new ClientBase(RestClient, ApiKey, "metadata/conflicts/telemetry");
        }

        [Test]
        public void should_expose_conflict_telemetry_snapshot_over_http()
        {
            var request = _telemetry.BuildRequest();
            request.Method = HttpMethod.Get;

            var response = _telemetry.Execute<MetadataConflictTelemetryResource>(request, HttpStatusCode.OK);

            response.Should().NotBeNull();
            response.TotalDecisions.Should().Be(0);
            response.DecisionsByReason.Should().NotBeNull().And.BeEmpty();
            response.DecisionsByProvider.Should().NotBeNull().And.BeEmpty();
            response.FieldSelectionsByProvider.Should().NotBeNull().And.BeEmpty();
        }
    }
}
