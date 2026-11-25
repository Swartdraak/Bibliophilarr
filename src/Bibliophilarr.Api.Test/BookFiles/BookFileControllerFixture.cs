using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Bibliophilarr.Api.V1.BookFiles;
using Bibliophilarr.Http.ErrorManagement;
using FluentAssertions;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Moq;
using NLog;
using NUnit.Framework;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Test.Common;

namespace NzbDrone.Api.Test.BookFiles
{
    [TestFixture]
    public class BookFileControllerFixture : TestBase<BookFileController>
    {
        [Test]
        public async Task should_return_bad_request_for_a_missing_file_selection()
        {
            var error = Assert.Catch(() => Subject.GetBookFiles(null, new List<int>(), new List<int>(), null));
            var context = new DefaultHttpContext();
            context.Request.Method = HttpMethods.Get;
            context.Request.Path = "/api/v1/bookfile";
            context.Features.Set<IExceptionHandlerPathFeature>(new ExceptionHandlerFeature { Error = error });
            using var output = new MemoryStream();
            context.Response.Body = output;

            var pipeline = new BibliophilarrErrorPipeline(LogManager.GetCurrentClassLogger());
            await pipeline.HandleException(context);

            context.Response.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
            using var response = JsonDocument.Parse(output.ToArray());
            const string instruction = "authorId, bookId, bookFileIds or unmapped must be provided";
            response.RootElement.GetProperty("message").GetString().Should().Be($"BadRequest: {instruction}");
            response.RootElement.GetProperty("content").GetString().Should().Be(instruction);
            Mocker.GetMock<IMediaFileService>().VerifyNoOtherCalls();
        }

        [Test]
        public void should_still_return_book_files_when_unmapped_is_selected()
        {
            var files = Mocker.GetMock<IMediaFileService>();
            files.Setup(service => service.GetUnmappedFiles()).Returns(new List<BookFile>());

            Subject.GetBookFiles(null, new List<int>(), new List<int>(), true).Should().BeEmpty();

            files.Verify(service => service.GetUnmappedFiles(), Times.Once());
        }
    }
}
