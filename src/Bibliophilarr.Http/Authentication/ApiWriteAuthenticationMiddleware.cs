using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;

namespace Bibliophilarr.Http.Authentication
{
    public sealed class ApiWriteAuthenticationMiddleware
    {
        private const string ApiScheme = "API";
        private readonly RequestDelegate _next;

        public ApiWriteAuthenticationMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            var method = context.Request.Method;
            if (context.GetEndpoint()?.Metadata.GetMetadata<VersionedApiControllerAttribute>() != null &&
                !HttpMethods.IsGet(method) && !HttpMethods.IsHead(method) &&
                !HttpMethods.IsOptions(method) && !HttpMethods.IsTrace(method))
            {
                var credential = await context.AuthenticateAsync(ApiScheme);
                if (!credential.Succeeded)
                {
                    await context.ChallengeAsync(ApiScheme);
                    return;
                }
            }

            await _next(context);
        }
    }
}
