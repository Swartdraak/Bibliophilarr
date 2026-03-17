using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.MetadataSource;
using Bibliophilarr.Api.V1.Metadata;

namespace NzbDrone.Api.Test.Metadata
{
    [TestFixture]
    public class MetadataProvidersControllerFixture
    {
        private class FakeRegistry : IMetadataProviderRegistry
        {
            private readonly IReadOnlyList<IMetadataProvider> _providers;

            public FakeRegistry(IReadOnlyList<IMetadataProvider> providers)
            {
                _providers = providers;
            }

            public IReadOnlyList<IMetadataProvider> GetProviders()
            {
                return _providers;
            }

            public T Execute<T>(System.Func<IMetadataProvider, T> operation, string operationName)
                where T : class
            {
                foreach (var provider in _providers)
                {
                    var result = operation(provider);
                    if (result != null)
                    {
                        return result;
                    }
                }

                return null;
            }
        }

        private class FakeProvider : IMetadataProvider
        {
            public string ProviderName => "OpenLibrary";
            public int Priority => 2;
            public bool IsEnabled => true;
            public bool SupportsAuthorSearch => true;
            public bool SupportsBookSearch => true;
            public bool SupportsIsbnLookup => true;
            public bool SupportsSeriesInfo => false;
            public bool SupportsCoverImages => true;
        }

        [Test]
        public void get_health_should_include_provider_capabilities_and_telemetry()
        {
            var telemetry = new MetadataProviderTelemetryService();
            telemetry.Record("OpenLibrary", 12, success: true, returnedNull: false, fallbackHit: false);

            var controller = new MetadataProvidersController(
                new FakeRegistry(new IMetadataProvider[] { new FakeProvider() }),
                telemetry);

            var result = controller.GetHealth();

            result.Should().ContainSingle();
            var health = result.First();
            health.ProviderName.Should().Be("OpenLibrary");
            health.SupportsBookSearch.Should().BeTrue();
            health.Telemetry.Should().NotBeNull();
            health.Telemetry.Successes.Should().Be(1);
        }

        [Test]
        public void get_telemetry_should_return_current_snapshots()
        {
            var telemetry = new MetadataProviderTelemetryService();
            telemetry.Record("Inventaire", 9, success: false, returnedNull: true, fallbackHit: false);

            var controller = new MetadataProvidersController(
                new FakeRegistry(new IMetadataProvider[] { new FakeProvider() }),
                telemetry);

            var result = controller.GetTelemetry();

            result.Should().ContainSingle(x => x.ProviderName == "Inventaire" && x.NullResults == 1);
        }
    }
}
