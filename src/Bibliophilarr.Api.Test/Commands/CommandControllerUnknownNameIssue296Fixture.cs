using System;
using System.Linq;
using System.Net;
using System.Text;
using Bibliophilarr.Api.V1.Commands;
using Bibliophilarr.Http.Exceptions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Composition;
using NzbDrone.Core.MediaFiles.BookImport.Manual;
using NzbDrone.Core.MediaFiles.Commands;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Update.Commands;
using NzbDrone.SignalR;
using BadRequestException = Bibliophilarr.Http.REST.BadRequestException;

namespace NzbDrone.Api.Test.Commands
{
    /// <summary>
    /// Issue #296 regression: POST /api/v1/command returned 500 (with an
    /// InvalidOperationException payload, logged at Fatal) for a command name that
    /// matches no registered Command implementation, instead of a 4xx client error.
    ///
    /// Mechanism (source-verified at head 5eac4ee3):
    ///   CommandController.StartCommand resolved the command type via
    ///     _knownTypes.GetImplementations(typeof(Command))
    ///                 .Single(c => c.Name.Replace("Command","").Equals(name, ...))
    ///   Enumerable.Single throws InvalidOperationException ("Sequence contains no
    ///   matching element") when zero implementations match. BibliophilarrErrorPipeline
    ///   maps an InvalidOperationException to the default HttpStatusCode.InternalServerError
    ///   and logs it at Fatal, so a benign client-input condition produced a 500.
    ///
    /// Fix (this branch): StartCommand resolves the implementation with FirstOrDefault
    /// and throws a 400 ApiException (BadRequestException) when the name is unknown,
    /// so the pipeline returns 400 and logs at Warn (matching the sibling
    /// ApplicationUpdateCommand guard and every other 4xx API rejection).
    ///
    /// These fixtures pin the repaired behavior at the controller seam:
    ///   * an unknown command name surfaces a 400 ApiException, never a 500-mapped
    ///     InvalidOperationException (or any non-4xx);
    ///   * the existing ApplicationUpdateCommand guard (400) is preserved;
    ///   * a resolvable command name is NOT rejected by the unknown-name guard (it
    ///     proceeds past type resolution to queue push, proven via a stubbed queue).
    ///
    /// Note: the durable end-to-end acceptance (POST /api/v1/command with an unknown
    /// name returning 400/404, not 5xx, over HTTP) lives in the application-test stage;
    /// the existing NzbDrone.Integration.Test/ApiTests/CommandFixture is [Ignore]d and
    /// requires a Postgres test target, so this unit-level fixture is the durable,
    /// CI-parity regression guard for the error path.
    /// </summary>
    [TestFixture]
    public class CommandControllerUnknownNameIssue296Fixture
    {
        private static KnownTypes BuildKnownTypes(params Type[] commandTypes)
        {
            return new KnownTypes(commandTypes.ToList());
        }

        private static CommandModel Tracked(int id)
        {
            return new CommandModel { Id = id, Body = new TestCommand() };
        }

        // Build a controller whose HttpContext.Request carries the given raw JSON body and
        // a User-Agent header, mirroring the real POST /api/v1/command request shape.
        private static CommandController BuildController(IManageCommandQueue queue, KnownTypes knownTypes, string bodyJson)
        {
            var controller = new CommandController(queue, Mock.Of<IBroadcastSignalRMessage>(), knownTypes);
            var context = new DefaultHttpContext();
            context.Request.Body = new System.IO.MemoryStream(Encoding.UTF8.GetBytes(bodyJson));
            context.Request.Headers["User-Agent"] = "test-agent";
            controller.ControllerContext = new ControllerContext { HttpContext = context };
            return controller;
        }

