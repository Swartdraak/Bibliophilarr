using Newtonsoft.Json;
using Bibliophilarr.Http.REST;

namespace Bibliophilarr.Api.V1.Indexers
{
    public class IndexerFlagResource : RestResource
    {
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Include)]
        public new int Id { get; set; }
        public string Name { get; set; }
        public string NameLower => Name.ToLowerInvariant();
    }
}
