using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using FluentAssertions;
using NLog;
using NUnit.Framework;
using NzbDrone.Common.Cache;
using NzbDrone.Common.Cloud;
using NzbDrone.Common.Http;
using NzbDrone.Core.Books;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.MetadataSource.BookInfo;
using NzbDrone.Core.MetadataSource.OpenLibrary;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MusicTests
{
    [TestFixture]
    public class RefreshBookServiceFallbackIntegrationFixture : CoreTest<RefreshBookService>
    {
        [SetUp]
        public void SetUp()
        {
            Mocker.GetMock<IConfigService>().SetupGet(x => x.EnableOpenLibraryProvider).Returns(true);
            Mocker.GetMock<IConfigService>().SetupGet(x => x.MetadataSource).Returns("https://api.bookinfo.invalid/v1");
            Mocker.GetMock<IConfigService>().SetupGet(x => x.MetadataProviderTimeoutSeconds).Returns(5);
            Mocker.GetMock<IConfigService>().SetupGet(x => x.MetadataProviderRetryBudget).Returns(0);
            Mocker.GetMock<IConfigService>().SetupGet(x => x.MetadataProviderCircuitBreakerThreshold).Returns(1);
            Mocker.GetMock<IConfigService>().SetupGet(x => x.MetadataProviderCircuitBreakerDurationSeconds).Returns(5);

            Mocker.SetConstant<IBibliophilarrCloudRequestBuilder>(new BibliophilarrCloudRequestBuilder());
            Mocker.SetConstant<IMetadataRequestBuilder>(Mocker.Resolve<MetadataRequestBuilder>());
            Mocker.GetMock<NzbDrone.Core.Http.ICachedHttpResponseService>()
                .Setup(x => x.Get(Moq.It.IsAny<HttpRequest>(), Moq.It.IsAny<bool>(), Moq.It.IsAny<TimeSpan>()))
                .Returns((HttpRequest request, bool useCache, TimeSpan ttl) => Mocker.GetMock<IHttpClient>().Object.Get(request));
        }

        [Test]
        public void get_skyhook_data_should_return_null_when_all_providers_miss_and_not_throw()
        {
            var telemetry = new MetadataProviderTelemetryService();
            var orchestrator = BuildOrchestrator(request =>
            {
                if (request.Url.FullUri.Contains("api.bookinfo.invalid", StringComparison.OrdinalIgnoreCase))
                {
                    throw new HttpRequestException("Name or service not known");
                }

                return new HttpResponse(request, new HttpHeader(), string.Empty, HttpStatusCode.NotFound);
            }, telemetry);

            Mocker.SetConstant<IMetadataProviderOrchestrator>(orchestrator);

            var method = typeof(RefreshBookService).GetMethod("GetSkyhookData", BindingFlags.Instance | BindingFlags.NonPublic);
            var localBook = new Book { ForeignBookId = "openlibrary:work:OL9999999W", AuthorMetadataId = 1 };

            var result = (Author)method.Invoke(Subject, new object[] { localBook });
            result.Should().BeNull();

            telemetry.GetOperationSnapshots().Should().Contain(x =>
                x.ProviderName == "BookInfo" &&
                x.OperationName == "get-book-info" &&
                x.Failures >= 1);
        }

        [Test]
        public void get_skyhook_data_should_use_fallback_provider_for_prefixed_work_ids_after_dns_failure()
        {
            var telemetry = new MetadataProviderTelemetryService();
            var orchestrator = BuildOrchestrator(HandleFallbackSuccessRequest, telemetry);

            Mocker.SetConstant<IMetadataProviderOrchestrator>(orchestrator);

            var method = typeof(RefreshBookService).GetMethod("GetSkyhookData", BindingFlags.Instance | BindingFlags.NonPublic);
            var localBook = new Book { ForeignBookId = "openlibrary:work:OL8547083W", AuthorMetadataId = 414 };

            var result = (Author)method.Invoke(Subject, new object[] { localBook });

            result.Should().NotBeNull();
            result.Metadata.Value.ForeignAuthorId.Should().Be("openlibrary:author:OL29303A");
            result.Books.Value.Should().ContainSingle();
            result.Books.Value.Single().ForeignBookId.Should().Be("openlibrary:work:OL8547083W");
            telemetry.GetOperationSnapshots().Should().Contain(x =>
                x.ProviderName == "OpenLibrary" &&
                x.OperationName == "get-book-info" &&
                x.FallbackHits == 1);

            telemetry.GetOperationSnapshots().Should().Contain(x =>
                x.ProviderName == "OpenLibrary" &&
                x.OperationName == "get-author-info" &&
                x.FallbackHits == 1);
        }

        private IMetadataProviderOrchestrator BuildOrchestrator(Func<HttpRequest, HttpResponse> handler, MetadataProviderTelemetryService telemetry)
        {
            Mocker.GetMock<IHttpClient>()
                .Setup(x => x.Get(Moq.It.IsAny<HttpRequest>()))
                .Returns<HttpRequest>(handler);

            var requestBuilder = Mocker.Resolve<MetadataRequestBuilder>();
            var logger = LogManager.GetCurrentClassLogger();
            var bookInfo = new BookInfoProxy(
                Mocker.GetMock<IHttpClient>().Object,
                Mocker.GetMock<NzbDrone.Core.Http.ICachedHttpResponseService>().Object,
                null,
                Mocker.GetMock<IAuthorService>().Object,
                Mocker.GetMock<IBookService>().Object,
                Mocker.GetMock<IEditionService>().Object,
                requestBuilder,
                logger,
                Mocker.Resolve<CacheManager>());

            var openLibraryClient = new OpenLibraryClient(
                Mocker.GetMock<IHttpClient>().Object,
                Mocker.GetMock<IConfigService>().Object,
                logger);
            var openLibrary = new OpenLibraryProvider(openLibraryClient, Mocker.GetMock<IConfigService>().Object, logger);

            return new MetadataProviderOrchestrator(
                new TestRegistry(new IMetadataProvider[] { bookInfo, openLibrary }),
                telemetry,
                logger);
        }

        private static HttpResponse HandleFallbackSuccessRequest(HttpRequest request)
        {
            if (request.Url.FullUri.Contains("api.bookinfo.invalid", StringComparison.OrdinalIgnoreCase))
            {
                throw new HttpRequestException("Name or service not known");
            }

            if (request.Url.FullUri.Contains("/works/OL8547083W.json", StringComparison.OrdinalIgnoreCase))
            {
                return Json(request, "{\"key\":\"/works/OL8547083W\",\"title\":\"The Divine Comedy\",\"authors\":[{\"author\":{\"key\":\"/authors/OL29303A\"}}]}");
            }

            if (request.Url.FullUri.Contains("/authors/OL29303A.json", StringComparison.OrdinalIgnoreCase))
            {
                return Json(request, "{\"key\":\"/authors/OL29303A\",\"name\":\"Dante Alighieri\"}");
            }

            if (request.Url.FullUri.Contains("/search.json", StringComparison.OrdinalIgnoreCase))
            {
                return Json(request, "{\"docs\":[{\"key\":\"/works/OL8547083W\",\"title\":\"The Divine Comedy\",\"author_name\":[\"Dante Alighieri\"],\"author_key\":[\"OL29303A\"],\"isbn\":[\"9781433200236\"]}]}");
            }

            return new HttpResponse(request, new HttpHeader(), string.Empty, HttpStatusCode.NotFound);
        }

        private static HttpResponse Json(HttpRequest request, string payload)
        {
            return new HttpResponse(request, new HttpHeader { ContentType = "application/json" }, payload);
        }

        private class TestRegistry : IMetadataProviderRegistry
        {
            private readonly IReadOnlyList<IMetadataProvider> _providers;

            public TestRegistry(IReadOnlyList<IMetadataProvider> providers)
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

            public T Execute<T>(Func<IMetadataProvider, T> operation, string operationName)
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
    }
}
