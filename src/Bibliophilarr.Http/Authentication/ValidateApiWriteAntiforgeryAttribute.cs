using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Bibliophilarr.Http.Authentication
{
    /// <summary>
    /// Validates an explicit API credential on versioned writes. Browser session
    /// credentials cannot authorize these actions by themselves.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, Inherited = true)]
    public sealed class ValidateApiWriteAntiforgeryAttribute : Attribute, IAsyncAuthorizationFilter
    {
        public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
        {
            var request = context.HttpContext;
            if (ApiWriteAuthenticationMiddleware.RequiresApiCredential(request) &&
                !await ApiWriteAuthenticationMiddleware.HasValidApiCredentialAsync(request))
            {
                context.Result = new ChallengeResult(ApiWriteAuthenticationMiddleware.ApiScheme);
            }
        }
    }
}
