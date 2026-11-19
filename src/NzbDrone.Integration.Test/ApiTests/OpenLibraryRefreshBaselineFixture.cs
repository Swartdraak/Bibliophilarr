using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using Bibliophilarr.Api.V1.Config;
using Bibliophilarr.Api.V1.Metadata;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Books.Commands;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Integration.Test.Client;

namespace NzbDrone.Integration.Test.ApiTests
{
    [TestFixture]
    public class OpenLibraryRefreshBaselineFixture : IntegrationTest
    {
        private ClientBase _metadataHealth;
        private ClientBase<MetadataProviderConfigResource> _metadataConfig;

        protected override void InitRestClients()
        {
            base.InitRestClients();
            _metadataHealth = new ClientBase(RestClient, ApiKey, "metadata/providers/health");
            _metadataConfig = new ClientBase<MetadataProviderConfigResource>(RestClient, ApiKey, "config/metadataprovider");
        }

        [Test]
        public void should_include_openlibrary_in_provider_health_projection_only_after_opt_in()
        {
            var config = _metadataConfig.GetSingle();
            config.EnableOpenLibraryProvider.Should().BeFalse();
            GetProviderHealth().Should().NotContain(x => x.ProviderName == "OpenLibrary");

            try
            {
                config.EnableOpenLibraryProvider = true;
                _metadataConfig.Put(config, HttpStatusCode.Accepted).EnableOpenLibraryProvider.Should().BeTrue();
                GetProviderHealth().Should().ContainSingle(x => x.ProviderName == "OpenLibrary" && x.IsEnabled);
            }
            finally
            {
                config.EnableOpenLibraryProvider = false;
                _metadataConfig.Put(config, HttpStatusCode.Accepted);
            }
        }

        [Test]
        public void should_skip_startup_backfill_by_default_and_complete_a_requested_backfill()
        {
            _metadataConfig.GetSingle().EnableOpenLibraryProvider.Should().BeFalse();
            Commands.WaitAll();
            Commands.All().Should().NotContain(x => x.Name == "BackfillOpenLibraryIds");

            var command = Commands.PostAndWait(new BackfillOpenLibraryIdsCommand { MaxLookups = 0 });

            command.Name.Should().Be("BackfillOpenLibraryIds");
            command.Status.Should().Be(CommandStatus.Completed);
        }

        private List<MetadataProviderHealthResource> GetProviderHealth()
        {
            var request = _metadataHealth.BuildRequest();
            request.Method = HttpMethod.Get;
            return _metadataHealth.Execute<List<MetadataProviderHealthResource>>(request, HttpStatusCode.OK);
        }
    }
}
