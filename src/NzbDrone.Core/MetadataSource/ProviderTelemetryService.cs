using System;
using NLog;

namespace NzbDrone.Core.MetadataSource
{
    public interface IProviderTelemetryService
    {
        void RecordSuccess(string providerName, string operation, double responseTimeMs, int resultCount = 0);

        void RecordFailure(string providerName, string operation, Exception exception);

        void RecordTimeout(string providerName, string operation);

        void RecordHealthChange(string providerName, ProviderHealth previousHealth, ProviderHealthStatus current);
    }

    public class ProviderTelemetryService : IProviderTelemetryService
    {
        private readonly IMetadataProviderRegistry _registry;
        private readonly Logger _logger;

        public ProviderTelemetryService(IMetadataProviderRegistry registry, Logger logger)
        {
            _registry = registry;
            _logger = logger;
        }

        public void RecordSuccess(string providerName, string operation, double responseTimeMs, int resultCount = 0)
        {
            _logger.Debug(
                "Provider '{0}' succeeded: operation={1}, responseMs={2:F0}, resultCount={3}",
                providerName,
                operation,
                responseTimeMs,
                resultCount);

            var existing = GetOrDefault(providerName);

            var updated = new ProviderHealthStatus
            {
                Health = ProviderHealth.Healthy,
                SuccessRate = ComputeNewSuccessRate(existing?.SuccessRate ?? 1.0, true),
                AverageResponseTimeMs = ComputeNewAverageResponseTime(existing?.AverageResponseTimeMs ?? responseTimeMs, responseTimeMs),
                ConsecutiveFailures = 0,
                LastSuccess = DateTime.UtcNow,
                LastFailure = existing?.LastFailure,
                LastChecked = DateTime.UtcNow,
                TotalSearches = (existing?.TotalSearches ?? 0) + 1,
                EmptyResultCount = (existing?.EmptyResultCount ?? 0) + (resultCount == 0 ? 1 : 0),
                TimeoutCount = existing?.TimeoutCount ?? 0,
                RateLimitWindowRequests = existing?.RateLimitWindowRequests ?? 0,
                RateLimitWindowLimit = existing?.RateLimitWindowLimit ?? 0,
                RateLimitRemaining = existing?.RateLimitRemaining ?? 0,
                RateLimitUsageRatio = existing?.RateLimitUsageRatio ?? 0,
                IsRateLimitNearCeiling = existing?.IsRateLimitNearCeiling ?? false,
                RetryAfterRemainingSeconds = existing?.RetryAfterRemainingSeconds ?? 0,
                CooldownUntilUtc = existing?.CooldownUntilUtc
            };

            _registry.UpdateProviderHealth(providerName, updated);
        }

        public void RecordFailure(string providerName, string operation, Exception exception)
        {
            _logger.Warn(
                exception,
                "Provider '{0}' failed: operation={1}",
                providerName,
                operation);

            var existing = GetOrDefault(providerName);
            var consecutiveFailures = (existing?.ConsecutiveFailures ?? 0) + 1;
            var successRate = ComputeNewSuccessRate(existing?.SuccessRate ?? 1.0, false);

            var newHealth = consecutiveFailures >= 5
                ? ProviderHealth.Unhealthy
                : consecutiveFailures >= 2
                    ? ProviderHealth.Degraded
                    : ProviderHealth.Healthy;

            var updated = new ProviderHealthStatus
            {
                Health = newHealth,
                SuccessRate = successRate,
                AverageResponseTimeMs = existing?.AverageResponseTimeMs ?? 0,
                ConsecutiveFailures = consecutiveFailures,
                LastSuccess = existing?.LastSuccess,
                LastFailure = DateTime.UtcNow,
                LastErrorMessage = exception?.Message,
                LastChecked = DateTime.UtcNow,
                TotalSearches = existing?.TotalSearches ?? 0,
                EmptyResultCount = existing?.EmptyResultCount ?? 0,
                TimeoutCount = existing?.TimeoutCount ?? 0,
                RateLimitWindowRequests = existing?.RateLimitWindowRequests ?? 0,
                RateLimitWindowLimit = existing?.RateLimitWindowLimit ?? 0,
                RateLimitRemaining = existing?.RateLimitRemaining ?? 0,
                RateLimitUsageRatio = existing?.RateLimitUsageRatio ?? 0,
                IsRateLimitNearCeiling = existing?.IsRateLimitNearCeiling ?? false,
                RetryAfterRemainingSeconds = existing?.RetryAfterRemainingSeconds ?? 0,
                CooldownUntilUtc = existing?.CooldownUntilUtc
            };

            if (existing != null && existing.Health != newHealth)
            {
                RecordHealthChange(providerName, existing.Health, updated);
            }

            _registry.UpdateProviderHealth(providerName, updated);
        }

