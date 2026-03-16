using Bibliophilarr.Http.REST;
using NzbDrone.Core.Configuration;

namespace Bibliophilarr.Api.V1.Config
{
    public class MetadataProviderConfigResource : RestResource
    {
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
        public string MetadataAuthorAliases { get; set; }
        public string MetadataTitleStripPatterns { get; set; }
    }

    public static class MetadataProviderConfigResourceMapper
    {
        public static MetadataProviderConfigResource ToResource(IConfigService model)
        {
            return new MetadataProviderConfigResource
            {
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
                MetadataAuthorAliases = model.MetadataAuthorAliases,
                MetadataTitleStripPatterns = model.MetadataTitleStripPatterns
            };
        }
    }
}
