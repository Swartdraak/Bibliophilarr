using System.Text.Encodings.Web;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Bibliophilarr.Http.Authentication
{
    public class NoAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        public NoAuthenticationHandler(IOptionsMonitor<AuthenticationSchemeOptions> options,
            ILoggerFactory logger,
            UrlEncoder encoder)
            : base(options, logger, encoder)
        {
        }

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            // Issue #287: the None scheme previously returned an anonymous Success, which was
            // invisible to the FallbackPolicy [API, Forms] RequireAuthenticatedUser (the "None"
            // scheme is not in the policy's scheme list, so its Success never reached
            // authorization). The net effect was that with AuthenticationMethod=None, an
            // empty/absent X-Api-Key request to GET /api/v1/book was served 200 instead of 401.
            //
            // Returning NoResult makes the None scheme invisible to the authentication
            // middleware in the same way the API scheme is when no key is presented: the
            // FallbackPolicy then sees NoResult from BOTH schemes and challenges with 401.
            // This is the correct security posture: the API key is always generated and
            // persisted by ConfigFileProvider, so no-key access should be denied regardless
            // of the AuthenticationMethod configuration.
            return Task.FromResult(AuthenticateResult.NoResult());
        }
    }
}
