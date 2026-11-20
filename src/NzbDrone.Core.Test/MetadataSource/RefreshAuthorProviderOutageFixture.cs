using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using FluentAssertions;
using Moq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using NzbDrone.Common.Http;
using NzbDrone.Core.Books;
using NzbDrone.Core.Books.Commands;
using NzbDrone.Core.Configuration;
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
