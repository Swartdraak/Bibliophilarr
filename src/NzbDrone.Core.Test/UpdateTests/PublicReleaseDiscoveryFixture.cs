using System;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Options;
using NUnit.Framework;
using NzbDrone.Common.Options;
using NzbDrone.Core.Update;

namespace NzbDrone.Core.Test.UpdateTests
{
    [TestFixture]
    public class PublicReleaseDiscoveryFixture
    {
        private const string StableRelease = """
            {"tag_name":"v1.2.3", "draft":false, "prerelease":false, "published_at":"2026-01-01T00:00:00Z", "html_url":"https://example.invalid/not-the-repository"}
            """;

        [Test]
        public async Task disabled_mode_does_not_request_the_network()
        {
            using var client = new HttpClient(new TestHandler(_ => throw new InvalidOperationException("Unexpected network request")));
            var service = new PublicReleaseDiscovery(Options.Create(new UpdateOptions()), client);

            var result = await service.Check(new Version(1, 2, 2, 17), CancellationToken.None);

            result.Status.Should().Be(PublicReleaseCheckStatus.Disabled);
            result.InstalledVersion.Should().Be("1.2.2.17");
        }

        [TestCase("1.2.2.79", PublicReleaseCheckStatus.Available)]
        [TestCase("1.2.3.79", PublicReleaseCheckStatus.UpToDate)]
        [TestCase("1.2.4", PublicReleaseCheckStatus.UpToDate)]
        public async Task compares_stable_version_ignoring_the_installed_build_revision(string installed, PublicReleaseCheckStatus expected)
        {
            using var client = new HttpClient(new TestHandler(request =>
            {
                request.RequestUri.AbsoluteUri.Should().Be("https://api.github.com/repos/Swartdraak/Bibliophilarr/releases/latest");
                request.Method.Should().Be(HttpMethod.Get);
                request.Headers.Authorization.Should().BeNull();
                request.Headers.Contains("Cookie").Should().BeFalse();
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(StableRelease) };
            }));
            var service = new PublicReleaseDiscovery(Options.Create(new UpdateOptions { PublicReleaseDiscovery = true }), client);

            var result = await service.Check(Version.Parse(installed), CancellationToken.None);

            result.Status.Should().Be(expected);
            result.InstalledVersion.Should().Be(installed);
            result.LatestVersion.Should().Be("1.2.3");
            result.ReleaseNotesUrl.Should().Be("https://github.com/Swartdraak/Bibliophilarr/releases/tag/v1.2.3");
        }

        [Test]
        public async Task should_link_only_to_verified_repository_paths_for_the_two_uploaded_assets()
        {
            var json = WithAssets("""
                [
                    {"name":"release-manifest.json","state":"uploaded","browser_download_url":"https://example.invalid/manifest"},
                    {"name":"SHA256SUMS.txt","state":"uploaded","browser_download_url":"https://example.invalid/checksums"}
                ]
                """);

            var result = await CheckReleaseWithAssets(json, "1.2.2");

            result.GetProperty("releaseManifestUrl").GetString().Should()
                  .Be("https://github.com/Swartdraak/Bibliophilarr/releases/download/v1.2.3/release-manifest.json");
            result.GetProperty("checksumsUrl").GetString().Should()
                  .Be("https://github.com/Swartdraak/Bibliophilarr/releases/download/v1.2.3/SHA256SUMS.txt");
        }

        [TestCase("[]", "1.2.2")]
        [TestCase("{}", "1.2.2")]
        [TestCase("[{\"name\":\"release-manifest.json\",\"state\":\"uploaded\"}]", "1.2.2")]
        [TestCase("[{\"name\":\"release-manifest.json\",\"state\":\"uploaded\"},{\"name\":\"SHA256SUMS.txt\",\"state\":\"new\"}]", "1.2.2")]
        [TestCase("[{\"name\":\"Release-Manifest.json\",\"state\":\"uploaded\"},{\"name\":\"SHA256SUMS.txt\",\"state\":\"uploaded\"}]", "1.2.2")]
        [TestCase("[{\"name\":\"release-manifest.json\",\"state\":\"uploaded\"},{\"name\":\"SHA256SUMS.txt\",\"state\":\"uploaded\"}]", "1.2.3")]
        public async Task should_not_offer_incomplete_unuploaded_or_unneeded_verification_links(string assets, string installed)
        {
            var result = await CheckReleaseWithAssets(WithAssets(assets), installed);

            result.GetProperty("releaseNotesUrl").GetString().Should()
                  .Be("https://github.com/Swartdraak/Bibliophilarr/releases/tag/v1.2.3");
            HasString(result, "releaseManifestUrl").Should().BeFalse();
            HasString(result, "checksumsUrl").Should().BeFalse();
        }

