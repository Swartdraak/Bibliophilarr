using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace NzbDrone.Core.MetadataSource.OpenLibrary.Resources
{
    /// <summary>
    /// A single document entry within an Open Library /search.json response.
    /// Only the subset of fields Bibliophilarr actually uses is modelled here.
    /// </summary>
    public class OlSearchDoc
    {
        /// <summary>/works/OL{n}W</summary>
        [JsonPropertyName("key")]
        public string Key { get; set; }

        [JsonPropertyName("title")]
        public string Title { get; set; }

        [JsonPropertyName("author_name")]
        public List<string> AuthorName { get; set; }

        /// <summary>e.g. ["/authors/OL26320A"]</summary>
        [JsonPropertyName("author_key")]
        public List<string> AuthorKey { get; set; }

        [JsonPropertyName("isbn")]
        public List<string> Isbn { get; set; }

        /// <summary>Open Library cover ID for building cover image URLs.</summary>
        [JsonPropertyName("cover_i")]
        public long? CoverId { get; set; }

        [JsonPropertyName("first_publish_year")]
        public int? FirstPublishYear { get; set; }

        [JsonPropertyName("number_of_pages_median")]
        public int? NumberOfPagesMedian { get; set; }

        [JsonPropertyName("language")]
        public List<string> Language { get; set; }

        [JsonPropertyName("subject")]
        public List<string> Subject { get; set; }

        [JsonPropertyName("series")]
        public List<string> Series { get; set; }

        [JsonPropertyName("series_with_number")]
        public List<string> SeriesWithNumber { get; set; }

        [JsonPropertyName("ratings_average")]
        public double? RatingsAverage { get; set; }

        [JsonPropertyName("ratings_count")]
        public int? RatingsCount { get; set; }

        [JsonPropertyName("want_to_read_count")]
        public int? WantToReadCount { get; set; }

        [JsonPropertyName("currently_reading_count")]
        public int? CurrentlyReadingCount { get; set; }

        [JsonPropertyName("already_read_count")]
        public int? AlreadyReadCount { get; set; }

        [JsonPropertyName("edition_count")]
        public int? EditionCount { get; set; }
    }
}
