using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using Bibliophilarr.Api.V1.Author;
using Bibliophilarr.Api.V1.Config;
using Bibliophilarr.Api.V1.RootFolders;
using Bibliophilarr.Api.V1.Search;
using DryIoc;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Moq;
using NLog;
using NLog.Config;
using NLog.Targets;
using NUnit.Framework;
using NzbDrone.Common.Cache;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Common.Http;
using NzbDrone.Common.Http.Dispatchers;
using NzbDrone.Common.Options;
using NzbDrone.Common.Serializer;
using NzbDrone.Common.TPL;
using NzbDrone.Core.Books;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.MetadataSource.Hardcover;
using NzbDrone.Core.MetadataSource.OpenLibrary;
using NzbDrone.Host;
using NzbDrone.Test.Common.Categories;
using AuthorModel = NzbDrone.Core.Books.Author;
using HttpClient = System.Net.Http.HttpClient;

namespace NzbDrone.Integration.Test.ApiTests
{
    [TestFixture]
    [IntegrationTest]
    [NonParallelizable]
    public class AuthorReresolutionFixture
    {
        private const string ForeignAuthorId = "hardcover:author:290042";
        private const string CanonicalName = "Fixture Canonical Author";

        [Test]
        public async Task should_keep_resolved_author_identity_after_host_restart()
        {
            var workspace = Directory.CreateTempSubdirectory("bibliophilarr-author-290-");
            var data = workspace.CreateSubdirectory("data").FullName;
            var library = workspace.CreateSubdirectory("library").FullName;
            var apiKey = Guid.NewGuid().ToString("N");
            var provider = new FixtureAuthorProvider();

            new XElement("Config",
                new XElement("BindAddress", "127.0.0.1"),
                new XElement("Port", 0),
                new XElement("ApiKey", apiKey))
                .Save(Path.Combine(data, "config.xml"));

            var args = new[] { $"-data={data}", "-nobrowser", "-nosingleinstancecheck" };

            try
            {
                AuthorResource created;
                using (var host = BuildHost(args, provider))
                {
                    using var firstTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
                    var cancellationToken = firstTimeout.Token;
                    await host.StartAsync(cancellationToken);
                    try
                    {
                        using var client = CreateClient(host, apiKey);
                        var rootFolder = new RootFolderResource
                        {
                            Name = "Fixture Library",
                            Path = library,
                            DefaultQualityProfileId = 1,
                            DefaultMetadataProfileId = 1,
                            OutputProfile = "default"
                        };
                        using var libraryResponse = await client.PostAsJsonAsync("rootfolder", rootFolder, cancellationToken);
                        libraryResponse.StatusCode.Should().Be(HttpStatusCode.Created);
                        provider.UncachedLookups.Should().Be(0);

                        var request = new AuthorResource
                        {
                            AuthorName = "Fixture Search Result",
                            ForeignAuthorId = ForeignAuthorId,
                            RootFolderPath = library,
                            QualityProfileId = 1,
                            MetadataProfileId = 1,
                            AddOptions = new AddAuthorOptions()
                        };
                        using var response = await client.PostAsJsonAsync("author", request, STJson.GetSerializerSettings(), cancellationToken);

                        response.StatusCode.Should().Be(HttpStatusCode.Created, "{0}", await response.Content.ReadAsStringAsync(cancellationToken));
                        created = await response.Content.ReadFromJsonAsync<AuthorResource>(STJson.GetSerializerSettings(), cancellationToken);
                        created.Should().NotBeNull();
                        created.Id.Should().BePositive();
                        created.AuthorMetadataId.Should().BePositive();
                        created.ForeignAuthorId.Should().Be(ForeignAuthorId);
                        created.AuthorName.Should().Be(CanonicalName);
                        provider.UncachedLookups.Should().BePositive();
                        await AssertAuthorIdentity(client, created, cancellationToken);
                    }
                    finally
                    {
                        await host.StopAsync(CancellationToken.None);
                    }
                }

                SQLiteConnection.ClearAllPools();

                // Restart with the original SQLite files and no provider available to re-resolve the author.
                using var restartedHost = BuildHost(args);
                using var restartTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
                var restartToken = restartTimeout.Token;
                await restartedHost.StartAsync(restartToken);
                try
                {
                    using var client = CreateClient(restartedHost, apiKey);
                    await AssertAuthorIdentity(client, created, restartToken);
                    var all = await client.GetFromJsonAsync<List<AuthorResource>>("author", STJson.GetSerializerSettings(), restartToken);
                    all.Should().ContainSingle().Which.Id.Should().Be(created.Id);
                }
                finally
                {
                    await restartedHost.StopAsync(CancellationToken.None);
                }
            }
            finally
            {
                SQLiteConnection.ClearAllPools();
                await DeleteWorkspace(workspace);
            }
        }

