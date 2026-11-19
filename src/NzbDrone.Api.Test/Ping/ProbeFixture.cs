using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Configuration;

namespace NzbDrone.Api.Test.Ping
{
    [TestFixture]
    public class ProbeFixture
    {
        [Test]
        public void probe()
        {
            var repository = new Mock<IConfigRepository>();
            repository
                .Setup(r => r.All())
                .Returns(new System.Collections.Generic.List<NzbDrone.Core.Configuration.Config>());
            repository.Object.Should().NotBeNull();
        }
    }
}
