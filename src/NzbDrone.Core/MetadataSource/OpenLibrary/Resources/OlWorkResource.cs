using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NzbDrone.Core.MetadataSource.OpenLibrary.Resources
{
    /// <summary>
    /// Open Library /works/{OLID}.json response.
    /// description may be a plain string or a typed object {"type":..., "value":...}.
    /// </summary>
    public class OlWorkResource
    {
        /// <summary>/works/OL{n}W</summary>
        [JsonPropertyName("key")]
        public string Key { get; set; }

        [JsonPropertyName("title")]
        public string Title { get; set; }

        /// <summary>
        /// May be a raw string or a {"type":"...", "value":"..."} node.
        /// Handled by OlTextValueConverter.
        /// </summary>
        [JsonPropertyName("description")]
        [JsonConverter(typeof(OlTextValueConverter))]
        public string Description { get; set; }

        /// <summary>List of cover IDs.</summary>
        [JsonPropertyName("covers")]
        public List<long> Covers { get; set; }

        [JsonPropertyName("subjects")]
        public List<string> Subjects { get; set; }

        [JsonPropertyName("subject_places")]
        public List<string> SubjectPlaces { get; set; }

        [JsonPropertyName("subject_people")]
        public List<string> SubjectPeople { get; set; }

        [JsonPropertyName("first_publish_date")]
        public string FirstPublishDate { get; set; }

        /// <summary>e.g. [{"author":{"key":"/authors/OL26320A"},"type":{"key":"/type/author_role"}}]</summary>
        [JsonPropertyName("authors")]
        public List<OlWorkAuthorEntry> Authors { get; set; }

        [JsonPropertyName("links")]
        public List<OlLink> Links { get; set; }
    }

    public class OlWorkAuthorEntry
    {
        [JsonPropertyName("author")]
        public OlKeyRef Author { get; set; }

        [JsonPropertyName("type")]
        public OlKeyRef Type { get; set; }
    }

    public class OlKeyRef
    {
        [JsonPropertyName("key")]
        public string Key { get; set; }
    }

    public class OlLink
    {
        [JsonPropertyName("title")]
        public string Title { get; set; }

        [JsonPropertyName("url")]
        public string Url { get; set; }
    }

    /// <summary>
    /// Converts Open Library's polymorphic description field:
    /// it may be a plain string or {"type":"/type/text", "value":"..."}.
    /// </summary>
    public class OlTextValueConverter : JsonConverter<string>
    {
        public override string Read(ref Utf8JsonReader reader, System.Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.String)
            {
                return reader.GetString();
            }

            if (reader.TokenType == JsonTokenType.StartObject)
            {
                string value = null;
                while (reader.Read())
                {
                    if (reader.TokenType == JsonTokenType.EndObject)
                    {
                        break;
                    }

                    if (reader.TokenType == JsonTokenType.PropertyName && reader.GetString() == "value")
                    {
                        reader.Read();
                        value = reader.GetString();
                    }
                    else if (reader.TokenType == JsonTokenType.PropertyName)
                    {
                        reader.Read(); // skip property value
                    }
                }

                return value;
            }

            return null;
        }

        public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options)
        {
            writer.WriteStringValue(value);
        }
    }
}
