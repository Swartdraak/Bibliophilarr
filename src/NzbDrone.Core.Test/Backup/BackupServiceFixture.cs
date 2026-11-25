using System;
using System.IO;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common;
using NzbDrone.Common.Disk;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.Backup;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.Backup
{
    [TestFixture]
    public class BackupServiceFixture : TestBase<BackupService>
    {
        [TestCase(false)]
        [TestCase(true)]
        public void should_attempt_to_clean_partial_extraction_without_replacing_the_original_failure(bool failCleanup)
        {
            var archive = Path.Combine(TempFolder, "fixture.zip");
            var extractFailure = new IOException("fixture extraction failure");
            string directory = null;
            Mocker.GetMock<IAppFolderInfo>().SetupGet(x => x.TempFolder).Returns(TempFolder);
            Mocker.GetMock<IArchiveService>().Setup(x => x.Extract(archive, It.IsAny<string>())).Callback((string _, string path) =>
            {
                directory = path;
                File.WriteAllText(Path.Combine(path, "fixture.txt"), "partially extracted fixture");
                throw extractFailure;
            });
            var disk = Mocker.GetMock<IDiskProvider>();
            disk.Setup(x => x.DeleteFolder(It.IsAny<string>(), true)).Callback((string path, bool _) =>
            {
                if (failCleanup)
                {
                    throw new UnauthorizedAccessException("fixture cleanup failure");
                }

                Directory.Delete(path, true);
            });

            try
            {
                Assert.Throws<IOException>(() => Subject.Restore(archive)).Should().BeSameAs(extractFailure);
                if (failCleanup)
                {
                    ExceptionVerification.ExpectedWarns(1, "Unable to clean up a temporary backup restore directory");
                }

                directory.Should().NotBeNull();
                disk.Verify(x => x.DeleteFolder(directory, true), Times.Once());
                Directory.Exists(directory).Should().Be(failCleanup);
            }
            finally
            {
                if (failCleanup && Directory.Exists(directory))
                {
                    Directory.Delete(directory, true);
                }
            }
        }
    }
}
