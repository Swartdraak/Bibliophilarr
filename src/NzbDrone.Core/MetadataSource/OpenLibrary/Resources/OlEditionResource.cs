using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace NzbDrone.Core.MetadataSource.OpenLibrary.Resources
{
    /// <summary>
    /// Open Library /books/{OLID}.json or /isbn/{ISBN}.json response.
    /// Represents a specific edition of a work.
    /// </summary>
    public class OlEditionResource
    {
        /// <summary>/books/OL{n}M</summary>
        [JsonPropertyName("key")]
        public string Key { get; set; }

        [JsonPropertyName("title")]
        public string Title { get; set; }

        [JsonPropertyName("isbn_10")]
        public List<string> Isbn10 { get; set; }

        [JsonPropertyName("isbn_13")]
        public List<string> Isbn13 { get; set; }

        [JsonPropertyName("publishers")]
        public List<string> Publishers { get; set; }

        [JsonPropertyName("publish_date")]
        public string PublishDate { get; set; }

        /// <summary>List of cover IDs.</summary>
        [JsonPropertyName("covers")]
        public List<long> Covers { get; set; }

        /// <summary>e.g. [{"key":"/works/OL45883W"}]</summary>
        [JsonPropertyName("works")]
        public List<OlKeyRef> Works { get; set; }

        /// <summary>e.g. [{"key":"/authors/OL26320A"}]</summary>
        [JsonPropertyName("authors")]
        public List<OlKeyRef> Authors { get; set; }

        [JsonPropertyName("number_of_pages")]
        public int? NumberOfPages { get; set; }

        [JsonPropertyName("languages")]
        public List<OlKeyRef> Languages { get; set; }

        [JsonPropertyName("physical_format")]
        public string PhysicalFormat { get; set; }

        [JsonPropertyName("description")]
        [JsonConverter(typeof(OlTextValueConverter))]
        public string Description { get; set; }
    }
}
