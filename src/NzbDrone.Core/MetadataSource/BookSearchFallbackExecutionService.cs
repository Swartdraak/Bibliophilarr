using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Common.Cache;
using NzbDrone.Common.Http;
using NzbDrone.Common.TPL;
using NzbDrone.Core.Books;
using Polly;
using Polly.CircuitBreaker;

namespace NzbDrone.Core.MetadataSource
{
    public interface IBookSearchFallbackExecutionService
    {
        List<Book> Search(IBookSearchFallbackProvider provider, string title, string author);
    }

    /// <summary>
    /// Orchestrates book search requests with rate-limit awareness, circuit-breaker
    /// protection, and provider health tracking. Wraps each provider's Search method
    /// with Polly-based resilience policies and adaptive request spacing.
    ///
    /// Health states: Normal → Degraded (near rate limit) → Unhealthy (breaker open).
    /// Transitions are logged and emitted to telemetry.
    /// </summary>
    public class BookSearchFallbackExecutionService : IBookSearchFallbackExecutionService
    {
        // When request count reaches this fraction of the provider's MaxRequests
        // within its time window, switch to degraded (slower) request spacing.
        private const double NearCeilingRatio = 0.85;

        // Number of consecutive failures before the circuit breaker opens.
        private const int CircuitBreakerFailureThreshold = 3;

        // How long the circuit breaker stays open before allowing a probe request.
        private static readonly TimeSpan CircuitBreakerBreakDuration = TimeSpan.FromMinutes(2);

        // Request spacing under normal conditions.
        private static readonly TimeSpan DefaultInterval = TimeSpan.FromSeconds(2);

        // Slower request spacing when near the rate-limit ceiling.
        private static readonly TimeSpan DegradedInterval = TimeSpan.FromSeconds(15);

        // How long to suppress all requests after the provider becomes unhealthy.
        private static readonly TimeSpan UnhealthySuppression = TimeSpan.FromMinutes(10);

        // Cooldown after receiving a 5xx server error before retrying.
        private static readonly TimeSpan ServerErrorCooldown = TimeSpan.FromMinutes(2);

        // Cooldown after a non-server failure (e.g. timeout, parse error).
        private static readonly TimeSpan FailureCooldown = TimeSpan.FromSeconds(45);

        private readonly IRateLimitService _rateLimitService;
        private readonly IProviderTelemetryService _providerTelemetryService;
        private readonly IMetadataProviderRegistry _providerRegistry;
        private readonly ConcurrentDictionary<string, DateTime> _cooldownStore;
        private readonly ConcurrentDictionary<string, RateLimitWindowState> _rateLimitWindowStore;
        private readonly ConcurrentDictionary<string, ResiliencePipeline<List<Book>>> _circuitBreakers;
        private readonly Logger _logger;

        public BookSearchFallbackExecutionService(IRateLimitService rateLimitService,
                                                  IProviderTelemetryService providerTelemetryService,
                                                  IMetadataProviderRegistry providerRegistry,
                                                  ICacheManager cacheManager,
                                                  Logger logger)
        {
            _rateLimitService = rateLimitService;
            _providerTelemetryService = providerTelemetryService;
            _providerRegistry = providerRegistry;
            _cooldownStore = cacheManager.GetCache<ConcurrentDictionary<string, DateTime>>(GetType(), "fallbackProviderCooldowns")
                .Get("fallbackProviderCooldowns", () => new ConcurrentDictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase));
            _rateLimitWindowStore = cacheManager.GetCache<ConcurrentDictionary<string, RateLimitWindowState>>(GetType(), "fallbackProviderRateLimitWindows")
                .Get("fallbackProviderRateLimitWindows", () => new ConcurrentDictionary<string, RateLimitWindowState>(StringComparer.OrdinalIgnoreCase));
            _circuitBreakers = new ConcurrentDictionary<string, ResiliencePipeline<List<Book>>>(StringComparer.OrdinalIgnoreCase);
            _logger = logger;
        }

