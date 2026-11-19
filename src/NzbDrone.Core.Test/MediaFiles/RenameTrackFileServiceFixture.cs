using System.Collections.Generic;
using System.IO;
using System.Linq;
using FizzWare.NBuilder;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.Commands;
using NzbDrone.Core.MediaFiles.Events;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Organizer;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.MediaFiles
{
    public class RenameTrackFileServiceFixture : CoreTest<RenameBookFileService>
    {
        private Author _author;
        private List<BookFile> _trackFiles;

        [SetUp]
        public void Setup()
        {
            _author = Builder<Author>.CreateNew()
                                     .With(author => author.Path = Path.Combine(Path.GetTempPath(), "author"))
                                     .Build();

            _trackFiles = Builder<BookFile>.CreateListOfSize(2)
                                                .All()
                                                .With(e => e.Author = _author)
                                                .With(e => e.CalibreId = 0)
                                                .Build()
                                                .ToList();

            foreach (var file in _trackFiles)
            {
                var edition = new Edition { Id = file.Id, Book = new Book { Id = file.Id } };
                file.EditionId = edition.Id;
                file.Edition = edition;
                file.Path = Path.Combine(_author.Path, $"original-{file.Id}.epub");

                var newName = $"renamed-{file.Id}";
                Mocker.GetMock<IBuildFileNames>()
                      .Setup(builder => builder.BuildBookFileName(_author, edition, file, null, null))
                      .Returns(newName);
                Mocker.GetMock<IBuildFileNames>()
                      .Setup(builder => builder.BuildBookFilePath(_author, edition, newName, ".epub"))
                      .Returns(Path.Combine(_author.Path, newName + ".epub"));
            }

            Mocker.GetMock<IAuthorService>()
                  .Setup(s => s.GetAuthor(_author.Id))
                  .Returns(_author);

            Mocker.GetMock<IMediaFileService>()
                .Setup(s => s.GetFilesByAuthor(_author.Id))
                .Returns(_trackFiles);
        }

        private void GivenNoTrackFiles()
        {
            Mocker.GetMock<IMediaFileService>()
                  .Setup(s => s.Get(It.IsAny<IEnumerable<int>>()))
                  .Returns(new List<BookFile>());
        }

        private void GivenTrackFiles()
        {
            Mocker.GetMock<IMediaFileService>()
                  .Setup(s => s.Get(It.IsAny<IEnumerable<int>>()))
                  .Returns<IEnumerable<int>>(ids => _trackFiles.Where(file => ids.Contains(file.Id)).ToList());
        }

        private void GivenMovedFiles()
        {
            Mocker.GetMock<IMoveBookFiles>()
                  .Setup(s => s.MoveBookFile(It.IsAny<BookFile>(), _author));
        }

        [Test]
        public void should_not_publish_event_if_no_files_to_rename()
        {
            GivenNoTrackFiles();

            Subject.Execute(new RenameFilesCommand(_author.Id, _trackFiles.Select(file => file.Id).ToList()));

            Mocker.GetMock<IEventAggregator>()
                  .Verify(v => v.PublishEvent(It.IsAny<AuthorRenamedEvent>()), Times.Never());
        }

        [Test]
        public void should_not_publish_event_if_no_files_are_renamed()
        {
            GivenTrackFiles();

            Mocker.GetMock<IMoveBookFiles>()
                  .Setup(s => s.MoveBookFile(It.IsAny<BookFile>(), It.IsAny<Author>()))
                  .Throws(new SameFilenameException("Same file name", "Filename"));

            Subject.Execute(new RenameFilesCommand(_author.Id, _trackFiles.Select(file => file.Id).ToList()));

            Mocker.GetMock<IEventAggregator>()
                  .Verify(v => v.PublishEvent(It.IsAny<AuthorRenamedEvent>()), Times.Never());
        }

        [Test]
        public void should_publish_event_if_files_are_renamed()
        {
            GivenTrackFiles();
            GivenMovedFiles();

            Subject.Execute(new RenameFilesCommand(_author.Id, _trackFiles.Select(file => file.Id).ToList()));

            Mocker.GetMock<IEventAggregator>()
                  .Verify(v => v.PublishEvent(It.IsAny<AuthorRenamedEvent>()), Times.Once());
        }

        [Test]
        public void should_update_moved_files()
        {
            GivenTrackFiles();
            GivenMovedFiles();

            Subject.Execute(new RenameFilesCommand(_author.Id, _trackFiles.Select(file => file.Id).ToList()));

            Mocker.GetMock<IMediaFileService>()
                  .Verify(v => v.Update(It.IsAny<BookFile>()), Times.Exactly(2));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void should_skip_unlinked_files_in_preview_and_rename(bool useEditionWrapper)
        {
            var unlinked = _trackFiles[0];
            unlinked.EditionId = 0;
            unlinked.Edition = useEditionWrapper ? new LazyLoaded<Edition>(null) : null;
            GivenTrackFiles();
            GivenMovedFiles();

            var previews = Subject.GetRenamePreviews(_author.Id);
            Assert.That(previews.Select(preview => preview.BookFileId), Is.EqualTo(new[] { _trackFiles[1].Id }));

            Subject.Execute(new RenameFilesCommand(_author.Id, _trackFiles.Select(file => file.Id).ToList()));

            Mocker.GetMock<IMoveBookFiles>()
                  .Verify(mover => mover.MoveBookFile(unlinked, _author), Times.Never());
            Mocker.GetMock<IMoveBookFiles>()
                  .Verify(mover => mover.MoveBookFile(_trackFiles[1], _author), Times.Once());
            ExceptionVerification.ExpectedWarns(2);
        }

        [Test]
        public void should_get_trackfiles_by_ids_only()
        {
            GivenTrackFiles();
            GivenMovedFiles();

            var files = new List<int> { _trackFiles[1].Id };

            Subject.Execute(new RenameFilesCommand(_author.Id, files));

            Mocker.GetMock<IMediaFileService>()
                  .Verify(v => v.Get(files), Times.Once());
        }
    }
}
