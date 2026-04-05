using System;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Cloud;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Common.Http;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Core.Update;

namespace NzbDrone.Core.Test.UpdateTests
{
    /// <summary>
    /// Unit tests for UpdatePackageProvider when cloud services endpoint is not configured.
    /// These tests ensure the guard behavior (HasServices == false) short-circuits gracefully
    /// and does not attempt any HTTP requests — a required property for local-only deployments.
    /// </summary>
    [TestFixture]
    public class UpdatePackageProviderServicesDisabledFixture : CoreTest<UpdatePackageProvider>
    {
        [SetUp]
        public void SetUp()
        {
            // Cloud request builder reports that services are not configured
            var noServicesBuilder = new Mock<IBibliophilarrCloudRequestBuilder>();
            noServicesBuilder.SetupGet(x => x.HasServices).Returns(false);
            noServicesBuilder.SetupGet(x => x.Services).Returns((IHttpRequestBuilderFactory)null);
            Mocker.SetConstant(noServicesBuilder.Object);

            Mocker.GetMock<IPlatformInfo>().SetupGet(c => c.Version).Returns(new Version("1.0.0"));
        }

        [Test]
        public void get_latest_update_returns_null_when_cloud_services_are_not_configured()
        {
            var result = Subject.GetLatestUpdate("develop", new Version(0, 1));

            result.Should().BeNull("no cloud endpoint is configured so no update info is available");

            // The HTTP client must never have been called
            Mocker.GetMock<IHttpClient>()
                .Verify(
                    x => x.Get<UpdatePackageAvailable>(It.IsAny<HttpRequest>()),
                    Times.Never(),
                    "update check should short-circuit before any HTTP request is made");
        }

        [Test]
        public void get_recent_updates_returns_empty_list_when_cloud_services_are_not_configured()
        {
            var result = Subject.GetRecentUpdates("develop", new Version(0, 1), null);

            result.Should().NotBeNull();
            result.Should().BeEmpty("no cloud endpoint is configured so the update history is unavailable");

            Mocker.GetMock<IHttpClient>()
                .Verify(
                    x => x.Get<System.Collections.Generic.List<UpdatePackage>>(It.IsAny<HttpRequest>()),
                    Times.Never(),
                    "update history check should short-circuit before any HTTP request is made");
        }
    }
}
