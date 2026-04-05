using System.Collections.Generic;

namespace Bibliophilarr.Api.V1.Search
{
    public class SearchTelemetryResource
    {
        public int UnsupportedEntityCount { get; set; }

        public Dictionary<string, int> UnsupportedEntityTypes { get; set; }

        public Dictionary<string, int> Terms { get; set; }
    }

    public static class SearchTelemetryResourceMapper
    {
        public static SearchTelemetryResource ToResource(SearchTelemetrySnapshot snapshot)
        {
            snapshot ??= new SearchTelemetrySnapshot();

            return new SearchTelemetryResource
            {
                UnsupportedEntityCount = snapshot.UnsupportedEntityCount,
                UnsupportedEntityTypes = new Dictionary<string, int>(snapshot.UnsupportedEntityTypes),
                Terms = new Dictionary<string, int>(snapshot.Terms)
            };
        }
    }
}
