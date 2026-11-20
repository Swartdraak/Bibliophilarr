using System.Collections.Generic;
using System.Linq;
using Bibliophilarr.Http.Authentication;
using Bibliophilarr.Http.Extensions;
using Bibliophilarr.Http.Frontend.Mappers;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;
using NLog;
using NzbDrone.Common.Instrumentation;
using NzbDrone.Core.Authentication;
using NzbDrone.Core.Configuration;

namespace Bibliophilarr.Http.Frontend
{
    [Authorize(Policy = "UI")]
    [ApiController]
    public class StaticResourceController : Controller
    {
        private readonly IEnumerable<IMapHttpRequestsToDisk> _requestMappers;
        private readonly Logger _logger;

        public StaticResourceController(IEnumerable<IMapHttpRequestsToDisk> requestMappers,
            Logger logger)
        {
            _requestMappers = requestMappers;
            _logger = logger;
        }

        [AllowAnonymous]
        [HttpGet("login")]
        public IActionResult LoginPage([FromServices] IAuthenticationService authenticationService,
                                       [FromServices] IConfigFileProvider config,
                                       [FromServices] IAntiforgery antiforgery,
                                       [FromQuery] bool setupInvalid = false)
        {
            if (_requestMappers.OfType<LoginHtmlMapper>().SingleOrDefault() is not { } mapper)
            {
                return MapResource("login");
            }

            var tokens = antiforgery.GetAndStoreTokens(HttpContext);
            Response.Headers.DisableCache();
            var showSetup = FirstAccountSetup.IsDirectLocalRequest(HttpContext) &&
                config.AuthenticationMethod == AuthenticationType.Forms &&
                config.AuthenticationRequired == AuthenticationRequiredType.Enabled &&
                authenticationService.FindUser() == null;
            return showSetup ? mapper.GetSetupResponse(tokens.RequestToken, setupInvalid) : mapper.GetLoginResponse(tokens.RequestToken);
        }

        [EnableCors("AllowGet")]
        [AllowAnonymous]
        [HttpGet("content/{**path:regex(^(?!/*api/).*)}")]
        public IActionResult IndexContent([FromRoute] string path)
        {
            return MapResource("Content/" + path);
        }

        [HttpGet("")]
        [HttpGet("/{**path:regex(^(?!(api|feed)/).*)}")]
        public IActionResult Index([FromRoute] string path)
        {
            return MapResource(path);
        }

        private IActionResult MapResource(string path)
        {
            path = "/" + (path ?? "");

            var mapper = _requestMappers.SingleOrDefault(m => m.CanHandle(path));

            if (mapper != null)
            {
                var result = mapper.GetResponse(path);

                if (result != null)
                {
                    if ((result as FileResult)?.ContentType == "text/html")
                    {
                        Response.Headers.DisableCache();
                    }

                    return result;
                }

                return NotFound();
            }

            _logger.Warn("Couldn't find handler for {0}", LogSanitizer.Sanitize(path));

            return NotFound();
        }
    }
}
