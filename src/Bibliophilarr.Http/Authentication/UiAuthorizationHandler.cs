using System.Net;
using System.Threading.Tasks;
using Bibliophilarr.Http.Extensions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Authentication;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Configuration.Events;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Http.Authentication
{
    public class UiAuthorizationHandler : AuthorizationHandler<BypassableDenyAnonymousAuthorizationRequirement>, IAuthorizationRequirement, IHandle<ConfigSavedEvent>
    {
        private readonly IConfigFileProvider _configService;
        private static AuthenticationRequiredType _authenticationRequired;

        public UiAuthorizationHandler(IConfigFileProvider configService)
        {
            _configService = configService;
            _authenticationRequired = configService.AuthenticationRequired;
        }

        protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, BypassableDenyAnonymousAuthorizationRequirement requirement)
        {
            if (_authenticationRequired == AuthenticationRequiredType.DisabledForLocalAddresses)
            {
                if (context.Resource is HttpContext httpContext &&
                    IPAddress.TryParse(httpContext.GetRemoteIP(), out var ipAddress))
                {
                    if (ipAddress.IsLocalAddress() ||
                        (_configService.TrustCgnatIpAddresses && ipAddress.IsCgnatIpAddress()))
                    {
                        context.Succeed(requirement);
                    }
                }
            }
            else
            {
                // AuthenticationRequired == Enabled: deny anonymous users explicitly.
                // Without this, the UI policy (BypassableDenyAnonymousAuthorizationRequirement)
                // would fall through without calling Succeed or Fail, allowing the SPA shell
                // to be served to unauthenticated users (issue #313 DEFECT-AUTH-POLICY-UNAUTH-200).
                // DenyAnonymousAuthorizationRequirement (the base class) has no custom
                // failure-reason plumbing, so we use the default reason.
                context.Fail();
            }

            return Task.CompletedTask;
        }

        public void Handle(ConfigSavedEvent message)
        {
            _authenticationRequired = _configService.AuthenticationRequired;
        }
    }
}
