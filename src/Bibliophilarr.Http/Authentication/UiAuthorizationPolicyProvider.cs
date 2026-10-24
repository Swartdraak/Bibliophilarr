using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using NzbDrone.Core.Configuration;

namespace NzbDrone.Http.Authentication
{
    public class UiAuthorizationPolicyProvider : IAuthorizationPolicyProvider
    {
        private const string POLICY_NAME = "UI";
        private const string API_POLICY_NAME = "API";
        private readonly IConfigFileProvider _config;

        public DefaultAuthorizationPolicyProvider FallbackPolicyProvider { get; }

        public UiAuthorizationPolicyProvider(IOptions<AuthorizationOptions> options,
            IConfigFileProvider config)
        {
            FallbackPolicyProvider = new DefaultAuthorizationPolicyProvider(options);
            _config = config;
        }

        public Task<AuthorizationPolicy> GetDefaultPolicyAsync() => FallbackPolicyProvider.GetDefaultPolicyAsync();

        public Task<AuthorizationPolicy> GetFallbackPolicyAsync() => FallbackPolicyProvider.GetFallbackPolicyAsync();

        public Task<AuthorizationPolicy> GetPolicyAsync(string policyName)
        {
            if (policyName.Equals(POLICY_NAME, StringComparison.OrdinalIgnoreCase))
            {
                var policy = new AuthorizationPolicyBuilder(_config.AuthenticationMethod.ToString())
                    .AddRequirements(new BypassableDenyAnonymousAuthorizationRequirement());

                return Task.FromResult(policy.Build());
            }

            // Issue #329 repair: the "API" policy is registered in Startup via
            // AddAuthorization(options => options.AddPolicy(...)). The policy provider must serve
            // it by name, because AuthorizationMiddleware resolves named policies through this
            // provider (not directly from AuthorizationOptions.Policies): without this, the
            // scoped [Authorize(Policy = "API")] on HostConfigController throws
            // InvalidOperationException ("AuthorizationPolicy named: 'API' was not found") and
            // the endpoint 500s instead of 401ing. The returned policy is scheme-pinned to
            // "API" (valid X-Api-Key / apikey query only; a browser Forms session is rejected),
            // which is the #329 host-config authorization contract.
            if (policyName.Equals(API_POLICY_NAME, StringComparison.OrdinalIgnoreCase))
            {
                var policy = new AuthorizationPolicyBuilder("API")
                    .RequireAuthenticatedUser()
                    .Build();

                return Task.FromResult(policy);
            }

            return FallbackPolicyProvider.GetPolicyAsync(policyName);
        }
    }
}
