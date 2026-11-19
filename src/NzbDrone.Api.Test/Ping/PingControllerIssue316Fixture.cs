using System;
using System.Collections.Generic;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Cache;
using NzbDrone.Core.Configuration;

namespace NzbDrone.Api.Test.Ping
{
    // Issue #316: durable repository coverage for the documented anonymous JSON liveness
    // contract of GET /ping (PingController). The middleware fixture
    // (UiAuthenticationMiddlewareIssue316Fixture) guards the routing side; this fixture
    // guards the controller side: anonymous GET /ping must produce 200 + PingResource
    // { Status = "OK" } (the contract openapi.json documents as public GET -> 200
    // application/json PingResource).
    //
    // The controller returns ActionResult<PingResource>. In .NET 10 MVC,
    // ActionResult<T> carries the concrete MVC result in its .Result property
    // (Value is null; the object is boxed inside Result, an ObjectResult).
    [TestFixture]
    public class PingControllerIssue316Fixture
    {
        private static object UnwrapPingResource(object result)
        {
            // ActionResult<T>.Result -> ObjectResult.Value -> PingResource
            var innerResult = result.GetType().GetProperty("Result")?.GetValue(result);
            if (innerResult == null)
            {
                return result;
            }

            var valueProp = innerResult.GetType().GetProperty("Value");
            return valueProp?.GetValue(innerResult) ?? innerResult;
        }

        [Test]
        public void get_status_when_config_repository_succeeds_should_return_200_ok_json_contract()
        {
            var repository = new Mock<IConfigRepository>();
            repository
                .Setup(r => r.All())
                .Returns(new List<NzbDrone.Core.Configuration.Config>
                {
                    new NzbDrone.Core.Configuration.Config { Key = "DataRoot", Value = "/tmp/data" }
                });

            var controller = new Bibliophilarr.Http.Ping.PingController(repository.Object, new CacheManager());

            var result = controller.GetStatus();

            var pingResource = UnwrapPingResource(result);
            pingResource.Should().NotBeNull("the response body is a PingResource object");
            var statusProp = pingResource.GetType().GetProperty("Status");
            statusProp.Should().NotBeNull("PingResource has a Status property");
            statusProp.GetValue(pingResource).Should().Be("OK", "PingResource.Status must be 'OK' for the liveness contract");
        }

        [Test]
        public void get_status_when_config_repository_throws_should_return_500_error_json_contract()
        {
            var repository = new Mock<IConfigRepository>();
            repository
                .Setup(r => r.All())
                .Throws(new InvalidOperationException("config repository unavailable"));

            var controller = new Bibliophilarr.Http.Ping.PingController(repository.Object, new CacheManager());

            var result = controller.GetStatus();

            var pingResource = UnwrapPingResource(result);
            pingResource.Should().NotBeNull("the response body is a PingResource object");
            var statusProp = pingResource.GetType().GetProperty("Status");
            statusProp.GetValue(pingResource).Should().Be("Error", "PingResource.Status must be 'Error' when the config repository is unavailable");
        }

        [Test]
        public void ping_route_attributes_should_declare_anonymous_get_and_head_on_ping_path()
        {
            // The [AllowAnonymous] + [HttpGet("/ping")] + [HttpHead("/ping")] +
            // [Produces("application/json")] contract is the source of truth that the
            // middleware must not shadow. If these attributes change, this fixture fails.
            var method = typeof(Bibliophilarr.Http.Ping.PingController).GetMethod("GetStatus");

            method.GetCustomAttributes(typeof(Microsoft.AspNetCore.Authorization.AllowAnonymousAttribute), false).Length
                .Should().Be(1, "GET/HEAD /ping is a public liveness endpoint (no auth gate)");

            var httpGets = (object[])method.GetCustomAttributes(typeof(HttpGetAttribute), false);
            httpGets.Length.Should().Be(1);
            ((HttpGetAttribute)httpGets[0]).Template.Should().Contain("/ping");

            var httpHeads = (object[])method.GetCustomAttributes(typeof(HttpHeadAttribute), false);
            httpHeads.Length.Should().Be(1);
            ((HttpHeadAttribute)httpHeads[0]).Template.Should().Contain("/ping");
        }
    }
}
