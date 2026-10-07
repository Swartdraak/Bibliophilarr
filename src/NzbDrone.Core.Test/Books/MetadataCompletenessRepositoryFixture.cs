using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.BooksTests
{
    [TestFixture]
    public class MetadataCompletenessRepositoryFixture : DbTest<MetadataCompletenessRepository, Book>
    {
        [Test]
        public void get_stats_should_report_hydration_gaps()
        {
            // 3 books: 1 with OpenLibraryWorkId, 2 without.
            var bookWithOlId = Db.Insert(new Book { ForeignBookId = "hardcover:work:1", TitleSlug = "hardcover-work-1", Title = "Book 1", CleanTitle = "Book 1", OpenLibraryWorkId = "OL1W" });
            Db.Insert(new Book { ForeignBookId = "hardcover:work:2", TitleSlug = "hardcover-work-2", Title = "Book 2", CleanTitle = "Book 2" });
            Db.Insert(new Book { ForeignBookId = "hardcover:work:3", TitleSlug = "hardcover-work-3", Title = "Book 3", CleanTitle = "Book 3" });

            // 2 series: 1 linked to a book, 1 with no links.
            var linkedSeries = Db.Insert(new Series { ForeignSeriesId = "hardcover:series:1", Title = "Linked" });
            Db.Insert(new Series { ForeignSeriesId = "hardcover:series:2", Title = "Unlinked" });

            // 1 series-book link (links the first series to the first book).
            Db.Insert(new SeriesBookLink { SeriesId = linkedSeries.Id, BookId = bookWithOlId.Id });

            // 2 editions: 1 with a book file, 1 without.
            var editionWithFile = Db.Insert(new Edition { BookId = bookWithOlId.Id, ForeignEditionId = "hardcover:edition:1", TitleSlug = "book-1-edition", Title = "Book 1 Edition", IsEbook = true });
            Db.Insert(new Edition { BookId = bookWithOlId.Id, ForeignEditionId = "hardcover:edition:2", TitleSlug = "book-1-audiobook", Title = "Book 1 Audiobook", IsEbook = false });

            // 1 book file linked to the first edition.
            Db.Insert(new BookFile { Path = "/test/book1.epub", EditionId = editionWithFile.Id, Quality = new QualityModel(Quality.EPUB) });

            // 1 author.
            Db.Insert(new Author { ForeignAuthorId = "hardcover:author:1", CleanName = "Test Author", Path = "/test/author" });

            var stats = Subject.GetStats();

            stats.TotalBooks.Should().Be(3);
            stats.BooksWithOpenLibraryWorkId.Should().Be(1);
            stats.BooksMissingOpenLibraryWorkId.Should().Be(2);

            stats.TotalSeries.Should().Be(2);
            stats.SeriesWithLinkedBooks.Should().Be(1);
            stats.SeriesWithoutLinkedBooks.Should().Be(1);

            stats.TotalEditions.Should().Be(2);
            stats.EditionsWithBookFile.Should().Be(1);
            stats.EditionsWithoutBookFile.Should().Be(1);
            stats.TotalBookFiles.Should().Be(1);

            stats.TotalAuthors.Should().Be(1);
        }

        [Test]
        public void get_stats_should_return_zeros_for_empty_database()
        {
            var stats = Subject.GetStats();

            stats.TotalBooks.Should().Be(0);
            stats.BooksWithOpenLibraryWorkId.Should().Be(0);
            stats.BooksMissingOpenLibraryWorkId.Should().Be(0);
            stats.TotalSeries.Should().Be(0);
            stats.SeriesWithLinkedBooks.Should().Be(0);
            stats.SeriesWithoutLinkedBooks.Should().Be(0);
            stats.TotalEditions.Should().Be(0);
            stats.EditionsWithBookFile.Should().Be(0);
            stats.EditionsWithoutBookFile.Should().Be(0);
            stats.TotalBookFiles.Should().Be(0);
            stats.TotalAuthors.Should().Be(0);
        }
    }
}
