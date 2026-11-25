using System;
using System.Linq;
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
        public PublicReleaseCheck(PublicReleaseCheckStatus status,
                                  string installedVersion,
                                  string latestVersion = null,
                                  DateTimeOffset? publishedAt = null,
                                  string releaseNotesUrl = null,
                                  string releaseManifestUrl = null,
                                  string checksumsUrl = null)
        {
            Status = status;
            InstalledVersion = installedVersion;
            LatestVersion = latestVersion;
            PublishedAt = publishedAt;
            ReleaseNotesUrl = releaseNotesUrl;
            ReleaseManifestUrl = releaseManifestUrl;
            ChecksumsUrl = checksumsUrl;
        }

        public PublicReleaseCheckStatus Status { get; }
        public string InstalledVersion { get; }
        public string LatestVersion { get; }
        public DateTimeOffset? PublishedAt { get; }
        public string ReleaseNotesUrl { get; }
        public string ReleaseManifestUrl { get; }
        public string ChecksumsUrl { get; }
    }

    public class PublicReleaseDiscovery : IPublicReleaseDiscovery
    {
        private const string LatestReleaseUrl = "https://api.github.com/repos/Swartdraak/Bibliophilarr/releases/latest";
        private const string OfficialReleaseRoot = "https://github.com/Swartdraak/Bibliophilarr/releases/";
        private const string ManifestAsset = "release-manifest.json";
        private const string ChecksumsAsset = "SHA256SUMS.txt";
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

                var releaseNotesUrl = OfficialReleaseRoot + "tag/" + release.Tag;
                if (status != PublicReleaseCheckStatus.Available ||
                    !HasUploadedAsset(release.Assets, ManifestAsset) ||
                    !HasUploadedAsset(release.Assets, ChecksumsAsset))
                {
                    return new PublicReleaseCheck(status, installed, latest.ToString(), release.PublishedAt, releaseNotesUrl);
                }

                var downloadRoot = OfficialReleaseRoot + "download/" + release.Tag + "/";
                return new PublicReleaseCheck(status,
                                              installed,
                                              latest.ToString(),
                                              release.PublishedAt,
                                              releaseNotesUrl,
                                              downloadRoot + ManifestAsset,
                                              downloadRoot + ChecksumsAsset);
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

        private static bool HasUploadedAsset(JsonElement assets, string name)
        {
            return assets.ValueKind == JsonValueKind.Array &&
                   assets.EnumerateArray().Any(asset =>
                       asset.ValueKind == JsonValueKind.Object &&
                       asset.TryGetProperty("name", out var assetName) &&
                       assetName.ValueKind == JsonValueKind.String &&
                       assetName.ValueEquals(name) &&
                       asset.TryGetProperty("state", out var state) &&
                       state.ValueKind == JsonValueKind.String &&
                       state.ValueEquals("uploaded"));
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

            [JsonPropertyName("assets")]
            public JsonElement Assets { get; set; }
        }
    }
}
