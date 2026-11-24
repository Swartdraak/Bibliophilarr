using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;
using DryIoc;
using DryIoc.Microsoft.DependencyInjection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Composition.Extensions;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Common.Http;
using NzbDrone.Common.Http.Dispatchers;
using NzbDrone.Common.Options;
using NzbDrone.Core.Authentication;
using NzbDrone.Core.Datastore;
using HttpClient = System.Net.Http.HttpClient;

namespace NzbDrone.Host.Test
{
    [TestFixture]
    [NonParallelizable]
    public class MetadataProviderUnauthorizedFallbackFixture
    {
        private const string ConfigRoute = "/api/v1/config/metadataprovider";
        private const string SearchRoute = "/api/v1/search?term=Orchard";
        private const string OperationRoute = "/api/v1/metadata/providers/telemetry/operations";
        private const string Operation = "search-for-new-entity";
        private const string WorkId = "openlibrary:work:OL981501W";
        private const string AuthorId = "openlibrary:author:OL981401A";
        private const string SearchBody = """
            {"numFound":1,"numFoundExact":true,"start":0,"docs":[
              {"key":"/works/OL981501W","title":"The Orchard Observatory",
               "author_name":["Rowan Orchard"],"author_key":["OL981401A"],"language":["eng"]}
            ]}
            """;

        [Test]
        public async Task should_fall_back_after_a_primary_401_and_only_count_provider_attempts_on_signed_valid_search()
        {
            Assert.That(
                Environment.GetEnvironmentVariable("BIBLIOPHILARR_HARDCOVER_API_TOKEN") is null,
                Is.True,
                "Run the offline fixture without ambient Hardcover credentials");
            var profile = Directory.CreateTempSubdirectory("bibliophilarr-provider-fallback-");
            var providerToken = Guid.NewGuid().ToString("N");
            var dispatches = new ConcurrentQueue<string>();
            var dispatcher = new Mock<IHttpDispatcher>(MockBehavior.Strict);
            dispatcher.Setup(x => x.GetResponseAsync(It.IsAny<HttpRequest>(), It.IsAny<CookieContainer>()))
                .ReturnsAsync((HttpRequest request, CookieContainer _) => Dispatch(request, providerToken, dispatches));

            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(75));
                var token = timeout.Token;
                string key;
                using (var first = BuildHost(profile, dispatcher.Object))
                {
                    await first.StartAsync(token);
                    try
                    {
                        key = await ReadKey(profile, token);
                        using var client = CreateClient(first);
                        await SaveProviderSettings(client, key, providerToken, token);
                        await AssertProviderSettings(client, key, providerToken, token);
                    }
                    finally
                    {
                        await first.StopAsync(TimeSpan.FromSeconds(15));
                    }
                }

                using var reopened = BuildHost(profile, dispatcher.Object);
                await reopened.StartAsync(token);
                try
                {
                    Assert.That(key == await ReadKey(profile, token), Is.True, "The generated app key must persist");
                    using var client = CreateClient(reopened);
                    await AssertProviderSettings(client, key, providerToken, token);
                    Assert.That(dispatches, Is.Empty, "Settings and startup must not contact metadata providers");
                    var baseline = await ReadCounts(client, key, token);
                    using var valid = await Send(client, HttpMethod.Get, SearchRoute, key, token);
                    var results = await ReadJson(valid, HttpStatusCode.OK, token);
                    Assert.That(
                        results.EnumerateArray().Select(item => item.GetProperty("foreignId").GetString()),
                        Is.EquivalentTo(new[] { AuthorId, WorkId }));
                    Assert.That(results.EnumerateArray().Single(item => item.GetProperty("foreignId").GetString() == WorkId)
                        .GetProperty("book").GetProperty("title").GetString(), Is.EqualTo("The Orchard Observatory"));
                    Assert.That(
                        dispatches.ToArray(),
                        Is.EqualTo(new[] { "Hardcover", "Hardcover", "OpenLibrary" }),
                        "Both Hardcover 401 responses must precede the keyless fallback");
                    var afterSearch = await ReadCounts(client, key, token);
                    Assert.That(afterSearch.Hardcover - baseline.Hardcover, Is.EqualTo(new Counts(1, 0, 1, 0, 0)));
                    Assert.That(afterSearch.OpenLibrary - baseline.OpenLibrary, Is.EqualTo(new Counts(1, 1, 0, 1, 2)));

                    using var anonymous = await Send(client, HttpMethod.Get, SearchRoute, null, token);
                    Assert.That(anonymous.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
                    using var wrongKey = await Send(client, HttpMethod.Get, SearchRoute, Guid.NewGuid().ToString("N"), token);
                    Assert.That(wrongKey.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
                    using var shortSearch = await Send(client, HttpMethod.Get, "/api/v1/search?term=ab", key, token);
                    var empty = await ReadJson(shortSearch, HttpStatusCode.OK, token);
                    Assert.That(empty.GetArrayLength(), Is.Zero);
                    Assert.That(await ReadCounts(client, key, token), Is.EqualTo(afterSearch));
                    Assert.That(
                        dispatches.ToArray(),
                        Is.EqualTo(new[] { "Hardcover", "Hardcover", "OpenLibrary" }),
                        "Denied and short searches must not issue provider requests");
                    dispatcher.Verify(x => x.GetResponseAsync(It.IsAny<HttpRequest>(), It.IsAny<CookieContainer>()), Times.Exactly(3));
                }
                finally
                {
                    await reopened.StopAsync(TimeSpan.FromSeconds(15));
                }
            }
            finally
            {
                SQLiteConnection.ClearAllPools();
                profile.Delete(recursive: true);
            }
        }

