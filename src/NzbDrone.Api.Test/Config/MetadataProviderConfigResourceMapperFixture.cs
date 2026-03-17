using Bibliophilarr.Api.V1.Config;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Configuration;

namespace NzbDrone.Api.Test.Config
{
    [TestFixture]
    public class MetadataProviderConfigResourceMapperFixture
    {
        [Test]
        public void to_resource_should_map_metadata_provider_settings_round_trip_surface()
        {
            var config = new Mock<IConfigService>();
            config.SetupGet(x => x.EnableBookInfoProvider).Returns(true);
            config.SetupGet(x => x.EnableOpenLibraryProvider).Returns(true);
            config.SetupGet(x => x.EnableInventaireProvider).Returns(false);
            config.SetupGet(x => x.MetadataProviderPriorityOrder).Returns("OpenLibrary,BookInfo,Inventaire");
            config.SetupGet(x => x.MetadataProviderTimeoutSeconds).Returns(25);
            config.SetupGet(x => x.MetadataProviderRetryBudget).Returns(3);
            config.SetupGet(x => x.MetadataProviderCircuitBreakerThreshold).Returns(4);
            config.SetupGet(x => x.MetadataProviderCircuitBreakerDurationSeconds).Returns(90);
            config.SetupGet(x => x.WriteAudioTags).Returns(WriteAudioTagsType.Sync);
            config.SetupGet(x => x.ScrubAudioTags).Returns(true);
            config.SetupGet(x => x.WriteBookTags).Returns(WriteBookTagsType.AllFiles);
            config.SetupGet(x => x.UpdateCovers).Returns(true);
            config.SetupGet(x => x.EmbedMetadata).Returns(false);

            var resource = MetadataProviderConfigResourceMapper.ToResource(config.Object);

            resource.EnableBookInfoProvider.Should().BeTrue();
            resource.EnableOpenLibraryProvider.Should().BeTrue();
            resource.EnableInventaireProvider.Should().BeFalse();
            resource.MetadataProviderPriorityOrder.Should().Be("OpenLibrary,BookInfo,Inventaire");
            resource.MetadataProviderTimeoutSeconds.Should().Be(25);
            resource.MetadataProviderRetryBudget.Should().Be(3);
            resource.MetadataProviderCircuitBreakerThreshold.Should().Be(4);
            resource.MetadataProviderCircuitBreakerDurationSeconds.Should().Be(90);
            resource.WriteAudioTags.Should().Be(WriteAudioTagsType.Sync);
            resource.ScrubAudioTags.Should().BeTrue();
            resource.WriteBookTags.Should().Be(WriteBookTagsType.AllFiles);
            resource.UpdateCovers.Should().BeTrue();
            resource.EmbedMetadata.Should().BeFalse();
        }
    }
}
