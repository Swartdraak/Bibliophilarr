using System;
using System.Data.SQLite;
using System.IO;
using System.Net;
using System.Threading.Tasks;
using Bibliophilarr.Http.ErrorManagement;
using Bibliophilarr.Http.Middleware;
using FluentAssertions;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using NLog;
using NLog.Config;
using NLog.Targets;
using NUnit.Framework;

namespace NzbDrone.Api.Test.Middleware
{
    [TestFixture]
    [NonParallelizable]
    public class HttpLogBoundaryFixture
    {
        private const string FixtureLogger = "HttpLogBoundaryFixture";
        private LoggingConfiguration _originalConfiguration;
        private MemoryTarget _target;

        [SetUp]
        public void SetUp()
        {
            _originalConfiguration = LogManager.Configuration;
            _target = new MemoryTarget("http-boundary") { Layout = "${logger}|${message}" };
            var configuration = new LoggingConfiguration();
            configuration.AddTarget(_target);
            configuration.AddRule(LogLevel.Trace, LogLevel.Fatal, _target, "Http");
            configuration.AddRule(LogLevel.Debug, LogLevel.Fatal, _target, "Api");
            configuration.AddRule(LogLevel.Error, LogLevel.Fatal, _target, FixtureLogger);
            LogManager.Configuration = configuration;
        }

        [TearDown]
        public void TearDown()
        {
            LogManager.Configuration = _originalConfiguration;
        }

        [Test]
        public async Task should_normalize_formatting_only_in_the_rendered_request_records()
        {
            const string method = "GET\t";
            var context = new DefaultHttpContext();
            context.Connection.RemoteIpAddress = IPAddress.Loopback;
            context.Request.Method = method;
            context.Request.Path = "/api/v1/fixture";
            context.Request.Headers["User-Agent"] = "FixtureClient/1.0\t(comment)";

            var middleware = new LoggingMiddleware(request =>
            {
                request.Request.Method.Should().Be(method);
                request.Response.StatusCode = StatusCodes.Status204NoContent;
                return Task.CompletedTask;
            }, null);

            await middleware.InvokeAsync(context);

            context.Response.StatusCode.Should().Be(StatusCodes.Status204NoContent);
            _target.Logs.Should().HaveCount(3).And.OnlyContain(line => !line.Contains('\t'));
            _target.Logs.Should().ContainSingle(line =>
                line.StartsWith("Http|Req:", StringComparison.Ordinal) &&
                line.Contains("[GET ]", StringComparison.Ordinal) &&
                line.Contains("FixtureClient/1.0 (comment)", StringComparison.Ordinal));
            _target.Logs.Should().ContainSingle(line =>
                line.StartsWith("Http|Res:", StringComparison.Ordinal) &&
                line.Contains("[GET ] /api/v1/fixture: 204", StringComparison.Ordinal));
            _target.Logs.Should().ContainSingle(line =>
                line.StartsWith("Api|[GET ] /api/v1/fixture: 204", StringComparison.Ordinal));
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task should_normalize_the_logged_method_without_changing_the_error_response(bool databaseError)
        {
            var context = new DefaultHttpContext();
            context.Request.Method = "GET\t";
            context.Request.Path = "/api/v1/fixture";
            context.Features.Set<IExceptionHandlerPathFeature>(new ExceptionHandlerFeature
            {
                Error = databaseError ? new SQLiteException("Fixture error.") : new InvalidOperationException("Fixture error.")
            });
            using var responseBody = new MemoryStream();
            context.Response.Body = responseBody;
            var pipeline = new BibliophilarrErrorPipeline(LogManager.GetLogger(FixtureLogger));

            await pipeline.HandleException(context);

            context.Request.Method.Should().Be("GET\t");
            context.Response.StatusCode.Should().Be(StatusCodes.Status500InternalServerError);
            context.Response.ContentType.Should().Be("application/json");
            responseBody.Length.Should().BeGreaterThan(0);
            _target.Logs.Should().ContainSingle(line =>
                line.Contains("GET ", StringComparison.Ordinal) && !line.Contains('\t'));
        }
    }
}
