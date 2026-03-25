using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

namespace Bibliophilarr.Http.Middleware
{
    public class SecurityHeadersMiddleware
    {
        private readonly RequestDelegate _next;

        public SecurityHeadersMiddleware(RequestDelegate next)
        {
            _next = next;
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
            headers["Content-Security-Policy"] =
                "default-src 'self'; " +
                "script-src 'self' 'unsafe-inline'; " +
                "style-src 'self' 'unsafe-inline'; " +
                "img-src 'self' data: https:; " +
                "font-src 'self'; " +
                "connect-src 'self' ws: wss:; " +
                "object-src 'none'; " +
                "frame-ancestors 'none'; " +
                "base-uri 'self'";

            await _next(context);
        }
    }
}
