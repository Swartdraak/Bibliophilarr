using System;
using System.IO;
using System.Net;
using Bibliophilarr.Api.V1.System.Backup;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Core.Backup;
using NzbDrone.Test.Common;

namespace NzbDrone.Api.Test.Backup
{
    [TestFixture]
    public class BackupControllerFixture : TestBase<BackupController>
    {
        [TestCase(true)]
        [TestCase(false)]
        public void should_dispose_input_and_clean_a_partially_written_upload_when_an_attempt_fails(bool failSaving)
        {
            using var upload = new MemoryStream(new byte[] { 1 });
            SetUpload(upload);
            var saveFailure = new IOException("fixture write failure");
            var restoreFailure = new RestoreBackupFailedException(HttpStatusCode.NotFound, "fixture archive rejection");
            string directory = null;
            var disk = Mocker.GetMock<IDiskProvider>();
            disk.Setup(x => x.SaveStream(upload, It.IsAny<string>())).Callback((Stream _, string path) =>
            {
                directory = Path.GetDirectoryName(path);
                File.WriteAllText(path, "partial fixture upload");
                if (failSaving)
                {
                    throw saveFailure;
                }
            });
            disk.Setup(x => x.DeleteFolder(It.IsAny<string>(), true)).Callback((string path, bool _) => Directory.Delete(path, true));
            var backup = Mocker.GetMock<IBackupService>();
            backup.Setup(x => x.Restore(It.IsAny<string>())).Callback((string path) =>
            {
                upload.CanRead.Should().BeFalse();
                File.Exists(path).Should().BeTrue();
                throw restoreFailure;
            });

            Exception failure = failSaving ? saveFailure : restoreFailure;
            Assert.Throws(failure.GetType(), () => Subject.UploadAndRestore()).Should().BeSameAs(failure);

            upload.CanRead.Should().BeFalse();
            directory.Should().NotBeNull();
            disk.Verify(x => x.DeleteFolder(directory, true), Times.Once());
            Directory.Exists(directory).Should().BeFalse();
            backup.Verify(x => x.Restore(It.IsAny<string>()), failSaving ? Times.Never() : Times.Once());
        }

        private void SetUpload(Stream upload)
        {
            var file = new Mock<IFormFile>();
            file.SetupGet(x => x.FileName).Returns("fixture.zip");
            file.Setup(x => x.OpenReadStream()).Returns(upload);
            var context = new DefaultHttpContext();
            context.Request.Form = new FormCollection(null, new FormFileCollection { file.Object });
            Subject.ControllerContext = new ControllerContext { HttpContext = context };
        }
    }
}
