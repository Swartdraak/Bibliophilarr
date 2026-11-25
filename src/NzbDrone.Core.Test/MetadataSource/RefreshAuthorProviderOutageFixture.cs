using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
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
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.MetadataSource.GoogleBooks;
using NzbDrone.Core.MetadataSource.Hardcover;
using NzbDrone.Core.MetadataSource.OpenLibrary;
using NzbDrone.Core.MetadataSource.OpenLibrary.Resources;
using NzbDrone.Core.ProgressMessaging;
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
        public void should_report_bulk_refresh_failures_in_the_last_client_progress_message()
        {
            var authors = Enumerable.Range(1, 5).Select(id => new Author
            {
                Id = id,
                Metadata = new AuthorMetadata { ForeignAuthorId = $"openlibrary:author:OL{id}A" }
            }).ToList();
            var ids = authors.Select(author => author.Id).ToList();
            Mocker.GetMock<IAuthorService>().Setup(service => service.GetAuthors(ids)).Returns(authors);
            Mocker.GetMock<IConfigService>().SetupGet(config => config.RescanAfterRefresh).Returns(RescanAfterRefreshType.Never);

            var orchestrator = Mocker.GetMock<IMetadataProviderOrchestrator>();
            orchestrator.Setup(service => service.GetAuthorInfo(authors[0].ForeignAuthorId))
                .Throws(new AuthorNotFoundException(authors[0].ForeignAuthorId, true));
            orchestrator.Setup(service => service.GetAuthorInfo(authors[1].ForeignAuthorId))
                .Throws(new MetadataProviderUnavailableException(
                    "get-author-info",
                    authors[1].ForeignAuthorId,
                    new InvalidOperationException("Early empty page"),
                    authoritativeBibliographyEndedEarly: false,
                    authorBibliographyProgress: (10, 10, 12)));
            orchestrator.Setup(service => service.GetAuthorInfo(authors[2].ForeignAuthorId))
                .Throws(new MetadataProviderUnavailableException(
                    "get-author-info",
                    authors[2].ForeignAuthorId,
                    new HttpRequestException("Network unavailable")));
            orchestrator.Setup(service => service.GetAuthorInfo(authors[3].ForeignAuthorId))
                .Throws(new InvalidOperationException("Unexpected refresh failure"));
            orchestrator.Setup(service => service.GetAuthorInfo(authors[4].ForeignAuthorId)).Returns((Author)null);

            var command = new BulkRefreshAuthorCommand(ids);
            var model = new CommandModel { Body = command };
            var queue = Mocker.GetMock<IManageCommandQueue>();
            queue.Setup(service => service.SetMessage(model, It.IsAny<string>()))
                .Callback<CommandModel, string>((current, message) => current.Message = message);

            var target = new ProgressMessageTarget(Mocker.GetMock<IEventAggregator>().Object, queue.Object);
            var logging = new LoggingConfiguration();
            logging.AddRule(LogLevel.Info, LogLevel.Info, target);
            using var logFactory = new LogFactory { Configuration = logging };
            Mocker.SetConstant(logFactory.GetLogger(nameof(RefreshAuthorService)));

            var previous = ProgressMessageContext.CommandModel;
            try
            {
                ProgressMessageContext.CommandModel = model;
                Subject.Execute(command);
            }
            finally
            {
                ProgressMessageContext.CommandModel = previous;
            }

            model.Message.Should().Be("Author refresh checked 5. Completed 0, no metadata match 2, " +
                "incomplete bibliography 1, provider metadata unavailable 1, other refresh errors 1.");
            foreach (var author in authors)
            {
                orchestrator.Verify(service => service.GetAuthorInfo(author.ForeignAuthorId), Times.Once);
            }

            Mocker.GetMock<IAuthorMetadataService>().VerifyNoOtherCalls();
            queue.Verify(service => service.SetMessage(model, model.Message), Times.Once);
            queue.VerifyNoOtherCalls();
            Mocker.GetMock<ICommandResultReporter>().Verify(reporter => reporter.Report(CommandResult.Unsuccessful), Times.Once);
        }

        [Test]
        public void should_identify_the_local_author_in_a_safe_incomplete_bibliography_event()
        {
            const string privateAuthorName = "Private Fixture Writer";
            const string privateAuthorId = "googlebooks:author:Private%20Fixture%20Writer";
            const string privateKey = "private-fixture-provider-key";
            var messages = new MemoryTarget { Layout = "${message}|${exception:format=ToString}" };
            var logging = new LoggingConfiguration();
            logging.AddRule(LogLevel.Warn, LogLevel.Error, messages);
            using var logFactory = new LogFactory { Configuration = logging };
            Mocker.SetConstant(logFactory.GetLogger(nameof(RefreshAuthorService)));
            Mocker.SetConstant<TimeProvider>(TimeProvider.System);

            var lastSync = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            var author = new Author
            {
                Id = 42,
                AuthorMetadataId = 21,
                LastInfoSync = lastSync,
                Metadata = new AuthorMetadata { Id = 21, Name = privateAuthorName, ForeignAuthorId = privateAuthorId }
            };
            var ids = new List<int> { author.Id };
            Mocker.GetMock<IAuthorService>().Setup(service => service.GetAuthors(ids)).Returns(new List<Author> { author });
            var config = Mocker.GetMock<IConfigService>();
            config.SetupGet(settings => settings.RescanAfterRefresh).Returns(RescanAfterRefreshType.Never);
            config.SetupGet(settings => settings.EnableGoogleBooksProvider).Returns(true);
            config.SetupGet(settings => settings.GoogleBooksApiKey).Returns(privateKey);

            var google = Mocker.Resolve<GoogleBooksFallbackSearchProvider>();
            Mocker.GetMock<IMetadataProviderRegistry>()
                .Setup(registry => registry.GetProviders()).Returns(new IMetadataProvider[] { google });
            Mocker.SetConstant<IMetadataProviderOrchestrator>(Mocker.Resolve<MetadataProviderOrchestrator>());

            var headers = new HttpHeader { ContentType = "application/json" };
            var http = Mocker.GetMock<IHttpClient>();
            http.Setup(client => client.Get(It.IsAny<HttpRequest>())).Returns<HttpRequest>(request =>
                new HttpResponse(
                    request,
                    headers,
                    request.Url.Query.Contains("startIndex=0", StringComparison.Ordinal)
                        ? """{"totalItems":2,"items":[{"id":"fixture-work"}]}"""
                        : """{"totalItems":2,"items":[]}"""));

            Subject.Execute(new BulkRefreshAuthorCommand(ids));

            messages.Logs.Should().ContainSingle(message => message.Contains(
                "Author bibliography for local author 42 from GoogleBooks ended at offset 1 with 1 distinct entries of 2 reported.",
                StringComparison.Ordinal));
            messages.Logs.Should().NotContain(message => message.Contains(privateAuthorId) ||
                message.Contains(privateAuthorName) || message.Contains(privateKey));
            http.Verify(client => client.Get(It.IsAny<HttpRequest>()), Times.Exactly(3));
            http.VerifyNoOtherCalls();
            Mocker.GetMock<ICatalogueIdentityRepository>().VerifyNoOtherCalls();
            Mocker.GetMock<IAuthorMetadataService>().VerifyNoOtherCalls();
            Mocker.GetMock<IBookService>().VerifyNoOtherCalls();
            Mocker.GetMock<IRefreshSeriesService>().VerifyNoOtherCalls();
            Mocker.GetMock<ICommandResultReporter>().Verify(reporter => reporter.Report(CommandResult.Unsuccessful), Times.Once());
            author.LastInfoSync.Should().Be(lastSync);
        }

        [Test]
        public void should_report_open_library_work_offset_distinct_count_and_total_without_changing_the_catalogue()
        {
            const string providerAuthorId = "OL26320A";
            const string authorName = "Private Open Library Fixture Author";
            var messages = new MemoryTarget { Layout = "${message}|${exception:format=ToString}" };
            var logging = new LoggingConfiguration();
            logging.AddRule(LogLevel.Warn, LogLevel.Error, messages);
            using var logFactory = new LogFactory { Configuration = logging };
            Mocker.SetConstant(logFactory.GetLogger(nameof(RefreshAuthorService)));
            Mocker.SetConstant<TimeProvider>(TimeProvider.System);

            var lastSync = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            var author = new Author
            {
                Id = 42,
                AuthorMetadataId = 21,
                LastInfoSync = lastSync,
                Metadata = new AuthorMetadata
                {
                    Id = 21,
                    Name = authorName,
                    ForeignAuthorId = $"openlibrary:author:{providerAuthorId}"
                }
            };
            var ids = new List<int> { author.Id };
            Mocker.GetMock<IAuthorService>().Setup(service => service.GetAuthors(ids)).Returns(new List<Author> { author });
            var config = Mocker.GetMock<IConfigService>();
            config.SetupGet(settings => settings.RescanAfterRefresh).Returns(RescanAfterRefreshType.Never);
            config.SetupGet(settings => settings.EnableOpenLibraryProvider).Returns(true);

            var client = Mocker.GetMock<IOpenLibraryClient>();
            client.Setup(api => api.GetAuthor(providerAuthorId, true))
                .Returns(new OlAuthorResource { Key = $"/authors/{providerAuthorId}", Name = authorName });
            client.Setup(api => api.GetAuthorWorks(providerAuthorId, 100, 0))
                .Returns(new OlAuthorWorksResponse
                {
                    Size = 4,
                    Entries = new List<OlWorkResource>
                    {
                        new () { Key = "/works/OL31W" },
                        new () { Key = "/works/OL31W" },
                        new () { Key = "/works/OL32W" }
                    }
                });
            client.Setup(api => api.GetAuthorWorks(providerAuthorId, 100, 3))
                .Returns(new OlAuthorWorksResponse { Size = 4 });
            client.Setup(api => api.Search($"author_key:{providerAuthorId}", 100, 0))
                .Returns(new OlSearchResponse { NumFound = 4, NumFoundExact = false });

            var provider = Mocker.Resolve<OpenLibraryProvider>();
            Mocker.GetMock<IMetadataProviderRegistry>()
                .Setup(registry => registry.GetProviders()).Returns(new IMetadataProvider[] { provider });
            Mocker.SetConstant<IMetadataProviderOrchestrator>(Mocker.Resolve<MetadataProviderOrchestrator>());

            Subject.Execute(new BulkRefreshAuthorCommand(ids));

            messages.Logs.Should().ContainSingle(message => message.Contains(
                "Author bibliography for local author 42 from OpenLibrary ended at offset 3 with 2 distinct entries of 4 reported.",
                StringComparison.Ordinal));
            messages.Logs.Should().NotContain(message => message.Contains(providerAuthorId) || message.Contains(authorName));
            client.Verify(api => api.GetAuthor(providerAuthorId, true), Times.Once());
            client.Verify(api => api.GetAuthorWorks(providerAuthorId, 100, 0), Times.Once());
            client.Verify(api => api.GetAuthorWorks(providerAuthorId, 100, 3), Times.Exactly(2));
            client.Verify(api => api.Search($"author_key:{providerAuthorId}", 100, 0), Times.Once());
            client.VerifyNoOtherCalls();
            Mocker.GetMock<ICatalogueIdentityRepository>().VerifyNoOtherCalls();
            Mocker.GetMock<IAuthorMetadataService>().VerifyNoOtherCalls();
            Mocker.GetMock<IBookService>().VerifyNoOtherCalls();
            Mocker.GetMock<IRefreshSeriesService>().VerifyNoOtherCalls();
            Mocker.GetMock<ICommandResultReporter>().Verify(reporter => reporter.Report(CommandResult.Unsuccessful), Times.Once());
            author.LastInfoSync.Should().Be(lastSync);
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

        [Test]
        public void should_preserve_authoritative_catalogue_when_google_books_confirms_incomplete_bibliography()
        {
            const string authorId = "googlebooks:author:Author%20Name";
            const string biography = "Existing biography";
            var syncTime = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            Mocker.GetMock<IConfigService>()
                .SetupGet(settings => settings.EnableGoogleBooksProvider).Returns(true);

            var http = Mocker.GetMock<IHttpClient>();
            http.Setup(client => client.Get(It.IsAny<HttpRequest>()))
                .Returns<HttpRequest>(request =>
                {
                    var isFirstPage = request.Url.Query.EndsWith("startIndex=0", StringComparison.Ordinal);
                    var json = isFirstPage
                        ? @"{""totalItems"":2,""items"":[{""id"":""volume-a"",""volumeInfo"":{""title"":""Book A"",""authors"":[""Author Name""]}}]}"
                        : @"{""totalItems"":2,""items"":[]}";
                    return new HttpResponse(
                        request, new HttpHeader { ContentType = "application/json" }, json, HttpStatusCode.OK);
                });
            Mocker.GetMock<IMetadataProviderRegistry>()
                .Setup(registry => registry.GetProviders()).Returns(new IMetadataProvider[] { Mocker.Resolve<GoogleBooksFallbackSearchProvider>() });

            var telemetry = new MetadataProviderTelemetryService();
            Mocker.SetConstant<IMetadataProviderTelemetryService>(telemetry);
            Mocker.SetConstant<IMetadataProviderOrchestrator>(Mocker.Resolve<MetadataProviderOrchestrator>());

            var stored = new Author
            {
                Id = 12,
                LastInfoSync = syncTime,
                Metadata = new AuthorMetadata { ForeignAuthorId = authorId, Overview = biography }
            };
            var authors = Mocker.GetMock<IAuthorService>();
            authors.Setup(service => service.GetAuthors(It.Is<IEnumerable<int>>(ids => ids.SequenceEqual(new[] { stored.Id })))).Returns(new List<Author> { stored });

            Subject.Execute(new RefreshAuthorCommand(stored.Id));

            http.Verify(client => client.Get(It.Is<HttpRequest>(request =>
                request.Url.Query.EndsWith("startIndex=0", StringComparison.Ordinal))), Times.Once);
            http.Verify(client => client.Get(It.Is<HttpRequest>(request =>
                request.Url.Query.EndsWith("startIndex=1", StringComparison.Ordinal))), Times.Exactly(2));
            http.VerifyNoOtherCalls();
            authors.Verify(service => service.GetAuthors(It.Is<IEnumerable<int>>(ids => ids.SequenceEqual(new[] { stored.Id }))), Times.Once);
            authors.VerifyNoOtherCalls();
            Mocker.GetMock<IAuthorMetadataService>().VerifyNoOtherCalls();
            Mocker.GetMock<IBookService>().VerifyNoOtherCalls();
            Mocker.GetMock<IEditionService>().VerifyNoOtherCalls();
            Mocker.GetMock<IRefreshSeriesService>().VerifyNoOtherCalls();
            Mocker.GetMock<IManageCommandQueue>().VerifyNoOtherCalls();
            stored.Metadata.Value.Overview.Should().Be(biography);
            stored.LastInfoSync.Should().Be(syncTime);
            Mocker.GetMock<ICommandResultReporter>()
                .Verify(reporter => reporter.Report(CommandResult.Unsuccessful), Times.Once);

            var operation = telemetry.GetOperationSnapshots().Single();
            operation.OperationName.Should().Be("get-author-info");
            operation.Failures.Should().Be(1);
            operation.NullResults.Should().Be(0);
            telemetry.HasRecentTransportOutage().Should().BeFalse();
            ExceptionVerification.ExpectedWarns("Metadata provider 'GoogleBooks' returned an incomplete bibliography during get-author-info");
            ExceptionVerification.ExpectedErrors(1);
        }
    }
}
