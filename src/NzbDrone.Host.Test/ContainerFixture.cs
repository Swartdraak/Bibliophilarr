using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using DryIoc;
using DryIoc.Microsoft.DependencyInjection;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Moq;
using NUnit.Framework;
using NzbDrone.Common;
using NzbDrone.Common.Composition.Extensions;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Common.Instrumentation.Extensions;
using NzbDrone.Common.Options;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Datastore.Extensions;
using NzbDrone.Core.Download;
using NzbDrone.Core.Download.TrackedDownloads;
using NzbDrone.Core.HealthCheck;
using NzbDrone.Core.HealthCheck.Checks;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Localization;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Host;
using NzbDrone.SignalR;
using NzbDrone.Test.Common;
using IServiceProvider = System.IServiceProvider;

namespace NzbDrone.App.Test
{
    [TestFixture]
    public class ContainerFixture : TestBase
    {
        private IServiceProvider _container;
        private IContainer _registrations;

        [SetUp]
        public void SetUp()
        {
            var args = new StartupContext("first", "second");

            var container = new Container(rules => rules.WithNzbDroneRules())
                .AutoAddServices(Bootstrap.ASSEMBLIES)
                .AddNzbDroneLogger()
                .AddDummyDatabase()
                .AddStartupContext(args);

            // set up a dummy broadcaster and lifetime to allow tests to resolve
            container.RegisterInstance<IHostLifetime>(new Mock<IHostLifetime>().Object);
            container.RegisterInstance<IBroadcastSignalRMessage>(new Mock<IBroadcastSignalRMessage>().Object);
            container.RegisterInstance<IOptions<PostgresOptions>>(new Mock<IOptions<PostgresOptions>>().Object);
            container.RegisterInstance<IOptions<AuthOptions>>(new Mock<IOptions<AuthOptions>>().Object);
            container.RegisterInstance<IOptions<AppOptions>>(new Mock<IOptions<AppOptions>>().Object);
            container.RegisterInstance<IOptions<ServerOptions>>(new Mock<IOptions<ServerOptions>>().Object);
            container.RegisterInstance<IOptions<UpdateOptions>>(new Mock<IOptions<UpdateOptions>>().Object);
            container.RegisterInstance<IOptions<LogOptions>>(new Mock<IOptions<LogOptions>>().Object);

            _registrations = container;
            _container = container.GetServiceProvider();
        }

        [Test]
        public void should_share_metadata_outage_state_through_host_registration()
        {
            var provider = new Mock<IMetadataProvider>();
            provider.SetupGet(x => x.ProviderName).Returns("OpenLibrary");
            provider.SetupGet(x => x.SupportsBookSearch).Returns(true);
            provider.As<ISearchForNewBook>().Setup(x => x.SearchForNewBook("fixture", null, true))
                .Throws(new HttpRequestException("Synthetic transport failure"));

            var registry = new Mock<IMetadataProviderRegistry>();
            registry.Setup(x => x.GetProviders()).Returns(new[] { provider.Object });
            _registrations.RegisterInstance(registry.Object, ifAlreadyRegistered: IfAlreadyRegistered.Replace);
            var events = new Mock<IEventAggregator>();
            _registrations.RegisterInstance(events.Object, ifAlreadyRegistered: IfAlreadyRegistered.Replace);
            _registrations.RegisterInstance(Mock.Of<ILocalizationService>(), ifAlreadyRegistered: IfAlreadyRegistered.Replace);

            var orchestrator = _container.GetRequiredService<IMetadataProviderOrchestrator>();
            var watcher = _container.GetServices<IProvideHealthCheck>().OfType<MetadataProviderConnectivityCheck>().Single();

            watcher.Check().Type.Should().Be(HealthCheckResult.Ok);
            orchestrator.SearchForNewBook("fixture", null);
            watcher.Check().Type.Should().Be(HealthCheckResult.Warning);
            events.Verify(x => x.PublishEvent(It.IsAny<MetadataProviderConnectivityChangedEvent>()), Times.Once());

            ExceptionVerification.ExpectedWarns(2);
        }

        [Test]
        public void should_be_able_to_resolve_indexers()
        {
            _container.GetRequiredService<IEnumerable<IIndexer>>().Should().NotBeEmpty();
        }

        [Test]
        public void should_be_able_to_resolve_downloadclients()
        {
            _container.GetRequiredService<IEnumerable<IDownloadClient>>().Should().NotBeEmpty();
        }

        [Test]
        public void container_should_inject_itself()
        {
            var factory = _container.GetRequiredService<IServiceFactory>();

            factory.Build<IIndexerFactory>().Should().NotBeNull();
        }

        [Test]
        public void should_resolve_command_executor_by_name()
        {
            var genericExecutor = typeof(IExecute<>).MakeGenericType(typeof(RssSyncCommand));

            var executor = _container.GetRequiredService(genericExecutor);

            executor.Should().NotBeNull();
            executor.Should().BeAssignableTo<IExecute<RssSyncCommand>>();
        }

        [Test]
        public void should_return_same_instance_via_resolve_and_resolveall()
        {
            var first = (DownloadMonitoringService)_container.GetRequiredService<IHandle<TrackedDownloadsRemovedEvent>>();
            var second = _container.GetServices<IHandle<TrackedDownloadsRemovedEvent>>().OfType<DownloadMonitoringService>().Single();

            first.Should().BeSameAs(second);
        }

        [Test]
        public void should_return_same_instance_of_singletons_by_same_interface()
        {
            var first = _container.GetServices<IHandle<TrackedDownloadsRemovedEvent>>().OfType<DownloadMonitoringService>().Single();
            var second = _container.GetServices<IHandle<TrackedDownloadsRemovedEvent>>().OfType<DownloadMonitoringService>().Single();

            first.Should().BeSameAs(second);
        }

        [Test]
        public void should_return_same_instance_of_singletons_by_different_interfaces()
        {
            var first = _container.GetServices<IHandle<BookGrabbedEvent>>().OfType<DownloadMonitoringService>().Single();
            var second = (DownloadMonitoringService)_container.GetRequiredService<IExecute<RefreshMonitoredDownloadsCommand>>();

            first.Should().BeSameAs(second);
        }
    }
}
