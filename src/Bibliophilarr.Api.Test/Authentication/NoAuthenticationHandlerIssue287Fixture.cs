using System.Threading.Tasks;
using Bibliophilarr.Http.Authentication;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using NUnit.Framework;

namespace NzbDrone.Api.Test.Authentication
{
    // Issue #287: with AuthenticationMethod=None, the None scheme registered via
    // AddNone("None") used NoAuthenticationHandler, which returned an anonymous
    // Success for every request. That Success was invisible to the FallbackPolicy
    // [API, Forms] RequireAuthenticatedUser (the "None" scheme is not in the
    // policy's scheme list), so an empty/absent X-Api-Key request to
    // GET /api/v1/book was served 200 instead of 401.
    //
    // The fix: NoAuthenticationHandler returns AuthenticateResult.NoResult(),
    // making the None scheme behave like the API scheme when no key is presented.
    // These fixtures pin that contract.
    [TestFixture]
    public class NoAuthenticationHandlerIssue287Fixture
    {
        [Test]
        public async Task no_authentication_scheme_should_return_no_result_not_anonymous_success()
        {
            // Core #287 contract: the None scheme must NOT produce an authenticated
            // (even anonymous) principal. Returning NoResult ensures the
            // FallbackPolicy RequireAuthenticatedUser sees no valid ticket from
            // this scheme and challenges with 401.
            var handler = CreateHandler();

            var result = await handler.AuthenticateAsync();

            result.Succeeded.Should().BeFalse(
                "the None scheme must not produce an authenticated principal; " +
                "an anonymous Success was invisible to the FallbackPolicy [API, Forms] " +
                "RequireAuthenticatedUser, causing unauthenticated 200 responses (issue #287)");
        }

        [Test]
        public async Task no_authentication_should_not_produce_a_principal()
        {
            // The old NoAuthenticationHandler returned a ClaimsPrincipal with
            // user=Anonymous, AuthType=None. That principal is no longer produced.
            var handler = CreateHandler();

            var result = await handler.AuthenticateAsync();

            result.Principal.Should().BeNull(
                "NoResult carries no principal; the old anonymous ClaimsPrincipal " +
                "allowed the FallbackPolicy to pass anonymous users through (issue #287)");
        }

        [Test]
        public async Task no_authentication_no_result_is_distinct_from_api_scheme_no_result()
        {
            // Both the None scheme and the API scheme (with no key) return NoResult.
            // This test documents that the NoResult is the correct, consistent
            // signal for "no authentication provided" across all schemes.
            var handler = CreateHandler();

            var result = await handler.AuthenticateAsync();

            // NoResult: Succeeded=false, Principal=null, Reason=null (no challenge).
            // This is distinct from a challenge (401) which would set Reason.
            result.Succeeded.Should().BeFalse();
            result.Principal.Should().BeNull();
        }

        private static NoAuthenticationHandler CreateHandler()
        {
            var context = new DefaultHttpContext();
            var options = new AuthenticationSchemeOptions();
            var handler = new NoAuthenticationHandler(
                new StaticOptionsMonitor<AuthenticationSchemeOptions>(options),
                LoggerFactory.Create(builder => { }),
                System.Text.Encodings.Web.UrlEncoder.Default);

            var scheme = new AuthenticationScheme("None", "None", typeof(NoAuthenticationHandler));
            handler.InitializeAsync(scheme, context).GetAwaiter().GetResult();
            return handler;
        }

        private sealed class StaticOptionsMonitor<TOptions> : IOptionsMonitor<TOptions>
        {
            public StaticOptionsMonitor(TOptions currentValue)
            {
                CurrentValue = currentValue;
            }

            public TOptions CurrentValue { get; }

            public TOptions Get(string name) => CurrentValue;

            public System.IDisposable OnChange(System.Action<TOptions, string> listener) => Mock.Of<System.IDisposable>();
        }
    }
}
