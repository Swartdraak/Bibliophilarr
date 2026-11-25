using System;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Common.Cache;
using NzbDrone.Common.EnvironmentInfo;

namespace NzbDrone.Common.Http
{
    public sealed class HardcoverRateLimitGate
    {
        private const string RetryAfterHeader = "Retry-After";
        private const string DeadlineFileName = "hardcover-retry-until";
        private const int MaxDeadlineBytes = 19;
        private const int MaxBodyBytes = 4096;
        private static readonly TimeSpan DefaultCooldown = TimeSpan.FromMinutes(5);
        private static readonly TimeSpan MaximumCooldown = TimeSpan.FromSeconds(int.MaxValue);
        private static readonly Regex BodyDelay = new Regex(
            @"\b(?:try again|retry)\s+(?:in|after)\s+(?<seconds>[0-9]+)\s+(?:seconds?|secs?)\b|""retry[_-]?after(?:[_-]?seconds)?""\s*:\s*""?(?<seconds>[0-9]+)(?=[\s"",}\]])",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private readonly SemaphoreSlim _dispatch = new SemaphoreSlim(1, 1);
        private readonly object _deadlineSync = new object();
        private long _retryUntilUtcTicks;
        private string _deadlinePath;
        private bool _deadlineLoaded;
        private DateTime _storageReadRetryAtUtc;
        private Logger _logger;

        private HardcoverRateLimitGate()
        {
        }

        public DateTime? CooldownUntilUtc => GetActiveDeadline(DateTime.UtcNow);

        public static HardcoverRateLimitGate Get(ICacheManager cacheManager, IAppFolderInfo appFolderInfo = null, Logger logger = null)
        {
            var gate = cacheManager.GetCache<HardcoverRateLimitGate>(typeof(HardcoverRateLimitGate))
                .Get("graphql", () => new HardcoverRateLimitGate());
            if (!string.IsNullOrWhiteSpace(appFolderInfo?.AppDataFolder))
            {
                gate.ConfigureStorage(appFolderInfo.AppDataFolder, logger);
            }

            return gate;
        }

        private void ConfigureStorage(string appDataFolder, Logger logger)
        {
            lock (_deadlineSync)
            {
                _deadlinePath ??= Path.Combine(appDataFolder, DeadlineFileName);
                _logger ??= logger ?? LogManager.GetCurrentClassLogger();
            }
        }

        internal static bool AppliesTo(HttpRequest request)
        {
            return string.Equals(request.Url.Scheme, "https", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(request.Url.Host, "api.hardcover.app", StringComparison.OrdinalIgnoreCase) &&
                request.Url.Port is null or 443 &&
                string.Equals(request.Url.Path, "/v1/graphql", StringComparison.Ordinal);
        }

        internal HttpResponse GetHeldResponse(HttpRequest request)
        {
            var now = DateTime.UtcNow;
            if (GetActiveDeadline(now) is not { } deadline)
            {
                return null;
            }

            var headers = new HttpHeader
            {
                [RetryAfterHeader] = Math.Ceiling((deadline - now).TotalSeconds).ToString(CultureInfo.InvariantCulture)
            };
            return new HttpResponse(request, headers, "Hardcover cooldown is active.", HttpStatusCode.TooManyRequests)
            {
                IsLocallySuppressed = true,
                OmitContentFromDiagnostics = true,
                HardcoverCooldownUntilUtc = deadline
            };
        }

        internal async Task<HttpResponse> DispatchAsync(HttpRequest request, Func<Task<HttpResponse>> send)
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
                if (response.StatusCode == HttpStatusCode.TooManyRequests && !response.IsLocallySuppressed)
                {
                    var now = DateTimeOffset.UtcNow;
                    var retryAfter = ReadRetryAfter(response, now);
                    var deadline = now.Add(retryAfter).UtcDateTime;
                    response.HardcoverCooldownUntilUtc = RecordDeadline(deadline);
                }

                return response;
            }
            finally
            {
                _dispatch.Release();
            }
        }

        private DateTime? GetActiveDeadline(DateTime now)
        {
            lock (_deadlineSync)
            {
                LoadDeadline(now);
                var ticks = Math.Max(_retryUntilUtcTicks, _storageReadRetryAtUtc.Ticks);
                return ticks > now.Ticks ? new DateTime(ticks, DateTimeKind.Utc) : null;
            }
        }

        private void LoadDeadline(DateTime now)
        {
            if (_deadlinePath == null || _deadlineLoaded || _storageReadRetryAtUtc > now)
            {
                return;
            }

            try
            {
                using var stream = File.OpenRead(_deadlinePath);
                Span<byte> buffer = stackalloc byte[MaxDeadlineBytes + 1];
                var length = stream.ReadAtLeast(buffer, buffer.Length, throwOnEndOfStream: false);
                if (length > MaxDeadlineBytes ||
                    !long.TryParse(Encoding.ASCII.GetString(buffer[..length]), NumberStyles.None, CultureInfo.InvariantCulture, out var ticks) ||
                    ticks <= 0 || ticks > DateTime.MaxValue.Ticks)
                {
                    throw new InvalidDataException();
                }

                _retryUntilUtcTicks = Math.Max(_retryUntilUtcTicks, ticks);
                _deadlineLoaded = true;
                _storageReadRetryAtUtc = default;
            }
            catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
            {
                _deadlineLoaded = true;
                _storageReadRetryAtUtc = default;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                _storageReadRetryAtUtc = now.Add(DefaultCooldown);
                _logger.Warn("Could not read the Hardcover cooldown state. Dispatch is held until the saved state can be read.");
            }
        }

        private DateTime RecordDeadline(DateTime deadline)
        {
            lock (_deadlineSync)
            {
                LoadDeadline(DateTime.UtcNow);
                _retryUntilUtcTicks = Math.Max(_retryUntilUtcTicks, deadline.Ticks);
                if (_deadlinePath != null)
                {
                    SaveDeadline(_retryUntilUtcTicks);
                }

                return new DateTime(_retryUntilUtcTicks, DateTimeKind.Utc);
            }
        }

        private void SaveDeadline(long ticks)
        {
            var temporaryPath = _deadlinePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            var created = false;
            try
            {
                using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    created = true;
                    stream.Write(Encoding.ASCII.GetBytes(ticks.ToString(CultureInfo.InvariantCulture)));
                    stream.Flush(flushToDisk: true);
                }

                File.Move(temporaryPath, _deadlinePath, overwrite: true);
                created = false;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _logger.Warn("Could not save the Hardcover cooldown state. The in-memory deadline remains active.");
            }
            finally
            {
                if (created)
                {
                    try
                    {
                        File.Delete(temporaryPath);
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        _logger.Warn("Could not remove a temporary Hardcover cooldown state file.");
                    }
                }
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
