using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using NzbDrone.Common.Cache;

namespace NzbDrone.Common.Http
{
    public sealed class InventaireRateLimitGate
    {
        public const string ProviderName = "Inventaire";

        private const int MaximumRequests = 40;
        private static readonly TimeSpan Window = TimeSpan.FromMinutes(1);
        private static readonly TimeSpan DefaultCooldown = TimeSpan.FromMinutes(5);

        private readonly SemaphoreSlim _wire = new SemaphoreSlim(1, 1);
        private readonly Queue<DateTime> _admissions = new Queue<DateTime>();
        private long _deadline;

        private InventaireRateLimitGate()
        {
        }

        public DateTime? CooldownUntilUtc
        {
            get
            {
                var ticks = Interlocked.Read(ref _deadline);
                return ticks > DateTime.UtcNow.Ticks ? new DateTime(ticks, DateTimeKind.Utc) : null;
            }
        }

        public static InventaireRateLimitGate Get(ICacheManager cache)
        {
            return cache.GetCache<InventaireRateLimitGate>(typeof(InventaireRateLimitGate))
                .Get("api", () => new InventaireRateLimitGate());
        }

        internal static bool AppliesTo(HttpRequest request)
        {
            return request.Method == HttpMethod.Get &&
                   string.Equals(request.Url.Scheme, "https", StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(request.Url.Host, "inventaire.io", StringComparison.OrdinalIgnoreCase) &&
                   request.Url.Port is null or 443 &&
                   request.Url.Path is "/api/search" or "/api/entities/serie-parts" or "/api/entities/reverse-claims" or "/api/entities/by-uris";
        }

        internal HttpResponse GetHeldResponse(HttpRequest request)
        {
            return CooldownUntilUtc is { } end ? Suppressed(request, end, DateTime.UtcNow) : null;
        }

        internal async Task<HttpResponse> DispatchAsync(HttpRequest request, Func<Task> pace, Func<Task<HttpResponse>> send)
        {
            var callerCancellation = request.CancellationToken;
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(callerCancellation);
            deadline.CancelAfter(request.RequestTimeout == TimeSpan.Zero ? TimeSpan.FromSeconds(100) : request.RequestTimeout);
            var cancellationToken = deadline.Token;
            await _wire.WaitAsync(cancellationToken);
            try
            {
                var held = GetHeldResponse(request);
                if (held != null)
                {
                    return held;
                }

                var now = DateTime.UtcNow;
                while (_admissions.TryPeek(out var first) && now - first >= Window)
                {
                    _admissions.Dequeue();
                }

                if (_admissions.Count >= MaximumRequests)
                {
                    var next = _admissions.Peek() + Window;
                    HoldUntil(next);
                    return Suppressed(request, next, now);
                }

                await pace().WaitAsync(cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();

                _admissions.Enqueue(DateTime.UtcNow);
                request.CancellationToken = cancellationToken;
                var response = await send();
                if (response.StatusCode == HttpStatusCode.TooManyRequests && !response.IsLocallySuppressed)
                {
                    now = DateTime.UtcNow;
                    var next = now + ReadCooldown(response.Headers["Retry-After"], now);
                    HoldUntil(next);
                    response.OmitContentFromDiagnostics = true;
                }

                return response;
            }
            finally
            {
                request.CancellationToken = callerCancellation;
                _wire.Release();
            }
        }

        private void HoldUntil(DateTime deadline)
        {
            Interlocked.Exchange(ref _deadline, Math.Max(Interlocked.Read(ref _deadline), deadline.Ticks));
        }

        private static HttpResponse Suppressed(HttpRequest request, DateTime end, DateTime now)
        {
            var seconds = Math.Max(1, Math.Ceiling((end - now).TotalSeconds));
            var headers = new HttpHeader { ["Retry-After"] = seconds.ToString(CultureInfo.InvariantCulture) };
            return new HttpResponse(request, headers, "Inventaire request is locally held.", HttpStatusCode.TooManyRequests)
            {
                IsLocallySuppressed = true,
                OmitContentFromDiagnostics = true
            };
        }

        private static TimeSpan ReadCooldown(string raw, DateTime now)
        {
            TimeSpan delay;
            if (long.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out var seconds))
            {
                delay = TimeSpan.FromSeconds(Math.Min(seconds, int.MaxValue));
            }
            else if (DateTimeOffset.TryParseExact(raw, "R", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var until))
            {
                delay = until.UtcDateTime - now;
            }
            else
            {
                delay = DefaultCooldown;
            }

            return delay <= TimeSpan.Zero ? DefaultCooldown : TimeSpan.FromSeconds(Math.Min(delay.TotalSeconds, int.MaxValue));
        }
    }
}
