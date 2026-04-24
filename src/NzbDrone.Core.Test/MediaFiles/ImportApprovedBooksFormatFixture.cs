using System.Collections.Generic;
using System.IO;
using FizzWare.NBuilder;
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
    }
}
