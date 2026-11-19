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
    public class CatalogueWorkReconciliationFixture : DbTest<CatalogueWorkReconciliationService, Book>
    {
        [Test]
        public void should_preserve_files_formats_monitoring_and_legacy_routes()
        {
            var author = Db.Insert(new AuthorMetadata { ForeignAuthorId = "hardcover:author:400" });
            var target = AddWork(author.Id, 111, "OL411W");
            var source = AddWork(author.Id, 112, "OL411W");
            var ebook = Db.Insert(new Edition { BookId = target.Id, ForeignEditionId = "hardcover:edition:1011", IsEbook = true, Monitored = true });
            var audio = Db.Insert(new Edition { BookId = source.Id, ForeignEditionId = "hardcover:edition:1012", Format = "Audiobook", Monitored = true });
            var epub = Db.Insert(new BookFile { Path = "/library/book.epub", EditionId = ebook.Id, Quality = new QualityModel(Quality.EPUB) });
            var m4b = Db.Insert(new BookFile { Path = "/library/book.m4b", EditionId = audio.Id, Quality = new QualityModel(Quality.M4B) });
            var series = Db.Insert(new Series { ForeignSeriesId = "hardcover:series:99", Title = "Saga" });
            Db.Insert(new SeriesBookLink { BookId = target.Id, SeriesId = series.Id, Position = "", IsPrimary = true });
            Db.Insert(new SeriesBookLink { BookId = source.Id, SeriesId = series.Id, Position = "3", SeriesPosition = 3 });

            var command = new ReconcileCatalogueWorksCommand { SourceBookId = source.Id, TargetBookId = target.Id };
            Subject.Execute(command);
            AllStoredModels.Should().HaveCount(2);

            command.DryRun = false;
            Subject.Execute(command);
            Subject.Execute(command);

            StoredModel.CanonicalId.Should().Be(target.CanonicalId);
            StoredModel.IsFormatMonitored(FormatType.Ebook).Should().BeTrue();
            StoredModel.IsFormatMonitored(FormatType.Audiobook).Should().BeTrue();
            Db.All<Edition>().Select(edition => edition.BookId).Should().Equal(target.Id, target.Id);
            Db.All<BookFile>().ToDictionary(file => file.Id, file => file.EditionId).Should()
                .Contain(epub.Id, ebook.Id).And.Contain(m4b.Id, audio.Id);
            Db.All<SeriesBookLink>().Should().ContainSingle().Which.Position.Should().Be("3");

            var identity = Mocker.Resolve<CatalogueIdentityRepository>();
            identity.FindByRoute(CatalogueEntityKind.Work, source.CanonicalId).Should().Be(target.Id);
            identity.FindByRoute(CatalogueEntityKind.Work, source.TitleSlug).Should().Be(target.Id);
            identity.FindByProviderId(CatalogueEntityKind.Work, "Hardcover", "112").Should().Be(target.Id);
        }

        [Test]
        public void should_leave_same_title_with_conflicting_work_identity_unchanged()
        {
            var author = Db.Insert(new AuthorMetadata { ForeignAuthorId = "hardcover:author:500" });
            var target = AddWork(author.Id, 211, "OL411W");
            var source = AddWork(author.Id, 212, "OL412W");

            var action = () => Subject.Execute(new ReconcileCatalogueWorksCommand { SourceBookId = source.Id, TargetBookId = target.Id, DryRun = false });
            action.Should().Throw<System.InvalidOperationException>();
            AllStoredModels.Should().HaveCount(2);
        }

        private Book AddWork(int authorId, int providerId, string openLibraryId)
        {
            return Storage.Insert(new Book
            {
                AuthorMetadataId = authorId,
                ForeignBookId = $"hardcover:work:{providerId}",
                Title = "Book",
                CleanTitle = "book",
                TitleSlug = $"hardcover-work-{providerId}",
                OpenLibraryWorkId = openLibraryId,
                Monitored = true
            });
        }
    }
}
