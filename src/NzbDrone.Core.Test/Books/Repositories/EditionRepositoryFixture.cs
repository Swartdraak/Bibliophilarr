using System.Collections.Generic;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.BooksTests.Repositories
{
    [TestFixture]
    public class EditionRepositoryFixture : DbTest<EditionRepository, Edition>
    {
        private Edition InsertEdition(int bookId, bool isEbook, bool monitored, string format = null)
        {
            var edition = new Edition
            {
                BookId = bookId,
                IsEbook = isEbook,
                Format = format,
                Monitored = monitored,
                ForeignEditionId = $"edition-{bookId}-{isEbook}-{monitored}-{System.Guid.NewGuid():N}",
                Title = "Test Edition",
                TitleSlug = $"test-{System.Guid.NewGuid():N}"
            };

            Db.Insert(edition);
            return edition;
        }

        [TestCase(true)]
        [TestCase(false)]
        public void adding_a_selected_audiobook_should_reuse_the_existing_ebook_and_preserve_book_options(bool dualFormat)
        {
            var author = Db.Insert(new AuthorMetadata
            {
                ForeignAuthorId = "hardcover:author:730",
                Name = "Local Author",
                TitleSlug = "local-author",
                SortName = "Local Author",
                NameLastFirst = "Author, Local",
                SortNameLastFirst = "author, local"
            });
            var book = Db.Insert(new Book
            {
                AuthorMetadataId = author.Id,
                ForeignBookId = "hardcover:work:732",
                Title = "Stored title",
                CleanTitle = "stored title",
                TitleSlug = "stored-title",
                AddOptions = new AddBookOptions { SearchForNewBook = true }
            });
            var ebook = InsertEdition(book.Id, isEbook: true, monitored: true, format: "Ebook");
            var audio = new Edition
            {
                ForeignEditionId = "hardcover:edition:734",
                Title = "Selected audiobook",
                TitleSlug = "selected-audiobook",
                Format = "Audiobook"
            };
            book.Editions = new List<Edition>
            {
                new Edition { ForeignEditionId = ebook.ForeignEditionId, Title = "Remote title", IsEbook = true },
                audio
            };

            var saved = Subject.AddEditionsForExistingBook(book, audio, dualFormat);
            saved.Should().HaveCount(2);
            saved.Should().ContainSingle(edition => edition.Id == ebook.Id && edition.Title == ebook.Title && edition.Monitored == dualFormat);
            saved.Should().ContainSingle(edition => edition.Id == audio.Id && edition.Id > 0 && edition.Monitored && edition.ManualAdd);
            Db.All<Book>().Should().ContainSingle(work =>
                work.Id == book.Id && work.Title == "Stored title" && work.AddOptions.SearchForNewBook &&
                work.AddOptions.AddType == BookAddType.Manual);
        }

        [Test]
        public void set_monitored_by_format_should_monitor_matching_format_edition()
        {
            var ebook = InsertEdition(bookId: 1, isEbook: true, monitored: false);
            var audiobook = InsertEdition(bookId: 1, isEbook: false, monitored: true);

            var result = Subject.SetMonitoredByFormat(ebook);

            result.Should().HaveCount(2);
            var stored = Db.All<Edition>();

            // The matching-format (ebook) edition is monitored.
            stored.Should().Contain(e => e.Id == ebook.Id && e.Monitored);

            // The other format (audiobook) is untouched and remains monitored
            // (dual-format tracking preserves monitoring of the other format).
            stored.Should().Contain(e => e.Id == audiobook.Id && e.Monitored);
        }

        [Test]
        public void setting_the_audiobook_edition_should_preserve_print_monitoring()
        {
            var print = InsertEdition(bookId: 1, isEbook: false, monitored: true, format: "Print");
            var audio = InsertEdition(bookId: 1, isEbook: false, monitored: false, format: "Audio CD");

            Subject.SetMonitoredByFormat(audio);

            var stored = Db.All<Edition>();
            stored.Should().Contain(edition => edition.Id == print.Id && edition.Monitored);
            stored.Should().Contain(edition => edition.Id == audio.Id && edition.Monitored);
        }

        [Test]
        public void set_monitored_by_format_should_not_throw_when_no_same_format_edition_matches()
        {
            var ebook = InsertEdition(bookId: 1, isEbook: true, monitored: false);
            var audio = InsertEdition(bookId: 1, isEbook: false, monitored: true);
            ebook.IsEbook = false;

            Subject.SetMonitoredByFormat(ebook);

            var stored = Db.All<Edition>();
            stored.Should().Contain(edition => edition.Id == ebook.Id && edition.IsEbook && !edition.Monitored);
            stored.Should().Contain(edition => edition.Id == audio.Id && edition.Monitored);
            ebook.IsEbook.Should().BeFalse();
            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void set_monitored_by_format_should_not_throw_when_edition_id_not_in_book()
        {
            // The imported edition id is not present in the book stored editions
            // (stale/unsaved edition id). Previously this threw an ArgumentException.
            var ebook = InsertEdition(bookId: 1, isEbook: true, monitored: true);

            var staleEdition = new Edition
            {
                Id = 12345,
                BookId = 1,
                IsEbook = true,
                Monitored = false,
                ForeignEditionId = "stale-edition",
                Title = "Stale",
                TitleSlug = "stale"
            };

            var act = () => Subject.SetMonitoredByFormat(staleEdition);

            act.Should().NotThrow();

            Db.All<Edition>().Should().ContainSingle(edition => edition.Id == ebook.Id && edition.Monitored);
            ExceptionVerification.ExpectedWarns(1);
        }
    }
}
