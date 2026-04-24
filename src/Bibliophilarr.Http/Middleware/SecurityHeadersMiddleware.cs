using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using NzbDrone.Common.EnvironmentInfo;

namespace Bibliophilarr.Http.Middleware
{
    public class SecurityHeadersMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly string _csp;

        public SecurityHeadersMiddleware(RequestDelegate next)
        {
            _next = next;

            // Debug builds use webpack eval-source-map which requires 'unsafe-eval'
            var scriptSrc = BuildInfo.IsDebug
                ? "script-src 'self' 'unsafe-inline' 'unsafe-eval'; "
                : "script-src 'self' 'unsafe-inline'; ";

            _csp = "default-src 'self'; " +
                scriptSrc +
                "style-src 'self' 'unsafe-inline'; " +
                "img-src 'self' data: https:; " +
                "font-src 'self'; " +
                "connect-src 'self' ws: wss:; " +
                "worker-src 'self' blob:; " +
                "object-src 'none'; " +
                "frame-ancestors 'none'; " +
                "base-uri 'self'";
        }

        public async Task InvokeAsync(HttpContext context)
        {
            var headers = context.Response.Headers;

            // Prevent MIME-type sniffing
            headers["X-Content-Type-Options"] = "nosniff";

            // Prevent clickjacking by disallowing framing
            headers["X-Frame-Options"] = "DENY";

            // Enable browser XSS protection as a safety net
            headers["X-XSS-Protection"] = "0";

            // Prevent leaking referrer information to external sites
            headers["Referrer-Policy"] = "strict-origin-when-cross-origin";

            // Restrict permissions for browser features not needed by the app
            headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=(), payment=()";

            // Content Security Policy: restrict resource loading to same origin,
            // allow inline styles/scripts needed by the SPA, and block object/embed
            headers["Content-Security-Policy"] = _csp;

            await _next(context);
        }
    }
}
