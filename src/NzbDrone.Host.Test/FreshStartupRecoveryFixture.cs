using System;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
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
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Common.Http.Dispatchers;
using NzbDrone.Common.Options;
using NzbDrone.Core.Authentication;
using NzbDrone.Core.Datastore;
using NzbDrone.Test.Common.Datastore;

namespace NzbDrone.Host.Test
{
    [TestFixture]
    [NonParallelizable]
    public class FreshStartupRecoveryFixture
    {
        [Test]
        public async Task should_complete_main_migrations_and_persist_a_working_generated_key_on_first_start()
        {
            var profile = Directory.CreateTempSubdirectory("bibliophilarr-fresh-startup-");
            var args = new[] { $"-data={profile.FullName}", "-nobrowser", "-nosingleinstancecheck" };
            var outbound = new Mock<IHttpDispatcher>(MockBehavior.Strict);

            try
            {
                Assert.That(profile.EnumerateFileSystemInfos(), Is.Empty);
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
                using var host = Bootstrap.CreateConsoleHostBuilder(args, new StartupContext(args))
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
                        services.PostConfigure<ServerOptions>(options => options.UrlBase = string.Empty);
                        services.PostConfigure<PostgresOptions>(options => options.Host = null);
                    })
                    .ConfigureWebHost(web => web.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0)))
                    .ConfigureContainer<DryIocServiceProvider>(provider => provider.Container.RegisterInstance(outbound.Object, ifAlreadyRegistered: IfAlreadyRegistered.Replace))
                    .Build();

                await host.StartAsync(timeout.Token);
                try
                {
                    var key = await ReadPersistedKey(Path.Combine(profile.FullName, "config.xml"), timeout.Token);
                    Assert.That(Guid.TryParseExact(key, "N", out var parsedKey) && parsedKey != Guid.Empty, Is.True, "Startup must persist a generated API key");

                    var address = host.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>().Addresses.Single();
                    var origin = new Uri(address);
                    Assert.That(origin.IsLoopback, Is.True);

                    using var handler = new SocketsHttpHandler { AllowAutoRedirect = false, UseProxy = false };
                    using var client = new HttpClient(handler) { BaseAddress = origin };
                    using var anonymous = await client.GetAsync("api/v1/system/status", timeout.Token);
                    Assert.That(anonymous.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
                    using var request = new HttpRequestMessage(HttpMethod.Get, "api/v1/system/status");
                    request.Headers.Add("X-Api-Key", key);
                    using var authenticated = await client.SendAsync(request, timeout.Token);
                    Assert.That(authenticated.StatusCode, Is.EqualTo(HttpStatusCode.OK));

                    var connectionString = new SQLiteConnectionStringBuilder
                    {
                        DataSource = Path.Combine(profile.FullName, "bibliophilarr.db"),
                        FailIfMissing = true,
                        Pooling = false,
                        ReadOnly = true
                    };
                    using var database = new SQLiteConnection(connectionString.ConnectionString);
                    database.Open();
                    SqliteDatabase.AssertCompleteMainMigration(database);
                    outbound.VerifyNoOtherCalls();
                }
                finally
                {
                    await host.StopAsync(TimeSpan.FromSeconds(15));
                }
            }
            finally
            {
                SQLiteConnection.ClearAllPools();
                profile.Delete(recursive: true);
            }
        }

        private static async Task<string> ReadPersistedKey(string path, CancellationToken cancellationToken)
        {
            while (true)
            {
                try
                {
                    // ApplicationStarted subscribers may still be writing the initial XML.
                    var key = XDocument.Load(path).Root?.Element("ApiKey")?.Value;
                    if (!string.IsNullOrWhiteSpace(key))
                    {
                        return key;
                    }
                }
                catch (IOException)
                {
                }
                catch (XmlException)
                {
                }

                await Task.Delay(TimeSpan.FromMilliseconds(50), cancellationToken);
            }
        }
    }
}
