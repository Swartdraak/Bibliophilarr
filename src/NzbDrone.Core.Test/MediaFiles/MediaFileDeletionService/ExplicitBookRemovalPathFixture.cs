using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions;
using System.Net;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Core.Books;
using NzbDrone.Core.Books.Calibre;
using NzbDrone.Core.Exceptions;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.RootFolders;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MediaFiles.MediaFileDeletionService
{
    [TestFixture]
    public class ExplicitBookRemovalPathFixture : CoreTest<Core.MediaFiles.MediaFileDeletionService>
    {
        private const int BookId = 11;
        private RootFolder _root;
        private string _parent;
        private string _grandparent;
        private BookFile _file;
        private Mock<IDiskProvider> _disk;

        [SetUp]
        public void SetUp()
        {
            _root = new RootFolder { Id = 1, Path = Path.Combine(Path.GetTempPath(), "registered-root-alias") };
            _grandparent = Path.Combine(_root.Path, "author");
            _parent = Path.Combine(_grandparent, "work");
            _file = new BookFile
            {
                Id = 2,
                Path = Path.Combine(_parent, "selected.epub"),
                EditionId = 3,
                Edition = new Edition { Id = 3, BookId = BookId },
                Size = 12,
                Modified = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            };

            Mocker.GetMock<IRootFolderService>()
                .Setup(service => service.GetBestRootFolder(It.IsAny<string>()))
                .Returns(_root);
            Mocker.GetMock<IMediaFileService>()
                .Setup(service => service.GetFilesByBook(BookId))
                .Returns(new List<BookFile> { _file });
            Mocker.GetMock<IMediaFileService>()
                .Setup(service => service.GetFileWithPath(It.IsAny<string>()))
                .Returns(_file);
            Mocker.GetMock<IMediaFileRepository>()
                .Setup(repository => repository.DeleteMatchingFile(_file))
                .Returns(true);

            _disk = Mocker.GetMock<IDiskProvider>();
            _disk.Setup(disk => disk.FolderExists(_root.Path)).Returns(true);
            _disk.Setup(disk => disk.GetDirectoryInfo(_root.Path))
                .Returns(Mock.Of<IDirectoryInfo>(directory => directory.Exists && directory.LinkTarget == "configured-target"));
            _disk.Setup(disk => disk.GetDirectoryInfo(_grandparent)).Returns(Mock.Of<IDirectoryInfo>(directory => directory.Exists));
            _disk.Setup(disk => disk.GetDirectoryInfo(_parent)).Returns(Mock.Of<IDirectoryInfo>(directory => directory.Exists));
            _disk.Setup(disk => disk.GetFileInfo(It.IsAny<string>()))
                .Returns(Mock.Of<IFileInfo>(file => file.Exists && file.Length == _file.Size && file.LastWriteTimeUtc == _file.Modified));
        }

        [TestCase(true)]
        [TestCase(false)]
        public void should_hold_a_linked_ancestor_before_any_effect(bool linkTargetExists)
        {
            _disk.Setup(disk => disk.GetDirectoryInfo(_grandparent))
                .Returns(Mock.Of<IDirectoryInfo>(directory => directory.Exists == linkTargetExists && directory.LinkTarget == "synthetic-target"));

            AssertHeld();
        }

        [TestCase(true)]
        [TestCase(false)]
        public void should_hold_a_linked_file_before_any_effect(bool linkTargetExists)
        {
            _disk.Setup(disk => disk.GetFileInfo(_file.Path))
                .Returns(Mock.Of<IFileInfo>(file => file.Exists == linkTargetExists && file.LinkTarget == "synthetic-target"));

            AssertHeld();
        }

        [Test]
        public void should_hold_a_nested_reparse_point_before_any_effect()
        {
            _disk.Setup(disk => disk.GetDirectoryInfo(_parent))
                .Returns(Mock.Of<IDirectoryInfo>(directory => directory.Exists && directory.Attributes == FileAttributes.ReparsePoint));

            AssertHeld();
        }

        [Test]
        public void should_hold_parent_segments_even_if_the_normalized_path_is_inside_the_root()
        {
            _file.Path = Path.Combine(_parent, "..", "selected.epub");

            AssertHeld();
        }

        [Test]
        public void should_hold_when_the_registered_root_exists_but_the_tracked_file_is_missing()
        {
            _disk.Setup(disk => disk.GetFileInfo(_file.Path)).Returns(Mock.Of<IFileInfo>());

            AssertHeld();
        }

        [Test]
        public void should_hold_when_the_tracked_file_becomes_unavailable_before_the_effect()
        {
            _disk.Setup(disk => disk.FileExists(_file.Path)).Returns(false);

            AssertHeld();
        }

        [Test]
        public void should_allow_a_directly_registered_root_alias()
        {
            _disk.SetupSequence(disk => disk.FileExists(_file.Path)).Returns(true).Returns(false);

            Subject.DeleteFilesForExplicitBookRemoval(BookId);

            _disk.Verify(disk => disk.GetDirectoryInfo(_root.Path), Times.Never());
            Mocker.GetMock<IRecycleBinProvider>().Verify(
                recycle => recycle.DeleteFile(_file.Path, It.IsAny<string>()), Times.Once());
            Mocker.GetMock<IMediaFileRepository>().Verify(
                repository => repository.DeleteMatchingFile(_file), Times.Once());
        }

        private void AssertHeld()
        {
            var error = Assert.Throws<NzbDroneClientException>(() => Subject.DeleteFilesForExplicitBookRemoval(BookId));
            Assert.That(error.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
            Mocker.GetMock<IRecycleBinProvider>().Verify(
                recycle => recycle.DeleteFile(It.IsAny<string>(), It.IsAny<string>()), Times.Never());
            Mocker.GetMock<ICalibreProxy>().Verify(
                calibre => calibre.DeleteBook(It.IsAny<BookFile>(), It.IsAny<CalibreSettings>()), Times.Never());
            Mocker.GetMock<IMediaFileRepository>().Verify(
                repository => repository.DeleteMatchingFile(It.IsAny<BookFile>()), Times.Never());
        }
    }
}
