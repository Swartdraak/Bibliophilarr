using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace NzbDrone.Core.MetadataSource.OpenLibrary.Resources
{
    /// <summary>Open Library /authors/{OLID}.json response.</summary>
    public class OlAuthorResource
    {
        /// <summary>/authors/OL{n}A</summary>
        [JsonPropertyName("key")]
        public string Key { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; }

        [JsonPropertyName("personal_name")]
        public string PersonalName { get; set; }

        /// <summary>May be a plain string or {"type":"...", "value":"..."}.</summary>
        [JsonPropertyName("bio")]
        [JsonConverter(typeof(OlTextValueConverter))]
        public string Bio { get; set; }

        /// <summary>List of photo IDs.</summary>
        [JsonPropertyName("photos")]
        public List<long> Photos { get; set; }

        [JsonPropertyName("birth_date")]
        public string BirthDate { get; set; }

        [JsonPropertyName("death_date")]
        public string DeathDate { get; set; }

        [JsonPropertyName("wikipedia")]
        public string Wikipedia { get; set; }

        [JsonPropertyName("links")]
        public List<OlLink> Links { get; set; }
    }
}