        private static HttpResponse Dispatch(HttpRequest request, string providerToken, ConcurrentQueue<string> dispatches)
        {
            if (request.Url.Scheme == "https" && request.Url.Host == "api.hardcover.app" &&
                request.Url.Path == "/v1/graphql" && request.Method == HttpMethod.Post)
            {
                Assert.That(
                    request.Headers.GetSingleValue("Authorization") == $"Bearer {providerToken}",
                    Is.True,
                    "Hardcover must dispatch with the persisted synthetic credential");
                dispatches.Enqueue("Hardcover");
                return new HttpResponse(request, new HttpHeader(), string.Empty, HttpStatusCode.Unauthorized);
            }

            if (request.Url.Scheme == "https" && request.Url.Host == "openlibrary.org" &&
                request.Url.Path == "/search.json" && request.Method == HttpMethod.Get)
            {
                Assert.That(string.IsNullOrEmpty(request.Headers.GetSingleValue("Authorization")), Is.True);
                Assert.That(string.IsNullOrEmpty(request.Headers.GetSingleValue("X-Api-Key")), Is.True);
                dispatches.Enqueue("OpenLibrary");
                return new HttpResponse(request, new HttpHeader { ContentType = "application/json" }, SearchBody);
            }

            throw new AssertionException("Unexpected outbound request in the metadata fallback fixture");
        }

