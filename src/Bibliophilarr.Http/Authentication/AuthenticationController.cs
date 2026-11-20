using System.Collections.Generic;
using System.IO;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Threading.Tasks;
using System.Xml;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
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
        private static readonly object FirstUserLock = new object();

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
        [ValidateAntiForgeryToken]
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
        public IActionResult LogoutPage()
        {
            return Redirect(_configFileProvider.UrlBase + "/");
        }

        [HttpPost("logout")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Logout()
        {
            _authService.Logout(HttpContext);
            await HttpContext.SignOutAsync(AuthenticationType.Forms.ToString());
            return Redirect(_configFileProvider.UrlBase + "/");
        }

        [HttpGet("bootstrap")]
        [DirectTerminalBootstrap]
        [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
        public IActionResult GetBootstrapToken([FromServices] IAntiforgery antiforgery)
        {
            var tokens = antiforgery.GetAndStoreTokens(HttpContext);
            return Ok(new { headerName = tokens.HeaderName, requestToken = tokens.RequestToken });
        }

        // Terminal setup remains available before authenticated host settings can be used.
        // The token exchange proves the submission belongs to the direct local terminal.
        [HttpPost("bootstrap")]
        [DirectTerminalBootstrap]
        [ValidateAntiForgeryToken]
        public IActionResult Bootstrap([FromForm] BootstrapResource resource)
        {
            return CreateFirstUser(resource);
        }

        [HttpPost("login/setup")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Setup([FromForm] FirstAccountResource resource)
        {
            if (!FirstAccountSetup.IsDirectLocalRequest(HttpContext) ||
                _configFileProvider.AuthenticationMethod != AuthenticationType.Forms ||
                _configFileProvider.AuthenticationRequired != AuthenticationRequiredType.Enabled)
            {
                return StatusCode(StatusCodes.Status403Forbidden);
            }

            var result = CreateFirstUser(resource);
            if (result is BadRequestObjectResult)
            {
                return LocalRedirect(_configFileProvider.UrlBase + "/login?setupInvalid=true");
            }

            if (result is OkObjectResult)
            {
                return await Login(new LoginResource { Username = resource.Username, Password = resource.Password });
            }

            return result;
        }

        private IActionResult CreateFirstUser(BootstrapResource resource)
        {
            lock (FirstUserLock)
            {
                if (_authService.FindUser() != null)
                {
                    _logger?.Warn("Rejected initial setup because a user already exists");
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

                if (resource is FirstAccountResource setup && setup.Password != setup.PasswordConfirmation)
                {
                    return BadRequest("Passwords must match.");
                }

                _authService.AddUser(resource.Username, resource.Password);
                _logger?.Info("Created the initial user through the setup endpoint");
                return Ok(new { message = "First user created. You can now log in." });
            }
        }
    }
}
