using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;

namespace Bibliophilarr.Http.Authentication
{
    public sealed class ApiWriteAuthenticationMiddleware
    {
        internal const string ApiScheme = "API";
        private readonly RequestDelegate _next;

        public ApiWriteAuthenticationMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            if (RequiresApiCredential(context) && !await HasValidApiCredentialAsync(context))
            {
                await context.ChallengeAsync(ApiScheme);
                return;
            }

            await _next(context);
        }

        internal static bool RequiresApiCredential(HttpContext context)
        {
            var method = context.Request.Method;
            return context.GetEndpoint()?.Metadata.GetMetadata<VersionedApiControllerAttribute>() != null &&
                   !HttpMethods.IsGet(method) && !HttpMethods.IsHead(method) &&
                   !HttpMethods.IsOptions(method) && !HttpMethods.IsTrace(method);
        }

        internal static async Task<bool> HasValidApiCredentialAsync(HttpContext context)
        {
            var credential = await context.AuthenticateAsync(ApiScheme);
            return credential.Succeeded;
        }
    }
}
