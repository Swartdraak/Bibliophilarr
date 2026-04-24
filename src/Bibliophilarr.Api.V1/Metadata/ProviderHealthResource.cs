using System;
using NzbDrone.Core.MetadataSource;

namespace Bibliophilarr.Api.V1.Metadata;

public class ProviderHealthResource
{
    public string ProviderName { get; set; }
    public string Health { get; set; }
    public double SuccessRate { get; set; }
    public double AverageResponseTimeMs { get; set; }
    public int ConsecutiveFailures { get; set; }
    public DateTime? LastSuccess { get; set; }
    public DateTime? LastFailure { get; set; }
    public string LastErrorMessage { get; set; }
    public DateTime LastChecked { get; set; }
    public int TotalSearches { get; set; }
    public int EmptyResultCount { get; set; }
    public int TimeoutCount { get; set; }
    public int RateLimitWindowRequests { get; set; }
    public int RateLimitWindowLimit { get; set; }
    public int RateLimitRemaining { get; set; }
    public double RateLimitUsageRatio { get; set; }
    public bool IsRateLimitNearCeiling { get; set; }
    public int RetryAfterRemainingSeconds { get; set; }
    public DateTime? CooldownUntilUtc { get; set; }
}

public static class ProviderHealthResourceMapper
{
    public static ProviderHealthResource ToResource(string providerName, ProviderHealthStatus status)
    {
        return new ProviderHealthResource
        {
            ProviderName = providerName,
            Health = status.Health.ToString(),
            SuccessRate = status.SuccessRate,
            AverageResponseTimeMs = status.AverageResponseTimeMs,
            ConsecutiveFailures = status.ConsecutiveFailures,
            LastSuccess = status.LastSuccess,
            LastFailure = status.LastFailure,
            LastErrorMessage = status.LastErrorMessage,
            LastChecked = status.LastChecked,
            TotalSearches = status.TotalSearches,
            EmptyResultCount = status.EmptyResultCount,
            TimeoutCount = status.TimeoutCount,
            RateLimitWindowRequests = status.RateLimitWindowRequests,
            RateLimitWindowLimit = status.RateLimitWindowLimit,
            RateLimitRemaining = status.RateLimitRemaining,
            RateLimitUsageRatio = status.RateLimitUsageRatio,
            IsRateLimitNearCeiling = status.IsRateLimitNearCeiling,
            RetryAfterRemainingSeconds = status.RetryAfterRemainingSeconds,
            CooldownUntilUtc = status.CooldownUntilUtc
        };
    }
}
