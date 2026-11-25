using System;
using System.Collections.Generic;
using Bibliophilarr.Http.Ping;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
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
    [TestFixture]
    public class PingControllerIssue316Fixture
    {
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

            var controller = new PingController(repository.Object, new CacheManager());

            var result = controller.GetStatus();

            var response = result.Result.Should().BeOfType<ObjectResult>().Which;
            response.StatusCode.Should().Be(StatusCodes.Status200OK);
            response.Value.Should().BeOfType<PingResource>().Which.Status.Should().Be("OK");
            repository.Verify(config => config.All(), Times.Once);
        }

        [Test]
        public void get_status_when_config_repository_throws_should_return_500_error_json_contract()
        {
            var repository = new Mock<IConfigRepository>();
            repository
                .Setup(r => r.All())
                .Throws(new InvalidOperationException("config repository unavailable"));

            var controller = new PingController(repository.Object, new CacheManager());

            var result = controller.GetStatus();

            var response = result.Result.Should().BeOfType<ObjectResult>().Which;
            response.StatusCode.Should().Be(StatusCodes.Status500InternalServerError);
            response.Value.Should().BeOfType<PingResource>().Which.Status.Should().Be("Error");
            repository.Verify(config => config.All(), Times.Once);
        }

        [Test]
        public void ping_route_attributes_should_declare_anonymous_get_and_head_on_ping_path()
        {
            // The [AllowAnonymous] + [HttpGet("/ping")] + [HttpHead("/ping")] +
            // [Produces("application/json")] contract is the source of truth that the
            // middleware must not shadow. If these attributes change, this fixture fails.
            var method = typeof(PingController).GetMethod(nameof(PingController.GetStatus));

            method.GetCustomAttributes(typeof(Microsoft.AspNetCore.Authorization.AllowAnonymousAttribute), false).Length
                .Should().Be(1, "GET/HEAD /ping is a public liveness endpoint (no auth gate)");

            var httpGets = (object[])method.GetCustomAttributes(typeof(HttpGetAttribute), false);
            httpGets.Length.Should().Be(1);
            ((HttpGetAttribute)httpGets[0]).Template.Should().Be("/ping");

            var httpHeads = (object[])method.GetCustomAttributes(typeof(HttpHeadAttribute), false);
            httpHeads.Length.Should().Be(1);
            ((HttpHeadAttribute)httpHeads[0]).Template.Should().Be("/ping");

            var produces = method.GetCustomAttributes(typeof(ProducesAttribute), false);
            produces.Should().ContainSingle().Which.Should().BeOfType<ProducesAttribute>()
                .Which.ContentTypes.Should().Equal("application/json");
        }
    }
}
