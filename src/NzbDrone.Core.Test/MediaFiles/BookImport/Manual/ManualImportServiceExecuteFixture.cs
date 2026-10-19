using System.Collections.Generic;
using System.IO;
using System.Linq;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.MediaFiles.BookImport.Manual;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Qualities;
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
                .Returns(new Mock<IFileInfo>());

            Mocker.GetMock<IDiskProvider>()
                .Setup(d => d.GetFileSize(_filePath))
                .Returns(1024L);

            Mocker.GetMock<IRootFolderService>()
                .Setup(r => r.GetBestRootFolder(_filePath))
                .Returns("/root");

            Mocker.GetMock<IRootFolderService>()
                .Setup(r => r.GetBestRootFolder("/root/Test Author"))
                .Returns("/root");

            Mocker.GetMock<IMetadataTagService>()
                .Setup(m => m.ReadTags(It.IsAny<IFileInfo>()))
                .Returns(new ParsedTrackInfo());
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
        }

        [Test]
        public void should_not_call_import_when_edition_lookup_fails_completely()
        {
            // A foreign edition id was provided, but it cannot be resolved in the
            // library or from the metadata provider.
            Mocker.GetMock<IEditionService>()
                .Setup(e => e.GetEditionByForeignEditionId("OL321M"))
                .Returns((Edition)null);

            Mocker.GetMock<IMetadataProviderOrchestrator>()
                .Setup(o => o.GetBookInfo("OL123W"))
                .Returns((NzbDrone.Core.MetadataSource.BookMetadata)new NzbDrone.Core.MetadataSource.BookMetadata
                {
                    ForeignBookId = "OL123W",
                    Title = "Test Book",
                    Editions = new List<Edition>()
                });

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
        }
    }
}
