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
