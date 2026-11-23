using System.Threading.Tasks;
using Bibliophilarr.Http.Middleware;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using NUnit.Framework;

namespace NzbDrone.Api.Test.Middleware
{
    // Issue #252 regression guard: the CSP script-src directive must stay
    // consistent with the webpack devtool of the bundle the build actually
    // ships. Release builds ship a production bundle (hidden-source-map, no
    // eval() calls — see scripts/ops/check_ui_build_mode.sh), so a Release
    // binary must NOT advertise 'unsafe-eval'; Debug builds ship the
    // eval-source-map development bundle, so a Debug binary must keep
    // 'unsafe-eval'. A mismatch is what blanked the UI in Release (CSP blocked
    // every eval() and the SPA root never mounted).
    [TestFixture]
    public class SecurityHeadersMiddlewareFixture
    {
        private static string GetCsp()
        {
            var context = new DefaultHttpContext();
            var middleware = new SecurityHeadersMiddleware(context => Task.CompletedTask);
            middleware.InvokeAsync(context).GetAwaiter().GetResult();
            return context.Response.Headers["Content-Security-Policy"].ToString();
        }

        private static string GetScriptSrc(string csp)
        {
            csp.Should().Contain("script-src");
            return csp.Split("script-src")[1].Split(';')[0].Trim();
        }

        [Test]
        public void csp_should_match_the_script_policy_for_the_build_configuration()
        {
            var scriptSrc = GetScriptSrc(GetCsp());

            scriptSrc.Should().Contain("'self'");
            scriptSrc.Should().Contain("'unsafe-inline'");
#if DEBUG
            scriptSrc.Should().Contain("'unsafe-eval'");
#else
            scriptSrc.Should().NotContain("'unsafe-eval'");
#endif
        }
    }
}
