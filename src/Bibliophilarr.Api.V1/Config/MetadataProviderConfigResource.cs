using Bibliophilarr.Http.REST;
using NzbDrone.Core.Configuration;

namespace Bibliophilarr.Api.V1.Config
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
        public int OpenLibrarySearchTimeoutSeconds { get; set; }
        public int OpenLibraryIsbnTimeoutSeconds { get; set; }
        public int OpenLibraryWorkTimeoutSeconds { get; set; }
        public int OpenLibrarySearchRetryBudget { get; set; }
        public int OpenLibraryIsbnRetryBudget { get; set; }
        public int OpenLibraryWorkRetryBudget { get; set; }
        public WriteAudioTagsType WriteAudioTags { get; set; }
        public bool ScrubAudioTags { get; set; }
        public WriteBookTagsType WriteBookTags { get; set; }
        public bool UpdateCovers { get; set; }
        public bool EmbedMetadata { get; set; }
        public bool EnableInventaireFallback { get; set; }
        public bool EnableGoogleBooksFallback { get; set; }
        public string GoogleBooksApiKey { get; set; }
        public bool EnableHardcoverFallback { get; set; }
        public string HardcoverApiToken { get; set; }
        public int HardcoverRequestTimeoutSeconds { get; set; }
        public int IsbnContextFallbackLimit { get; set; }
        public bool EnableMetadataConflictStrategyVariants { get; set; }
        public string MetadataAuthorAliases { get; set; }
        public string MetadataTitleStripPatterns { get; set; }
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
                OpenLibrarySearchTimeoutSeconds = model.OpenLibrarySearchTimeoutSeconds,
                OpenLibraryIsbnTimeoutSeconds = model.OpenLibraryIsbnTimeoutSeconds,
                OpenLibraryWorkTimeoutSeconds = model.OpenLibraryWorkTimeoutSeconds,
                OpenLibrarySearchRetryBudget = model.OpenLibrarySearchRetryBudget,
                OpenLibraryIsbnRetryBudget = model.OpenLibraryIsbnRetryBudget,
                OpenLibraryWorkRetryBudget = model.OpenLibraryWorkRetryBudget,
                WriteAudioTags = model.WriteAudioTags,
                ScrubAudioTags = model.ScrubAudioTags,
                WriteBookTags = model.WriteBookTags,
                UpdateCovers = model.UpdateCovers,
                EmbedMetadata = model.EmbedMetadata,
                EnableInventaireFallback = model.EnableInventaireFallback,
                EnableGoogleBooksFallback = model.EnableGoogleBooksFallback,
                GoogleBooksApiKey = model.GoogleBooksApiKey,
                EnableHardcoverFallback = model.EnableHardcoverFallback,
                HardcoverApiToken = model.HardcoverApiToken,
                HardcoverRequestTimeoutSeconds = model.HardcoverRequestTimeoutSeconds,
                IsbnContextFallbackLimit = model.IsbnContextFallbackLimit,
                EnableMetadataConflictStrategyVariants = model.EnableMetadataConflictStrategyVariants,
                MetadataAuthorAliases = model.MetadataAuthorAliases,
                MetadataTitleStripPatterns = model.MetadataTitleStripPatterns
            };
        }
    }
}
