using System.Collections.Generic;
using System.IO;
using FizzWare.NBuilder;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Core.Books;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.RootFolders;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.MediaFiles
{
    public class UpgradeMediaFileServiceFixture : CoreTest<UpgradeMediaFileService>
    {
        private BookFile _trackFile;
        private LocalBook _localTrack;
        private string _rootPath = @"C:\Test\Music\Author".AsOsAgnostic();

        [SetUp]
        public void Setup()
        {
            _localTrack = new LocalBook();
            _localTrack.Author = new Author
            {
                Path = _rootPath
            };

            _trackFile = Builder<BookFile>
                .CreateNew()
                .Build();

            Mocker.GetMock<IDiskProvider>()
                .Setup(c => c.FileExists(It.IsAny<string>()))
                .Returns(true);

            Mocker.GetMock<IDiskProvider>()
                .Setup(c => c.FolderExists(It.IsAny<string>()))
                .Returns(true);

            Mocker.GetMock<IDiskProvider>()
                 .Setup(c => c.GetParentFolder(It.IsAny<string>()))
                 .Returns<string>(c => Path.GetDirectoryName(c));

            Mocker.GetMock<IRootFolderService>()
                .Setup(c => c.GetBestRootFolder(It.IsAny<string>()))
                .Returns(new RootFolder());
        }

        private void GivenSingleTrackWithSingleTrackFile()
        {
            _localTrack.Book = Builder<Book>.CreateNew()
                .With(e => e.BookFiles = new LazyLoaded<List<BookFile>>(
                          new List<BookFile>
                          {
                              new BookFile
                              {
                                  Id = 1,
                                  Path = Path.Combine(_rootPath, @"Season 01\30.rock.s01e01.avi"),
                              }
                          }))
                .Build();
        }

        [Test]
        public void should_preserve_library_file_when_copying_from_a_different_path_with_replacement_disabled()
        {
            GivenSingleTrackWithSingleTrackFile();
            var existing = _localTrack.Book.BookFiles.Value[0];
            existing.Quality = new QualityModel(Quality.EPUB);
            existing.EditionId = 17;
            existing.Part = 1;
            _trackFile.Quality = new QualityModel(Quality.EPUB);
            _trackFile.EditionId = 17;
            _trackFile.Part = 1;

            _localTrack.Book.BookFiles.Value.Insert(0, new BookFile
            {
                Id = 2,
                EditionId = 17,
                Part = 2,
                Quality = new QualityModel(Quality.EPUB)
            });
            _trackFile.Path = Path.Combine(_rootPath, "incoming", "book.epub");
            _localTrack.Path = _trackFile.Path;
            Mocker.GetMock<IConfigService>().SetupGet(c => c.EnableDualFormatTracking).Returns(true);

            var result = Subject.UpgradeBookFile(_trackFile, _localTrack, copyOnly: true, replaceExisting: false);

            result.PreservedFile.Should().BeSameAs(existing);
            result.OldFiles.Should().BeEmpty();
            Mocker.GetMock<IRecycleBinProvider>().Verify(v => v.DeleteFile(It.IsAny<string>(), It.IsAny<string>()), Times.Never());
            Mocker.GetMock<IMediaFileService>().Verify(v => v.Delete(It.IsAny<BookFile>(), It.IsAny<DeleteMediaFileReason>()), Times.Never());
            Mocker.GetMock<IMoveBookFiles>().Verify(v => v.CopyBookFile(It.IsAny<BookFile>(), It.IsAny<LocalBook>()), Times.Never());
        }

        [Test]
        public void should_copy_the_other_format_when_replacement_is_disabled()
        {
            GivenSingleTrackWithSingleTrackFile();
            _localTrack.Book.BookFiles.Value[0].Quality = new QualityModel(Quality.EPUB);
            _trackFile.Quality = new QualityModel(Quality.MP3);
            Mocker.GetMock<IConfigService>().SetupGet(c => c.EnableDualFormatTracking).Returns(true);

            var result = Subject.UpgradeBookFile(_trackFile, _localTrack, copyOnly: true, replaceExisting: false);

            result.PreservedFile.Should().BeNull();
            result.OldFiles.Should().BeEmpty();
            Mocker.GetMock<IMoveBookFiles>().Verify(v => v.CopyBookFile(_trackFile, _localTrack, false), Times.Once());
            Mocker.GetMock<IRecycleBinProvider>().Verify(v => v.DeleteFile(It.IsAny<string>(), It.IsAny<string>()), Times.Never());
        }

        [TestCase(17, 2)]
        [TestCase(18, 1)]
        public void should_copy_a_nonconflicting_part_or_edition_without_deleting_a_tracked_file(int editionId, int part)
        {
            GivenSingleTrackWithSingleTrackFile();
            var existing = _localTrack.Book.BookFiles.Value[0];
            existing.EditionId = 17;
            existing.Part = 1;
            existing.Quality = new QualityModel(Quality.MP3);
            _trackFile.EditionId = editionId;
            _trackFile.Part = part;
            _trackFile.Quality = new QualityModel(Quality.MP3);
            Mocker.GetMock<IConfigService>().SetupGet(c => c.EnableDualFormatTracking).Returns(true);
            Mocker.GetMock<IMoveBookFiles>().Setup(v => v.CopyBookFile(_trackFile, _localTrack, false)).Returns(_trackFile);

            var result = Subject.UpgradeBookFile(_trackFile, _localTrack, copyOnly: true, replaceExisting: false);

            result.PreservedFile.Should().BeNull();
            result.BookFile.Should().BeSameAs(_trackFile);
            result.OldFiles.Should().BeEmpty();
            Mocker.GetMock<IRecycleBinProvider>().Verify(v => v.DeleteFile(It.IsAny<string>(), It.IsAny<string>()), Times.Never());
            Mocker.GetMock<IMediaFileService>().Verify(v => v.Delete(It.IsAny<BookFile>(), It.IsAny<DeleteMediaFileReason>()), Times.Never());
        }

        [Test]
        public void should_delete_single_track_file_once()
        {
            GivenSingleTrackWithSingleTrackFile();

            Subject.UpgradeBookFile(_trackFile, _localTrack);

            Mocker.GetMock<IRecycleBinProvider>().Verify(v => v.DeleteFile(It.IsAny<string>(), It.IsAny<string>()), Times.Once());
        }

        [Test]
        public void should_delete_track_file_from_database()
        {
            GivenSingleTrackWithSingleTrackFile();

            Subject.UpgradeBookFile(_trackFile, _localTrack);

            Mocker.GetMock<IMediaFileService>().Verify(v => v.Delete(It.IsAny<BookFile>(), DeleteMediaFileReason.Upgrade), Times.Once());
        }

        [Test]
        public void should_delete_existing_file_fromdb_if_file_doesnt_exist()
        {
            GivenSingleTrackWithSingleTrackFile();

            Mocker.GetMock<IDiskProvider>()
                .Setup(c => c.FileExists(It.IsAny<string>()))
                .Returns(false);

            Subject.UpgradeBookFile(_trackFile, _localTrack);

            // Mocker.GetMock<IMediaFileService>().Verify(v => v.Delete(_localTrack.Book.BookFiles.Value, DeleteMediaFileReason.Upgrade), Times.Once());
        }

        [Test]
        public void should_not_try_to_recyclebin_existing_file_if_file_doesnt_exist()
        {
            GivenSingleTrackWithSingleTrackFile();

            Mocker.GetMock<IDiskProvider>()
                .Setup(c => c.FileExists(It.IsAny<string>()))
                .Returns(false);

            Subject.UpgradeBookFile(_trackFile, _localTrack);

            Mocker.GetMock<IRecycleBinProvider>().Verify(v => v.DeleteFile(It.IsAny<string>(), It.IsAny<string>()), Times.Never());
        }

        [Test]
        public void should_return_old_track_file_in_oldFiles()
        {
            GivenSingleTrackWithSingleTrackFile();

            Subject.UpgradeBookFile(_trackFile, _localTrack).OldFiles.Count.Should().Be(1);
        }

        [Test]
        [Ignore("Pending bibliophilarr fix")]
        public void should_import_if_existing_file_doesnt_exist_in_db()
        {
            _localTrack.Book = Builder<Book>.CreateNew()
                .With(e => e.BookFiles = new LazyLoaded<List<BookFile>>())
                .Build();

            Subject.UpgradeBookFile(_trackFile, _localTrack);

            // Mocker.GetMock<IMediaFileService>().Verify(v => v.Delete(_localTrack.Book.BookFiles.Value, It.IsAny<DeleteMediaFileReason>()), Times.Never());
        }
    }
}
