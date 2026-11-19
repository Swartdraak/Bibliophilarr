using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Bibliophilarr.Api.V1.Update;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.Update;
using NzbDrone.Core.Update.History;

namespace NzbDrone.Api.Test.Update
{
    [TestFixture]
    public class PublicReleaseCheckControllerFixture
    {
        [Test]
        public void listing_updates_does_not_trigger_the_public_check()
        {
            var recent = new Mock<IRecentUpdateProvider>();
            recent.Setup(service => service.GetRecentUpdatePackages()).Returns(new List<UpdatePackage>());
            var publicDiscovery = new Mock<IPublicReleaseDiscovery>(MockBehavior.Strict);
            var controller = new UpdateController(recent.Object, Mock.Of<IUpdateHistoryService>(), publicDiscovery.Object);

            controller.GetRecentUpdates().Should().BeEmpty();
            publicDiscovery.VerifyNoOtherCalls();
        }

        [Test]
        public async Task explicit_public_check_bypasses_installable_packages()
        {
            var recent = new Mock<IRecentUpdateProvider>(MockBehavior.Strict);
            var result = new PublicReleaseCheck(PublicReleaseCheckStatus.Available, BuildInfo.Version.ToString(), "99.0.0");
            var publicDiscovery = new Mock<IPublicReleaseDiscovery>();
            publicDiscovery.Setup(service => service.Check(It.IsAny<Version>(), It.IsAny<CancellationToken>())).ReturnsAsync(result);
            var controller = new UpdateController(recent.Object, Mock.Of<IUpdateHistoryService>(), publicDiscovery.Object);

            (await controller.CheckPublicRelease(CancellationToken.None)).Should().Be(result);
            publicDiscovery.Verify(service => service.Check(BuildInfo.Version, CancellationToken.None), Times.Once);
            recent.VerifyNoOtherCalls();
        }
    }
}
