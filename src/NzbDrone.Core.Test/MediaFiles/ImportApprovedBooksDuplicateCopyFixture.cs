using System;
using System.Collections.Generic;
using System.Linq;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.BookImport;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Profiles.Qualities;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.MediaFiles
{
    // Issue #426 regression (IMPORT/DUPLICATE): a manual copy import submitted with
    // replaceExistingFiles=false still replaced the original BookFile record when the
    // identical file was re-imported.  ImportApprovedBooks.Import never consulted the
    // flag for the incoming-file path: the only remaining uses were the dead
    // RemoveExistingTrackFiles call (commented out) and event emission.  A duplicate
    // import with the same book+part was not short-circuited by the "already
    // imported" guard (which only keys on book id + part within a single Import
    // call), so it flowed into UpgradeBookFile, which unconditionally recycles the
    // prior file record (UpgradeMediaFileService: delete prior record + add new
    // record -> new identity despite identical bytes).
    //
    // Fix: when replaceExisting is false AND a DB BookFile record already backs the
    // incoming path for the same book+part+edition, the duplicate import is
    // short-circuited: the original record (and original file) are preserved, no
    // upgrade/copy is issued, and the result reports the skip.  The explicit
    // replaceExisting=true path keeps the existing upgrade behavior unchanged.
    [TestFixture]
    public class ImportApprovedBooksDuplicateCopyFixture : CoreTest<ImportApprovedBooks>
    {
        private Author _author;
        private Book _book;
        private Edition _edition;
        private BookFile _existing;
        private string _filePath;

        [SetUp]
        public void Setup()
        {
            _filePath = "/root/Test Author/Test Book.epub";

            _author = new Author
            {
                Id = 1,
                Name = "Test Author",
                ForeignAuthorId = "OL1A",
                Path = "/root/Test Author",
                QualityProfile = new LazyLoaded<QualityProfile>(new QualityProfile { Items = Qualities.QualityFixture.GetDefaultQualities() }),
                Metadata = new AuthorMetadata { Name = "Test Author", ForeignAuthorId = "OL1A" }
            };

            _edition = new Edition
            {
                Id = 7,
                BookId = 42,
                Book = new LazyLoaded<Book>(),
                ForeignEditionId = "OL123M",
                Title = "Test Book",
                Format = "Ebook",
                IsEbook = true,
                Monitored = true
            };

            _book = new Book
            {
                Id = 42,
                ForeignBookId = "OL123W",
                Title = "Test Book",
                Author = _author,
                AuthorMetadataId = 5,
                Editions = new List<Edition> { _edition }
            };

            _existing = new BookFile
            {
                Id = 101,
                Path = _filePath,
                Part = 1,
                Size = 2048,
                CalibreId = 0,
                Quality = new QualityModel(Quality.EPUB),
                EditionId = 7,
                Edition = new LazyLoaded<Edition>(_edition),
                Author = new LazyLoaded<Author>(_author)
            };

            _book.BookFiles = new LazyLoaded<List<BookFile>>(new List<BookFile> { _existing });

            Mocker.GetMock<IEditionService>()
                .Setup(s => s.SetMonitored(_edition))
                .Returns(new List<Edition> { _edition });

            Mocker.GetMock<IEditionService>()
                .Setup(s => s.SetMonitoredByFormat(_edition))
                .Returns(new List<Edition> { _edition });

            // The DB already backs the incoming path for this book+part (the
            // duplicate file record the issue reports getting replaced).
            Mocker.GetMock<IMediaFileService>()
                .Setup(s => s.GetFileWithPath(_filePath))
                .Returns(_existing);

            // The upgrader recycles the prior record and adds a new one (the
            // identity-replacing behavior #426 reports).
            Mocker.GetMock<IUpgradeMediaFiles>()
                .Setup(s => s.UpgradeBookFile(It.IsAny<BookFile>(), It.IsAny<LocalBook>(), It.IsAny<bool>(), It.IsAny<bool>()))
                .Returns(new BookFileMoveResult
                {
                    BookFile = new BookFile { Id = 202 },
                    OldFiles = new List<BookFile> { _existing }
                });
        }

        private ImportDecision<LocalBook> BuildDecision()
        {
            return new ImportDecision<LocalBook>(
                new LocalBook
                {
                    Author = _author,
                    Book = _book,
                    Edition = _edition,
                    Part = 1,
                    ExistingFile = false,
                    Path = _filePath,
                    Quality = new QualityModel(Quality.EPUB),
                    FileTrackInfo = new ParsedTrackInfo { ReleaseGroup = "TEST" }
                });
        }

        [TestCase(7, 1, nameof(Quality.EPUB), ImportResultType.Skipped)]
        [TestCase(7, 1, nameof(Quality.AZW3), ImportResultType.Skipped)]
        [TestCase(8, 1, nameof(Quality.EPUB), ImportResultType.Imported)]
        [TestCase(7, 2, nameof(Quality.EPUB), ImportResultType.Imported)]
        [TestCase(8, 1, nameof(Quality.M4B), ImportResultType.Imported)]
        public void should_only_skip_the_same_edition_part_and_format_in_the_batch(
            int editionId, int part, string qualityName, ImportResultType expected)
        {
            var first = BuildDecision();
            first.Item.Path = "/root/Test Author/first.epub";
            first.Item.ExistingFile = true;
            first.Item.Size = 2048;

            var second = BuildDecision();
            second.Item.Path = $"/root/Test Author/second.{qualityName.ToLowerInvariant()}";
            second.Item.ExistingFile = true;
            second.Item.Size = 1024;
            second.Item.Part = part;

            if (editionId != _edition.Id)
            {
                second.Item.Edition = new Edition
                {
                    Id = editionId,
                    BookId = _book.Id,
                    Book = _book,
                    ForeignEditionId = "OL124M",
                    Title = _book.Title,
                    Format = qualityName == nameof(Quality.M4B) ? "Audiobook" : "Ebook",
                    IsEbook = qualityName != nameof(Quality.M4B)
                };
                _book.Editions.Value.Add(second.Item.Edition);
            }

            second.Item.Quality = new QualityModel(qualityName switch
            {
                nameof(Quality.AZW3) => Quality.AZW3,
                nameof(Quality.M4B) => Quality.M4B,
                _ => Quality.EPUB
            });

            if (qualityName == nameof(Quality.AZW3))
            {
                // Both candidates are ebooks. The higher-ranked AZW3 is
                // considered first, so the EPUB is the redundant part.
                (first, second) = (second, first);
            }

            var results = Subject.Import(new List<ImportDecision<LocalBook>> { first, second }, false);

            Assert.That(results, Has.Count.EqualTo(2));
            Assert.That(results.Single(x => x.ImportDecision == first).Result, Is.EqualTo(ImportResultType.Imported));
            Assert.That(results.Single(x => x.ImportDecision == second).Result, Is.EqualTo(expected));
            Mocker.GetMock<IMediaFileService>()
                .Verify(x => x.AddMany(It.Is<List<BookFile>>(files =>
                    files.Any(f => f.Path == first.Item.Path && f.EditionId == _edition.Id) &&
                    files.Any(f => f.Path == second.Item.Path && f.EditionId == editionId) ==
                        (expected == ImportResultType.Imported))), Times.Once);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void should_not_skip_a_new_file_when_the_first_equivalent_item_was_unsuccessful(bool firstImportThrows)
        {
            var first = BuildDecision();
            first.Item.Size = 4096;
            if (firstImportThrows)
            {
                first.Item.Path = "/downloads/unreadable.epub";
                Mocker.GetMock<IUpgradeMediaFiles>()
                    .Setup(x => x.UpgradeBookFile(It.IsAny<BookFile>(), first.Item, true, false))
                    .Throws(new UnauthorizedAccessException());
            }

            var second = BuildDecision();
            second.Item.Path = "/downloads/recoverable.epub";
            second.Item.Size = 2048;

            var results = Subject.Import(new List<ImportDecision<LocalBook>> { first, second }, false, null, ImportMode.Copy);

            Assert.That(results, Has.Count.EqualTo(2));
            Assert.That(results.Single(x => x.ImportDecision == first).Result, Is.EqualTo(ImportResultType.Skipped));
            Assert.That(results.Single(x => x.ImportDecision == first).PreservedExistingFileId, Is.EqualTo(firstImportThrows ? (int?)null : _existing.Id));
            Assert.That(results.Single(x => x.ImportDecision == second).Result, Is.EqualTo(ImportResultType.Imported));
            Mocker.GetMock<IMediaFileService>()
                .Verify(x => x.AddMany(It.Is<List<BookFile>>(files =>
                    files.Count == 1 && files[0].Path == second.Item.Path)), Times.Once);
            ExceptionVerification.ExpectedWarns(firstImportThrows ? 1 : 0, "Couldn't import book");
        }

        [Test]
        public void should_preserve_existing_bookfile_record_when_duplicate_copy_import_with_replace_existing_false()
        {
            var decision = BuildDecision();

            var results = Subject.Import(new List<ImportDecision<LocalBook>> { decision }, false, null, ImportMode.Copy);

            // The command still reports the file (one result), but as skipped, not imported.
            Assert.That(results, Has.Count.EqualTo(1));
            Assert.That(results[0].Result, Is.EqualTo(ImportResultType.Skipped));
            Assert.That(results[0].Errors, Has.Count.EqualTo(1));
            Assert.That(results[0].PreservedExistingFileId, Is.EqualTo(_existing.Id));

            // The preserved original record identity must be cited in the skip reason.
            Assert.That(results[0].Errors[0], Does.Contain("101"));

            // AC1: the original record is preserved - no recycle/upgrade issued.
            Mocker.GetMock<IUpgradeMediaFiles>()
                .Verify(
                    u => u.UpgradeBookFile(It.IsAny<BookFile>(), It.IsAny<LocalBook>(), It.IsAny<bool>(), It.IsAny<bool>()),
                    Times.Never);

            // No new record is added and the existing record is not deleted.
            Mocker.GetMock<IMediaFileService>()
                .Verify(m => m.AddMany(It.Is<List<BookFile>>(l => l.Count > 0)), Times.Never);
            Mocker.GetMock<IMediaFileService>()
                .Verify(m => m.Delete(_existing, DeleteMediaFileReason.ManualOverride), Times.Never);
        }

        [TestCase(true, ImportMode.Copy)]
        [TestCase(false, ImportMode.Move)]
        public void should_preserve_a_tracked_path_when_in_place_or_moving_with_different_metadata(bool existingFile, ImportMode mode)
        {
            var decision = BuildDecision();
            decision.Item.ExistingFile = existingFile;
            decision.Item.Part = 2;

            var results = Subject.Import(new List<ImportDecision<LocalBook>> { decision }, false, null, mode);

            Assert.That(results, Has.Count.EqualTo(1));
            Assert.That(results[0].Result, Is.EqualTo(ImportResultType.Skipped));
            Assert.That(results[0].Errors[0], Does.Contain("101"));
            Mocker.GetMock<IUpgradeMediaFiles>()
                .Verify(u => u.UpgradeBookFile(It.IsAny<BookFile>(), It.IsAny<LocalBook>(), It.IsAny<bool>(), It.IsAny<bool>()), Times.Never);
            Mocker.GetMock<IMediaFileService>()
                .Verify(m => m.Delete(It.IsAny<BookFile>(), It.IsAny<DeleteMediaFileReason>()), Times.Never);
        }

        [Test]
        public void should_report_the_preserved_destination_record_when_the_source_path_is_different()
        {
            var decision = BuildDecision();
            decision.Item.Path = "/downloads/Test Book.epub";
            Mocker.GetMock<IUpgradeMediaFiles>()
                .Setup(s => s.UpgradeBookFile(It.IsAny<BookFile>(), decision.Item, true, false))
                .Returns(new BookFileMoveResult { PreservedFile = _existing });

            var results = Subject.Import(new List<ImportDecision<LocalBook>> { decision }, false, null, ImportMode.Copy);

            Assert.That(results, Has.Count.EqualTo(1));
            Assert.That(results[0].Result, Is.EqualTo(ImportResultType.Skipped));
            Assert.That(results[0].Errors[0], Does.Contain("101"));
            Assert.That(results[0].PreservedExistingFileId, Is.EqualTo(_existing.Id));
            Mocker.GetMock<IMediaFileService>()
                .Verify(m => m.AddMany(It.Is<List<BookFile>>(l => l.Count > 0)), Times.Never);
        }

        [Test]
        public void should_replace_existing_bookfile_when_duplicate_import_with_replace_existing_true()
        {
            // AC2: explicit replacement intent keeps the existing upgrade/replace path.
            var decision = BuildDecision();

            var results = Subject.Import(new List<ImportDecision<LocalBook>> { decision }, true, null, ImportMode.Copy);

            Assert.That(results, Has.Count.EqualTo(1));
            Assert.That(results[0].Result, Is.EqualTo(ImportResultType.Imported));
            Mocker.GetMock<IUpgradeMediaFiles>()
                .Verify(
                    u => u.UpgradeBookFile(It.IsAny<BookFile>(), It.IsAny<LocalBook>(), It.IsAny<bool>(), It.IsAny<bool>()),
                    Times.Once);
            Mocker.GetMock<IMediaFileService>()
                .Verify(m => m.AddMany(It.Is<List<BookFile>>(l => l.Count == 1)), Times.Once);
        }

        [Test]
        public void should_still_import_when_replace_existing_false_but_no_record_backs_path()
        {
            // Non-duplicate import with replaceExistingFiles=false: the file has no
            // backing record yet, so the normal import path must still run.
            Mocker.GetMock<IMediaFileService>()
                .Setup(s => s.GetFileWithPath(_filePath))
                .Returns((BookFile)null);

            var decision = BuildDecision();

            var results = Subject.Import(new List<ImportDecision<LocalBook>> { decision }, false, null, ImportMode.Copy);

            Assert.That(results, Has.Count.EqualTo(1));
            Assert.That(results[0].Result, Is.EqualTo(ImportResultType.Imported));
            Assert.That(results[0].Errors, Is.Empty);
            Mocker.GetMock<IUpgradeMediaFiles>()
                .Verify(
                    u => u.UpgradeBookFile(It.IsAny<BookFile>(), It.IsAny<LocalBook>(), It.IsAny<bool>(), It.IsAny<bool>()),
                    Times.Once);
        }
    }
}