        [TestCase("invalid-json")]
        [TestCase("{\"tag_name\":\"v1.2.3\", \"draft\": false}")]
        [TestCase("{\"tag_name\":\"v1.2.3-rc.1\", \"draft\":false, \"prerelease\":false, \"published_at\":\"2026-01-01T00:00:00Z\"}")]
        [TestCase("{\"tag_name\":\"v1.2.3\", \"draft\":true, \"prerelease\":false, \"published_at\":\"2026-01-01T00:00:00Z\"}")]
        [TestCase("{\"tag_name\":\"v1.2.3\", \"draft\":false, \"prerelease\":true, \"published_at\":\"2026-01-01T00:00:00Z\"}")]
        public async Task invalid_or_non_stable_metadata_is_unavailable(string json)
        {
            using var client = new HttpClient(new TestHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json) }));
            var service = new PublicReleaseDiscovery(Options.Create(new UpdateOptions { PublicReleaseDiscovery = true }), client);

            var result = await service.Check(new Version(1, 0, 0), CancellationToken.None);

            result.Status.Should().Be(PublicReleaseCheckStatus.Unavailable);
            result.ReleaseNotesUrl.Should().BeNull();
        }

        [TestCase(HttpStatusCode.NotFound)]
        [TestCase(HttpStatusCode.TooManyRequests)]
        [TestCase(HttpStatusCode.Redirect)]
        public async Task unexpected_http_status_is_unavailable(HttpStatusCode responseCode)
        {
            using var client = new HttpClient(new TestHandler(_ => new HttpResponseMessage(responseCode)));
            var service = new PublicReleaseDiscovery(Options.Create(new UpdateOptions { PublicReleaseDiscovery = true }), client);

            var result = await service.Check(new Version(1, 0, 0), CancellationToken.None);

            result.Status.Should().Be(PublicReleaseCheckStatus.Unavailable);
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task offline_and_timeout_are_unavailable(bool timeout)
        {
            using var client = new HttpClient(new TestHandler(_ => throw (timeout ? new TaskCanceledException() : new HttpRequestException())));
            var service = new PublicReleaseDiscovery(Options.Create(new UpdateOptions { PublicReleaseDiscovery = true }), client);

            var result = await service.Check(new Version(1, 0, 0), CancellationToken.None);

            result.Status.Should().Be(PublicReleaseCheckStatus.Unavailable);
        }

        private static async Task<JsonElement> CheckReleaseWithAssets(string json, string installed)
        {
            var calls = 0;
            using var client = new HttpClient(new TestHandler(request =>
            {
                calls++;
                request.RequestUri.AbsoluteUri.Should().Be("https://api.github.com/repos/Swartdraak/Bibliophilarr/releases/latest");
                request.Method.Should().Be(HttpMethod.Get);
                request.Headers.Authorization.Should().BeNull();
                request.Headers.Contains("Cookie").Should().BeFalse();
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json) };
            }));
            var service = new PublicReleaseDiscovery(Options.Create(new UpdateOptions { PublicReleaseDiscovery = true }), client);
            var result = await service.Check(Version.Parse(installed), CancellationToken.None);

            calls.Should().Be(1);
            return JsonSerializer.SerializeToElement(result, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        }

        private static string WithAssets(string assets)
        {
            return StableRelease[..StableRelease.LastIndexOf('}')] + ", \"assets\": " + assets + "}";
        }

        private static bool HasString(JsonElement element, string propertyName)
        {
            return element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String;
        }

        private sealed class TestHandler : HttpMessageHandler
        {
            private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;

            public TestHandler(Func<HttpRequestMessage, HttpResponseMessage> respond)
            {
                _respond = respond;
            }

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                return Task.FromResult(_respond(request));
            }
        }
    }
}
