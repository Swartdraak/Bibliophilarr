using System;
using System.Collections.Generic;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.Authentication;

namespace Bibliophilarr.Http.Authentication
{
    public class BasicAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        private readonly IAuthenticationService _authService;

        public BasicAuthenticationHandler(IAuthenticationService authService,
            IOptionsMonitor<AuthenticationSchemeOptions> options,
            ILoggerFactory logger,
            UrlEncoder encoder)
            : base(options, logger, encoder)
        {
            _authService = authService;
        }

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.ContainsKey("Authorization"))
            {
                return Task.FromResult(AuthenticateResult.Fail("Authorization header missing."));
            }

            var authorizationHeader = Request.Headers["Authorization"].ToString();

            if (!AuthenticationHeaderValue.TryParse(authorizationHeader, out var headerValue) ||
                !string.Equals(headerValue.Scheme, "Basic", StringComparison.OrdinalIgnoreCase) ||
                string.IsNullOrWhiteSpace(headerValue.Parameter))
            {
                return Task.FromResult(AuthenticateResult.Fail("Authorization code not formatted properly."));
            }

            string authBase64;

            try
            {
                authBase64 = Encoding.UTF8.GetString(Convert.FromBase64String(headerValue.Parameter));
            }
            catch (FormatException ex)
            {
                Logger.LogWarning(ex, "Invalid Basic authentication header received.");
                return Task.FromResult(AuthenticateResult.Fail("Authorization code not formatted properly."));
            }

            var delimiterIndex = authBase64.IndexOf(':');

            if (delimiterIndex < 0)
            {
                Logger.LogWarning("Invalid Basic authentication header received without a username/password delimiter.");
                return Task.FromResult(AuthenticateResult.Fail("Authorization code not formatted properly."));
            }

            var authUsername = authBase64.Substring(0, delimiterIndex);
            var authPassword = authBase64.Substring(delimiterIndex + 1);

            var user = _authService.Login(Request, authUsername, authPassword);

            if (user == null)
            {
                return Task.FromResult(AuthenticateResult.Fail("The username or password is not correct."));
            }

            var claims = new List<Claim>
            {
                new Claim("user", user.Username),
                new Claim("identifier", user.Identifier.ToString()),
                new Claim("AuthType", AuthenticationType.Basic.ToString())
            };

            var identity = new ClaimsIdentity(claims, "Basic", "user", "identifier");
            var principal = new ClaimsPrincipal(identity);
            var ticket = new AuthenticationTicket(principal, "Basic");

            return Task.FromResult(AuthenticateResult.Success(ticket));
        }

        protected override Task HandleChallengeAsync(AuthenticationProperties properties)
        {
            Response.Headers["WWW-Authenticate"] = $"Basic realm=\"{BuildInfo.AppName}\"";
            Response.StatusCode = 401;
            return Task.CompletedTask;
        }

        protected override Task HandleForbiddenAsync(AuthenticationProperties properties)
        {
            Response.StatusCode = 403;
            return Task.CompletedTask;
        }
    }
}
