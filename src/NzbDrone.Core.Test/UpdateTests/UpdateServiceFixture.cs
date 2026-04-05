using System;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Common;
using NzbDrone.Common.Disk;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Common.Http;
using NzbDrone.Common.Model;
using NzbDrone.Common.Processes;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Core.Update;
using NzbDrone.Core.Update.Commands;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.UpdateTests
{
    [TestFixture]
    public class UpdateServiceFixture : CoreTest<InstallUpdateService>
    {
        [SetUp]
        public void Setup()
        {
            Mocker.GetMock<IAppFolderInfo>().SetupGet(c => c.TempFolder).Returns(TempFolder);
            Mocker.GetMock<IProcessProvider>().Setup(c => c.GetCurrentProcess()).Returns(new ProcessInfo { Id = 12 });
        }

        [Test]
        public void should_throw_when_application_updates_are_disabled()
        {
            Action act = () => Subject.Execute(new ApplicationUpdateCommand());

            act.Should().Throw<CommandFailedException>()
                .WithMessage("Application updates are disabled until release pipeline support is implemented.");
        }

        [Test]
        public void should_not_attempt_to_download_extract_or_start_when_updates_are_disabled()
        {
            Action act = () => Subject.Execute(new ApplicationUpdateCommand());

            act.Should().Throw<CommandFailedException>();

            Mocker.GetMock<ICheckUpdateService>().Verify(c => c.AvailableUpdate(), Moq.Times.Never());
            Mocker.GetMock<IDiskProvider>().Verify(c => c.DeleteFolder(Moq.It.IsAny<string>(), true), Moq.Times.Never());
            Mocker.GetMock<IHttpClient>().Verify(c => c.DownloadFile(Moq.It.IsAny<string>(), Moq.It.IsAny<string>(), null), Moq.Times.Never());
            Mocker.GetMock<IArchiveService>().Verify(c => c.Extract(Moq.It.IsAny<string>(), Moq.It.IsAny<string>()), Moq.Times.Never());
            Mocker.GetMock<IDiskTransferService>().Verify(c => c.TransferFolder(Moq.It.IsAny<string>(), Moq.It.IsAny<string>(), Moq.It.IsAny<TransferMode>()), Moq.Times.Never());
            Mocker.GetMock<IProcessProvider>().Verify(c => c.Start(Moq.It.IsAny<string>(), Moq.It.IsAny<string>(), null, null, null), Moq.Times.Never());
        }

        [TearDown]
        public void TearDown()
        {
            ExceptionVerification.IgnoreErrors();
        }
    }
}