        [Test]
        public async Task should_persist_provider_settings_and_fall_back_after_a_primary_401()
        {
            const string configPath = "config/metadataprovider";
            const string tokenVariable = "BIBLIOPHILARR_HARDCOVER_API_TOKEN";
            const string syntheticToken = "biblio-offline-provider-fixture";
            const string positiveTerm = "Fixture fallback work";
            const string negativeTerm = "Fixture provider miss";
            var dispatcher = new Mock<IHttpDispatcher>(MockBehavior.Strict);
            var calls = new ConcurrentQueue<string>();
            dispatcher.Setup(client => client.GetResponseAsync(It.IsAny<HttpRequest>(), It.IsAny<CookieContainer>()))
                .Returns((HttpRequest request, CookieContainer _) =>
                {
                    calls.Enqueue(request.Url.Host);
                    var headers = new HttpHeader { ContentType = "application/json" };

                    if (request.Url.Host == "api.hardcover.app" && request.Url.Path == "/v1/graphql" && request.Method == HttpMethod.Post)
                    {
                        var configuredTokenUsed = request.Headers.GetSingleValue("authorization") == $"Bearer {syntheticToken}";
                        configuredTokenUsed.Should().BeTrue("the persisted synthetic token must be used");
                        return Task.FromResult(new HttpResponse(request, headers, "{}", HttpStatusCode.Unauthorized));
                    }

                    if (request.Url.Host == "openlibrary.org" && request.Url.Path == "/search.json" && request.Method == HttpMethod.Get)
                    {
                        var query = request.Url.Query.TrimStart('?').Split('&');
                        var hit = query.Contains($"q={Uri.EscapeDataString(positiveTerm.ToLowerInvariant())}");
                        var miss = query.Contains($"q={Uri.EscapeDataString(negativeTerm.ToLowerInvariant())}");
                        (hit || miss).Should().BeTrue("the normal search term must reach the Open Library adapter");
                        var body = hit
                            ? """{"numFound":1,"docs":[{"key":"/works/OL276330W","title":"Fixture fallback work","author_key":["OL276330A"],"author_name":["Fixture Provider Author"]}]}"""
                            : """{"numFound":0,"docs":[]}""";
                        return Task.FromResult(new HttpResponse(request, headers, body));
                    }

                    throw new InvalidOperationException("Unexpected outbound call in the offline metadata fixture.");
                });

            var workspace = Directory.CreateTempSubdirectory("bibliophilarr-provider-330-");
            var data = workspace.FullName;
            var apiKey = Guid.NewGuid().ToString("N");
            var args = new[] { $"-data={data}", "-nobrowser", "-nosingleinstancecheck" };
            var previousToken = Environment.GetEnvironmentVariable(tokenVariable);
            Environment.SetEnvironmentVariable(tokenVariable, null);
            try
            {
                new XElement("Config",
                    new XElement("BindAddress", "127.0.0.1"),
                    new XElement("Port", 0),
                    new XElement("ApiKey", apiKey))
                    .Save(Path.Combine(data, "config.xml"));

                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
                var cancellationToken = timeout.Token;
                using (var host = BuildHost(args, dispatcher.Object))
                {
                    await host.StartAsync(cancellationToken);
                    try
                    {
                        using var client = CreateClient(host, apiKey);
                        var config = await client.GetFromJsonAsync<MetadataProviderConfigResource>(configPath, STJson.GetSerializerSettings(), cancellationToken);
                        config.EnableHardcoverFallback = true;
                        config.HardcoverApiToken = syntheticToken;
                        config.EnableOpenLibraryProvider = true;
                        config.EnableGoogleBooksProvider = false;
                        config.EnableGoogleBooksFallback = false;
                        config.EnableInventaireProvider = false;
                        config.EnableInventaireFallback = false;
                        config.MetadataProviderPriorityOrder = "Hardcover,OpenLibrary";
                        using var saved = await client.PutAsJsonAsync($"{configPath}/{config.Id}", config, STJson.GetSerializerSettings(), cancellationToken);
                        saved.StatusCode.Should().Be(HttpStatusCode.Accepted);
                        await AssertProviderConfig(client, syntheticToken, cancellationToken);
                    }
                    finally
                    {
                        await host.StopAsync(CancellationToken.None);
                    }
                }

                SQLiteConnection.ClearAllPools();
                using var restartedHost = BuildHost(args, dispatcher.Object);
                await restartedHost.StartAsync(cancellationToken);
                try
                {
                    using var client = CreateClient(restartedHost, apiKey);
                    await AssertProviderConfig(client, syntheticToken, cancellationToken);
                    calls.Should().BeEmpty();

                    (await Search(client, "xy", cancellationToken)).Should().BeEmpty();
                    (await GetEntitySearchTelemetry(client, cancellationToken)).Should().BeEmpty();
                    calls.Should().BeEmpty();

                    var messages = new MemoryTarget { Layout = "${message}" };
                    var capture = new LoggingConfiguration();
                    capture.AddRuleForAllLevels(messages);
                    var settings = LogManager.Configuration;
                    try
                    {
                        LogManager.Configuration = capture;
                        var result = await Search(client, positiveTerm, cancellationToken);
                        var book = result.Select(item => item.Book).Where(item => item != null).Should().ContainSingle().Which;
                        book.ForeignBookId.Should().Be("openlibrary:work:OL276330W");
                        book.Title.Should().Be(positiveTerm);
                        calls.ToArray().Should().Equal("api.hardcover.app", "api.hardcover.app", "openlibrary.org");

                        var log = messages.Logs.ToList();
                        var failureIndex = log.FindIndex(message => message.Contains("Metadata provider 'Hardcover' failed during search-for-new-entity"));
                        failureIndex.Should().BeGreaterThanOrEqualTo(0);
                        log.FindIndex(message => message.Contains("OpenLibraryProvider.SearchForNewBook"))
                            .Should().BeGreaterThan(failureIndex);

                        (await Search(client, negativeTerm, cancellationToken)).Should().BeEmpty();
                    }
                    finally
                    {
                        LogManager.Configuration = settings;
                    }

                    calls.ToArray().Should().Equal(
                        "api.hardcover.app",
                        "api.hardcover.app",
                        "openlibrary.org",
                        "api.hardcover.app",
                        "api.hardcover.app",
                        "openlibrary.org");
                    var telemetry = await GetEntitySearchTelemetry(client, cancellationToken);
                    var hardcover = telemetry.Single(provider => provider.ProviderName == "Hardcover");
                    hardcover.Calls.Should().Be(2);
                    hardcover.Failures.Should().Be(2);
                    hardcover.Successes.Should().Be(0);
                    var openLibrary = telemetry.Single(provider => provider.ProviderName == "OpenLibrary");
                    openLibrary.Calls.Should().Be(2);
                    openLibrary.Successes.Should().Be(2);
                    openLibrary.Failures.Should().Be(0);
                    openLibrary.FallbackHits.Should().Be(1);
                    openLibrary.TotalResultCount.Should().Be(2);
                }
                finally
                {
                    await restartedHost.StopAsync(CancellationToken.None);
                }
            }
            finally
            {
                Environment.SetEnvironmentVariable(tokenVariable, previousToken);
                SQLiteConnection.ClearAllPools();
                await DeleteWorkspace(workspace);
            }
        }

