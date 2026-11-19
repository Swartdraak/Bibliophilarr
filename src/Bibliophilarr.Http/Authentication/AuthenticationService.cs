using Bibliophilarr.Http.Extensions;
using Microsoft.AspNetCore.Http;
using NLog;
using NzbDrone.Common.Instrumentation;
using NzbDrone.Core.Authentication;
using NzbDrone.Core.Configuration;

namespace Bibliophilarr.Http.Authentication
{
    public interface IAuthenticationService
    {
        void LogUnauthorized(HttpRequest context);
        User Login(HttpRequest request, string username, string password);
        void Logout(HttpContext context);
        User FindUser();
        User AddUser(string username, string password);
    }

    public class AuthenticationService : IAuthenticationService
    {
        private static readonly Logger _authLogger = LogManager.GetLogger("Auth");
        private readonly IConfigFileProvider _configFileProvider;
        private readonly IUserService _userService;

        public AuthenticationService(IConfigFileProvider configFileProvider, IUserService userService)
        {
            _configFileProvider = configFileProvider;
            _userService = userService;
        }

        public User Login(HttpRequest request, string username, string password)
        {
            // Issue #369: the auth method is read per call from the live config provider instead of
            // a process-lifetime static cache. The old cache captured AuthenticationMethod at
            // construction (first boot -> "None"), so after POST /bootstrap created the first user,
            // POST /login was rejected with loginFailed=true and no cookie until a restart. The
            // auth-method activation path (PUT /api/v1/config/host -> <AuthenticationMethod>Forms)
            // persists to config.xml and IConfigFileProvider clears its value cache on save
            // (ConfigFileProvider.SaveConfigDictionary -> _cache.Clear()), so a per-call read always
            // observes the current method. This mirrors UiAuthorizationPolicyProvider, which already
            // resolves the "UI" policy from the per-request AuthenticationMethod.
            if (_configFileProvider.AuthenticationMethod == AuthenticationType.None)
            {
                return null;
            }

            var user = _userService.FindUser(username, password);

            if (user != null)
            {
                LogSuccess(request, username);

                return user;
            }

            LogFailure(request, username);

            return null;
        }

        public void Logout(HttpContext context)
        {
            // Issue #369: read the live auth method per call (see Login) instead of the static cache.
            if (_configFileProvider.AuthenticationMethod == AuthenticationType.None)
            {
                return;
            }

            if (context.User != null)
            {
                LogLogout(context.Request, context.User.Identity.Name);
            }
        }

        public void LogUnauthorized(HttpRequest context)
        {
            _authLogger.Info("Auth-Unauthorized ip {0} url '{1}'", context.GetRemoteIP(), LogSanitizer.Sanitize(context.Path));
        }

        public User FindUser()
        {
            return _userService.FindUser();
        }

        public User AddUser(string username, string password)
        {
            return _userService.Add(username, password);
        }

        private void LogInvalidated(HttpRequest context)
        {
            _authLogger.Info("Auth-Invalidated ip {0}", context.GetRemoteIP());
        }

        private void LogFailure(HttpRequest context, string username)
        {
            _authLogger.Warn("Auth-Failure ip {0} username '{1}'", context.GetRemoteIP(), LogSanitizer.Sanitize(username));
        }

        private void LogSuccess(HttpRequest context, string username)
        {
            _authLogger.Info("Auth-Success ip {0} username '{1}'", context.GetRemoteIP(), LogSanitizer.Sanitize(username));
        }

        private void LogLogout(HttpRequest context, string username)
        {
            _authLogger.Info("Auth-Logout ip {0} username '{1}'", context.GetRemoteIP(), LogSanitizer.Sanitize(username));
        }
    }
}
