using System.Net.Http;
using Bibliophilarr.Http.Extensions;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Integration.Test.Client;

namespace NzbDrone.Integration.Test
{
    [TestFixture]
    public class CorsFixture : IntegrationTest
    {
        private SimpleRestRequest BuildGet(string route = "author")
        {
            var request = new SimpleRestRequest(route, HttpMethod.Get);
            request.AddHeader("Origin", "http://a.different.domain");
            request.AddHeader(AccessControlHeaders.RequestMethod, "POST");

            return request;
        }

        private SimpleRestRequest BuildOptions(string route = "author")
        {
            var request = new SimpleRestRequest(route, HttpMethod.Options);
            request.AddHeader("Origin", "http://a.different.domain");
            request.AddHeader(AccessControlHeaders.RequestMethod, "POST");

            return request;
        }

        [Test]
        public void should_not_have_allow_headers_in_response_when_not_included_in_the_request()
        {
            var request = BuildOptions();
            var response = ExecuteRequest(request);

            response.Headers.Should().NotContainKey(AccessControlHeaders.AllowHeaders);
        }

        [Test]
        public void should_have_allow_headers_in_response_when_included_in_the_request()
        {
            var request = BuildOptions();
            request.AddHeader(AccessControlHeaders.RequestHeaders, "X-Test");

            var response = ExecuteRequest(request);

            response.Headers.Should().ContainKey(AccessControlHeaders.AllowHeaders);
        }

        [Test]
        public void should_have_allow_origin_in_response()
        {
            var request = BuildOptions();
            var response = ExecuteRequest(request);

            response.Headers.Should().ContainKey(AccessControlHeaders.AllowOrigin);
        }

        [Test]
        public void should_have_allow_methods_in_response()
        {
            var request = BuildOptions();
            var response = ExecuteRequest(request);

            response.Headers.Should().ContainKey(AccessControlHeaders.AllowMethods);
        }

        [Test]
        public void should_not_have_allow_methods_in_non_options_request()
        {
            var request = BuildGet();
            var response = ExecuteRequest(request);

            response.Headers.Should().NotContainKey(AccessControlHeaders.AllowMethods);
        }

        [Test]
        public void should_have_allow_origin_in_non_options_request()
        {
            var request = BuildGet();
            var response = ExecuteRequest(request);

            response.Headers.Should().ContainKey(AccessControlHeaders.AllowOrigin);
        }

        [Test]
        public void should_not_have_allow_origin_in_non_api_request()
        {
            var request = BuildGet("../abc");
            var response = ExecuteRequest(request);

            response.Headers.Should().NotContainKey(AccessControlHeaders.AllowOrigin);
        }
    }
}