        private static IHost BuildHost(DirectoryInfo profile, IHttpDispatcher dispatcher)
        {
            var args = new[] { $"-data={profile.FullName}", "-nobrowser", "-nosingleinstancecheck" };

            // Bootstrap can instantiate the providers before later container callbacks run.
            var container = new Container(rules => rules.WithNzbDroneRules());
            container.RegisterDelegate<IHttpDispatcher>(_ => dispatcher, setup: Setup.Decorator);
            return Bootstrap.CreateConsoleHostBuilder(args, new StartupContext(args))
                .UseServiceProviderFactory(new DryIocServiceProviderFactory(container))
                .ConfigureServices(services =>
                {
                    services.PostConfigure<AuthOptions>(options =>
                    {
                        options.Method = nameof(AuthenticationType.Forms);
                        options.Required = nameof(AuthenticationRequiredType.Enabled);
                        options.ApiKey = null;
                    });
                    services.PostConfigure<LogOptions>(options => options.AnalyticsEnabled = false);
                    services.PostConfigure<UpdateOptions>(options => options.PublicReleaseDiscovery = false);
                    services.PostConfigure<PostgresOptions>(options => options.Host = null);
                    services.PostConfigure<ServerOptions>(options => options.UrlBase = string.Empty);
                })
                .ConfigureWebHost(web => web.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0)))
                .Build();
        }

        private static HttpClient CreateClient(IHost host)
        {
            var address = host.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>().Addresses.Single();
            var origin = new Uri(address);
            Assert.That(origin.IsLoopback, Is.True);
            return new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = false, UseProxy = false }) { BaseAddress = origin };
        }

        private static async Task<string> ReadKey(DirectoryInfo profile, CancellationToken token)
        {
            var path = Path.Combine(profile.FullName, "config.xml");
            while (true)
            {
                try
                {
                    var key = XDocument.Load(path).Root?.Element("ApiKey")?.Value;
                    if (!string.IsNullOrWhiteSpace(key))
                    {
                        Assert.That(Guid.TryParseExact(key, "N", out var parsed) && parsed != Guid.Empty, Is.True);
                        return key;
                    }
                }
                catch (IOException)
                {
                }
                catch (XmlException)
                {
                }

                await Task.Delay(50, token);
            }
        }

        private static async Task SaveProviderSettings(HttpClient client, string key, string providerToken, CancellationToken token)
        {
            using var config = await Send(client, HttpMethod.Get, ConfigRoute, key, token);
            var resource = JsonNode.Parse((await ReadJson(config, HttpStatusCode.OK, token)).GetRawText()).AsObject();
            resource["enableHardcoverFallback"] = true;
            resource["hardcoverApiToken"] = providerToken;
            resource["enableOpenLibraryProvider"] = true;
            resource["enableGoogleBooksProvider"] = false;
            resource["enableGoogleBooksFallback"] = false;
            resource["enableInventaireProvider"] = false;
            resource["enableInventaireFallback"] = false;
            resource["metadataProviderRetryBudget"] = 0;
            resource["openLibrarySearchRetryBudget"] = 0;
            using var saved = await Send(client, HttpMethod.Put, ConfigRoute + "/1", key, token, JsonContent.Create(resource));
            Assert.That(saved.StatusCode, Is.EqualTo(HttpStatusCode.Accepted));
        }

        private static async Task AssertProviderSettings(HttpClient client, string key, string providerToken, CancellationToken token)
        {
            using var config = await Send(client, HttpMethod.Get, ConfigRoute, key, token);
            var body = await ReadJson(config, HttpStatusCode.OK, token);
            Assert.That(body.GetProperty("enableHardcoverFallback").GetBoolean(), Is.True);
            Assert.That(body.GetProperty("enableOpenLibraryProvider").GetBoolean(), Is.True);
            Assert.That(
                body.GetProperty("hardcoverApiToken").GetString() == providerToken,
                Is.True,
                "The configured synthetic provider credential must round-trip");
        }

        private static async Task<(Counts Hardcover, Counts OpenLibrary)> ReadCounts(HttpClient client, string key, CancellationToken token)
        {
            using var response = await Send(client, HttpMethod.Get, OperationRoute, key, token);
            var operations = (await ReadJson(response, HttpStatusCode.OK, token)).EnumerateArray()
                .Where(entry => entry.GetProperty("operationName").GetString() == Operation)
                .ToDictionary(entry => entry.GetProperty("providerName").GetString(), entry => new Counts(
                    entry.GetProperty("calls").GetInt64(),
                    entry.GetProperty("successes").GetInt64(),
                    entry.GetProperty("failures").GetInt64(),
                    entry.GetProperty("fallbackHits").GetInt64(),
                    entry.GetProperty("totalResultCount").GetInt64()));
            Assert.That(operations.Keys, Is.SubsetOf(new[] { "Hardcover", "OpenLibrary" }));
            return (operations.GetValueOrDefault("Hardcover"), operations.GetValueOrDefault("OpenLibrary"));
        }

        private static async Task<HttpResponseMessage> Send(HttpClient client, HttpMethod method, string path, string key, CancellationToken token, HttpContent body = null)
        {
            using var request = new HttpRequestMessage(method, path) { Content = body };
            if (key != null)
            {
                request.Headers.Add("X-Api-Key", key);
            }

            return await client.SendAsync(request, token);
        }

        private static async Task<JsonElement> ReadJson(HttpResponseMessage response, HttpStatusCode status, CancellationToken token)
        {
            Assert.That(response.StatusCode, Is.EqualTo(status));
            Assert.That(response.Content.Headers.ContentType?.MediaType, Is.EqualTo("application/json"));
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
            return document.RootElement.Clone();
        }

        private readonly struct Counts
        {
            public Counts(long calls, long successes, long failures, long fallbackHits, long results)
            {
                Calls = calls;
                Successes = successes;
                Failures = failures;
                FallbackHits = fallbackHits;
                Results = results;
            }

            public long Calls { get; }
            public long Successes { get; }
            public long Failures { get; }
            public long FallbackHits { get; }
            public long Results { get; }

            public static Counts operator -(Counts after, Counts before) => new Counts(
                after.Calls - before.Calls,
                after.Successes - before.Successes,
                after.Failures - before.Failures,
                after.FallbackHits - before.FallbackHits,
                after.Results - before.Results);
        }
    }
}
