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
        private Edition InsertEdition(int bookId, bool isEbook, bool monitored)
        {
            var edition = new Edition
            {
                BookId = bookId,
                IsEbook = isEbook,
                Monitored = monitored,
                ForeignEditionId = $"edition-{bookId}-{isEbook}-{monitored}-{System.Guid.NewGuid():N}",
                Title = "Test Edition",
                TitleSlug = $"test-{System.Guid.NewGuid():N}"
            };

            Db.Insert(edition);
            return edition;
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
        public void set_monitored_by_format_should_not_throw_when_no_same_format_edition_matches()
        {
            // The imported edition IsEbook does not match any stored edition for the book
            // (format misclassification). Previously this threw an ArgumentException and
            // aborted the entire RescanFolders batch (see issue #203).
            InsertEdition(bookId: 1, isEbook: true, monitored: false);

            var mismatchedEdition = new Edition
            {
                Id = 999,
                BookId = 1,
                IsEbook = false,
                Monitored = false,
                ForeignEditionId = "mismatched-edition",
                Title = "Mismatched",
                TitleSlug = "mismatched"
            };

            var act = () => Subject.SetMonitoredByFormat(mismatchedEdition);

            act.Should().NotThrow();

            ExceptionVerification.ExpectedWarns(2);
        }

        [Test]
        public void set_monitored_by_format_should_not_throw_when_edition_id_not_in_book()
        {
            // The imported edition id is not present in the book stored editions
            // (stale/unsaved edition id). Previously this threw an ArgumentException.
            InsertEdition(bookId: 1, isEbook: true, monitored: false);

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

            ExceptionVerification.ExpectedWarns(2);
        }
    }
}