        private static async Task AssertProviderConfig(HttpClient client, string expectedToken, CancellationToken cancellationToken)
        {
            var config = await client.GetFromJsonAsync<MetadataProviderConfigResource>(
                "config/metadataprovider",
                STJson.GetSerializerSettings(),
                cancellationToken);
            config.EnableHardcoverFallback.Should().BeTrue();
            config.EnableOpenLibraryProvider.Should().BeTrue();
            config.EnableGoogleBooksProvider.Should().BeFalse();
            config.EnableGoogleBooksFallback.Should().BeFalse();
            config.EnableInventaireFallback.Should().BeFalse();
            config.MetadataProviderPriorityOrder.Should().Be("Hardcover,OpenLibrary");
            var tokenMatches = config.HardcoverApiToken == expectedToken;
            tokenMatches.Should().BeTrue("the synthetic token must survive config round-trip");
        }

        private static Task<List<SearchResource>> Search(HttpClient client, string term, CancellationToken cancellationToken)
        {
            return client.GetFromJsonAsync<List<SearchResource>>(
                $"search?term={Uri.EscapeDataString(term)}",
                STJson.GetSerializerSettings(),
                cancellationToken);
        }

        private static async Task<List<MetadataProviderOperationTelemetrySnapshot>> GetEntitySearchTelemetry(HttpClient client, CancellationToken cancellationToken)
        {
            var results = await client.GetFromJsonAsync<List<MetadataProviderOperationTelemetrySnapshot>>(
                "metadata/providers/telemetry/operations",
                STJson.GetSerializerSettings(),
                cancellationToken);
            return results.Where(operation => operation.OperationName == "search-for-new-entity").ToList();
        }

