using System.Collections.Generic;
using NzbDrone.Core.MetadataSource;

namespace Bibliophilarr.Api.V1.Metadata
{
    public class MetadataConflictTelemetryResource
    {
        public int TotalDecisions { get; set; }

        public Dictionary<string, int> DecisionsByReason { get; set; }

        public Dictionary<string, int> DecisionsByProvider { get; set; }
    }

    public static class MetadataConflictTelemetryResourceMapper
    {
        public static MetadataConflictTelemetryResource ToResource(MetadataConflictTelemetrySnapshot snapshot)
        {
            snapshot ??= new MetadataConflictTelemetrySnapshot();

            return new MetadataConflictTelemetryResource
            {
                TotalDecisions = snapshot.TotalDecisions,
                DecisionsByReason = new Dictionary<string, int>(snapshot.DecisionsByReason),
                DecisionsByProvider = new Dictionary<string, int>(snapshot.DecisionsByProvider)
            };
        }
    }
}
