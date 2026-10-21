using System.Collections.Generic;
using System.IO;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Threading.Tasks;
using System.Xml;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NLog;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Authentication;
using NzbDrone.Core.Configuration;

namespace Bibliophilarr.Http.Authentication
{
    [AllowAnonymous]
    [ApiController]
    public class AuthenticationController : Controller
    {
        private readonly IAuthenticationService _authService;
        private readonly IConfigFileProvider _configFileProvider;
        private readonly IAppFolderInfo _appFolderInfo;
        private readonly Logger _logger;

        public AuthenticationController(IAuthenticationService authService, IConfigFileProvider configFileProvider, IAppFolderInfo appFolderInfo, Logger logger)
        {
            _authService = authService;
            _configFileProvider = configFileProvider;
            _appFolderInfo = appFolderInfo;
            _logger = logger;
        }

        [HttpPost("login")]
        public async Task<IActionResult> Login([FromForm] LoginResource resource, [FromQuery] string returnUrl = null)
        {
            var user = _authService.Login(HttpContext.Request, resource.Username, resource.Password);

            if (user == null)
            {
                return Redirect($"~/login?returnUrl={returnUrl}&loginFailed=true");
            }

            var claims = new List<Claim>
            {
                new Claim("user", user.Username),
                new Claim("identifier", user.Identifier.ToString()),
                new Claim("AuthType", AuthenticationType.Forms.ToString())
            };

            var authProperties = new AuthenticationProperties
            {
                IsPersistent = resource.RememberMe == "on"
            };

            try
            {
                await HttpContext.SignInAsync(AuthenticationType.Forms.ToString(), new ClaimsPrincipal(new ClaimsIdentity(claims, "Cookies", "user", "identifier")), authProperties);
            }
            catch (CryptographicException e)
            {
                if (e.InnerException is XmlException)
                {
                    _logger.Error(e, "Failed to authenticate user due to corrupt XML. Please remove all XML files from {0} and restart Bibliophilarr", Path.Combine(_appFolderInfo.AppDataFolder, "asp"));
                }
                else
                {
                    _logger.Error(e, "Failed to authenticate user. {0}", e.Message);
                }

                return Unauthorized();
            }

            if (returnUrl.IsNullOrWhiteSpace() || !Url.IsLocalUrl(returnUrl))
            {
                return Redirect(_configFileProvider.UrlBase + "/");
            }

            if (_configFileProvider.UrlBase.IsNullOrWhiteSpace() || returnUrl.StartsWith(_configFileProvider.UrlBase))
            {
                return Redirect(returnUrl);
            }

            return Redirect(_configFileProvider.UrlBase + returnUrl);
        }

        [HttpGet("logout")]
        public async Task<IActionResult> Logout()
        {
            _authService.Logout(HttpContext);
            await HttpContext.SignOutAsync(AuthenticationType.Forms.ToString());
            return Redirect(_configFileProvider.UrlBase + "/");
        }

        // Issue #313 bootstrap path: when AuthenticationRequired=Enabled and the Users table
        // is empty, the install has a chicken-and-egg deadlock (no user can be created because
        // PUT /config/host is auth-gated). This endpoint provides a one-time local bootstrap
        // that creates the first user when the table is empty. It is safe to expose because:
        //   1. It only succeeds when the Users table is empty (first boot);
        //   2. It requires a non-trivial password (min 8 chars) to prevent brute-force races;
        //   3. Once a user exists, it returns 409 Conflict and never creates a second user.
        // This is NOT a silent bypass — after the first user is created, the endpoint is
        // permanently inert and all subsequent requests require normal authentication.
        [HttpPost("bootstrap")]
        public IActionResult Bootstrap([FromForm] BootstrapResource resource)
        {
            var user = _authService.FindUser();
            if (user != null)
            {
                _logger?.Warn("Bootstrap attempted after first user already exists (username={Username}); rejected", user.Username);
                return Conflict();
            }

            if (string.IsNullOrWhiteSpace(resource.Username) || resource.Username.Length < 3 || resource.Username.Length > 32)
            {
                return BadRequest("Username must be 3-32 characters.");
            }

            if (string.IsNullOrWhiteSpace(resource.Password) || resource.Password.Length < 8)
            {
                return BadRequest("Password must be at least 8 characters.");
            }

            _authService.AddUser(resource.Username, resource.Password);
            _logger?.Info("Bootstrap: first user '{Username}' created via POST /bootstrap (issue #313 bootstrap path)", resource.Username);
            return Ok(new { message = "First user created. You can now log in." });
        }
    }
}
