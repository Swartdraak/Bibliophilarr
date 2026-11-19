using System;
using System.Text;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Cloud;
using NzbDrone.Common.Http;
using NzbDrone.Core.HealthCheck.Checks;
using NzbDrone.Core.Localization;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.HealthCheck.Checks
{
    [TestFixture]
    public class SystemTimeCheckFixture : CoreTest<SystemTimeCheck>
    {
        [SetUp]
        public void Setup()
        {
            var cloudBuilder = new Mock<IBibliophilarrCloudRequestBuilder>();
            cloudBuilder.SetupGet(x => x.HasServices).Returns(true);
            cloudBuilder.SetupGet(x => x.Services).Returns(new HttpRequestBuilder("https://services.example.test/v1/").CreateFactory());

            Mocker.SetConstant(cloudBuilder.Object);
        }

        private void GivenServerResponse(string json)
        {
            Mocker.GetMock<ILocalizationService>()
                  .Setup(s => s.GetLocalizedString(It.IsAny<string>()))
                  .Returns("System time is off by more than 1 day. Scheduled tasks may not run correctly until the time is corrected");

            Mocker.GetMock<IHttpClient>()
                  .Setup(s => s.Execute(It.IsAny<HttpRequest>()))
                  .Returns<HttpRequest>(r => new HttpResponse(r, new HttpHeader(), Encoding.ASCII.GetBytes(json)));
        }

        [TestCase("utc")]
        [TestCase("dateTimeUtc")]
        public void should_not_return_error_when_system_time_is_close_to_server_time(string property)
        {
            GivenServerResponse($"{{\"{property}\":\"{DateTime.UtcNow:O}\"}}");

            Subject.Check().ShouldBeOk();
        }

        [TestCase("utc")]
        [TestCase("dateTimeUtc")]
        public void should_return_error_when_system_time_is_more_than_one_day_from_server_time(string property)
        {
            GivenServerResponse($"{{\"{property}\":\"{DateTime.UtcNow.AddDays(2):O}\"}}");

            Subject.Check().ShouldBeError();
            ExceptionVerification.ExpectedErrors(1);
        }

        [TestCase("{}")]
        [TestCase("null")]
        [TestCase("{\"utc\":\"invalid\"}")]
        public void should_skip_clock_comparison_when_no_timestamp_is_available(string response)
        {
            GivenServerResponse(response);

            Subject.Check().ShouldBeOk();
            ExceptionVerification.ExpectedWarns(1);
        }
    }
}
