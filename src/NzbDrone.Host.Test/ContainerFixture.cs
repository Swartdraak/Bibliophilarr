using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using Bibliophilarr.Api.V1.Author;
using Bibliophilarr.Api.V1.Books;
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
using NzbDrone.Core.Books;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Datastore.Extensions;
using NzbDrone.Core.Download;
using NzbDrone.Core.Download.TrackedDownloads;
using NzbDrone.Core.HealthCheck;
using NzbDrone.Core.HealthCheck.Checks;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Localization;
using NzbDrone.Core.MediaCover;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.Organizer;
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

        [TestCase(false)]
        [TestCase(true)]
        public void should_route_book_and_author_lookup_through_enabled_providers(bool transportFailure)
        {
            const string term = "fixture author";
            const string bookId = "openlibrary:work:OL123W";
            const string authorId = "openlibrary:author:OL456A";

            var disabled = new Mock<IMetadataProvider>();
            disabled.SetupGet(x => x.ProviderName).Returns("GoogleBooks");
            disabled.SetupGet(x => x.SupportsBookSearch).Returns(true);
            disabled.SetupGet(x => x.SupportsAuthorSearch).Returns(true);
            var disabledBooks = disabled.As<ISearchForNewBook>();
            var disabledAuthors = disabled.As<ISearchForNewAuthor>();

            var enabled = new Mock<IMetadataProvider>();
            enabled.SetupGet(x => x.ProviderName).Returns("OpenLibrary");
            enabled.SetupGet(x => x.IsEnabled).Returns(true);
            enabled.SetupGet(x => x.Priority).Returns(1);
            enabled.SetupGet(x => x.SupportsBookSearch).Returns(true);
            enabled.SetupGet(x => x.SupportsAuthorSearch).Returns(true);
            var bookSearch = enabled.As<ISearchForNewBook>();
            var authorSearch = enabled.As<ISearchForNewAuthor>();

            if (transportFailure)
            {
                bookSearch.Setup(x => x.SearchForNewBook(term, null, true)).Throws(new HttpRequestException("Fixture connection unavailable"));
                authorSearch.Setup(x => x.SearchForNewAuthor(term)).Throws(new HttpRequestException("Fixture connection unavailable"));
            }
            else
            {
                bookSearch.Setup(x => x.SearchForNewBook(term, null, true))
                    .Returns(new List<Book> { new Book { ForeignBookId = bookId, Title = "Fixture Book" } });
                authorSearch.Setup(x => x.SearchForNewAuthor(term))
                    .Returns(new List<Author> { new Author { Metadata = new AuthorMetadata { ForeignAuthorId = authorId, Name = "Fixture Author" } } });
            }

            _registrations.RegisterInstance<IMetadataProviderRegistry>(
                new MetadataProviderRegistry(new[] { disabled.Object, enabled.Object }),
                ifAlreadyRegistered: IfAlreadyRegistered.Replace);

            // Raw provider contracts are not a routing facade. Model the binding that
            // the old controller constructors received on the packaged application.
            _registrations.RegisterInstance(disabledBooks.Object, ifAlreadyRegistered: IfAlreadyRegistered.Replace);
            _registrations.RegisterInstance(disabledAuthors.Object, ifAlreadyRegistered: IfAlreadyRegistered.Replace);
            _registrations.RegisterInstance(Mock.Of<IMapCoversToLocal>(), ifAlreadyRegistered: IfAlreadyRegistered.Replace);
            _registrations.RegisterInstance(Mock.Of<IBuildFileNames>(), ifAlreadyRegistered: IfAlreadyRegistered.Replace);
            _registrations.RegisterInstance(Mock.Of<IEventAggregator>(), ifAlreadyRegistered: IfAlreadyRegistered.Replace);

            var bookLookup = ActivatorUtilities.CreateInstance<BookLookupController>(_container);
            var authorLookup = ActivatorUtilities.CreateInstance<AuthorLookupController>(_container);
            var books = (IEnumerable<BookResource>)bookLookup.Search(term);
            var authors = (IEnumerable<AuthorResource>)authorLookup.Search(term);

            if (transportFailure)
            {
                books.Should().BeEmpty();
                authors.Should().BeEmpty();
                ExceptionVerification.ExpectedWarns(4);
            }
            else
            {
                books.Should().ContainSingle().Which.ForeignBookId.Should().Be(bookId);
                authors.Should().ContainSingle().Which.ForeignAuthorId.Should().Be(authorId);
            }

            bookSearch.Verify(x => x.SearchForNewBook(term, null, true), Times.AtLeastOnce);
            authorSearch.Verify(x => x.SearchForNewAuthor(term), Times.Once);
            disabledBooks.Verify(x => x.SearchForNewBook(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>()), Times.Never);
            disabledAuthors.Verify(x => x.SearchForNewAuthor(It.IsAny<string>()), Times.Never);
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
