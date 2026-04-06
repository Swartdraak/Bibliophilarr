using System.IO;
using FizzWare.NBuilder;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Core.Books;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Organizer;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.MediaFiles
{
    [TestFixture]
    public class BookFileMovingServiceFormatFixture : CoreTest<BookFileMovingService>
    {
        private Author _author;
        private Edition _edition;
        private BookFile _bookFile;
        private LocalBook _localBook;

        [SetUp]
        public void Setup()
        {
            _author = Builder<Author>.CreateNew()
                .With(a => a.Id = 1)
                .With(a => a.Path = "/media/books/Author Name".AsOsAgnostic())
                .Build();

            var book = Builder<Book>.CreateNew()
                .With(b => b.Author = _author)
                .Build();

            _edition = Builder<Edition>.CreateNew()
                .With(e => e.Book = book)
                .With(e => e.IsEbook = false)
                .Build();

            _bookFile = Builder<BookFile>.CreateNew()
                .With(f => f.Path = "/downloads/book.m4b".AsOsAgnostic())
                .With(f => f.Quality = new QualityModel(Quality.M4B))
                .With(f => f.Edition = _edition)
                .Build();

            _localBook = new LocalBook
            {
                Author = _author,
                Book = book,
                Edition = _edition,
                Path = "/downloads/book.m4b".AsOsAgnostic(),
                Quality = new QualityModel(Quality.M4B)
            };

            // Default: DiskProvider says files exist / folders exist
            Mocker.GetMock<IDiskProvider>()
                .Setup(s => s.FileExists(It.IsAny<string>()))
                .Returns(true);

            Mocker.GetMock<IDiskProvider>()
                .Setup(s => s.FolderExists(It.IsAny<string>()))
                .Returns(true);

            // Default: BuildBookFileName returns a simple name
            Mocker.GetMock<IBuildFileNames>()
                .Setup(s => s.BuildBookFileName(It.IsAny<Author>(), It.IsAny<Edition>(), It.IsAny<BookFile>(), null, null))
                .Returns("Book Title");

            Mocker.GetMock<IBuildFileNames>()
                .Setup(s => s.BuildBookFilePath(It.IsAny<Author>(), It.IsAny<Edition>(), It.IsAny<string>(), It.IsAny<string>()))
                .Returns<Author, Edition, string, string>((a, e, f, ext) =>
                    Path.Combine(a.Path, f + ext));

            Mocker.GetMock<IBuildFileNames>()
                .Setup(s => s.BuildBookPath(It.IsAny<Author>()))
                .Returns<Author>(a => a.Path);

            Mocker.GetMock<IDiskTransferService>()
                .Setup(s => s.TransferFile(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TransferMode>(), It.IsAny<bool>()))
                .Returns(TransferMode.Move);
        }

        [Test]
        public void should_use_format_path_when_flag_on_and_profile_has_root()
        {
            Mocker.GetMock<IConfigService>()
                .Setup(s => s.EnableDualFormatTracking).Returns(true);

            var formatPath = "/media/audiobooks/Author Name".AsOsAgnostic();

            Mocker.GetMock<IBuildAuthorPaths>()
                .Setup(s => s.BuildFormatPath(_author, FormatType.Audiobook))
                .Returns(formatPath);

            Subject.MoveBookFile(_bookFile, _localBook);

            var expectedPath = Path.Combine(formatPath, "Book Title.m4b");

            Mocker.GetMock<IDiskTransferService>()
                .Verify(v => v.TransferFile(
                    It.IsAny<string>(),
                    expectedPath,
                    TransferMode.Move,
                    It.IsAny<bool>()),
                    Times.Once());
        }

        [Test]
        public void should_use_standard_path_when_flag_off()
        {
            Mocker.GetMock<IConfigService>()
                .Setup(s => s.EnableDualFormatTracking).Returns(false);

            Subject.MoveBookFile(_bookFile, _localBook);

            var expectedPath = Path.Combine(_author.Path, "Book Title.m4b");

            Mocker.GetMock<IDiskTransferService>()
                .Verify(v => v.TransferFile(
                    It.IsAny<string>(),
                    expectedPath,
                    TransferMode.Move,
                    It.IsAny<bool>()),
                    Times.Once());
        }

        [Test]
        public void should_use_standard_path_when_flag_on_but_format_path_same_as_author()
        {
            Mocker.GetMock<IConfigService>()
                .Setup(s => s.EnableDualFormatTracking).Returns(true);

            // Format path falls back to author.Path (no profile configured)
            Mocker.GetMock<IBuildAuthorPaths>()
                .Setup(s => s.BuildFormatPath(_author, FormatType.Audiobook))
                .Returns(_author.Path);

            Subject.MoveBookFile(_bookFile, _localBook);

            var expectedPath = Path.Combine(_author.Path, "Book Title.m4b");

            Mocker.GetMock<IDiskTransferService>()
                .Verify(v => v.TransferFile(
                    It.IsAny<string>(),
                    expectedPath,
                    TransferMode.Move,
                    It.IsAny<bool>()),
                    Times.Once());
        }
    }
}
