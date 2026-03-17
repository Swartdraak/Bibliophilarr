using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MetadataSource
{
    [TestFixture]
    public class MetadataQueryNormalizationServiceFixture : CoreTest<MetadataQueryNormalizationService>
    {
        [Test]
        public void should_expand_author_aliases_from_config()
        {
            Mocker.GetMock<IConfigService>()
                .Setup(x => x.MetadataAuthorAliases)
                .Returns("{\"terry mancour\":[\"t. l. mancour\"]}");

            var aliases = Subject.ExpandAuthorAliases(new[] { "Terry Mancour" });

            aliases.Should().Contain("Terry Mancour");
            aliases.Should().Contain("t. l. mancour");
        }

        [Test]
        public void should_strip_series_suffix_using_configured_patterns()
        {
            Mocker.GetMock<IConfigService>()
                .Setup(x => x.MetadataTitleStripPatterns)
                .Returns("[\"\\\\s*:\\\\s*book\\\\s*\\\\d+[^$]*$\"]");

            var variants = Subject.BuildTitleVariants("Spellmonger: Book 1 Of The Spellmonger Series");

            variants.Should().Contain("Spellmonger");
        }
    }
}