        public List<Book> Search(IBookSearchFallbackProvider provider, string title, string author)
        {
            if (provider == null)
            {
                return new List<Book>();
            }

            if (TryGetActiveCooldown(provider.ProviderName, out var remaining))
            {
                UpdateRateLimitHealth(provider, 0, remaining);
                _logger.Debug("Skipping fallback provider {0} for {1:F0}s because cooldown is active", provider.ProviderName, remaining.TotalSeconds);
                return new List<Book>();
            }

            var health = GetHealth(provider.ProviderName);
            if (health?.Health == ProviderHealth.Unhealthy && health.LastFailure.HasValue)
            {
                var unhealthyAge = DateTime.UtcNow - health.LastFailure.Value;
                if (unhealthyAge < UnhealthySuppression)
                {
                    _logger.Debug("Skipping fallback provider {0} because recent failures marked it unhealthy", provider.ProviderName);
                    return new List<Book>();
                }
            }

            _rateLimitService.WaitAndPulse("metadata-fallback", provider.ProviderName, GetRateLimitInterval(provider, health));
            var rateLimitSnapshot = TrackRateLimitWindow(provider);
            UpdateRateLimitHealth(provider, rateLimitSnapshot, TimeSpan.Zero);

            var watch = Stopwatch.StartNew();
            var pipeline = GetOrCreateCircuitBreaker(provider.ProviderName);

            try
            {
                var books = pipeline.Execute((_) => provider.Search(title, author) ?? new List<Book>(), CancellationToken.None);
                watch.Stop();
                _providerTelemetryService.RecordSuccess(provider.ProviderName, "fallback-search", watch.Elapsed.TotalMilliseconds, books.Count);
                ClearCooldown(provider.ProviderName);
                UpdateRateLimitHealth(provider, rateLimitSnapshot, TimeSpan.Zero);
                return books;
            }
            catch (BrokenCircuitException)
            {
                watch.Stop();
                _logger.Warn("Circuit breaker is open for provider {0}; skipping search", provider.ProviderName);
                SetCooldown(provider.ProviderName, CircuitBreakerBreakDuration);
                UpdateRateLimitHealth(provider, rateLimitSnapshot, CircuitBreakerBreakDuration);
                return new List<Book>();
            }
            catch (TooManyRequestsException e)
            {
                watch.Stop();
                _providerTelemetryService.RecordFailure(provider.ProviderName, "fallback-search", e);

                var retryAfter = e.RetryAfter > TimeSpan.Zero ? e.RetryAfter : TimeSpan.FromMinutes(5);
                SetCooldown(provider.ProviderName, Clamp(retryAfter, TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(15)));
                UpdateRateLimitHealth(provider, rateLimitSnapshot, retryAfter);

                _logger.Warn("Fallback provider {0} returned 429, dampening queries for {1:F0}s", provider.ProviderName, retryAfter.TotalSeconds);
                return new List<Book>();
            }
            catch (HttpException e) when (e.Response?.StatusCode == HttpStatusCode.RequestTimeout || e.Response?.HasHttpServerError == true)
            {
                watch.Stop();
                if (e.Response?.StatusCode == HttpStatusCode.RequestTimeout)
                {
                    _providerTelemetryService.RecordTimeout(provider.ProviderName, "fallback-search");
                }
                else
                {
                    _providerTelemetryService.RecordFailure(provider.ProviderName, "fallback-search", e);
                }

                SetCooldown(provider.ProviderName, ServerErrorCooldown);
                UpdateRateLimitHealth(provider, rateLimitSnapshot, ServerErrorCooldown);
                return new List<Book>();
            }
            catch (Exception e)
            {
                watch.Stop();
                _providerTelemetryService.RecordFailure(provider.ProviderName, "fallback-search", e);

                var healthAfterFailure = GetHealth(provider.ProviderName);
                if (healthAfterFailure?.ConsecutiveFailures >= 2)
                {
                    SetCooldown(provider.ProviderName, FailureCooldown);
                }

                UpdateRateLimitHealth(provider, rateLimitSnapshot, TimeSpan.Zero);

                return new List<Book>();
            }
        }

        private int TrackRateLimitWindow(IBookSearchFallbackProvider provider)
        {
            if (provider == null)
            {
                return 0;
            }

            var info = provider.RateLimitInfo;
            var limit = info?.MaxRequests > 0 ? info.MaxRequests : 0;
            if (limit == 0)
            {
                return 0;
            }

            var window = info.TimeWindow > TimeSpan.Zero ? info.TimeWindow : TimeSpan.FromMinutes(1);
            var now = DateTime.UtcNow;

            var state = _rateLimitWindowStore.AddOrUpdate(
                provider.ProviderName,
                _ => new RateLimitWindowState
                {
                    WindowStartUtc = now,
                    RequestCount = 1,
                    MaxRequests = limit,
                    WindowDuration = window
                },
                (_, existing) =>
                {
                    if (existing.WindowDuration != window || existing.MaxRequests != limit || now - existing.WindowStartUtc >= window)
                    {
                        existing.WindowStartUtc = now;
                        existing.RequestCount = 1;
                        existing.MaxRequests = limit;
                        existing.WindowDuration = window;
                        return existing;
                    }

                    existing.RequestCount += 1;
                    existing.MaxRequests = limit;
                    existing.WindowDuration = window;
                    return existing;
                });

            return state.RequestCount;
        }

