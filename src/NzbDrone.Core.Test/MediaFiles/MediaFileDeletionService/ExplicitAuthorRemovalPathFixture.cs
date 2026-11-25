using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions;
using System.Net;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Core.Books;
using NzbDrone.Core.Books.Events;
using NzbDrone.Core.Exceptions;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.RootFolders;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MediaFiles.MediaFileDeletionService
{
    [TestFixture]
    public class ExplicitAuthorRemovalPathFixture : CoreTest<Core.MediaFiles.MediaFileDeletionService>
    {
        private Author _author;
        private RootFolder _root;
        private Mock<IDirectoryInfo> _directory;
        private Mock<IDiskProvider> _disk;

        [SetUp]
        public void SetUp()
        {
            _root = new RootFolder { Id = 1, Path = Path.Combine(Path.GetTempPath(), "configured-root-alias") };
            _author = new Author { Id = 2, Path = Path.Combine(_root.Path, "selected-author") };
            var roots = Mocker.GetMock<IRootFolderService>();
            roots.Setup(service => service.GetBestRootFolder(_author.Path)).Returns(_root);
            roots.Setup(service => service.All()).Returns(new List<RootFolder> { _root });
            Mocker.GetMock<IAuthorService>()
                .Setup(service => service.AllAuthorPaths())
                .Returns(new Dictionary<int, string>
                {
                    [_author.Id] = _author.Path,
                    [3] = Path.Combine(_root.Path, "retained-author")
                });

            _disk = Mocker.GetMock<IDiskProvider>();
            _disk.Setup(disk => disk.FolderExists(_root.Path)).Returns(true);
            _disk.Setup(disk => disk.FolderExists(_author.Path)).Returns(true);
            _directory = new Mock<IDirectoryInfo>();
            _directory.SetupGet(directory => directory.Exists).Returns(true);
            _directory.Setup(directory => directory.EnumerateFileSystemInfos("*", It.IsAny<EnumerationOptions>()))
                .Returns(Array.Empty<IFileSystemInfo>());
            _disk.Setup(disk => disk.GetDirectoryInfo(_author.Path)).Returns(_directory.Object);
        }

        [TestCase(true)]
        [TestCase(false)]
        public void should_hold_a_linked_child_without_enumerating_it(bool targetExists)
        {
            var child = new Mock<IDirectoryInfo>();
            child.SetupGet(directory => directory.Exists).Returns(targetExists);
            child.SetupGet(directory => directory.LinkTarget).Returns("synthetic-link-target");
            _directory.Setup(directory => directory.EnumerateFileSystemInfos("*", It.IsAny<EnumerationOptions>()))
                .Returns(new IFileSystemInfo[] { child.Object });

            var error = Assert.Throws<NzbDroneClientException>(() => Subject.ValidateLibraryFolderRemoval(_author));

            Assert.That(error.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
            child.Verify(directory => directory.EnumerateFileSystemInfos(It.IsAny<string>(), It.IsAny<EnumerationOptions>()), Times.Never());
            Mocker.GetMock<IRecycleBinProvider>().Verify(recycle => recycle.DeleteFolder(It.IsAny<string>()), Times.Never());
        }

        [Test]
        public void should_keep_the_existing_recycle_operation_for_an_ordinary_folder()
        {
            Subject.ValidateLibraryFolderRemoval(_author);
            Subject.HandleAsync(new AuthorDeletedEvent(_author, true, false));

            _disk.Verify(disk => disk.GetDirectoryInfo(_root.Path), Times.Never());
            Mocker.GetMock<IRecycleBinProvider>().Verify(recycle => recycle.DeleteFolder(_author.Path), Times.Once());
        }

        [Test]
        public void should_recheck_the_registered_root_before_the_recycle_operation()
        {
            Subject.ValidateLibraryFolderRemoval(_author);
            _disk.Setup(disk => disk.FolderExists(_root.Path)).Returns(false);

            Subject.HandleAsync(new AuthorDeletedEvent(_author, true, false));

            Mocker.GetMock<IRecycleBinProvider>().Verify(recycle => recycle.DeleteFolder(It.IsAny<string>()), Times.Never());
        }
    }
}
