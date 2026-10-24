using System.Net;
using System.Net.Http;
using FluentAssertions;
using NUnit.Framework;

namespace NzbDrone.Integration.Test
{
    // Issue #329 regression: the SPA's boot endpoint /initialize.json must reach the SPA's
    // index bundle on an anonymous GET, even when authentication is required (the test
    // runner's config uses AuthenticationMethod=Forms + AuthenticationRequired=
    // DisabledForLocalAddresses, and the test client connects from localhost).
    //
    // Before the fix, UiAuthenticationMiddleware (introduced for #313) classified
    // GET /initialize.json as a UI-shell GET and issued a Forms challenge (401, empty
    // body) BEFORE routing/AllowAnonymous was evaluated. The SPA's index.ts performs
    // `const response = await fetch('/initialize.json'); window.Bibliophilarr =
    // await response.json();` as a top-level await with no error handling, so the 401
    // (empty body) made response.json() throw, bootstrap() never ran, and every authed
    // SPA route rendered a blank app shell (the b4-b7 byte-identical screenshots in the
    // issue). The controller is [AllowAnonymous] and must serve the bootstrap payload
    // (apiRoot, apiKey, urlBase, ...) exactly like the pre-#315 behavior the UI bundle
    // relies on.
    [TestFixture]
    public class InitializeJsonIssue329Fixture : IntegrationTest
    {
        private readonly HttpClient _httpClient = new HttpClient();

        private (HttpResponseMessage Response, string Body) GetInitializeJson()
        {
            // /initialize.json is served at the site root (not under /api/v1), so build the
            // absolute URL from RootUrl (RootUrl ends with '/'), matching how the SPA's index
            // bundle fetches it and how IndexHtmlFixture addresses the root.
            var request = new HttpRequestMessage(HttpMethod.Get, RootUrl + "initialize.json");
            var response = _httpClient.Send(request);
            var body = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            return (response, body);
        }

        [Test]
        public void anonymous_get_initialize_json_should_serve_bootstrap_payload_not_forms_challenge()
        {
            var (response, body) = GetInitializeJson();

            response.StatusCode.Should().Be(HttpStatusCode.OK,
                "issue #329: a Forms challenge (401) on /initialize.json blanks every SPA route");
            response.Headers.Location.Should().BeNull(
                "issue #329: a Forms challenge sets Location: /login; the bootstrap must pass through to the [AllowAnonymous] controller");
            body.Should().NotBeNullOrWhiteSpace(
                "issue #329: the 401 challenge body is empty, which is what crashes the SPA's response.json()");
            body.Should().Contain("\"apiRoot\"");
            body.Should().Contain("\"apiKey\"");
            body.Should().Contain("\"urlBase\"");
        }

        [Test]
        public void initialize_json_api_key_should_match_persisted_key_per_request()
        {
            var (_, body) = GetInitializeJson();

            // The runner's ApiKey is the key persisted to config.xml at startup; the
            // controller must read it per request (not a stale static cache captured at
            // construction, which could predate the finalized key — issue #309 class).
            // This is the per-request read the fix enforces: the served key equals the
            // persisted key the test runner exposes, so a stale constructor-time cache
            // (which can hold a pre-finalization key) would NOT match.
            body.Should().Contain($"\"apiKey\": \"{ApiKey}\"");

            // The bootstrap payload must also carry the SPA's apiRoot and urlBase fields
            // (the SPA consumes both; apiRoot is relative and urlBase is the server-rendered
            // value — returned independently, as the SPA's index bundle expects).
            body.Should().Contain("\"apiRoot\": \"/api/v1\"");
            body.Should().Contain("\"urlBase\":");
        }
    }
}
