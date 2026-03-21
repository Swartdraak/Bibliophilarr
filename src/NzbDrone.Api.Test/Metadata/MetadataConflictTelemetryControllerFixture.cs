using Bibliophilarr.Api.V1.Metadata;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.MetadataSource;

namespace NzbDrone.Api.Test.Metadata
{
    [TestFixture]
    public class MetadataConflictTelemetryControllerFixture
    {
        [Test]
        public void should_include_field_selection_counters_in_contract_response()
        {
            var service = new Mock<IMetadataConflictTelemetryService>();
            service.Setup(x => x.GetSnapshot())
                .Returns(new MetadataConflictTelemetrySnapshot
                {
                    TotalDecisions = 4,
                    DecisionsByReason =
                    {
                        ["tie-break"] = 4
                    },
                    DecisionsByProvider =
                    {
                        ["Inventaire"] = 3,
                        ["OpenLibrary"] = 1
                    },
                    FieldSelectionsByProvider =
                    {
                        ["title:Inventaire"] = 2,
                        ["cover-links:OpenLibrary"] = 2
                    },
                    LastDecisionScoreBreakdownByProvider =
                    {
                        ["Inventaire"] = "title:20,author:20,foreign-book-id:20",
                        ["OpenLibrary"] = "title:20,author:20,foreign-book-id:20,cover-images:3"
                    }
                });

            var controller = new MetadataConflictTelemetryController(service.Object);

            var response = controller.GetSnapshot();

            response.TotalDecisions.Should().Be(4);
            response.DecisionsByReason["tie-break"].Should().Be(4);
            response.DecisionsByProvider["Inventaire"].Should().Be(3);
            response.FieldSelectionsByProvider["title:Inventaire"].Should().Be(2);
            response.FieldSelectionsByProvider["cover-links:OpenLibrary"].Should().Be(2);
            response.LastDecisionScoreBreakdownByProvider["Inventaire"].Should().Contain("title:20");
            response.LastDecisionScoreBreakdownByProvider["OpenLibrary"].Should().Contain("cover-images:3");
        }
    }
}
