using System.Collections.Generic;
using System.IO;
using FizzWare.NBuilder;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.DecisionEngine;
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
                .Setup(s => s.UpgradeBookFile(It.IsAny<BookFile>(), It.IsAny<LocalBook>(), It.IsAny<bool>(), It.IsAny<bool>()))
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

        [TestCase(true)]
        [TestCase(false)]
        public void should_reject_incompatible_files_before_preparing_or_moving_them(bool audiobook)
        {
            _ebookEdition.Format = "Ebook";
            _audiobookEdition.Format = "Audiobook";
            if (audiobook)
            {
                GivenAudiobookImport();
                _approvedDecisions[0].Item.Edition = _ebookEdition;
            }
            else
            {
                GivenEbookImport();
                _approvedDecisions[0].Item.Edition = _audiobookEdition;
            }

            var results = Subject.Import(_approvedDecisions, false);

            results.Should().ContainSingle(result => result.Result == ImportResultType.Rejected);
            Mocker.GetMock<IEditionService>().Verify(s => s.SetMonitored(It.IsAny<Edition>()), Times.Never());
            Mocker.GetMock<IUpgradeMediaFiles>().Verify(s => s.UpgradeBookFile(It.IsAny<BookFile>(), It.IsAny<LocalBook>(), It.IsAny<bool>(), It.IsAny<bool>()), Times.Never());
            Mocker.GetMock<IMediaFileService>().Verify(s => s.AddMany(It.Is<List<BookFile>>(files => files.Count > 0)), Times.Never());
        }

        [Test]
        public void should_prepare_and_persist_both_formats_of_one_work()
        {
            Mocker.GetMock<IConfigService>().Setup(s => s.EnableDualFormatTracking).Returns(true);
            _ebookEdition.ForeignEditionId = "ebook-edition";
            _ebookEdition.Format = "Ebook";
            _audiobookEdition.ForeignEditionId = "audiobook-edition";
            _audiobookEdition.Format = "Audiobook";
            GivenEbookImport();
            var ebookDecision = _approvedDecisions[0];
            GivenAudiobookImport();
            var audiobookDecision = _approvedDecisions[0];
            _approvedDecisions.Insert(0, ebookDecision);

            var results = Subject.Import(_approvedDecisions, false);

            results.Should().OnlyContain(result => result.Result == ImportResultType.Imported).And.HaveCount(2);
            Mocker.GetMock<IEditionService>().Verify(s => s.SetMonitoredByFormat(_ebookEdition), Times.Once());
            Mocker.GetMock<IEditionService>().Verify(s => s.SetMonitoredByFormat(_audiobookEdition), Times.Once());
            Mocker.GetMock<IMediaFileService>().Verify(s => s.AddMany(It.Is<List<BookFile>>(files =>
                files.Count == 2 &&
                files.Exists(file => file.Path == ebookDecision.Item.Path && file.EditionId == _ebookEdition.Id) &&
                files.Exists(file => file.Path == audiobookDecision.Item.Path && file.EditionId == _audiobookEdition.Id))), Times.Once());
        }

        [Test]
        public void should_add_the_selected_edition_when_the_work_already_has_a_different_format()
        {
            _ebookEdition.ForeignEditionId = "ebook-edition";
            _ebookEdition.Format = "Ebook";
            _audiobookEdition.ForeignEditionId = "audiobook-edition";
            _audiobookEdition.Format = "Audiobook";
            _book.Editions = new List<Edition> { _ebookEdition };

            GivenAudiobookImport();
            var remoteEdition = new Edition { ForeignEditionId = _audiobookEdition.ForeignEditionId, Format = "Audiobook" };
            var remoteBook = new Book
            {
                ForeignBookId = _book.ForeignBookId,
                Author = _author,
                Editions = new List<Edition> { remoteEdition }
            };
            _approvedDecisions[0].Item.Book = remoteBook;
            _approvedDecisions[0].Item.Edition = remoteEdition;

            Mocker.GetMock<IBookService>().Setup(s => s.FindById(_book.ForeignBookId)).Returns(_book);
            Mocker.GetMock<IEditionService>().SetupSequence(s => s.GetEditionByForeignEditionId(remoteEdition.ForeignEditionId))
                .Returns((Edition)null)
                .Returns(_audiobookEdition);

            var results = Subject.Import(_approvedDecisions, false);

            results.Should().ContainSingle(result => result.Result == ImportResultType.Imported);
            Mocker.GetMock<IEditionService>().Verify(s => s.InsertMany(It.Is<List<Edition>>(editions =>
                editions.Count == 1 && editions[0].ForeignEditionId == remoteEdition.ForeignEditionId && editions[0].BookId == _book.Id)), Times.Once());
            Mocker.GetMock<IMediaFileService>().Verify(s => s.AddMany(It.Is<List<BookFile>>(files =>
                files.Count == 1 && files[0].EditionId == _audiobookEdition.Id)), Times.Once());
        }

        [TestCase(true)]
        [TestCase(false)]
        public void should_validate_and_use_the_stored_edition_when_resolving_a_remote_edition(bool compatible)
        {
            GivenAudiobookImport();
            var storedEdition = compatible ? _audiobookEdition : _ebookEdition;
            _ebookEdition.Format = "Ebook";
            _audiobookEdition.Format = "Audiobook";
            var remoteEdition = new Edition { BookId = _book.Id, ForeignEditionId = "same-edition", Format = "Audiobook" };
            _approvedDecisions[0].Item.Edition = remoteEdition;
            storedEdition.ForeignEditionId = remoteEdition.ForeignEditionId;
            Mocker.GetMock<IEditionService>().Setup(s => s.GetEditionByForeignEditionId(remoteEdition.ForeignEditionId)).Returns(storedEdition);

            var results = Subject.Import(_approvedDecisions, false);

            results.Should().ContainSingle(result => result.Result == (compatible ? ImportResultType.Imported : ImportResultType.Rejected));
            Mocker.GetMock<IEditionService>()
                .Verify(s => s.SetMonitored(storedEdition), compatible ? Times.Once() : Times.Never());
            Mocker.GetMock<IMediaFileService>().Verify(s => s.AddMany(It.Is<List<BookFile>>(files =>
                files.Count == 1 && files[0].EditionId == storedEdition.Id)), compatible ? Times.Once() : Times.Never());
        }

        [TestCase(false)]
        [TestCase(true)]
        public void should_reject_a_selected_edition_owned_by_a_different_work(bool resolveByForeignId)
        {
            Mocker.GetMock<IConfigService>().Setup(s => s.EnableDualFormatTracking).Returns(true);
            GivenAudiobookImport();

            var otherWork = new Book { Id = _book.Id + 1, Author = _author };
            var storedEdition = new Edition
            {
                Id = 30,
                Book = otherWork,
                BookId = otherWork.Id,
                ForeignEditionId = "other-work-audio",
                Format = "Audiobook",
                Monitored = true
            };
            _approvedDecisions[0].Item.Edition = resolveByForeignId
                ? new Edition { ForeignEditionId = storedEdition.ForeignEditionId, Format = "Audiobook" }
                : storedEdition;
            Mocker.GetMock<IEditionService>().Setup(s => s.GetEditionByForeignEditionId(storedEdition.ForeignEditionId)).Returns(storedEdition);
            Mocker.GetMock<IEditionService>().Setup(s => s.SetMonitoredByFormat(storedEdition)).Returns(new List<Edition> { storedEdition });

            var result = Subject.Import(_approvedDecisions, false).Should().ContainSingle().Which;

            result.Result.Should().Be(ImportResultType.Rejected);
            result.Errors.Should().Equal("The selected edition belongs to a different work");
            result.ImportDecision.Rejections.Should().ContainSingle().Which.Type.Should().Be(RejectionType.Permanent);
            Mocker.GetMock<IEditionService>().Verify(s => s.SetMonitoredByFormat(It.IsAny<Edition>()), Times.Never());
            Mocker.GetMock<IEditionService>().Verify(s => s.SetMonitored(It.IsAny<Edition>()), Times.Never());
            Mocker.GetMock<IUpgradeMediaFiles>().Verify(s => s.UpgradeBookFile(It.IsAny<BookFile>(), It.IsAny<LocalBook>(), It.IsAny<bool>(), It.IsAny<bool>()), Times.Never());
            Mocker.GetMock<IMediaFileService>().Verify(s => s.AddMany(It.Is<List<BookFile>>(files => files.Count > 0)), Times.Never());
        }

        [TestCase(true)]
        [TestCase(false)]
        public void should_persist_later_files_when_one_approved_book_cannot_be_prepared(bool missingAuthor)
        {
            GivenAudiobookImport();
            Mocker.GetMock<IConfigService>().Setup(s => s.EnableDualFormatTracking).Returns(true);
            _book.ForeignBookId = "valid-work";
            var brokenBook = new Book { Id = 2, ForeignBookId = "broken-work", Title = "Broken Book" };
            var brokenEdition = new Edition { Id = 30, BookId = brokenBook.Id };
            var brokenDecision = new ImportDecision<LocalBook>(new LocalBook
            {
                Author = missingAuthor ? null : _author,
                Book = brokenBook,
                Edition = brokenEdition,
                Part = 1,
                ExistingFile = true,
                Path = Path.Combine(_author.Path, "Broken Book.epub"),
                Quality = new QualityModel(Quality.EPUB),
                FileTrackInfo = new ParsedTrackInfo()
            });
            _approvedDecisions.Insert(0, brokenDecision);
            Mocker.GetMock<IEditionService>()
                .Setup(s => s.SetMonitoredByFormat(brokenEdition))
                .Throws(new System.InvalidOperationException("Unable to update edition"));

            var results = Subject.Import(_approvedDecisions, false);

            results.Should().ContainSingle(result => result.ImportDecision == brokenDecision && result.Result == ImportResultType.Rejected);
            results.Should().ContainSingle(result => result.ImportDecision == _approvedDecisions[1] && result.Result == ImportResultType.Imported);
            Mocker.GetMock<IMediaFileService>().Verify(s => s.AddMany(It.Is<List<BookFile>>(files =>
                files.Count == 1 && files[0].EditionId == _audiobookEdition.Id)), Times.Once());
            if (!missingAuthor)
            {
                ExceptionVerification.ExpectedErrors(1);
            }
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
            _ebookEdition.ForeignEditionId = "fixture-ebook-edition";
            _audiobookEdition.ForeignEditionId = "fixture-audiobook-edition";

            Mocker.SetConstant<IMonitorNewBookService>(new MonitorNewBookService(TestLogger));
            Mocker.GetMock<IBookService>()
                .SetupSequence(service => service.FindById(_book.ForeignBookId))
                .Returns((Book)null)
                .Returns(_book);
            Mocker.GetMock<IBookService>()
                .Setup(s => s.GetBooksByAuthor(It.IsAny<int>()))
                .Returns(new List<Book>());
            Mocker.GetMock<IBookService>()
                .Setup(s => s.InsertMany(It.IsAny<List<Book>>()))
                .Callback<List<Book>>(b =>
                {
                    if (b != null && b.Count > 0)
                    {
                        b[0].Id = 1000;
                    }
                });

            GivenEbookImport();

            var results = Subject.Import(_approvedDecisions, false);

            results.Should().ContainSingle().Which.Result.Should().Be(ImportResultType.Imported);
            _book.Id.Should().Be(1000);
            _book.Monitored.Should().BeFalse();
            var importedPath = _approvedDecisions[0].Item.Path;
            Mocker.GetMock<IMediaFileService>()
                .Verify(service => service.AddMany(It.Is<List<BookFile>>(files =>
                    files.Count == 1 && files[0].Path == importedPath && files[0].EditionId == _ebookEdition.Id)), Times.Once);
            ExceptionVerification.ExpectedWarns(1, "Unknown NewItemMonitorTypes value");
        }
    }
}
