using System.Net;
using NUnit.Framework;
using NzbDrone.Integration.Test.Client;

namespace NzbDrone.Integration.Test.ApiTests
{
    // Issue #403 [AUTHZ/CONTRACT][INTEGRATIONS]: anonymous GET /api/v1/search must be
    // challenged with 401 (Unauthorized) under AuthenticationMethod=None +
    // AuthenticationRequired=Enabled, never a 3xx redirect.
    //
    // The integrations-stage governed recovery handoff (case ab36355914, task
    // t_87f353e1, source run 4462, head 92c9c7e0) recorded a 3xx redirect for an
    // anonymous GET /api/v1/search?term=probe with AuthenticationMethod=None
    // (default) + AuthenticationRequired=Enabled (default). This fixture reproduces
    // that scenario in the STANDARD repository integration harness (NzbDroneRunner,
    // CI-parity with the existing metadata-diagnostics-integration lane) and pins
    // the 401/403 (never 3xx, never 200) contract for an anonymous /api/v1 GET.
    //
    // Mechanism established at the current head (de65e3c9): with
    // AuthenticationMethod=None + AuthenticationRequired=Enabled, an anonymous
    // /api/v1 GET is denied by the UI policy (UiAuthorizationHandler.context.Fail()
    // for anonymous users under Enabled — the BypassableDenyAnonymous requirement's
    // Enabled branch) and the resulting challenge resolves through the Forms default
    // challenge scheme (DefaultChallengeScheme=Forms,
    // AuthenticationBuilderExtensions.cs L69-73), which emits a 302 redirect to
    // /login (CookieAuthenticationOptions.LoginPath="/login") instead of the 401 the
    // API scheme's HandleChallengeAsync would emit. The committed fallback-policy
    // chain (Startup.cs L212-214, [API, Forms] RequireAuthenticatedUser) is intact,
    // but the observed challenge path for an anonymous /api/v1 GET under the None
    // auth method is the Forms 3xx, not the API 401. This fixture is the durable,
    // reusable repository test the issue asked for: it turns the 3xx-vs-401
    // observation into a failing repository test (RED at base) that will go GREEN
    // when the authz contract is repaired, and stays GREEN afterwards (contract pin).
    [TestFixture]
    public class ApiAuthzContractIssue403AnonymousApiGetFixture : IntegrationTest
    {
        [OneTimeSetUp]
        public void EnableFullAuthentication()
        {
            // The issue #403 recorded scenario is AuthenticationRequired=Enabled. The
            // shared NzbDroneRunner integration harness seeds
            // AuthenticationRequired=DisabledForLocalAddresses (NzbDroneRunner.cs L255)
            // so local test traffic is not challenged by default; under that mode an
            // anonymous local GET to /api/v1/search passes straight through (200),
            // which is the harness's designed behavior, not the production default.
            // Flip the live config to Enabled BEFORE any anonymous request so this
            // fixture exercises the issue #403 scenario exactly:
            // AuthenticationMethod=None (harness default, NzbDroneRunner L254) +
            // AuthenticationRequired=Enabled (per the issue's recorded state and the
            // app's own ConfigFileProvider default, ConfigFileProvider.cs L213-216).
            // [OneTimeSetUp] here runs after the base SmokeTestSetup (the base class's
            // method has the lower-priority OneTime attribute), so the host is up, the
            // API key client is ready, and the config PUT authenticates with the key
            // the harness generated. The base [SetUp] IntegrationSetUp then runs
            // before every test and observes Enabled for the rest of the fixture.
            var config = HostConfig.GetSingle();
            config.AuthenticationRequired = NzbDrone.Core.Authentication.AuthenticationRequiredType.Enabled;
            HostConfig.Put(config, HttpStatusCode.Accepted);
        }

