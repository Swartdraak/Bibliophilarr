using System;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.MetadataSource.Registry;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MetadataSource.Registry
{
    [TestFixture]
    public class MetadataProviderRegistryFixture : CoreTest<MetadataProviderRegistry>
    {
        private static IMetadataProvider MakeProvider(string name, int priority, bool enabled)
        {
            return new StubProvider(name, priority, enabled);
        }

        // ── Register ─────────────────────────────────────────────────────────

        [Test]
        public void register_null_provider_should_throw()
        {
            Action act = () => Subject.Register(null);
            act.Should().Throw<ArgumentNullException>();
        }

        [Test]
        public void register_provider_should_appear_in_get_providers()
        {
            Subject.Register(MakeProvider("Open Library", 1, true));

            Subject.GetProviders().Should().HaveCount(1);
        }

        // ── GetProviders ordering ─────────────────────────────────────────────

        [Test]
        public void get_providers_should_return_all_providers_ordered_by_priority()
        {
            Subject.Register(MakeProvider("Google Books", 3, true));
            Subject.Register(MakeProvider("Inventaire", 2, true));
            Subject.Register(MakeProvider("Open Library", 1, true));

            var result = Subject.GetProviders();
            result.Should().HaveCount(3);
            result[0].ProviderName.Should().Be("Open Library");
            result[1].ProviderName.Should().Be("Inventaire");
            result[2].ProviderName.Should().Be("Google Books");
        }

        [Test]
        public void get_providers_includes_disabled_providers()
        {
            Subject.Register(MakeProvider("Open Library", 1, true));
            Subject.Register(MakeProvider("Disabled Provider", 2, false));

            Subject.GetProviders().Should().HaveCount(2);
        }

        // ── GetEnabledProviders ───────────────────────────────────────────────

        [Test]
        public void get_enabled_providers_excludes_disabled_providers()
        {
            Subject.Register(MakeProvider("Open Library", 1, true));
            Subject.Register(MakeProvider("Disabled Provider", 2, false));

            var result = Subject.GetEnabledProviders();
            result.Should().HaveCount(1);
            result[0].ProviderName.Should().Be("Open Library");
        }

        [Test]
        public void get_enabled_providers_returns_empty_when_all_disabled()
        {
            Subject.Register(MakeProvider("Disabled A", 1, false));
            Subject.Register(MakeProvider("Disabled B", 2, false));

            Subject.GetEnabledProviders().Should().BeEmpty();
        }

        [Test]
        public void get_enabled_providers_should_be_ordered_by_priority()
        {
            Subject.Register(MakeProvider("Google Books", 3, true));
            Subject.Register(MakeProvider("Open Library", 1, true));
            Subject.Register(MakeProvider("Inventaire", 2, true));

            var result = Subject.GetEnabledProviders();
            result[0].ProviderName.Should().Be("Open Library");
            result[1].ProviderName.Should().Be("Inventaire");
            result[2].ProviderName.Should().Be("Google Books");
        }

        // ── GetPrimaryProvider ────────────────────────────────────────────────

        [Test]
        public void get_primary_provider_returns_null_when_no_providers()
        {
            Subject.GetPrimaryProvider().Should().BeNull();
        }

        [Test]
        public void get_primary_provider_returns_null_when_all_disabled()
        {
            Subject.Register(MakeProvider("Disabled", 1, false));

            Subject.GetPrimaryProvider().Should().BeNull();
        }

        [Test]
        public void get_primary_provider_returns_highest_priority_enabled()
        {
            Subject.Register(MakeProvider("Google Books", 3, true));
            Subject.Register(MakeProvider("Inventaire", 2, true));
            Subject.Register(MakeProvider("Open Library", 1, true));

            Subject.GetPrimaryProvider().ProviderName.Should().Be("Open Library");
        }

        [Test]
        public void get_primary_provider_skips_disabled_provider_at_top_priority()
        {
            Subject.Register(MakeProvider("Open Library", 1, false));
            Subject.Register(MakeProvider("Inventaire", 2, true));

            Subject.GetPrimaryProvider().ProviderName.Should().Be("Inventaire");
        }

        // ── Stub implementation ──────────────────────────────────────────────

        private sealed class StubProvider : IMetadataProvider
        {
            public StubProvider(string name, int priority, bool enabled)
            {
                ProviderName = name;
                Priority = priority;
                IsEnabled = enabled;
            }

            public string ProviderName { get; }
            public int Priority { get; }
            public bool IsEnabled { get; }
            public bool SupportsAuthorSearch => true;
            public bool SupportsBookSearch => true;
            public bool SupportsIsbnLookup => true;
            public bool SupportsSeriesInfo => false;
            public bool SupportsCoverImages => true;

            public RateLimitInfo GetRateLimits() => new RateLimitInfo
            {
                MaxRequestsPerWindow = 100,
                Window = TimeSpan.FromMinutes(5),
                RequiresApiKey = false
            };
        }
    }
}
