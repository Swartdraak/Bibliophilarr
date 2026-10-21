using System;
using System.Net;
using System.Threading.Tasks;
using Bibliophilarr.Http.Extensions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Authentication;
using NzbDrone.Core.Configuration;

namespace Bibliophilarr.Http.Middleware
{
    // Issue #313 acceptance A1/A2: with AuthenticationRequired=Enabled (no local bypass),
    // an unauthenticated UI request must return 401 (Unauthorized), not 403 (Forbidden).
    //
    // Without this middleware the denial flows through UseAuthorization -> ForbidAsync on the
    // Forms (cookie) scheme. Because CookieAuthenticationOptions.AccessDeniedPath is set,
    // ForbidAsync yields 403 Access Denied (the API scheme, which has no AccessDeniedPath,
    // stays 401). To satisfy the issue's literal 401 assertion for the UI path, this middleware
    // challenges with the Forms scheme before authorization: a Forms challenge emits
    // 401 Unauthorized for a browser UI request (no client to carry a WWW-Authenticate header).
    //
    // Property precedence is preserved:
    //   - DisabledForLocalAddresses local/CGNAT bypass is honored here (anonymous + local is
    //     allowed through, so the UI policy still applies to authenticated requests).
    //   - Authenticated users are never challenged (the policy still runs in UseAuthorization).
    //   - /login and /content/* are [AllowAnonymous] and never reach this challenge.
    //   - /api/* requests use the API scheme and are untouched by this UI middleware.
    public class UiAuthenticationMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly IConfigFileProvider _configService;

        public UiAuthenticationMiddleware(RequestDelegate next, IConfigFileProvider configService)
        {
            _next = next;
            _configService = configService;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            var method = context.Request.Method;
            var path = context.Request.Path.Value;

            // This middleware governs the anonymous-browser-UI denial ONLY: GET requests that
            // resolve to the SPA shell (StaticResourceController.Index / IndexContent). It must
            // NOT shadow:
            //   - API endpoints (the API scheme produces its own 401 via the fallback policy),
            //   - [AllowAnonymous] routes (login GET+POST, /content/*, the one-time POST
            //     /bootstrap first-user provisioning, the /ping liveness endpoint), and
            //   - non-GET UI methods (POST/PUT/etc are API/controller traffic).
            // A Forms challenge here yields 401 for an anonymous browser UI request, which is
            // exactly what issue #313 A1/A2 assert. Authenticated users and the
            // DisabledForLocalAddresses local-IP/CGNAT bypass are left to the UI policy in
            // UseAuthorization so its property precedence is preserved unchanged.
            //
            // Issue #316: /ping is a [AllowAnonymous] JSON liveness endpoint (PingController).
            // It must pass through so the controller's 200 + JSON {"Status":"OK"} is served,
            // not a Forms challenge that falls through to the SPA shell (index.html). The
            // exclusion is for the EXACT path /ping only: /ping/extra is still a UI-shell GET
            // (SPA catch-all) and is governed normally.
            var isUiShellGet =
                HttpMethodCompare(method, "GET") &&
                !string.IsNullOrEmpty(path) &&
                !path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase) &&
                !path.StartsWith("/login", StringComparison.OrdinalIgnoreCase) &&
                !path.StartsWith("/content/", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(path, "/ping", StringComparison.OrdinalIgnoreCase);

            if (isUiShellGet)
            {
                var user = context.User;
                var isAuthenticated = user != null && user.Identity != null && user.Identity.IsAuthenticated;

                if (!isAuthenticated &&
                    _configService.AuthenticationRequired == AuthenticationRequiredType.Enabled)
                {
                    // Anonymous UI request with auth required: deny with 401 (Forms challenge).
                    await context.ChallengeAsync(NzbDrone.Core.Authentication.AuthenticationType.Forms.ToString());
                    return;
                }

                // DisabledForLocalAddresses: mirror the UI-policy local-IP/CGNAT bypass so the
                // middleware does not shadow the existing local-access behavior.
                if (_configService.AuthenticationRequired == AuthenticationRequiredType.DisabledForLocalAddresses &&
                    !isAuthenticated)
                {
                    if (IPAddress.TryParse(context.GetRemoteIP(), out var ip) &&
                        (ip.IsLocalAddress() || (_configService.TrustCgnatIpAddresses && ip.IsCgnatIpAddress())))
                    {
                        await _next(context);
                        return;
                    }
                }
            }

            await _next(context);
        }

        private static bool HttpMethodCompare(string method, string expected)
        {
            return string.Equals(method, expected, StringComparison.OrdinalIgnoreCase);
        }
    }
}