        // Primary contract: the exact issue #403 endpoint shape — an anonymous GET
        // against /api/v1/search (the search term is a query parameter only; the
        // authorization denial happens in the authorization middleware BEFORE the
        // controller action runs, so the term value is irrelevant to the status
        // code and the controller's orchestrator dependency is never touched).
        // RED at base: the response is a 302 redirect to /login (Forms default
        // challenge scheme) instead of 401 (the API scheme's challenge contract).
        [Test]
        public void anonymous_get_api_v1_search_should_be_challenged_with_401_not_3xx()
        {
            var request = new SimpleRestRequest("search");
            request.AddParameter("term", "probe");

            var response = ExecuteAnonymousRequest(RootUrl + "api/v1/", request);

            var isChallenge = response.StatusCode == HttpStatusCode.Unauthorized
                || response.StatusCode == HttpStatusCode.Forbidden;
            Assert.That(
                isChallenge,
                Is.True,
                $"Anonymous GET /api/v1/search must be challenged with 401 (or 403); got {response.StatusCode}. " +
                "A 3xx redirect here violates the authz contract: under AuthenticationMethod=None + " +
                "AuthenticationRequired=Enabled an anonymous /api/v1 request must be denied with a 401/403 " +
                "challenge, never redirected (issue #403).");
            Assert.That(
                response.StatusCode,
                Is.Not.EqualTo(HttpStatusCode.OK),
                "Anonymous GET /api/v1/search must NOT be served 200 (issue #287/#403 contract).");
        }

        // Same contract against a second V1 API endpoint (config/host) — proves the
        // 3xx-vs-401 behavior is a property of the /api/v1/* authz surface under the
        // None auth method + Enabled required, not an artifact of the search route.
        // Note: /api/v1/config/host carries a scoped [Authorize(Policy = "API")], so it
        // challenges with the API scheme's 401 even under the None method (it PASSES
        // at base) — the contrast with /api/v1/search (fallback policy) is itself the
        // contract evidence: the fallback-policy surface is the one that deviates.
        [Test]
        public void anonymous_get_api_v1_config_host_should_be_challenged_with_401_not_3xx()
        {
            var request = new SimpleRestRequest("config/host");

            var response = ExecuteAnonymousRequest(RootUrl + "api/v1/", request);

            var isChallenge = response.StatusCode == HttpStatusCode.Unauthorized
                || response.StatusCode == HttpStatusCode.Forbidden;
            Assert.That(
                isChallenge,
                Is.True,
                $"Anonymous GET /api/v1/config/host must be challenged with 401 (or 403); got {response.StatusCode} (issue #403 contract).");
        }

        // Positive control: an authorized request (X-Api-Key header) to the same
        // endpoint succeeds — proves the challenge above is a denial, not a broken
        // route. HostConfig.GetSingle() uses the standard authenticated RestClient
        // (X-Api-Key header from NzbDroneRunner.ApiKey).
        [Test]
        public void authorized_get_api_v1_config_host_should_succeed()
        {
            var config = HostConfig.GetSingle();

            Assert.That(config, Is.Not.Null);
            Assert.That(config.Id, Is.GreaterThan(0));
        }

        // Issue #403 recorded scenario, explicitly: AuthenticationMethod=None +
        // AuthenticationRequired=Enabled. Reads the live config, asserts the
        // recorded auth state holds in this fixture, and re-runs the anonymous
        // /api/v1 GET against it so the fixture fails loudly if the auth state ever
        // drifts away from the issue's recorded scenario. RED at base (same 302).
        [Test]
        public void recorded_auth_state_none_method_enabled_required_should_challenge_anonymous_api_get()
        {
            var config = HostConfig.GetSingle();

            // Recorded auth state, now live in this fixture: AuthenticationMethod=None
            // (harness default, NzbDroneRunner L254) + AuthenticationRequired=Enabled
            // (flipped in the OneTimeSetUp above, matching the issue #403 recorded
            // state and the app's ConfigFileProvider default).
            Assert.That(config.AuthenticationMethod,
                Is.EqualTo(NzbDrone.Core.Authentication.AuthenticationType.None),
                "Harness default auth method must be None (the issue #403 recorded state).");
            Assert.That(config.AuthenticationRequired,
                Is.EqualTo(NzbDrone.Core.Authentication.AuthenticationRequiredType.Enabled),
                "Auth-required must be Enabled (the issue #403 recorded state).");

            var request = new SimpleRestRequest("search");
            request.AddParameter("term", "probe");

            var response = ExecuteAnonymousRequest(RootUrl + "api/v1/", request);

            var isChallenge = response.StatusCode == HttpStatusCode.Unauthorized
                || response.StatusCode == HttpStatusCode.Forbidden;
            Assert.That(
                isChallenge,
                Is.True,
                $"Under AuthenticationMethod=None + AuthenticationRequired=Enabled, anonymous GET /api/v1/search " +
                $"must be 401/403; got {response.StatusCode} (issue #403 recorded scenario).");
        }
    }
}