        // Unknown command names must be rejected with a 400 (BadRequest), never a 500.
        [TestCase("noSuchCommand")]
        [TestCase("rescanFoldersXYZ-does-not-exist")]
        [TestCase("totallyUnknownCommand123")]
        public void unknown_command_name_should_return_400_not_500(string name)
        {
            var controller = BuildController(
                Mock.Of<IManageCommandQueue>(),
                BuildKnownTypes(typeof(RescanFoldersCommand), typeof(ManualImportCommand), typeof(ApplicationUpdateCommand)),
                $"{{ \"Name\": \"{name}\", \"SendUpdatesToClient\": false }}");

            var resource = new CommandResource { Name = name };

            Exception thrown = null;
            try
            {
                controller.StartCommand(resource);
            }
            catch (Exception ex)
            {
                thrown = ex;
            }

            Assert.IsInstanceOf<ApiException>(thrown, $"unknown command name '{name}' must be rejected with an ApiException (4xx), not {(thrown == null ? "no exception" : thrown.GetType().Name)} (issue #296)");
            Assert.AreEqual(HttpStatusCode.BadRequest, ((ApiException)thrown).StatusCode, $"unknown command name '{name}' must map to 400 Bad Request, not 500 (issue #296)");
        }

        // The existing ApplicationUpdateCommand guard must be preserved (400 with the
        // dedicated disabled message): a registered-but-disabled command is still
        // rejected with the dedicated BadRequestException, not the unknown-name guard.
        [Test]
        public void application_update_command_should_still_return_400_with_disabled_message()
        {
            var controller = BuildController(
                Mock.Of<IManageCommandQueue>(),
                BuildKnownTypes(typeof(ApplicationUpdateCommand)),
                "{ \"Name\": \"ApplicationUpdate\", \"SendUpdatesToClient\": false }");

            var resource = new CommandResource { Name = "ApplicationUpdate" };

            Exception thrown = null;
            try
            {
                controller.StartCommand(resource);
            }
            catch (Exception ex)
            {
                thrown = ex;
            }

            Assert.IsInstanceOf<BadRequestException>(thrown, "ApplicationUpdateCommand must still be rejected with the dedicated 400 guard (issue #296 must not regress it)");
            Assert.AreEqual(HttpStatusCode.BadRequest, ((ApiException)thrown).StatusCode);
            StringAssert.Contains("Application updates are disabled", thrown.Message);
        }

        // Lookup-semantics regression guard: a resolvable, non-disabled command name must
        // NOT be rejected by the unknown-name guard. It proceeds past type resolution to
        // queue push; with a stubbed queue it completes with 201 Created, proving the
        // guard only fires for genuinely unknown names and the happy path is unchanged.
        [Test]
        public void resolvable_command_name_should_not_be_rejected_by_unknown_name_guard()
        {
            var queue = Mock.Of<IManageCommandQueue>(q =>
                q.Push<RescanFoldersCommand>(It.IsAny<RescanFoldersCommand>(), It.IsAny<CommandPriority>(), It.IsAny<CommandTrigger>()) == Tracked(7));

            var controller = BuildController(
                queue,
                BuildKnownTypes(typeof(RescanFoldersCommand)),
                "{ \"Name\": \"rescanFolders\", \"SendUpdatesToClient\": false }");

            // "rescanFolders" resolves to RescanFoldersCommand (suffix-stripped, case-insensitive).
            var resource = new CommandResource { Name = "rescanFolders" };

            ActionResult<CommandResource> result = null;
            Exception thrown = null;
            try
            {
                result = controller.StartCommand(resource);
            }
            catch (Exception ex)
            {
                thrown = ex;
            }

            Assert.IsNull(thrown, $"resolvable command name 'rescanFolders' must not be rejected by the unknown-name guard; got {(thrown == null ? "none" : thrown.GetType().Name + ": " + thrown.Message)} (issue #296)");
            Assert.IsInstanceOf<CreatedAtActionResult>(result.Result, "resolvable command name must proceed to queue push and return 201 Created (happy path unchanged)");
        }
    }
}
