using System;
using System.Text.RegularExpressions;
using Diacritical;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.DependencyInjection;
using NzbDrone.Core.Authentication;
using NzbDrone.Core.Configuration;

namespace Bibliophilarr.Http.Authentication
{
    public static class AuthenticationBuilderExtensions
    {
        private static readonly Regex CookieNameRegex = new Regex(@"[^a-z0-9]+", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public static AuthenticationBuilder AddApiKey(this AuthenticationBuilder authenticationBuilder, string name, Action<ApiKeyAuthenticationOptions> options)
        {
            return authenticationBuilder.AddScheme<ApiKeyAuthenticationOptions, ApiKeyAuthenticationHandler>(name, options);
        }

        public static AuthenticationBuilder AddBasic(this AuthenticationBuilder authenticationBuilder, string name)
        {
            return authenticationBuilder.AddScheme<AuthenticationSchemeOptions, BasicAuthenticationHandler>(name, options => { });
        }

        public static AuthenticationBuilder AddNone(this AuthenticationBuilder authenticationBuilder, string name)
        {
            return authenticationBuilder.AddScheme<AuthenticationSchemeOptions, NoAuthenticationHandler>(name, options => { });
        }

        public static AuthenticationBuilder AddExternal(this AuthenticationBuilder authenticationBuilder, string name)
        {
            return authenticationBuilder.AddScheme<AuthenticationSchemeOptions, NoAuthenticationHandler>(name, options => { });
        }

        public static AuthenticationBuilder AddAppAuthentication(this IServiceCollection services)
        {
            services.AddOptions<CookieAuthenticationOptions>(AuthenticationType.Forms.ToString())
                .Configure<IConfigFileProvider>((options, configFileProvider) =>
                {
                    // Replace diacritics and replace non-word characters to ensure cookie name doesn't contain any valid URL characters not allowed in cookie names
                    var instanceName = configFileProvider.InstanceName;
                    instanceName = instanceName.RemoveDiacritics();
                    instanceName = CookieNameRegex.Replace(instanceName, string.Empty);

                    options.Cookie.Name = $"{instanceName}Auth";
                    options.AccessDeniedPath = "/login?loginFailed=true";
                    options.LoginPath = "/login";
                    options.ExpireTimeSpan = TimeSpan.FromDays(7);
                    options.SlidingExpiration = true;
                    options.ReturnUrlParameter = "returnUrl";
                });

            // Issue #349: set the default authenticate/challenge schemes. Before this, the
            // builder used AddAuthentication() with NO default scheme, so the UseAuthentication
            // middleware did not authenticate via the Forms (cookie) scheme on the normal request
            // pipeline: context.User stayed anonymous even when a valid BibliophilarrAuth Forms
            // cookie was present, and UiAuthenticationMiddleware (which reads
            // context.User.Identity.IsAuthenticated before UseAuthorization) challenged every
            // in-session UI GET with 401. The cookie itself authenticated fine when the scheme
            // was invoked explicitly (AuthenticateAsync("Forms") succeeds and yields the user),
            // and the API scheme still worked because [Authorize(Policy = "API")] names the API
            // scheme explicitly in its policy's AuthenticationSchemes - but the UI path, which
            // relies on the default authenticate to populate context.User, got nothing.
            // Pinning the default authenticate + challenge schemes to the Forms cookie makes the
            // issued Forms session accepted for in-session UI requests (post-login and after a
            // SIGTERM restart) without changing the cookie name, the API-scheme behavior, or the
            // #313 anonymous-UI-401 / #329 SPA-shell contracts.
            return services.AddAuthentication(options =>
                {
                    options.DefaultAuthenticateScheme = AuthenticationType.Forms.ToString();
                    options.DefaultChallengeScheme = AuthenticationType.Forms.ToString();
                })
                .AddNone(AuthenticationType.None.ToString())
                .AddExternal(AuthenticationType.External.ToString())
                .AddBasic(AuthenticationType.Basic.ToString())
                .AddCookie(AuthenticationType.Forms.ToString())
                .AddApiKey("API", options =>
                {
                    options.HeaderName = "X-Api-Key";
                    options.QueryName = "apikey";
                })
                .AddApiKey("SignalR", options =>
                {
                    options.HeaderName = "X-Api-Key";
                    options.QueryName = "access_token";
                });
        }
    }
}
