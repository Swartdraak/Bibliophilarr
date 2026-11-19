using System;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using NzbDrone.Common.Options;

namespace NzbDrone.Core.Update
{
    public interface IPublicReleaseDiscovery
    {
        bool Enabled { get; }
        Task<PublicReleaseCheck> Check(Version installedVersion, CancellationToken cancellationToken);
    }

    public enum PublicReleaseCheckStatus
    {
        Disabled,
        Unavailable,
        UpToDate,
        Available
    }

    public sealed class PublicReleaseCheck
    {
        public PublicReleaseCheck(PublicReleaseCheckStatus status, string installedVersion, string latestVersion = null, DateTimeOffset? publishedAt = null, string releaseNotesUrl = null)
        {
            Status = status;
            InstalledVersion = installedVersion;
            LatestVersion = latestVersion;
            PublishedAt = publishedAt;
            ReleaseNotesUrl = releaseNotesUrl;
        }

        public PublicReleaseCheckStatus Status { get; }
        public string InstalledVersion { get; }
        public string LatestVersion { get; }
        public DateTimeOffset? PublishedAt { get; }
        public string ReleaseNotesUrl { get; }
    }

    public class PublicReleaseDiscovery : IPublicReleaseDiscovery
    {
        private const string LatestReleaseUrl = "https://api.github.com/repos/Swartdraak/Bibliophilarr/releases/latest";
        private const string ReleaseNotesRoot = "https://github.com/Swartdraak/Bibliophilarr/releases/tag/";
        private static readonly Regex StableTag = new Regex(@"\Av(?:0|[1-9][0-9]*)\.(?:0|[1-9][0-9]*)\.(?:0|[1-9][0-9]*)\z", RegexOptions.NonBacktracking);
        private static readonly HttpClient Client = new HttpClient(new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            UseCookies = false,
            PooledConnectionLifetime = TimeSpan.FromMinutes(5)
        })
        {
            Timeout = TimeSpan.FromSeconds(8),
            MaxResponseContentBufferSize = 1024 * 1024
        };

        private readonly HttpClient _client;

        public PublicReleaseDiscovery(IOptions<UpdateOptions> options)
            : this(options, Client)
        {
        }

        internal PublicReleaseDiscovery(IOptions<UpdateOptions> options, HttpClient client)
        {
            Enabled = options.Value.PublicReleaseDiscovery;
            _client = client;
        }

        public bool Enabled { get; }

        public async Task<PublicReleaseCheck> Check(Version installedVersion, CancellationToken cancellationToken)
        {
            var installed = installedVersion.ToString();
            if (!Enabled)
            {
                return new PublicReleaseCheck(PublicReleaseCheckStatus.Disabled, installed);
            }

            var unavailable = new PublicReleaseCheck(PublicReleaseCheckStatus.Unavailable, installed);
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, LatestReleaseUrl);
                request.Headers.UserAgent.ParseAdd("Bibliophilarr-PublicReleaseCheck");
                request.Headers.Accept.ParseAdd("application/vnd.github+json");
                request.Headers.Add("X-GitHub-Api-Version", "2026-03-10");

                using var response = await _client.SendAsync(request, cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    return unavailable;
                }

                var json = await response.Content.ReadAsStringAsync(cancellationToken);
                var release = JsonSerializer.Deserialize<GitHubRelease>(json);
                if (release?.Draft != false || release.Prerelease != false || !release.PublishedAt.HasValue ||
                    release.Tag == null || !StableTag.IsMatch(release.Tag) || !Version.TryParse(release.Tag[1..], out var latest))
                {
                    return unavailable;
                }

                // Archive builds can add a fourth assembly revision to the installed version.
                var installedCore = new Version(installedVersion.Major, installedVersion.Minor, Math.Max(0, installedVersion.Build));
                var status = latest > installedCore ? PublicReleaseCheckStatus.Available : PublicReleaseCheckStatus.UpToDate;

                return new PublicReleaseCheck(status, installed, latest.ToString(), release.PublishedAt, ReleaseNotesRoot + release.Tag);
            }
            catch (HttpRequestException)
            {
                return unavailable;
            }
            catch (JsonException)
            {
                return unavailable;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return unavailable;
            }
        }

        private sealed class GitHubRelease
        {
            [JsonPropertyName("tag_name")]
            public string Tag { get; set; }

            [JsonPropertyName("draft")]
            public bool? Draft { get; set; }

            [JsonPropertyName("prerelease")]
            public bool? Prerelease { get; set; }

            [JsonPropertyName("published_at")]
            public DateTimeOffset? PublishedAt { get; set; }
        }
    }
}
