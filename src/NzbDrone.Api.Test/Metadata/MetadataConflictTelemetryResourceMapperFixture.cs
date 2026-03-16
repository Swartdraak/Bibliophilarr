using Bibliophilarr.Api.V1.Metadata;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.MetadataSource;

namespace NzbDrone.Api.Test.Metadata
{
    [TestFixture]
    public class MetadataConflictTelemetryResourceMapperFixture
    {
        [Test]
        public void should_map_conflict_snapshot_fields()
        {
            var snapshot = new MetadataConflictTelemetrySnapshot
            {
                TotalDecisions = 5,
                DecisionsByReason =
                {
                    ["tie-break"] = 3,
                    ["quality-score"] = 2
                },
                DecisionsByProvider =
                {
                    ["Inventaire"] = 4,
                    ["OpenLibrary"] = 1
                }
            };

            var resource = MetadataConflictTelemetryResourceMapper.ToResource(snapshot);

            resource.TotalDecisions.Should().Be(5);
            resource.DecisionsByReason["tie-break"].Should().Be(3);
            resource.DecisionsByReason["quality-score"].Should().Be(2);
            resource.DecisionsByProvider["Inventaire"].Should().Be(4);
            resource.DecisionsByProvider["OpenLibrary"].Should().Be(1);
        }
    }
}