        private void UpdateRateLimitHealth(IBookSearchFallbackProvider provider, int observedRequests, TimeSpan retryAfter)
        {
            if (provider == null)
            {
                return;
            }

            var now = DateTime.UtcNow;
            var info = provider.RateLimitInfo;
            var limit = info?.MaxRequests > 0 ? info.MaxRequests : 0;

            var requests = observedRequests;
            if (requests <= 0 && _rateLimitWindowStore.TryGetValue(provider.ProviderName, out var existingWindow))
            {
                if (now - existingWindow.WindowStartUtc < existingWindow.WindowDuration)
                {
                    requests = existingWindow.RequestCount;
                    limit = existingWindow.MaxRequests;
                }
                else
                {
                    requests = 0;
                }
            }

            var remaining = limit > 0 ? Math.Max(0, limit - requests) : 0;
            var usage = limit > 0 ? Math.Min(1.0, (double)requests / limit) : 0;
            var cooldownRemaining = retryAfter;

            if (_cooldownStore.TryGetValue(provider.ProviderName, out var until) && until > now)
            {
                cooldownRemaining = until - now;
            }

            var cooldownUntil = cooldownRemaining > TimeSpan.Zero ? now.Add(cooldownRemaining) : (DateTime?)null;
            var health = GetHealth(provider.ProviderName) ?? new ProviderHealthStatus();

            health.RateLimitWindowRequests = requests;
            health.RateLimitWindowLimit = limit;
            health.RateLimitRemaining = remaining;
            health.RateLimitUsageRatio = usage;
            health.IsRateLimitNearCeiling = limit > 0 && usage >= NearCeilingRatio;
            health.RetryAfterRemainingSeconds = cooldownRemaining > TimeSpan.Zero ? (int)Math.Ceiling(cooldownRemaining.TotalSeconds) : 0;
            health.CooldownUntilUtc = cooldownUntil;
            health.LastChecked = DateTime.UtcNow;

            _providerRegistry.UpdateProviderHealth(provider.ProviderName, health);
        }

        private ProviderHealthStatus GetHealth(string providerName)
        {
            var healthMap = _providerRegistry.GetProvidersHealthStatus();
            return healthMap.TryGetValue(providerName, out var status) ? status : null;
        }

        private ResiliencePipeline<List<Book>> GetOrCreateCircuitBreaker(string providerName)
        {
            return _circuitBreakers.GetOrAdd(providerName, name =>
                new ResiliencePipelineBuilder<List<Book>>()
                    .AddCircuitBreaker(new CircuitBreakerStrategyOptions<List<Book>>
                    {
                        FailureRatio = 1.0,
                        SamplingDuration = TimeSpan.FromSeconds(30),
                        MinimumThroughput = CircuitBreakerFailureThreshold,
                        BreakDuration = CircuitBreakerBreakDuration,
                        ShouldHandle = new PredicateBuilder<List<Book>>()
                            .Handle<HttpException>()
                            .Handle<TooManyRequestsException>(),
                        OnOpened = args =>
                        {
                            _logger.Warn("Circuit breaker opened for provider {0}", name);
                            return ValueTask.CompletedTask;
                        },
                        OnHalfOpened = args =>
                        {
                            _logger.Info("Circuit breaker half-opened for provider {0}, allowing probe request", name);
                            return ValueTask.CompletedTask;
                        },
                        OnClosed = args =>
                        {
                            _logger.Info("Circuit breaker closed for provider {0}, resuming normal operation", name);
                            return ValueTask.CompletedTask;
                        }
                    })
                    .Build());
        }

        private TimeSpan GetRateLimitInterval(IBookSearchFallbackProvider provider, ProviderHealthStatus health)
        {
            var info = provider.RateLimitInfo;
            var interval = DefaultInterval;

            if (info != null && info.MaxRequests > 0 && info.TimeWindow > TimeSpan.Zero)
            {
                interval = TimeSpan.FromTicks(info.TimeWindow.Ticks / info.MaxRequests);
            }

            if (health?.Health == ProviderHealth.Degraded)
            {
                interval = interval > DegradedInterval ? interval : DegradedInterval;
            }

            return interval < DefaultInterval ? DefaultInterval : interval;
        }

        private bool TryGetActiveCooldown(string providerName, out TimeSpan remaining)
        {
            remaining = TimeSpan.Zero;

            if (_cooldownStore.TryGetValue(providerName, out var until) && until > DateTime.UtcNow)
            {
                remaining = until - DateTime.UtcNow;
                return true;
            }

            return false;
        }

        private void SetCooldown(string providerName, TimeSpan duration)
        {
            if (duration <= TimeSpan.Zero)
            {
                return;
            }

            var until = DateTime.UtcNow.Add(duration);
            _cooldownStore.AddOrUpdate(providerName, until, (_, existing) => existing > until ? existing : until);
        }

        private void ClearCooldown(string providerName)
        {
            _cooldownStore.TryRemove(providerName, out _);
        }

        private static TimeSpan Clamp(TimeSpan value, TimeSpan min, TimeSpan max)
        {
            if (value < min)
            {
                return min;
            }

            if (value > max)
            {
                return max;
            }

            return value;
        }

        private class RateLimitWindowState
        {
            public DateTime WindowStartUtc { get; set; }
            public int RequestCount { get; set; }
            public int MaxRequests { get; set; }
            public TimeSpan WindowDuration { get; set; }
        }
    }
}
