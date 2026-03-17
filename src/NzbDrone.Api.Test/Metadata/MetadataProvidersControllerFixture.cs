using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.MetadataSource;
using Readarr.Api.V1.Metadata;

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

            public void RegisterProvider(IMetadataProvider provider)
            {
            }

            public void UnregisterProvider(string providerName)
            {
            }

            public IMetadataProvider GetProvider(string providerName)
            {
                return _providers.FirstOrDefault(p => p.ProviderName == providerName);
            }

            public List<IMetadataProvider> GetAllProviders()
            {
                return _providers.ToList();
            }

            public List<IMetadataProvider> GetEnabledProviders()
            {
                return _providers.Where(p => p.IsEnabled).ToList();
            }

            public List<IMetadataProvider> GetProvidersWithCapability(string capability)
            {
                return new List<IMetadataProvider>();
            }

            public List<ISearchForNewBookV2> GetBookSearchProviders()
            {
                return new List<ISearchForNewBookV2>();
            }

            public List<ISearchForNewAuthorV2> GetAuthorSearchProviders()
            {
                return new List<ISearchForNewAuthorV2>();
            }

            public List<IProvideBookInfoV2> GetBookInfoProviders()
            {
                return new List<IProvideBookInfoV2>();
            }

            public List<IProvideAuthorInfoV2> GetAuthorInfoProviders()
            {
                return new List<IProvideAuthorInfoV2>();
            }

            public void EnableProvider(string providerName)
            {
            }

            public void DisableProvider(string providerName)
            {
            }

            public void SetProviderPriority(string providerName, int priority)
            {
            }

            public Dictionary<string, ProviderHealthStatus> GetProvidersHealthStatus()
            {
                return new Dictionary<string, ProviderHealthStatus>();
            }

            public void UpdateProviderHealth(string providerName, ProviderHealthStatus healthStatus)
            {
            }

            public int Count => _providers.Count;

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
