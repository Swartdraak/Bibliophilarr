using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace NzbDrone.Core.MetadataSource.OpenLibrary.Resources
{
    /// <summary>Top-level envelope returned by Open Library /search.json.</summary>
    public class OlSearchResponse
    {
        [JsonPropertyName("numFound")]
        public int NumFound { get; set; }

        [JsonPropertyName("start")]
        public int Start { get; set; }

        [JsonPropertyName("docs")]
        public List<OlSearchDoc> Docs { get; set; } = new List<OlSearchDoc>();
    }
}
