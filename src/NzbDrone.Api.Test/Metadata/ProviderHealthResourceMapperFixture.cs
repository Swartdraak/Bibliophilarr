using System;
using Bibliophilarr.Api.V1.Metadata;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.MetadataSource;

namespace NzbDrone.Api.Test.Metadata
{
    [TestFixture]
    public class ProviderHealthResourceMapperFixture
    {
        [Test]
        public void should_map_rate_limit_and_retry_after_fields()
        {
            var status = new ProviderHealthStatus
            {
                Health = ProviderHealth.Degraded,
                RateLimitWindowRequests = 9,
                RateLimitWindowLimit = 10,
                RateLimitRemaining = 1,
                RateLimitUsageRatio = 0.9,
                IsRateLimitNearCeiling = true,
                RetryAfterRemainingSeconds = 42,
                CooldownUntilUtc = DateTime.UtcNow.AddSeconds(42)
            };

            var resource = ProviderHealthResourceMapper.ToResource("Hardcover", status);

            resource.ProviderName.Should().Be("Hardcover");
            resource.Health.Should().Be("Degraded");
            resource.RateLimitWindowRequests.Should().Be(9);
            resource.RateLimitWindowLimit.Should().Be(10);
            resource.RateLimitRemaining.Should().Be(1);
            resource.RateLimitUsageRatio.Should().Be(0.9);
            resource.IsRateLimitNearCeiling.Should().BeTrue();
            resource.RetryAfterRemainingSeconds.Should().Be(42);
            resource.CooldownUntilUtc.Should().NotBeNull();
        }
    }
}
