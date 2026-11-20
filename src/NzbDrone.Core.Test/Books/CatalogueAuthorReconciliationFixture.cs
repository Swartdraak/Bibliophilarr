using System;
using System.Linq;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.Books.Commands;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.BooksTests
{
    [TestFixture]
    public class CatalogueAuthorReconciliationFixture : DbTest<CatalogueAuthorReconciliationService, Author>
    {
        [Test]
        public void should_reconcile_an_explicit_pair_and_preserve_routes_editions_and_files()
        {
            var target = AddAuthor("openlibrary:author:OL5A", "/library/author", "prior");
            var source = AddAuthor("hardcover:author:5", "/library/author", "alias");
            var targetWork = AddWork(target, "openlibrary:work:OL43W", "OL43W");
            var sourceWork = AddWork(source, "hardcover:work:43", "OL43W");
            var edition = Db.Insert(new Edition
            {
                BookId = sourceWork.Id,
                ForeignEditionId = "hardcover:source-edition:41",
                Title = "Work",
                TitleSlug = "work",
                Format = "Audiobook"
            });
            var file = Db.Insert(new BookFile { EditionId = edition.Id, Path = "/library/author/work.m4b", Quality = new QualityModel(Quality.M4B) });
            Db.Insert(new AuthorFormatProfile { AuthorId = source.Id, FormatType = FormatType.Audiobook, RootFolderPath = "/audio" });
            Db.Insert(new AuthorFormatProfile { AuthorId = target.Id, FormatType = FormatType.Audiobook, RootFolderPath = "/audio" });
            Mocker.GetMock<IAuthorCanonicalizationService>().Setup(service => service.VerifyDistinctAuthorities(source, target)).Returns(true);
            var command = new ReconcileCatalogueAuthorsCommand { SourceAuthorId = source.Id, TargetAuthorId = target.Id };

            Subject.Execute(command);
            AllStoredModels.Should().HaveCount(2);

            command.DryRun = false;
            Subject.Execute(command);

            AllStoredModels.Should().ContainSingle().Which.Id.Should().Be(target.Id);
            Db.All<Book>().Should().ContainSingle().Which.Id.Should().Be(targetWork.Id);
            Db.All<Edition>().Should().ContainSingle().Which.BookId.Should().Be(targetWork.Id);
            Db.All<BookFile>().Should().ContainSingle().Which.Id.Should().Be(file.Id);
            Db.All<BookFile>().Single().Path.Should().Be(file.Path);
            Db.All<AuthorFormatProfile>().Should().ContainSingle().Which.AuthorId.Should().Be(target.Id);
            var identity = Mocker.Resolve<CatalogueIdentityRepository>();
            identity.FindByRoute(CatalogueEntityKind.Author, source.Metadata.Value.CanonicalId).Should().Be(target.AuthorMetadataId);
            Mocker.Resolve<AuthorRepository>().FindByRetiredId(source.Id).Id.Should().Be(target.Id);
            identity.FindByRoute(CatalogueEntityKind.Author, source.Metadata.Value.TitleSlug).Should().Be(target.AuthorMetadataId);
            identity.FindByProviderId(CatalogueEntityKind.Author, "Hardcover", "5").Should().Be(target.AuthorMetadataId);
        }

        [Test]
        public void should_leave_both_author_scopes_intact_when_paths_differ()
        {
            var target = AddAuthor("openlibrary:author:OL5A", "/library/first", "prior");
            var source = AddAuthor("hardcover:author:5", "/library/second", "alias");
            var command = new ReconcileCatalogueAuthorsCommand { SourceAuthorId = source.Id, TargetAuthorId = target.Id, DryRun = false };

            Action action = () => Subject.Execute(command);

            action.Should().Throw<InvalidOperationException>().WithMessage("*paths*");
            AllStoredModels.Should().HaveCount(2);
        }

        [Test]
        public void should_roll_back_an_earlier_work_reconciliation_when_a_later_pair_conflicts()
        {
            var target = AddAuthor("openlibrary:author:OL5A", "/library/author", "prior");
            var source = AddAuthor("hardcover:author:5", "/library/author", "alias");
            var retained = AddWork(target, "openlibrary:work:OL43W", "OL43W");
            var retiring = AddWork(source, "hardcover:work:43", "OL43W");
            var otherRetained = AddWork(target, "openlibrary:work:OL44W", "OL44W");
            var otherRetiring = AddWork(source, "hardcover:work:44", "OL44W");
            otherRetained.AudiobookMonitoringOverride = false;
            otherRetiring.AudiobookMonitoringOverride = true;
            Db.Update(otherRetained);
            Db.Update(otherRetiring);
            Mocker.GetMock<IAuthorCanonicalizationService>().Setup(service => service.VerifyDistinctAuthorities(source, target)).Returns(true);
            var command = new ReconcileCatalogueAuthorsCommand { SourceAuthorId = source.Id, TargetAuthorId = target.Id, DryRun = false };

            Action action = () => Subject.Execute(command);

            action.Should().Throw<InvalidOperationException>().WithMessage("*monitoring overrides*");
            AllStoredModels.Should().HaveCount(2);
            Db.All<Book>().Select(book => book.Id).Should().BeEquivalentTo(new[] { retained.Id, retiring.Id, otherRetained.Id, otherRetiring.Id });
            Mocker.Resolve<CatalogueIdentityRepository>().FindByProviderId(CatalogueEntityKind.Work, "Hardcover", "43").Should().Be(retiring.Id);
        }

        private Author AddAuthor(string foreignId, string path, string slug)
        {
            var metadata = Db.Insert(new AuthorMetadata
            {
                ForeignAuthorId = foreignId,
                TitleSlug = slug,
                Name = "Test Author",
                SortName = "test author",
                NameLastFirst = "Author, Test",
                SortNameLastFirst = "author, test"
            });
            var author = Storage.Insert(new Author { AuthorMetadataId = metadata.Id, Metadata = metadata, Path = path, CleanName = "testauthor" });
            Mocker.GetMock<IAuthorRepository>().Setup(repository => repository.Find(author.Id)).Returns(author);
            return author;
        }

        private Book AddWork(Author author, string foreignId, string openLibraryId)
        {
            return Db.Insert(new Book
            {
                AuthorMetadataId = author.AuthorMetadataId,
                ForeignBookId = foreignId,
                OpenLibraryWorkId = openLibraryId,
                Title = "Work",
                CleanTitle = "work",
                TitleSlug = foreignId
            });
        }
    }
}
