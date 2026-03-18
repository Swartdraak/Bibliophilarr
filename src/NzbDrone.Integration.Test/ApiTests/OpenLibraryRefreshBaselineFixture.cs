using System.Collections.Generic;
using System.Linq;
using System.Net;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Integration.Test.Client;
using RestSharp;

namespace NzbDrone.Integration.Test.ApiTests
{
    [TestFixture]
    public class OpenLibraryRefreshBaselineFixture : IntegrationTest
    {
        private ClientBase _metadataHealth;

        private class MetadataProviderHealthResourceEnvelope
        {
            public string ProviderName { get; set; }
            public int Priority { get; set; }
            public bool IsEnabled { get; set; }
        }

        protected override void InitRestClients()
        {
            base.InitRestClients();
            _metadataHealth = new ClientBase(RestClient, ApiKey, "metadata/providers/health");
        }

        [Test]
        public void should_include_openlibrary_in_provider_health_projection()
        {
            var request = _metadataHealth.BuildRequest();
            request.Method = Method.GET;

            var health = _metadataHealth.Execute<List<MetadataProviderHealthResourceEnvelope>>(request, HttpStatusCode.OK);

            health.Should().NotBeNull();
            health.Should().Contain(x => x.ProviderName == "OpenLibrary");
            health.Should().Contain(x => x.ProviderName == "BookInfo");
        }

        [Test]
        public void should_complete_openlibrary_backfill_command_on_startup()
        {
            Commands.WaitAll();
            var commands = Commands.All();

            commands.Should().NotBeNull();
            commands.Should().Contain(x => x.Name == "BackfillOpenLibraryIds");
            commands.Where(x => x.Name == "BackfillOpenLibraryIds").Should().OnlyContain(x => x.Status == NzbDrone.Core.Messaging.Commands.CommandStatus.Completed);
        }
    }
}
