using NzbDrone.Core.Configuration;
using Readarr.Http.REST;

namespace Readarr.Api.V1.Config
{
    public class MetadataProviderConfigResource : RestResource
    {
        public bool EnableOpenLibraryProvider { get; set; }
        public bool EnableGoogleBooksProvider { get; set; }
        public bool EnableInventaireProvider { get; set; }
        public string MetadataProviderPriorityOrder { get; set; }
        public int MetadataProviderTimeoutSeconds { get; set; }
        public int MetadataProviderRetryBudget { get; set; }
        public int MetadataProviderCircuitBreakerThreshold { get; set; }
        public int MetadataProviderCircuitBreakerDurationSeconds { get; set; }
        public WriteAudioTagsType WriteAudioTags { get; set; }
        public bool ScrubAudioTags { get; set; }
        public WriteBookTagsType WriteBookTags { get; set; }
        public bool UpdateCovers { get; set; }
        public bool EmbedMetadata { get; set; }
    }

    public static class MetadataProviderConfigResourceMapper
    {
        public static MetadataProviderConfigResource ToResource(IConfigService model)
        {
            return new MetadataProviderConfigResource
            {
                Id = 1,
                EnableOpenLibraryProvider = model.EnableOpenLibraryProvider,
                EnableGoogleBooksProvider = model.EnableGoogleBooksProvider,
                EnableInventaireProvider = model.EnableInventaireProvider,
                MetadataProviderPriorityOrder = model.MetadataProviderPriorityOrder,
                MetadataProviderTimeoutSeconds = model.MetadataProviderTimeoutSeconds,
                MetadataProviderRetryBudget = model.MetadataProviderRetryBudget,
                MetadataProviderCircuitBreakerThreshold = model.MetadataProviderCircuitBreakerThreshold,
                MetadataProviderCircuitBreakerDurationSeconds = model.MetadataProviderCircuitBreakerDurationSeconds,
                WriteAudioTags = model.WriteAudioTags,
                ScrubAudioTags = model.ScrubAudioTags,
                WriteBookTags = model.WriteBookTags,
                UpdateCovers = model.UpdateCovers,
                EmbedMetadata = model.EmbedMetadata
            };
        }
    }
}
