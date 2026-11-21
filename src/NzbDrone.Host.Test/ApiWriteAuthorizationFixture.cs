using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using DryIoc;
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
using NzbDrone.Core.Update;

namespace NzbDrone.Host.Test
{
    [TestFixture]
    [NonParallelizable]
    public class ApiWriteAuthorizationFixture
    {
        [Test]
        public async Task UpdateCheckRequiresAnExplicitApiCredentialEvenWithAFormsSession()
        {
            const string username = "api-write-fixture";
            const string password = "Local-Fixture-Password-42";
            const string checkPath = "api/v1/update/check";
            var apiKey = Guid.NewGuid().ToString("N");
            var profile = Directory.CreateTempSubdirectory("bibliophilarr-api-write-");
            var args = new[] { $"-data={profile.FullName}", "-nobrowser", "-nosingleinstancecheck" };
            var discovery = new Mock<IPublicReleaseDiscovery>(MockBehavior.Strict);
            discovery.SetupGet(service => service.Enabled).Returns(false);
            discovery.Setup(service => service.Check(It.IsAny<Version>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new PublicReleaseCheck(PublicReleaseCheckStatus.UpToDate, BuildInfo.Version.ToString()));
            var outbound = new Mock<IHttpDispatcher>(MockBehavior.Strict);

            try
            {
                new XElement("Config",
                    new XElement("BindAddress", "127.0.0.1"),
                    new XElement("Port", 0),
                    new XElement("LaunchBrowser", false),
                    new XElement("AuthenticationMethod", "Forms"),
                    new XElement("AuthenticationRequired", "Enabled"),
                    new XElement("ApiKey", apiKey)).Save(Path.Combine(profile.FullName, "config.xml"));

                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
                var cancellationToken = timeout.Token;
                using var host = Bootstrap.CreateConsoleHostBuilder(args, new StartupContext(args))
                    .ConfigureServices(services =>
                    {
                        services.PostConfigure<AuthOptions>(options =>
                        {
                            options.Enabled = false;
                            options.Method = nameof(AuthenticationType.Forms);
                            options.Required = nameof(AuthenticationRequiredType.Enabled);
                            options.ApiKey = apiKey;
                        });
                        services.PostConfigure<LogOptions>(options => options.AnalyticsEnabled = false);
                        services.PostConfigure<UpdateOptions>(options => options.PublicReleaseDiscovery = false);
                        services.PostConfigure<ServerOptions>(options => options.UrlBase = string.Empty);
                        services.PostConfigure<PostgresOptions>(options => options.Host = null);
                    })
                    .ConfigureWebHost(web => web.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0)))
                    .ConfigureContainer<IContainer>(container =>
                    {
                        container.RegisterInstance(discovery.Object, ifAlreadyRegistered: IfAlreadyRegistered.Replace);
                        container.RegisterInstance(outbound.Object, ifAlreadyRegistered: IfAlreadyRegistered.Replace);
                    })
                    .Build();

                await host.StartAsync(cancellationToken);
                try
                {
                    host.Services.GetRequiredService<IUserService>().Add(username, password);
                    var address = host.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>().Addresses.Single();
                    var origin = new Uri(address);
                    Assert.That(origin.IsLoopback, Is.True);

                    using var formsHandler = new HttpClientHandler { AllowAutoRedirect = false, UseProxy = false };
                    using var forms = new HttpClient(formsHandler) { BaseAddress = origin };
                    await Login(forms, username, password, cancellationToken);

                    using var session = await forms.GetAsync("initialize.json", cancellationToken);
                    Assert.That(session.StatusCode, Is.EqualTo(HttpStatusCode.OK), "the Forms login must create a usable session");
                    using var formsOnly = await forms.PostAsync(checkPath, null, cancellationToken);
                    Assert.That(formsOnly.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
                    discovery.Verify(service => service.Check(It.IsAny<Version>(), It.IsAny<CancellationToken>()), Times.Never());

                    using var keyHandler = new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false, UseProxy = false };
                    using var keyOnly = new HttpClient(keyHandler) { BaseAddress = origin };
                    using var header = new HttpRequestMessage(HttpMethod.Post, checkPath);
                    header.Headers.Add("X-Api-Key", apiKey);
                    using var headerResponse = await keyOnly.SendAsync(header, cancellationToken);
                    Assert.That(headerResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

                    using var bearer = new HttpRequestMessage(HttpMethod.Post, checkPath);
                    bearer.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
                    using var bearerResponse = await keyOnly.SendAsync(bearer, cancellationToken);
                    Assert.That(bearerResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

                    using var sessionWithKey = new HttpRequestMessage(HttpMethod.Post, checkPath);
                    sessionWithKey.Headers.Add("X-Api-Key", apiKey);
                    using var sessionWithKeyResponse = await forms.SendAsync(sessionWithKey, cancellationToken);
                    Assert.That(sessionWithKeyResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

                    discovery.Verify(service => service.Check(BuildInfo.Version, It.IsAny<CancellationToken>()), Times.Exactly(3));
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

        private static async Task Login(HttpClient client, string username, string password, CancellationToken cancellationToken)
        {
            using var tokenResponse = await client.GetAsync("bootstrap", cancellationToken);
            Assert.That(tokenResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            using var tokens = JsonDocument.Parse(await tokenResponse.Content.ReadAsStringAsync(cancellationToken));
            var token = tokens.RootElement.GetProperty("requestToken").GetString();
            using var form = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["username"] = username,
                ["password"] = password,
                ["__RequestVerificationToken"] = token
            });
            using var response = await client.PostAsync("login", form, cancellationToken);
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Redirect));
            Assert.That(response.Headers.Location?.OriginalString, Is.EqualTo("/"));
        }
    }
}
