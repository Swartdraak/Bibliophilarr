using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace NzbDrone.Core.MetadataSource.OpenLibrary.Resources
{
    public class OlWorkEditionsResponse
    {
        [JsonPropertyName("links")]
        public OlAuthorWorksLinks Links { get; set; }

        [JsonPropertyName("size")]
        public int Size { get; set; }

        [JsonPropertyName("entries")]
        public List<OlEditionResource> Entries { get; set; } = new List<OlEditionResource>();
    }
}
