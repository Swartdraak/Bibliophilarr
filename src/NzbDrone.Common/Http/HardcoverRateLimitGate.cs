using System;
using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace NzbDrone.Common.Http
{
    internal sealed class HardcoverRateLimitGate
    {
        private const string RetryAfterHeader = "Retry-After";
        private const int MaxBodyBytes = 4096;
        private static readonly TimeSpan DefaultCooldown = TimeSpan.FromMinutes(5);
        private static readonly TimeSpan MaximumCooldown = TimeSpan.FromSeconds(int.MaxValue);
        private static readonly Regex BodyDelay = new Regex(
            @"\b(?:try again|retry)\s+(?:in|after)\s+(?<seconds>[0-9]+)\s+(?:seconds?|secs?)\b|""retry[_-]?after(?:[_-]?seconds)?""\s*:\s*""?(?<seconds>[0-9]+)(?=[\s"",}\]])",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private readonly SemaphoreSlim _dispatch = new SemaphoreSlim(1, 1);
        private long _retryUntilUtcTicks;

        public TimeSpan Remaining => TimeSpan.FromTicks(Math.Max(0, Interlocked.Read(ref _retryUntilUtcTicks) - DateTimeOffset.UtcNow.UtcTicks));

        public static bool AppliesTo(HttpRequest request)
        {
            return string.Equals(request.Url.Scheme, "https", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(request.Url.Host, "api.hardcover.app", StringComparison.OrdinalIgnoreCase) &&
                request.Url.Port is null or 443 &&
                string.Equals(request.Url.Path, "/v1/graphql", StringComparison.Ordinal);
        }

        public HttpResponse GetHeldResponse(HttpRequest request)
        {
            var remaining = Remaining;
            if (remaining <= TimeSpan.Zero)
            {
                return null;
            }

            var headers = new HttpHeader
            {
                [RetryAfterHeader] = Math.Ceiling(remaining.TotalSeconds).ToString(CultureInfo.InvariantCulture)
            };
            return new HttpResponse(request, headers, "Hardcover cooldown is active.", HttpStatusCode.TooManyRequests);
        }

        public async Task<HttpResponse> DispatchAsync(HttpRequest request, Func<Task<HttpResponse>> send)
        {
            await _dispatch.WaitAsync();
            try
            {
                var held = GetHeldResponse(request);
                if (held != null)
                {
                    return held;
                }

                var response = await send();
                if (response.StatusCode == HttpStatusCode.TooManyRequests)
                {
                    var now = DateTimeOffset.UtcNow;
                    var retryAfter = ReadRetryAfter(response, now);
                    Interlocked.Exchange(ref _retryUntilUtcTicks, now.Add(retryAfter).UtcTicks);
                }

                return response;
            }
            finally
            {
                _dispatch.Release();
            }
        }

        private static TimeSpan ReadRetryAfter(HttpResponse response, DateTimeOffset now)
        {
            var delay = TimeSpan.Zero;
            foreach (var value in response.Headers.GetValues(RetryAfterHeader) ?? Array.Empty<string>())
            {
                foreach (var header in value.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
                {
                    if (TryReadSeconds(header, out var seconds))
                    {
                        delay = LongerDelay(delay, seconds);
                    }
                    else if (DateTimeOffset.TryParse(header, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var until))
                    {
                        delay = LongerDelay(delay, until - now);
                    }
                }
            }

            if (response.ResponseData is { Length: > 0 and <= MaxBodyBytes })
            {
                foreach (Match match in BodyDelay.Matches(response.Content))
                {
                    if (TryReadSeconds(match.Groups["seconds"].Value, out var seconds))
                    {
                        delay = LongerDelay(delay, seconds);
                    }
                }
            }

            if (delay <= TimeSpan.Zero)
            {
                return DefaultCooldown;
            }

            return delay > MaximumCooldown ? MaximumCooldown : delay;
        }

        private static bool TryReadSeconds(string value, out TimeSpan delay)
        {
            delay = TimeSpan.Zero;
            if (!long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var seconds))
            {
                return false;
            }

            delay = TimeSpan.FromSeconds(Math.Min(seconds, int.MaxValue));
            return true;
        }

        private static TimeSpan LongerDelay(TimeSpan left, TimeSpan right) => left >= right ? left : right;
    }
}
