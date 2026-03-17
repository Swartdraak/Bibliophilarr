using System.Text.Json.Serialization;
using Bibliophilarr.Http.REST;

namespace Bibliophilarr.Api.V1.Languages
{
    public class LanguageResource : RestResource
    {
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public new int Id { get; set; }
        public string Name { get; set; }
        public string NameLower => Name.ToLowerInvariant();
    }
}
