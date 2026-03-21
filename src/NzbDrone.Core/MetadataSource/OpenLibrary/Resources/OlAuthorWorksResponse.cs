using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace NzbDrone.Core.MetadataSource.OpenLibrary.Resources
{
    /// <summary>
    /// Open Library /authors/{OLID}/works.json response.
    /// </summary>
    public class OlAuthorWorksResponse
    {
        [JsonPropertyName("links")]
        public OlAuthorWorksLinks Links { get; set; }

        [JsonPropertyName("size")]
        public int Size { get; set; }

        [JsonPropertyName("entries")]
        public List<OlWorkResource> Entries { get; set; } = new List<OlWorkResource>();
    }

    public class OlAuthorWorksLinks
    {
        [JsonPropertyName("self")]
        public string Self { get; set; }

        [JsonPropertyName("author")]
        public string Author { get; set; }

        [JsonPropertyName("next")]
        public string Next { get; set; }
    }
}