        private static async Task DeleteWorkspace(DirectoryInfo workspace)
        {
            for (var attempt = 1; ; attempt++)
            {
                try
                {
                    workspace.Delete(recursive: true);
                    return;
                }
                catch (Exception ex) when (attempt < 4 && ex is IOException or UnauthorizedAccessException)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(250 * attempt));
                }
            }
        }

        private static IHost BuildHost(string[] args, params IMetadataProvider[] providers)
        {
            return BuildHost(args, null, providers);
        }

        private static IHost BuildHost(string[] args, IHttpDispatcher dispatcher, params IMetadataProvider[] providers)
        {
            return Bootstrap.CreateConsoleHostBuilder(args, new StartupContext(args))
                .ConfigureServices(services =>
                {
                    services.PostConfigure<LogOptions>(options => options.AnalyticsEnabled = false);
                    services.PostConfigure<PostgresOptions>(options => options.Host = null);
                    services.PostConfigure<UpdateOptions>(options => options.PublicReleaseDiscovery = false);
                })
                .ConfigureWebHost(webHost => webHost.UseUrls("http://127.0.0.1:0"))
                .ConfigureContainer<IContainer>(container =>
                {
                    container.RegisterInstance(dispatcher ?? new Mock<IHttpDispatcher>(MockBehavior.Strict).Object, ifAlreadyRegistered: IfAlreadyRegistered.Replace);

                    // Bootstrap can resolve the orchestrator before this callback, so keep its registry instance.
                    var registry = container.Resolve<IMetadataProviderRegistry>();

                    // GetAllProviders returns a snapshot taken under the registry lock.
                    var registeredProviders = registry.GetAllProviders();
                    foreach (var provider in registeredProviders)
                    {
                        registry.UnregisterProvider(provider.ProviderName);
                    }

                    foreach (var provider in providers)
                    {
                        registry.RegisterProvider(provider);
                    }

                    if (dispatcher != null)
                    {
                        var configuration = container.Resolve<IConfigService>();
                        var http = new NzbDrone.Common.Http.HttpClient(
                            Array.Empty<IHttpRequestInterceptor>(),
                            container.Resolve<ICacheManager>(),
                            container.Resolve<IRateLimitService>(),
                            dispatcher,
                            LogManager.GetLogger(nameof(HttpClient)));
                        var openLibrary = new OpenLibraryClient(http, configuration, LogManager.GetLogger(nameof(OpenLibraryClient)));
                        registry.RegisterProvider(new OpenLibraryProvider(openLibrary, configuration, LogManager.GetLogger(nameof(OpenLibraryProvider))));
                        registry.RegisterProvider(new HardcoverFallbackSearchProvider(configuration, http, LogManager.GetLogger(nameof(HardcoverFallbackSearchProvider))));
                    }
                })
                .Build();
        }

        private static HttpClient CreateClient(IHost host, string apiKey)
        {
            var addresses = host.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>().Addresses;
            var address = addresses.Single();
            var client = new HttpClient(new HttpClientHandler { UseProxy = false }) { BaseAddress = new Uri(address + "/api/v1/") };
            client.DefaultRequestHeaders.Add("X-Api-Key", apiKey);
            return client;
        }

        private static async Task AssertAuthorIdentity(HttpClient client, AuthorResource created, CancellationToken cancellationToken)
        {
            var author = await client.GetFromJsonAsync<AuthorResource>($"author/{created.Id}", STJson.GetSerializerSettings(), cancellationToken);
            author.Should().NotBeNull();
            author.Id.Should().Be(created.Id);
            author.AuthorMetadataId.Should().Be(created.AuthorMetadataId);
            author.ForeignAuthorId.Should().Be(ForeignAuthorId);
            author.AuthorName.Should().Be(CanonicalName);
        }

        private sealed class FixtureAuthorProvider : IMetadataProvider, IProvideAuthorInfo
        {
            private int _uncachedLookups;

            public string ProviderName => "Hardcover";
            public int Priority => 1;
            public bool IsEnabled => true;
            public bool SupportsAuthorSearch => true;
            public bool SupportsBookSearch => false;
            public bool SupportsIsbnLookup => false;
            public bool SupportsSeriesInfo => false;
            public bool SupportsCoverImages => false;

            public int UncachedLookups => Volatile.Read(ref _uncachedLookups);

            public AuthorModel GetAuthorInfo(string bibliophilarrId, bool useCache)
            {
                if (bibliophilarrId != ForeignAuthorId)
                {
                    return null;
                }

                if (!useCache)
                {
                    Interlocked.Increment(ref _uncachedLookups);
                }

                return new AuthorModel
                {
                    Metadata = new AuthorMetadata
                    {
                        ForeignAuthorId = ForeignAuthorId,
                        Name = CanonicalName,
                        NameLastFirst = "Author, Fixture Canonical",
                        SortName = "fixture canonical author",
                        SortNameLastFirst = "author fixture canonical"
                    },
                    Books = new List<Book>(),
                    Series = new List<Series>()
                };
            }

            public HashSet<string> GetChangedAuthors(DateTime startTime) => new HashSet<string>();
        }
    }
}
