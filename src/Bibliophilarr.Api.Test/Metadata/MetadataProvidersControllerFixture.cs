using System;
using System.Collections.Generic;
using System.Linq;
using Bibliophilarr.Api.V1.Metadata;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Http;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.MetadataSource.Inventaire;

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

            public bool HasConfiguredBookLookup => GetEnabledProviders().Any(provider => provider is ISearchForNewBook && (provider.SupportsBookSearch || provider.SupportsIsbnLookup));

            public IReadOnlyList<IMetadataProvider> GetProviders()
            {
                return GetEnabledProviders();
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
            public string ProviderName { get; init; } = "OpenLibrary";
            public int Priority => 2;
            public bool IsEnabled { get; init; } = true;
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
            telemetry.Record("OpenLibrary", "get-book-info", 12, success: true, returnedNull: false, fallbackHit: false);

            var controller = new MetadataProvidersController(
                new FakeRegistry(new IMetadataProvider[] { new FakeProvider() }),
                telemetry,
                Array.Empty<IBookSearchFallbackProvider>());

            var result = controller.GetHealth();

            result.Should().ContainSingle();
            var health = result.First();
            health.ProviderName.Should().Be("OpenLibrary");
            health.SupportsBookSearch.Should().BeTrue();
            health.Telemetry.Should().NotBeNull();
            health.Telemetry.Successes.Should().Be(1);
            health.Telemetry.Operations.Should().ContainSingle(x => x.OperationName == "get-book-info");
        }

        [TestCase(true)]
        [TestCase(false)]
        public void get_health_should_include_inventaire_once_as_fallback_only(bool enabled)
        {
            var hardcover = new FakeProvider { ProviderName = "Hardcover" };
            var googleBooks = new FakeProvider { ProviderName = "GoogleBooks", IsEnabled = false };
            var registry = new FakeRegistry(new[] { hardcover, googleBooks, new FakeProvider() });
            var config = new Mock<IConfigService>();
            config.SetupGet(settings => settings.EnableInventaireFallback).Returns(enabled);
            var http = new Mock<IHttpClient>(MockBehavior.Strict);
            var inventaire = new InventaireFallbackSearchProvider(config.Object, http.Object, NLog.LogManager.GetCurrentClassLogger());
            var coverFallback = new Mock<IBookSearchFallbackProvider>(MockBehavior.Strict);
            coverFallback.SetupGet(provider => provider.ProviderName).Returns("HARDCOVER");
            var googleFallback = new Mock<IBookSearchFallbackProvider>(MockBehavior.Strict);
            googleFallback.SetupGet(provider => provider.ProviderName).Returns("GoogleBooks");
            var telemetry = new MetadataProviderTelemetryService();
            if (enabled)
            {
                telemetry.Record("Inventaire", "fallback-search", 4, success: true, returnedNull: false, fallbackHit: true);
            }

            var controller = new MetadataProvidersController(
                registry,
                telemetry,
                new[] { coverFallback.Object, googleFallback.Object, inventaire });

            var result = controller.GetHealth().ToList();

            result.Select(item => item.ProviderName).Should().Equal("Hardcover", "OpenLibrary", "Inventaire");
            result[0].IsFallbackOnly.Should().BeFalse();
            var fallback = result[2];
            fallback.IsEnabled.Should().Be(enabled);
            fallback.IsFallbackOnly.Should().BeTrue();
            fallback.Priority.Should().Be(0);
            fallback.SupportsBookSearch.Should().BeTrue();
            fallback.SupportsAuthorSearch.Should().BeFalse();
            fallback.SupportsIsbnLookup.Should().BeFalse();
            fallback.SupportsSeriesInfo.Should().BeTrue();
            fallback.Telemetry.Calls.Should().Be(enabled ? 1 : 0);
            fallback.Telemetry.Operations.Should().HaveCount(enabled ? 1 : 0);
            http.VerifyNoOtherCalls();
        }

        [Test]
        public void get_telemetry_should_return_current_snapshots()
        {
            var telemetry = new MetadataProviderTelemetryService();
            telemetry.Record("Inventaire", "search-by-isbn", 9, success: false, returnedNull: true, fallbackHit: false);

            var controller = new MetadataProvidersController(
                new FakeRegistry(new IMetadataProvider[] { new FakeProvider() }),
                telemetry,
                Array.Empty<IBookSearchFallbackProvider>());

            var result = controller.GetTelemetry();

            result.Should().ContainSingle(x => x.ProviderName == "Inventaire" && x.NullResults == 1);
        }

        [Test]
        public void get_operation_telemetry_should_return_operation_snapshots()
        {
            var telemetry = new MetadataProviderTelemetryService();
            telemetry.Record("OpenLibrary", "get-book-info", 5, success: true, returnedNull: false, fallbackHit: true);

            var controller = new MetadataProvidersController(
                new FakeRegistry(new IMetadataProvider[] { new FakeProvider() }),
                telemetry,
                Array.Empty<IBookSearchFallbackProvider>());

            var result = controller.GetOperationTelemetry();

            result.Should().ContainSingle(x =>
                x.ProviderName == "OpenLibrary" &&
                x.OperationName == "get-book-info" &&
                x.FallbackHits == 1);
        }
    }
}
