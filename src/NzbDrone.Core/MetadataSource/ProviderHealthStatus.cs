using System;

namespace NzbDrone.Core.MetadataSource
{
    /// <summary>
    /// Health status of a metadata provider
    /// </summary>
    public enum ProviderHealth
    {
        /// <summary>
        /// Provider is operating normally
        /// </summary>
        Healthy,

        /// <summary>
        /// Provider is experiencing issues but still functional
        /// </summary>
        Degraded,

        /// <summary>
        /// Provider is unavailable or non-functional
        /// </summary>
        Unhealthy,

        /// <summary>
        /// Provider health status is unknown
        /// </summary>
        Unknown
    }

    /// <summary>
    /// Detailed health status information for a provider
    /// </summary>
    public class ProviderHealthStatus
    {
        /// <summary>
        /// Current health status
        /// </summary>
        public ProviderHealth Health { get; set; }

        /// <summary>
        /// Average response time in milliseconds
        /// </summary>
        public double AverageResponseTimeMs { get; set; }

        /// <summary>
        /// Success rate (0.0 to 1.0)
        /// </summary>
        public double SuccessRate { get; set; }

        /// <summary>
        /// Number of consecutive failures
        /// </summary>
        public int ConsecutiveFailures { get; set; }

        /// <summary>
        /// Last successful request time
        /// </summary>
        public DateTime? LastSuccess { get; set; }

        /// <summary>
        /// Last failed request time
        /// </summary>
        public DateTime? LastFailure { get; set; }

        /// <summary>
        /// Last error message
        /// </summary>
        public string LastErrorMessage { get; set; }

        /// <summary>
        /// Time when health status was last checked
        /// </summary>
        public DateTime LastChecked { get; set; }

        /// <summary>
        /// Total number of search operations recorded for this provider (cumulative)
        /// </summary>
        public int TotalSearches { get; set; }

        /// <summary>
        /// Number of searches that returned zero results (cumulative)
        /// </summary>
        public int EmptyResultCount { get; set; }

        /// <summary>
        /// Number of requests that timed out (cumulative)
        /// </summary>
        public int TimeoutCount { get; set; }

        /// <summary>
        /// Number of requests observed in the active provider rate-limit window
        /// </summary>
        public int RateLimitWindowRequests { get; set; }

        /// <summary>
        /// Maximum requests allowed in the active provider rate-limit window
        /// </summary>
        public int RateLimitWindowLimit { get; set; }

        /// <summary>
        /// Remaining requests before the provider rate-limit window is exhausted
        /// </summary>
        public int RateLimitRemaining { get; set; }

        /// <summary>
        /// Usage ratio within the active provider rate-limit window (0.0 to 1.0+)
        /// </summary>
        public double RateLimitUsageRatio { get; set; }

        /// <summary>
        /// True when provider usage is close to rate-limit exhaustion
        /// </summary>
        public bool IsRateLimitNearCeiling { get; set; }

        /// <summary>
        /// Retry-After/cooldown remaining in seconds when rate-limited or cooling down
        /// </summary>
        public int RetryAfterRemainingSeconds { get; set; }

        /// <summary>
        /// UTC timestamp when current provider cooldown expires
        /// </summary>
        public DateTime? CooldownUntilUtc { get; set; }

        public ProviderHealthStatus()
        {
            Health = ProviderHealth.Unknown;
            AverageResponseTimeMs = 0;
            SuccessRate = 0;
            ConsecutiveFailures = 0;
            LastChecked = DateTime.UtcNow;
        }
    }
}
