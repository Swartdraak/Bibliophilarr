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
        public void csp_should_omit_unsafe_eval_for_release_builds_so_the_production_bundle_cannot_be_blank_blocked()
        {
            var scriptSrc = GetScriptSrc(GetCsp());

            // This fixture is compiled as Release (Bibliophilarr.Test.OutputType
            // convention), so BuildInfo.IsDebug is false — exactly the Release
            // CSP branch a shipped binary uses.
            NzbDrone.Common.EnvironmentInfo.BuildInfo.IsDebug.Should().BeFalse();
            scriptSrc.Should().Contain("'self'");
            scriptSrc.Should().Contain("'unsafe-inline'");
            scriptSrc.Should().NotContain("'unsafe-eval'");
        }

        [Test]
        public void csp_should_allow_unsafe_eval_only_for_debug_builds_that_ship_the_eval_source_map_bundle()
        {
            // Documented symmetric half of the #252 consistency rule: a Debug
            // build ships the eval-source-map dev bundle (eval() calls), so a
            // Debug binary MUST keep 'unsafe-eval' in the CSP. This branch is
            // exercised when the fixture is compiled as Debug (e.g. a
            // Debug-configuration test run); it self-skips under Release.
            if (!NzbDrone.Common.EnvironmentInfo.BuildInfo.IsDebug)
            {
                return;
            }

            var scriptSrc = GetScriptSrc(GetCsp());

            scriptSrc.Should().Contain("'unsafe-eval'");
        }
    }
}
