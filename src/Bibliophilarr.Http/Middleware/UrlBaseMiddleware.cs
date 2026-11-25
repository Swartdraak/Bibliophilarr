using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.Http.HttpResults;
using NzbDrone.Common.Extensions;

namespace Bibliophilarr.Http.Middleware
{
    public class UrlBaseMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly string _urlBase;

        public UrlBaseMiddleware(RequestDelegate next, string urlBase)
        {
            _next = next;
            _urlBase = urlBase;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            if (_urlBase.IsNotNullOrWhiteSpace() && context.Request.PathBase.Value.IsNullOrWhiteSpace())
            {
                if (!_urlBase.StartsWith('/'))
                {
                    context.Response.StatusCode = StatusCodes.Status400BadRequest;
                    return;
                }

                var destination = UriHelper.BuildRelative(new PathString(_urlBase), context.Request.Path, context.Request.QueryString);
                if (!RedirectHttpResult.IsLocalUrl(destination))
                {
                    context.Response.StatusCode = StatusCodes.Status400BadRequest;
                    return;
                }

                await TypedResults.LocalRedirect(destination, preserveMethod: true).ExecuteAsync(context);

                return;
            }

            await _next(context);
        }
    }
}
