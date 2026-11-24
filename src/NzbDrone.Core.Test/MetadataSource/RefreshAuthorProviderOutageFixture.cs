using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using FluentAssertions;
using Moq;
using Newtonsoft.Json.Linq;
using NLog;
using NLog.Config;
using NLog.Targets;
using NUnit.Framework;
using NzbDrone.Common.Http;
using NzbDrone.Core.Books;
using NzbDrone.Core.Books.Commands;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Exceptions;
using NzbDrone.Core.HealthCheck;
using NzbDrone.Core.HealthCheck.Checks;
using NzbDrone.Core.Localization;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.MetadataSource.Hardcover;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.MetadataSource
{
    [TestFixture]
    public class RefreshAuthorProviderOutageFixture : CoreTest<RefreshAuthorService>
    {
        [TestCase("hardcover:author:123")]
        [TestCase("openlibrary:author:OL123A")]
        public void should_not_update_or_delete_catalogue_after_incomplete_bibliography(string authorId)
        {
            var messages = new MemoryTarget { Layout = "${level}" };
            var logging = new LoggingConfiguration();
            logging.AddRule(LogLevel.Warn, LogLevel.Error, messages);
            using var logFactory = new LogFactory { Configuration = logging };
            Mocker.SetConstant(logFactory.GetLogger(nameof(RefreshAuthorService)));

            var lastSync = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            var stored = new Author
            {
                Id = 12,
                AuthorMetadataId = 21,
                LastInfoSync = lastSync,
                Metadata = new AuthorMetadata { Id = 21, ForeignAuthorId = authorId }
            };
            Mocker.GetMock<IAuthorService>()
                .Setup(service => service.GetAuthors(It.Is<IEnumerable<int>>(ids => ids.SequenceEqual(new[] { stored.Id }))))
                .Returns(new List<Author> { stored });
            Mocker.GetMock<IMetadataProviderOrchestrator>()
                .Setup(provider => provider.GetAuthorInfo(authorId, true))
                .Throws(new MetadataProviderUnavailableException(
                    "get-author-info",
                    authorId,
                    new InvalidOperationException("Incomplete bibliography fixture.")));

            Subject.Execute(new RefreshAuthorCommand(stored.Id));

            Mocker.GetMock<IAuthorMetadataService>().VerifyNoOtherCalls();
            Mocker.GetMock<IBookService>().VerifyNoOtherCalls();
            Mocker.GetMock<IRefreshSeriesService>().VerifyNoOtherCalls();
            Mocker.GetMock<ICommandResultReporter>().Verify(reporter => reporter.Report(CommandResult.Unsuccessful), Times.Once());
            stored.LastInfoSync.Should().Be(lastSync);
            messages.Logs.Should().ContainInOrder("Warn", "Error");
        }

        [Test]
        public void should_preserve_existing_author_biography_when_hardcover_is_unreachable()
        {
            const string authorId = "hardcover:author:123";
            const string biography = "Existing biography";
            var config = Mocker.GetMock<IConfigService>();
            config.SetupGet(settings => settings.EnableHardcoverFallback).Returns(true);
            config.SetupGet(settings => settings.HardcoverApiToken).Returns("hardcover-token");
            config.SetupGet(settings => settings.HardcoverRequestTimeoutSeconds).Returns(0);

            var http = Mocker.GetMock<IHttpClient>();
            http.Setup(client => client.Post<JObject>(It.IsAny<HttpRequest>()))
                .Throws(new HttpRequestException("No route to host."));
            Mocker.GetMock<IMetadataProviderRegistry>()
                .Setup(registry => registry.GetProviders()).Returns(new IMetadataProvider[] { Mocker.Resolve<HardcoverFallbackSearchProvider>() });

            var telemetry = new MetadataProviderTelemetryService();
            Mocker.SetConstant<IMetadataProviderTelemetryService>(telemetry);
            Mocker.SetConstant<IMetadataProviderOrchestrator>(Mocker.Resolve<MetadataProviderOrchestrator>());

            var stored = new Author { Id = 12, Metadata = new AuthorMetadata { ForeignAuthorId = authorId, Overview = biography } };
            var authors = Mocker.GetMock<IAuthorService>();
            authors.Setup(service => service.GetAuthors(It.Is<IEnumerable<int>>(ids => ids.SequenceEqual(new[] { stored.Id })))).Returns(new List<Author> { stored });

            Subject.Execute(new RefreshAuthorCommand(stored.Id));

            http.Verify(client => client.Post<JObject>(It.IsAny<HttpRequest>()), Times.Exactly(2));
            http.VerifyNoOtherCalls();
            authors.Verify(service => service.GetAuthors(It.Is<IEnumerable<int>>(ids => ids.SequenceEqual(new[] { stored.Id }))), Times.Once);
            authors.VerifyNoOtherCalls();
            Mocker.GetMock<IAuthorMetadataService>().VerifyNoOtherCalls();
            Mocker.GetMock<IRefreshSeriesService>().VerifyNoOtherCalls();
            stored.Metadata.Value.Overview.Should().Be(biography);
            Mocker.GetMock<ICommandResultReporter>()
                .Verify(reporter => reporter.Report(CommandResult.Unsuccessful), Times.Once);

            var localization = Mock.Of<ILocalizationService>(service =>
                service.GetLocalizedString("MetadataProviderUnreachableHealthCheckMessage") == "Metadata provider unreachable");
            var result = new MetadataProviderConnectivityCheck(telemetry, localization).Check();
            result.Type.Should().Be(HealthCheckResult.Warning);
            result.Message.Should().Be("Metadata provider unreachable");
            ExceptionVerification.ExpectedWarns(3);
            ExceptionVerification.ExpectedErrors(1);
        }
    }
}
