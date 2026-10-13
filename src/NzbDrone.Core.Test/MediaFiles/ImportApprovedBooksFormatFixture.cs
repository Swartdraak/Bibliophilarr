using System.Collections.Generic;
using System.IO;
using System.Linq;
using FizzWare.NBuilder;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.BookImport;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Profiles.Qualities;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.RootFolders;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.MediaFiles
{
    [TestFixture]
    public class ImportApprovedBooksFormatFixture : CoreTest<ImportApprovedBooks>
    {
        private List<ImportDecision<LocalBook>> _approvedDecisions;
        private Author _author;
        private Book _book;
        private Edition _ebookEdition;
        private Edition _audiobookEdition;

        [SetUp]
        public void Setup()
        {
            _approvedDecisions = new List<ImportDecision<LocalBook>>();

            _author = Builder<Author>.CreateNew()
                .With(e => e.QualityProfile = new QualityProfile { Items = Qualities.QualityFixture.GetDefaultQualities() })
                .With(s => s.Path = @"C:\Test\Music\Author Name".AsOsAgnostic())
                .Build();

            _book = Builder<Book>.CreateNew()
                .With(e => e.Author = _author)
                .Build();

            _ebookEdition = Builder<Edition>.CreateNew()
                .With(e => e.Id = 10)
                .With(e => e.Book = _book)
                .With(e => e.BookId = _book.Id)
                .With(e => e.Monitored = true)
                .With(e => e.IsEbook = true)
                .Build();

            _audiobookEdition = Builder<Edition>.CreateNew()
                .With(e => e.Id = 20)
                .With(e => e.Book = _book)
                .With(e => e.BookId = _book.Id)
                .With(e => e.Monitored = true)
                .With(e => e.IsEbook = false)
                .Build();

            _book.Editions = new List<Edition> { _ebookEdition, _audiobookEdition };

            var rootFolder = Builder<RootFolder>.CreateNew()
                .With(r => r.IsCalibreLibrary = false)
                .Build();

            Mocker.GetMock<IUpgradeMediaFiles>()
                .Setup(s => s.UpgradeBookFile(It.IsAny<BookFile>(), It.IsAny<LocalBook>(), It.IsAny<bool>()))
                .Returns(new BookFileMoveResult());

            Mocker.GetMock<IMediaFileService>()
                .Setup(s => s.GetFilesByBook(It.IsAny<int>()))
                .Returns(new List<BookFile>());

            Mocker.GetMock<IRootFolderService>()
                .Setup(s => s.GetBestRootFolder(It.IsAny<string>()))
                .Returns(rootFolder);
        }

        private void GivenAudiobookImport()
        {
            _approvedDecisions.Clear();
            _approvedDecisions.Add(new ImportDecision<LocalBook>(
                new LocalBook
                {
                    Author = _author,
                    Book = _book,
                    Edition = _audiobookEdition,
                    Part = 1,
                    Path = Path.Combine(_author.Path, "Book Title - Part 01.m4b"),
                    Quality = new QualityModel(Quality.M4B),
                    FileTrackInfo = new ParsedTrackInfo { ReleaseGroup = "TEST" }
                }));

            Mocker.GetMock<IEditionService>()
                .Setup(s => s.SetMonitoredByFormat(_audiobookEdition))
                .Returns(new List<Edition> { _ebookEdition, _audiobookEdition });

            Mocker.GetMock<IEditionService>()
                .Setup(s => s.SetMonitored(_audiobookEdition))
                .Returns(new List<Edition> { _ebookEdition, _audiobookEdition });
        }

        private void GivenEbookImport()
        {
            _approvedDecisions.Clear();
            _approvedDecisions.Add(new ImportDecision<LocalBook>(
                new LocalBook
                {
                    Author = _author,
                    Book = _book,
                    Edition = _ebookEdition,
                    Part = 1,
                    Path = Path.Combine(_author.Path, "Book Title.epub"),
                    Quality = new QualityModel(Quality.EPUB),
                    FileTrackInfo = new ParsedTrackInfo { ReleaseGroup = "TEST" }
                }));

            Mocker.GetMock<IEditionService>()
                .Setup(s => s.SetMonitoredByFormat(_ebookEdition))
                .Returns(new List<Edition> { _ebookEdition, _audiobookEdition });

            Mocker.GetMock<IEditionService>()
                .Setup(s => s.SetMonitored(_ebookEdition))
                .Returns(new List<Edition> { _ebookEdition, _audiobookEdition });
        }

        [Test]
        public void should_use_SetMonitoredByFormat_when_flag_on_and_audiobook()
        {
            Mocker.GetMock<IConfigService>()
                .Setup(s => s.EnableDualFormatTracking).Returns(true);

            GivenAudiobookImport();

            Subject.Import(_approvedDecisions, false);

            Mocker.GetMock<IEditionService>()
                .Verify(v => v.SetMonitoredByFormat(_audiobookEdition), Times.Once());

            Mocker.GetMock<IEditionService>()
                .Verify(v => v.SetMonitored(It.IsAny<Edition>()), Times.Never());
        }

        [Test]
        public void should_use_SetMonitoredByFormat_when_flag_on_and_ebook()
        {
            Mocker.GetMock<IConfigService>()
                .Setup(s => s.EnableDualFormatTracking).Returns(true);

            GivenEbookImport();

            Subject.Import(_approvedDecisions, false);

            Mocker.GetMock<IEditionService>()
                .Verify(v => v.SetMonitoredByFormat(_ebookEdition), Times.Once());

            Mocker.GetMock<IEditionService>()
                .Verify(v => v.SetMonitored(It.IsAny<Edition>()), Times.Never());
        }

        [Test]
        public void should_use_SetMonitored_when_flag_off()
        {
            Mocker.GetMock<IConfigService>()
                .Setup(s => s.EnableDualFormatTracking).Returns(false);

            GivenAudiobookImport();

            Subject.Import(_approvedDecisions, false);

            Mocker.GetMock<IEditionService>()
                .Verify(v => v.SetMonitored(_audiobookEdition), Times.Once());

            Mocker.GetMock<IEditionService>()
                .Verify(v => v.SetMonitoredByFormat(It.IsAny<Edition>()), Times.Never());
        }

        [Test]
        public void should_use_SetMonitored_when_flag_off_ebook()
        {
            Mocker.GetMock<IConfigService>()
                .Setup(s => s.EnableDualFormatTracking).Returns(false);

            GivenEbookImport();

            Subject.Import(_approvedDecisions, false);

            Mocker.GetMock<IEditionService>()
                .Verify(v => v.SetMonitored(_ebookEdition), Times.Once());
            Mocker.GetMock<IEditionService>()
                .Verify(v => v.SetMonitoredByFormat(It.IsAny<Edition>()), Times.Never());
        }

        [Test]
        public void should_not_silently_drop_a_book_when_author_monitor_new_items_is_out_of_range()
        {
            Mocker.GetMock<IConfigService>()
                .Setup(s => s.EnableDualFormatTracking).Returns(false);

            // Regression (issue #256): a corrupted AuthorFormatProfiles/Authors
            // MonitorNewItems value (e.g. 3) used to make MonitorNewBookService throw
            // NotImplementedException. ImportApprovedBooks.EnsureBookAdded caught and logged
            // that exception and rejected the book, so the import reported 100% match rate
            // yet persisted no books. The service now falls back to a safe default, so the
            // import must complete without throwing and without rejecting the book.
            _author.MonitorNewItems = (NewItemMonitorTypes)3;
            _book.Id = 0;
            _book.ForeignBookId = "test-foreign-book-id";

            Mocker.GetMock<IBookService>()
                .Setup(s => s.FindById(It.IsAny<string>()))
                .Returns((Book)null);
            Mocker.GetMock<IBookService>()
                .Setup(s => s.GetBooksByAuthor(It.IsAny<int>()))
                .Returns(new List<Book>());
            Mocker.GetMock<IBookService>()
                .Setup(s => s.InsertMany(It.IsAny<List<Book>>()))
                .Callback<List<Book>>(b => { if (b != null && b.Count > 0) b[0].Id = 1000; });

            GivenEbookImport();

            var results = Subject.Import(_approvedDecisions, false);

            results.Should().NotBeNull();
            Mocker.GetMock<IMonitorNewBookService>()
                .Verify(v => v.ShouldMonitorNewBook(It.IsAny<Book>(), It.IsAny<List<Book>>(), (NewItemMonitorTypes)3), Times.Once());
        }
    }
}