        public void RecordTimeout(string providerName, string operation)
        {
            _logger.Warn("Provider '{0}' timed out: operation={1}", providerName, operation);

            var existing = GetOrDefault(providerName);
            var consecutiveFailures = (existing?.ConsecutiveFailures ?? 0) + 1;
            var successRate = ComputeNewSuccessRate(existing?.SuccessRate ?? 1.0, false);

            var newHealth = consecutiveFailures >= 5
                ? ProviderHealth.Unhealthy
                : consecutiveFailures >= 2
                    ? ProviderHealth.Degraded
                    : ProviderHealth.Healthy;

            var updated = new ProviderHealthStatus
            {
                Health = newHealth,
                SuccessRate = successRate,
                AverageResponseTimeMs = existing?.AverageResponseTimeMs ?? 0,
                ConsecutiveFailures = consecutiveFailures,
                LastSuccess = existing?.LastSuccess,
                LastFailure = DateTime.UtcNow,
                LastErrorMessage = "Request timed out",
                LastChecked = DateTime.UtcNow,
                TotalSearches = existing?.TotalSearches ?? 0,
                EmptyResultCount = existing?.EmptyResultCount ?? 0,
                TimeoutCount = (existing?.TimeoutCount ?? 0) + 1,
                RateLimitWindowRequests = existing?.RateLimitWindowRequests ?? 0,
                RateLimitWindowLimit = existing?.RateLimitWindowLimit ?? 0,
                RateLimitRemaining = existing?.RateLimitRemaining ?? 0,
                RateLimitUsageRatio = existing?.RateLimitUsageRatio ?? 0,
                IsRateLimitNearCeiling = existing?.IsRateLimitNearCeiling ?? false,
                RetryAfterRemainingSeconds = existing?.RetryAfterRemainingSeconds ?? 0,
                CooldownUntilUtc = existing?.CooldownUntilUtc
            };

            if (existing != null && existing.Health != newHealth)
            {
                RecordHealthChange(providerName, existing.Health, updated);
            }

            _registry.UpdateProviderHealth(providerName, updated);
        }

        public void RecordHealthChange(
            string providerName,
            ProviderHealth previousHealth,
            ProviderHealthStatus current)
        {
            _logger.Info(
                "Provider '{0}' health changed: {1} → {2} (successRate={3:P0}, consecutiveFailures={4})",
                providerName,
                previousHealth,
                current.Health,
                current.SuccessRate,
                current.ConsecutiveFailures);
        }

        private ProviderHealthStatus GetOrDefault(string providerName)
        {
            var map = _registry.GetProvidersHealthStatus();

            if (map.TryGetValue(providerName, out var status))
            {
                return status;
            }

            return null;
        }

        private static double ComputeNewSuccessRate(double previousRate, bool succeeded)
        {
            // Exponential moving average: α = 0.1
            const double alpha = 0.1;
            var sample = succeeded ? 1.0 : 0.0;

            return ((1.0 - alpha) * previousRate) + (alpha * sample);
        }

        private static double ComputeNewAverageResponseTime(double previousAvg, double newSample)
        {
            // Exponential moving average: α = 0.2
            const double alpha = 0.2;

            return ((1.0 - alpha) * previousAvg) + (alpha * newSample);
        }
    }
}
