using Bibliophilarr.Api.V1.Search;
using FluentAssertions;
using Moq;
using NUnit.Framework;

namespace NzbDrone.Api.Test.Search
{
    [TestFixture]
    public class SearchTelemetryControllerFixture
    {
        [Test]
        public void should_return_search_telemetry_snapshot()
        {
            var service = new Mock<ISearchTelemetryService>();
            service.Setup(x => x.GetSnapshot())
                .Returns(new SearchTelemetrySnapshot
                {
                    UnsupportedEntityCount = 3,
                    UnsupportedEntityTypes =
                    {
                        ["System.Version"] = 2,
                        ["<null>"] = 1
                    },
                    Terms =
                    {
                        ["anne"] = 2,
                        ["<empty>"] = 1
                    }
                });

            var controller = new SearchTelemetryController(service.Object);

            var response = controller.GetSnapshot();

            response.UnsupportedEntityCount.Should().Be(3);
            response.UnsupportedEntityTypes["System.Version"].Should().Be(2);
            response.UnsupportedEntityTypes["<null>"].Should().Be(1);
            response.Terms["anne"].Should().Be(2);
            response.Terms["<empty>"].Should().Be(1);
        }
    }
}
