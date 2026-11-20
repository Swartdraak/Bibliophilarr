using System;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Bibliophilarr.Http.Authentication
{
    [AttributeUsage(AttributeTargets.Method)]
    public sealed class DirectTerminalBootstrapAttribute : Attribute, IAuthorizationFilter, IOrderedFilter
    {
        // Evaluate the local transport boundary before the MVC antiforgery filter.
        public int Order => 0;

        public void OnAuthorization(AuthorizationFilterContext context)
        {
            var headers = context.HttpContext.Request.Headers;
            if (!FirstAccountSetup.IsDirectLocalRequest(context.HttpContext) ||
                headers.ContainsKey("Origin") ||
                headers.ContainsKey("Sec-Fetch-Site"))
            {
                context.Result = new StatusCodeResult(StatusCodes.Status403Forbidden);
            }
        }
    }
}
