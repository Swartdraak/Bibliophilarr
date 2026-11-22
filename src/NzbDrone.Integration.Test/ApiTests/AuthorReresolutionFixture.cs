using System;
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
using Bibliophilarr.Api.V1.RootFolders;
using DryIoc;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Common.Http.Dispatchers;
using NzbDrone.Common.Options;
using NzbDrone.Common.Serializer;
using NzbDrone.Core.Books;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Host;
using NzbDrone.Test.Common.Categories;
using AuthorModel = NzbDrone.Core.Books.Author;

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
                    // Test HTTP requests use the real loopback client. The app itself has no network fixture.
                    var dispatcher = new Mock<IHttpDispatcher>();
                    dispatcher.Setup(client => client.GetResponseAsync(It.IsAny<NzbDrone.Common.Http.HttpRequest>(), It.IsAny<CookieContainer>()))
                        .ThrowsAsync(new InvalidOperationException("The author persistence fixture does not permit outbound HTTP."));
                    container.RegisterInstance(dispatcher.Object, ifAlreadyRegistered: IfAlreadyRegistered.Replace);

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
