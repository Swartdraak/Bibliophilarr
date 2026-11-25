using System.Collections.Generic;
using System.IO.Abstractions;
using System.Linq;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Core.Books;
using NzbDrone.Core.DecisionEngine;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.BookImport;
using NzbDrone.Core.MediaFiles.BookImport.Manual;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.MediaFiles.BookImport.Manual
{
    [TestFixture]
    public class ManualImportFolderPreviewFixture : TestBase<ManualImportService>
    {
        private const string NoLocalEdition = "No matching edition in the library. Add the book or select a local edition before importing.";

        private string _folder;
        private Author _owner;
        private Book _work;
        private Edition _edition;
        private LocalBook _candidate;
        private ImportDecision<LocalBook> _decision;

        [SetUp]
        public void Setup()
        {
            _folder = @"C:\Test\Incoming".AsOsAgnostic();
            var path = @"C:\Test\Incoming\Observatory.epub".AsOsAgnostic();
            _owner = new Author
            {
                Id = 13,
                AuthorMetadataId = 20,
                Metadata = new AuthorMetadata { Name = "Test Author", ForeignAuthorId = "OL20A" }
            };
            _work = new Book
            {
                Id = 30,
                AuthorMetadataId = 20,
                ForeignBookId = "OL30W",
                Title = "Observatory",
                Author = _owner
            };
            _edition = new Edition
            {
                Id = 40,
                BookId = 30,
                ForeignEditionId = "OL40M",
                IsEbook = true,
                Book = _work
            };
            _work.Editions = new List<Edition> { _edition };

            _candidate = new LocalBook { Path = path, Author = _owner, Book = _work, Edition = _edition };
            _decision = new ImportDecision<LocalBook>(_candidate);

            Mocker.GetMock<IDiskProvider>().Setup(d => d.FolderExists(_folder)).Returns(true);
            Mocker.GetMock<IDiskProvider>().Setup(d => d.FileExists(path)).Returns(true);
            Mocker.GetMock<IDiskProvider>().Setup(d => d.GetFileInfo(path)).Returns(Mock.Of<IFileInfo>(file => file.FullName == path));
            Mocker.GetMock<IDiskProvider>().Setup(d => d.GetFileSize(path)).Returns(512);
            Mocker.GetMock<IDiskScanService>()
                .Setup(d => d.GetBookFiles(_folder, true))
                .Returns(new[] { Mock.Of<IFileInfo>(file => file.FullName == path) });
            Mocker.GetMock<IMakeImportDecision>()
                .Setup(d => d.GetImportDecisions(
                    It.IsAny<List<IFileInfo>>(),
                    It.IsAny<IdentificationOverrides>(),
                    It.IsAny<ImportDecisionMakerInfo>(),
                    It.Is<ImportDecisionMakerConfig>(c => !c.AddNewAuthors)))
                .Returns(() => new List<ImportDecision<LocalBook>> { _decision });
        }

        [Test]
        public void should_prepopulate_the_exact_local_work_and_edition()
        {
            var result = Scan();
            result.Author.Should().BeSameAs(_owner);
            result.Book.Should().BeSameAs(_work);
            result.Edition.Should().BeSameAs(_edition);
            result.Rejections.Should().BeEmpty();
        }

        [TestCase(false)]
        [TestCase(true)]
        public void should_preserve_a_local_match_when_the_selected_owner_fills_a_missing_author(bool singleFile)
        {
            _candidate.Author = null;

            var result = Scan(singleFile, _owner);
            result.Author.Should().BeSameAs(_owner);
            result.Book.Should().BeSameAs(_work);
            result.Edition.Should().BeSameAs(_edition);
            result.Rejections.Should().BeEmpty();
        }

        [TestCase(false)]
        [TestCase(true)]
        public void should_hold_a_match_when_identification_returns_an_unresolved_provider_author(bool singleFile)
        {
            _candidate.Author = new Author { Metadata = _owner.Metadata };

            AssertHeld(Scan(singleFile, _owner));
        }

        [Test]
        public void should_hold_a_match_when_the_selected_author_does_not_own_the_work()
        {
            _candidate.Author = null;
            var selected = new Author
            {
                Id = 24,
                AuthorMetadataId = 21,
                Metadata = new AuthorMetadata { Name = _owner.Name, ForeignAuthorId = "OL21A" }
            };

            AssertHeld(Scan(author: selected));
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase(" ")]
        public void should_hold_a_local_edition_that_cannot_be_selected_by_the_import_command(string foreignEditionId)
        {
            _edition.ForeignEditionId = foreignEditionId;

            AssertHeld(Scan());
        }

        [Test]
        public void should_keep_the_existing_rejection_for_a_truly_unresolved_file()
        {
            _candidate.Author = null;
            _candidate.Book = null;
            _candidate.Edition = null;
            _decision.Reject(new Rejection("Couldn't find similar book for the fixture"));

            var result = Scan();
            result.Book.Should().BeNull();
            result.Edition.Should().BeNull();
            result.Rejections.Should().ContainSingle()
                .Which.Reason.Should().Be("Couldn't find similar book for the fixture");
        }

        [TestCase(false)]
        [TestCase(true)]
        public void should_hold_a_provider_only_candidate_even_when_it_has_an_edition_id(bool singleFile)
        {
            _owner.Id = 0;
            _owner.AuthorMetadataId = 0;
            _work.Id = 0;
            _work.AuthorMetadataId = 0;
            _edition.Id = 0;
            _edition.BookId = 0;

            var result = Subject.GetMediaFiles(singleFile ? _candidate.Path : _folder, null, null, FilterFilesType.Matched, true).Single();
            AssertHeld(result);
            result.Author.Should().BeNull();
        }

        [Test]
        public void should_prepopulate_provider_hits_resolved_to_the_exact_local_owner()
        {
            _candidate.Author = new Author { Metadata = _owner.Metadata };
            _candidate.Book = new Book { ForeignBookId = _work.ForeignBookId };
            _candidate.Edition = new Edition { ForeignEditionId = _edition.ForeignEditionId };
            Mocker.GetMock<IAuthorService>().Setup(a => a.FindById(_owner.ForeignAuthorId)).Returns(_owner);
            Mocker.GetMock<IBookService>().Setup(b => b.FindById(_work.ForeignBookId)).Returns(_work);
            Mocker.GetMock<IEditionService>()
                .Setup(e => e.GetEditionByForeignEditionId(_edition.ForeignEditionId))
                .Returns(_edition);

            var result = Scan();
            result.Author.Should().BeSameAs(_owner);
            result.Book.Should().BeSameAs(_work);
            result.Edition.Should().BeSameAs(_edition);
            result.Rejections.Should().BeEmpty();
        }

        [Test]
        public void should_resolve_the_exact_edition_when_the_work_is_already_local()
        {
            _candidate.Edition = new Edition { ForeignEditionId = _edition.ForeignEditionId };
            Mocker.GetMock<IEditionService>()
                .Setup(e => e.GetEditionByForeignEditionId(_edition.ForeignEditionId))
                .Returns(_edition);

            var result = Scan();
            result.Book.Should().BeSameAs(_work);
            result.Edition.Should().BeSameAs(_edition);
            result.Rejections.Should().BeEmpty();
        }

        [TestCase(0, 30, "Ebook")]
        [TestCase(40, 99, "Ebook")]
        [TestCase(40, 30, "Audiobook")]
        public void should_hold_an_uninstalled_foreign_owner_or_incompatible_edition(int editionId, int bookId, string format)
        {
            _edition.Id = editionId;
            _edition.BookId = bookId;
            _edition.Format = format;
            _edition.IsEbook = format == "Ebook";

            AssertHeld(Scan());
        }

        [Test]
        public void should_hold_a_work_returned_for_a_different_same_name_local_author()
        {
            _candidate.Author = new Author
            {
                Id = 24,
                AuthorMetadataId = 21,
                Metadata = new AuthorMetadata { Name = _owner.Name, ForeignAuthorId = "OL21A" }
            };

            AssertHeld(Scan());
        }

        private ManualImportItem Scan(bool singleFile = false, Author author = null)
        {
            return Subject.GetMediaFiles(singleFile ? _candidate.Path : _folder, null, author, FilterFilesType.Matched, true).Single();
        }

        private static void AssertHeld(ManualImportItem result)
        {
            result.Book.Should().BeNull();
            result.Edition.Should().BeNull();
            result.Rejections.Should().ContainSingle(r =>
                r.Type == RejectionType.Permanent && r.Reason == NoLocalEdition);
        }
    }
}
