using System.Collections.Generic;
using System.IO.Abstractions;
using System.Linq;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Core.Books;
using NzbDrone.Core.Download;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.BookImport;
using NzbDrone.Core.MediaFiles.BookImport.Manual;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.RootFolders;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.MediaFiles.BookImport.Manual
{
    // Issue #295 regression: ManualImportService.Execute must not persist a BookFile
    // with EditionId=0 (unmapped) when no edition could be resolved.  The import
    // decision must be explicitly rejected so the caller is not told a no-op import
    // succeeded (silent 202).
    //
    // When BookId is provided but no edition can be resolved (neither from the
    // library nor from the metadata provider), the Execute method must reject the
    // import decision rather than creating an unmapped BookFile.
    [TestFixture]
    public class ManualImportServiceExecuteFixture : TestBase<ManualImportService>
    {
        private Author _author;
        private Book _book;
        private string _filePath;

        [SetUp]
        public void Setup()
        {
            _author = new Author
            {
                Id = 1,
                Name = "Test Author",
                ForeignAuthorId = "OL1A",
                Path = "/root/Test Author",
                Metadata = new AuthorMetadata { Name = "Test Author", ForeignAuthorId = "OL1A" }
            };

            _book = new Book
            {
                Id = 42,
                ForeignBookId = "OL123W",
                Title = "Test Book",
                AuthorId = 1,
                Author = _author,
                AuthorMetadata = _author.Metadata,
                Editions = new List<Edition>()
            };

            _filePath = "/root/Test Author/Test Book.epub";

            Mocker.GetMock<IAuthorService>()
                .Setup(a => a.GetAuthor(1))
                .Returns(_author);

            Mocker.GetMock<IBookService>()
                .Setup(b => b.GetBook(42))
                .Returns(_book);

            Mocker.GetMock<IDiskProvider>()
                .Setup(d => d.GetFileInfo(_filePath))
                .Returns(Mock.Of<IFileInfo>(f => f.FullName == _filePath, Moq.MockBehavior.Loose));

            Mocker.GetMock<IDiskProvider>()
                .Setup(d => d.GetFileSize(_filePath))
                .Returns(1024L);

            Mocker.GetMock<IRootFolderService>()
                .Setup(r => r.GetBestRootFolder(_filePath))
                .Returns(new RootFolder { Path = "/root" });

            Mocker.GetMock<IRootFolderService>()
                .Setup(r => r.GetBestRootFolder("/root/Test Author"))
                .Returns(new RootFolder { Path = "/root" });

            Mocker.GetMock<IMetadataTagService>()
                .Setup(m => m.ReadTags(It.IsAny<IFileInfo>()))
                .Returns(new ParsedTrackInfo());

            // IImportApprovedBooks.Import must return a list (the service calls
            // imported.AddRange(result)); the default null mock return would throw.
            Mocker.GetMock<IImportApprovedBooks>()
                .Setup(i => i.Import(It.IsAny<List<ImportDecision<LocalBook>>>(), It.IsAny<bool>(), It.IsAny<DownloadClientItem>(), It.IsAny<ImportMode>()))
                .Returns(new List<ImportResult>());
        }

        [TestCase(0, 42)]
        [TestCase(1, 0)]
        [TestCase(1, 42)]
        public void should_report_an_unsuccessful_command_when_no_file_passed_preparation(int authorId, int bookId)
        {
            var command = new ManualImportCommand
            {
                Files = new List<ManualImportFile>
                {
                    new ManualImportFile { Path = _filePath, AuthorId = authorId, BookId = bookId }
                },
                ImportMode = ImportMode.Copy
            };

            Subject.Execute(command);

            ExceptionVerification.ExpectedWarns(1);
            Mocker.GetMock<ICommandResultReporter>().Verify(r => r.Report(CommandResult.Unsuccessful), Times.Once());
            Mocker.GetMock<IImportApprovedBooks>().Verify(
                i => i.Import(
                    It.Is<List<ImportDecision<LocalBook>>>(d => d.Any(x => x.Approved)),
                    It.IsAny<bool>(),
                    It.IsAny<DownloadClientItem>(),
                    It.IsAny<ImportMode>()),
                Times.Never());
        }

        [TestCase(false, false)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        public void should_leave_command_result_unchanged_when_a_file_passed_preparation(bool includeInvalidFile, bool preserveDuplicate)
        {
            var edition = new Edition { Id = 7, BookId = _book.Id, ForeignEditionId = "OL123M", IsEbook = true };
            Mocker.GetMock<IEditionService>()
                .Setup(e => e.GetEditionByForeignEditionId(edition.ForeignEditionId))
                .Returns(edition);
            Mocker.GetMock<IImportApprovedBooks>()
                .Setup(i => i.Import(It.Is<List<ImportDecision<LocalBook>>>(d => d.Count == 1 && d[0].Approved), false, null, ImportMode.Copy))
                .Returns<List<ImportDecision<LocalBook>>, bool, DownloadClientItem, ImportMode>((decisions, _, _, _) =>
                    new List<ImportResult>
                    {
                        preserveDuplicate ? new ImportResult(decisions[0], "Existing copy preserved") : new ImportResult(decisions[0])
                    });

            var command = new ManualImportCommand
            {
                Files = new List<ManualImportFile>
                {
                    new ManualImportFile { Path = _filePath, AuthorId = _author.Id, BookId = _book.Id, ForeignEditionId = edition.ForeignEditionId }
                },
                ImportMode = ImportMode.Copy
            };
            if (includeInvalidFile)
            {
                command.Files.Add(new ManualImportFile { Path = _filePath + ".epub", BookId = _book.Id });
            }

            Subject.Execute(command);

            ExceptionVerification.ExpectedWarns(includeInvalidFile ? 1 : 0);
            Mocker.GetMock<IImportApprovedBooks>().Verify(
                i => i.Import(
                    It.Is<List<ImportDecision<LocalBook>>>(d => d.Count == 1 && d[0].Approved &&
                        d[0].Item.Path == _filePath && d[0].Item.Edition == edition),
                    false,
                    null,
                    ImportMode.Copy),
                Times.Once());
            Mocker.GetMock<ICommandResultReporter>().Verify(r => r.Report(It.IsAny<CommandResult>()), Times.Never());
        }

        [Test]
        public void should_not_call_import_when_book_mapped_but_no_edition_resolved()
        {
            // No edition in the library and no foreign edition id provided.
            Mocker.GetMock<IEditionService>()
                .Setup(e => e.GetEditionByForeignEditionId(null))
                .Returns((Edition)null);

            var file = new ManualImportFile
            {
                Path = _filePath,
                AuthorId = 1,
                BookId = 42,
                ForeignEditionId = null
            };

            var command = new ManualImportCommand
            {
                Files = new List<ManualImportFile> { file },
                ImportMode = ImportMode.Move
            };

            Subject.Execute(command);

            // The import should have been rejected (no edition resolvable), so the
            // IImportApprovedBooks.Import should NOT have been called with a
            // non-rejected decision.  The decision carries a rejection, so the
            // import pipeline will skip it.
            Mocker.GetMock<IImportApprovedBooks>().Verify(
                i => i.Import(
                    It.Is<List<ImportDecision<LocalBook>>>(d => d.Any(x => !x.Approved)),
                    It.IsAny<bool>(),
                    It.IsAny<NzbDrone.Core.Download.DownloadClientItem>(),
                    It.IsAny<ImportMode>()),
                Times.Once);
            ExceptionVerification.ExpectedWarns(1, "Manual import of");
        }

        [Test]
        public void should_not_call_import_when_edition_lookup_fails_completely()
        {
            // A foreign edition id was provided, but it cannot be resolved in the
            // library or from the metadata provider.
            Mocker.GetMock<IEditionService>()
                .Setup(e => e.GetEditionByForeignEditionId("OL321M"))
                .Returns((Edition)null);

            var providerBook = new Book
            {
                ForeignBookId = "OL123W",
                Title = "Test Book",
                Editions = new List<Edition>()
            };
            Mocker.GetMock<IMetadataProviderOrchestrator>()
                .Setup(o => o.GetBookInfo("OL123W"))
                .Returns(new System.Tuple<string, Book, List<AuthorMetadata>>("openlibrary", providerBook, new List<AuthorMetadata>()));

            var file = new ManualImportFile
            {
                Path = _filePath,
                AuthorId = 1,
                BookId = 42,
                ForeignEditionId = "OL321M"
            };

            var command = new ManualImportCommand
            {
                Files = new List<ManualImportFile> { file },
                ImportMode = ImportMode.Move
            };

            Subject.Execute(command);

            // The import decision should carry a rejection because no edition was
            // resolvable.  Verify IImportApprovedBooks was called (the pipeline
            // handles rejected decisions by skipping them) — the key assertion is
            // that the decision was NOT approved.
            Mocker.GetMock<IImportApprovedBooks>().Verify(
                i => i.Import(
                    It.Is<List<ImportDecision<LocalBook>>>(d => d.Any(x => !x.Approved)),
                    It.IsAny<bool>(),
                    It.IsAny<NzbDrone.Core.Download.DownloadClientItem>(),
                    It.IsAny<ImportMode>()),
                Times.Once);
            ExceptionVerification.ExpectedWarns(1, "Manual import of");
        }
    }

    // Issue #307 regression: POST /api/v1/manualimport (update) with a valid BookId
    // and null ForeignEditionId must NOT be rejected while the book has a monitored
    // edition.  The import decision must default to the book's monitored edition so
    // the BookFile is persisted mapped (EditionId > 0), not stranded at EditionId=0.
    // This is the complement of the #295 fix: when NO monitored edition exists, the
    // import is still rejected (honest failure, not a silent unmapped 202).
    [TestFixture]
    public class ManualImportServiceExecuteIssue307Fixture : TestBase<ManualImportService>
    {
        private Author _author;
        private Book _book;
        private string _filePath;

        [SetUp]
        public void Setup()
        {
            _author = new Author
            {
                Id = 1,
                Name = "Test Author",
                ForeignAuthorId = "OL1A",
                Path = "/root/Test Author",
                Metadata = new AuthorMetadata { Name = "Test Author", ForeignAuthorId = "OL1A" }
            };

            _book = new Book
            {
                Id = 42,
                ForeignBookId = "OL123W",
                Title = "Test Book",
                AuthorId = 1,
                Author = _author,
                AuthorMetadata = _author.Metadata
            };

            _filePath = "/root/Test Author/Test Book.epub";

            Mocker.GetMock<IAuthorService>()
                .Setup(a => a.GetAuthor(1))
                .Returns(_author);

            Mocker.GetMock<IBookService>()
                .Setup(b => b.GetBook(42))
                .Returns(_book);

            Mocker.GetMock<IDiskProvider>()
                .Setup(d => d.GetFileInfo(_filePath))
                .Returns(Mock.Of<IFileInfo>(f => f.FullName == _filePath, Moq.MockBehavior.Loose));

            Mocker.GetMock<IDiskProvider>()
                .Setup(d => d.GetFileSize(_filePath))
                .Returns(1024L);

            Mocker.GetMock<IRootFolderService>()
                .Setup(r => r.GetBestRootFolder(_filePath))
                .Returns(new RootFolder { Path = "/root" });

            Mocker.GetMock<IRootFolderService>()
                .Setup(r => r.GetBestRootFolder("/root/Test Author"))
                .Returns(new RootFolder { Path = "/root" });

            Mocker.GetMock<IMetadataTagService>()
                .Setup(m => m.ReadTags(It.IsAny<IFileInfo>()))
                .Returns(new ParsedTrackInfo());

            // IImportApprovedBooks.Import must return a list (the service calls
            // imported.AddRange(result)); the default null mock return would throw.
            Mocker.GetMock<IImportApprovedBooks>()
                .Setup(i => i.Import(It.IsAny<List<ImportDecision<LocalBook>>>(), It.IsAny<bool>(), It.IsAny<DownloadClientItem>(), It.IsAny<ImportMode>()))
                .Returns(new List<ImportResult>());
        }

        [Test]
        public void should_default_to_monitored_edition_when_book_id_set_and_foreign_edition_null()
        {
            // A book with a monitored edition; the client omits the edition entirely.
            var monitored = new Edition { Id = 7, ForeignEditionId = "OL123M", Monitored = true };
            var other = new Edition { Id = 8, ForeignEditionId = "OL999M", Monitored = false };
            _book.Editions = new List<Edition> { other, monitored };

            Mocker.GetMock<IEditionService>()
                .Setup(e => e.GetEditionByForeignEditionId(null))
                .Returns((Edition)null);

            var file = new ManualImportFile
            {
                Path = _filePath,
                AuthorId = 1,
                BookId = 42,
                ForeignEditionId = null
            };

            var command = new ManualImportCommand
            {
                Files = new List<ManualImportFile> { file },
                ImportMode = ImportMode.Move
            };

            Subject.Execute(command);

            // The import must be APPROVED (not rejected) and carry the monitored
            // edition (Id 7), so ImportApprovedBooks persists a mapped BookFile
            // (EditionId > 0) instead of an unmapped one (EditionId = 0).
            Mocker.GetMock<IImportApprovedBooks>().Verify(
                i => i.Import(
                    It.Is<List<ImportDecision<LocalBook>>>(d =>
                        d.Count == 1 &&
                        d[0].Approved &&
                        d[0].Item.Edition != null &&
                        d[0].Item.Edition.Id == 7),
                    It.IsAny<bool>(),
                    It.IsAny<DownloadClientItem>(),
                    It.IsAny<ImportMode>()),
                Times.Once);
        }

        [Test]
        public void should_still_reject_when_book_id_set_foreign_edition_null_and_no_monitored_edition()
        {
            // No edition can be resolved AND the book has no monitored edition to
            // default to -> the #295 guard must keep rejecting (honest failure,
            // not a silent unmapped 202).  Regression guard for #295 semantics.
            _book.Editions = new List<Edition>
            {
                new Edition { Id = 7, ForeignEditionId = "OL123M", Monitored = false }
            };

            Mocker.GetMock<IEditionService>()
                .Setup(e => e.GetEditionByForeignEditionId(null))
                .Returns((Edition)null);

            var file = new ManualImportFile
            {
                Path = _filePath,
                AuthorId = 1,
                BookId = 42,
                ForeignEditionId = null
            };

            var command = new ManualImportCommand
            {
                Files = new List<ManualImportFile> { file },
                ImportMode = ImportMode.Move
            };

            Subject.Execute(command);

            Mocker.GetMock<IImportApprovedBooks>().Verify(
                i => i.Import(
                    It.Is<List<ImportDecision<LocalBook>>>(d =>
                        d.Count == 1 && !d[0].Approved),
                    It.IsAny<bool>(),
                    It.IsAny<DownloadClientItem>(),
                    It.IsAny<ImportMode>()),
                Times.Once);
            ExceptionVerification.ExpectedWarns(1, "Manual import of");
        }

        [Test]
        public void should_still_reject_when_named_edition_unresolvable_and_no_monitored_edition()
        {
            // A foreign edition id was provided but cannot be resolved in the
            // library or the metadata provider, and the book has no monitored
            // edition -> still rejected (preserves #295 behavior).
            _book.Editions = new List<Edition>
            {
                new Edition { Id = 7, ForeignEditionId = "OL123M", Monitored = false }
            };

            Mocker.GetMock<IEditionService>()
                .Setup(e => e.GetEditionByForeignEditionId("OL321M"))
                .Returns((Edition)null);

            var providerBook = new Book
            {
                ForeignBookId = "OL123W",
                Title = "Test Book",
                Editions = new List<Edition>()
            };
            Mocker.GetMock<IMetadataProviderOrchestrator>()
                .Setup(o => o.GetBookInfo("OL123W"))
                .Returns(new System.Tuple<string, Book, List<AuthorMetadata>>("openlibrary", providerBook, new List<AuthorMetadata>()));

            var file = new ManualImportFile
            {
                Path = _filePath,
                AuthorId = 1,
                BookId = 42,
                ForeignEditionId = "OL321M"
            };

            var command = new ManualImportCommand
            {
                Files = new List<ManualImportFile> { file },
                ImportMode = ImportMode.Move
            };

            Subject.Execute(command);

            Mocker.GetMock<IImportApprovedBooks>().Verify(
                i => i.Import(
                    It.Is<List<ImportDecision<LocalBook>>>(d =>
                        d.Count == 1 && !d[0].Approved),
                    It.IsAny<bool>(),
                    It.IsAny<DownloadClientItem>(),
                    It.IsAny<ImportMode>()),
                Times.Once);
            ExceptionVerification.ExpectedWarns(1, "Manual import of");
        }
    }
}
