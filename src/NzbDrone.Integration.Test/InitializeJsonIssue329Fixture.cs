using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using NUnit.Framework;
using NzbDrone.Core.Authentication;

namespace NzbDrone.Integration.Test
{
    [TestFixture]
    public class InitializeJsonIssue329Fixture : IntegrationTest
    {
        [Test]
        public async Task local_ui_bypass_should_allow_the_spa_to_initialize()
        {
            using var client = new HttpClient { BaseAddress = new Uri(RootUrl) };
            using var response = await client.GetAsync("initialize.json");

            await AssertInitializeResponse(response);
        }

        [Test]
        public async Task required_authentication_should_protect_spa_bootstrap_until_forms_login()
        {
            const string username = "integration-user";
            const string password = "integration-user-password";
            var original = HostConfig.GetSingle();
            var config = HostConfig.GetSingle();
            config.AuthenticationMethod = AuthenticationType.Forms;
            config.AuthenticationRequired = AuthenticationRequiredType.Enabled;
            config.Username = username;
            config.Password = password;
            config.PasswordConfirmation = password;
            HostConfig.Put(config, HttpStatusCode.Accepted);

            try
            {
                using var handler = new HttpClientHandler { AllowAutoRedirect = false };
                using var client = new HttpClient(handler) { BaseAddress = new Uri(RootUrl) };
                using var anonymous = await client.GetAsync("initialize.json");
                Assert.That(anonymous.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));

                using var browserNavigation = new HttpRequestMessage(HttpMethod.Get, "authors");
                browserNavigation.Headers.Accept.ParseAdd("text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
                using var redirected = await client.SendAsync(browserNavigation);
                Assert.That(redirected.StatusCode, Is.EqualTo(HttpStatusCode.Redirect));
                Assert.That(redirected.Headers.Location?.AbsolutePath, Is.EqualTo("/login"));
                Assert.That(redirected.Headers.Location?.Query, Does.Contain("returnUrl=%2Fauthors"));

                using var bootstrapNavigation = new HttpRequestMessage(HttpMethod.Get, "initialize.json");
                bootstrapNavigation.Headers.Accept.ParseAdd("text/html");
                using var bootstrapDenied = await client.SendAsync(bootstrapNavigation);
                Assert.That(bootstrapDenied.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));

                using var tokenResponse = await client.GetAsync("bootstrap");
                Assert.That(tokenResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
                using var tokens = JsonDocument.Parse(await tokenResponse.Content.ReadAsStringAsync());
                var requestToken = tokens.RootElement.GetProperty("requestToken").GetString();
                Assert.That(requestToken, Is.Not.Null.And.Not.Empty);

                using var unsignedForm = LoginForm(username, password);
                using var unsigned = await client.PostAsync("login", unsignedForm);
                Assert.That(unsigned.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));

                using var badForm = LoginForm(username, "invalid-password", requestToken);
                using var rejected = await client.PostAsync("login", badForm);
                Assert.That(rejected.StatusCode, Is.EqualTo(HttpStatusCode.Redirect));
                Assert.That(rejected.Headers.Location?.OriginalString, Does.Contain("loginFailed=true"));
                Assert.That(rejected.Headers.Contains("Set-Cookie"), Is.False);

                using var validForm = LoginForm(username, password, requestToken);
                using var login = await client.PostAsync("login", validForm);
                Assert.That(login.StatusCode, Is.EqualTo(HttpStatusCode.Redirect));
                Assert.That(login.Headers.Location?.OriginalString, Is.EqualTo("/"));
                Assert.That(handler.CookieContainer.GetCookies(client.BaseAddress)["BibliophilarrAuth"], Is.Not.Null);

                using var response = await client.GetAsync("initialize.json");
                await AssertInitializeResponse(response);

                using var page = await client.GetAsync("");
                Assert.That(page.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            }
            finally
            {
                HostConfig.Put(original, HttpStatusCode.Accepted);
            }
        }

        [Test]
        public async Task basic_authentication_should_allow_an_authenticated_ui_and_bootstrap()
        {
            const string username = "integration-user";
            const string password = "integration-user-password";
            var original = HostConfig.GetSingle();
            var config = HostConfig.GetSingle();
            config.AuthenticationMethod = AuthenticationType.Basic;
            config.AuthenticationRequired = AuthenticationRequiredType.Enabled;
            config.Username = username;
            config.Password = password;
            config.PasswordConfirmation = password;
            HostConfig.Put(config, HttpStatusCode.Accepted);

            try
            {
                using var handler = new HttpClientHandler { AllowAutoRedirect = false };
                using var client = new HttpClient(handler) { BaseAddress = new Uri(RootUrl) };
                using var anonymous = await client.GetAsync("initialize.json");
                Assert.That(anonymous.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));

                var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{username}:{password}"));
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
                using var bootstrap = await client.GetAsync("initialize.json");
                await AssertInitializeResponse(bootstrap);

                using var page = await client.GetAsync("authors");
                Assert.That(page.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            }
            finally
            {
                HostConfig.Put(original, HttpStatusCode.Accepted);
            }
        }

        private async Task AssertInitializeResponse(HttpResponseMessage response)
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(response.Headers.CacheControl?.NoStore, Is.True);
            using var payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.That(payload.RootElement.GetProperty("apiRoot").GetString(), Is.EqualTo("/api/v1"));
            Assert.That(payload.RootElement.GetProperty("apiKey").GetString() == ApiKey, Is.True, "bootstrap should use the configured API key");
        }

        private static FormUrlEncodedContent LoginForm(string username, string password, string requestToken = null)
        {
            var fields = new Dictionary<string, string>
            {
                ["username"] = username,
                ["password"] = password
            };
            if (requestToken != null)
            {
                fields["__RequestVerificationToken"] = requestToken;
            }

            return new FormUrlEncodedContent(fields);
        }
    }
}
