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

    [JsonConverter(typeof(OlKeyRefConverter))]
    public class OlKeyRef
    {
        [JsonPropertyName("key")]
        public string Key { get; set; }
    }

    public class OlKeyRefConverter : JsonConverter<OlKeyRef>
    {
        public override OlKeyRef Read(ref Utf8JsonReader reader, System.Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.String)
            {
                return new OlKeyRef { Key = reader.GetString() };
            }

            if (reader.TokenType == JsonTokenType.StartObject)
            {
                string key = null;

                while (reader.Read())
                {
                    if (reader.TokenType == JsonTokenType.EndObject)
                    {
                        break;
                    }

                    if (reader.TokenType == JsonTokenType.PropertyName)
                    {
                        var propertyName = reader.GetString();
                        reader.Read();

                        if (propertyName == "key" && reader.TokenType == JsonTokenType.String)
                        {
                            key = reader.GetString();
                        }
                    }
                }

                return new OlKeyRef { Key = key };
            }

            if (reader.TokenType == JsonTokenType.Null)
            {
                return null;
            }

            // Some Open Library payloads contain malformed mixed token types in arrays.
            // Consume unknown tokens and keep processing instead of failing the entire work payload.
            using (JsonDocument.ParseValue(ref reader))
            {
            }

            return null;
        }

        public override void Write(Utf8JsonWriter writer, OlKeyRef value, JsonSerializerOptions options)
        {
            if (value == null)
            {
                writer.WriteNullValue();
                return;
            }

            writer.WriteStartObject();
            writer.WriteString("key", value.Key);
            writer.WriteEndObject();
        }
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
