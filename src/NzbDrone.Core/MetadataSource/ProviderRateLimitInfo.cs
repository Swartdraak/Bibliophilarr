using System;

namespace NzbDrone.Core.MetadataSource
{
    /// <summary>
    /// Represents rate limiting information for a metadata provider
    /// </summary>
    public class ProviderRateLimitInfo
    {
        /// <summary>
        /// Maximum number of requests allowed in the time window
        /// </summary>
        public int MaxRequests { get; set; }

        /// <summary>
        /// Time window for rate limiting
        /// </summary>
        public TimeSpan TimeWindow { get; set; }

        /// <summary>
        /// Whether this provider requires an API key
        /// </summary>
        public bool RequiresApiKey { get; set; }

        /// <summary>
        /// Whether this provider supports authenticated requests for higher limits
        /// </summary>
        public bool SupportsAuthentication { get; set; }

        /// <summary>
        /// Maximum requests per time window when authenticated (if different)
        /// </summary>
        public int? AuthenticatedMaxRequests { get; set; }

        public ProviderRateLimitInfo()
        {
            MaxRequests = 100;
            TimeWindow = TimeSpan.FromMinutes(5);
            RequiresApiKey = false;
            SupportsAuthentication = false;
        }
    }
}
