using System.Collections.Generic;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Books;

namespace NzbDrone.Core.Test.BooksTests
{
    [TestFixture]
    public class BookEditionSelectorFixture
    {
        [Test]
        public void should_return_null_when_book_has_no_editions()
        {
            var book = new Book
            {
                Editions = new List<Edition>()
            };

            book.GetPreferredEdition().Should().BeNull();
        }

        [Test]
        public void should_return_monitored_edition_when_single_monitored_exists()
        {
            var first = new Edition { ForeignEditionId = "edition-1", Monitored = false };
            var monitored = new Edition { ForeignEditionId = "edition-2", Monitored = true };

            var book = new Book
            {
                Editions = new List<Edition> { first, monitored }
            };

            book.GetPreferredEdition().Should().BeSameAs(monitored);
        }

        [Test]
        public void should_fallback_to_first_edition_when_none_are_monitored()
        {
            var first = new Edition { ForeignEditionId = "edition-1", Monitored = false };
            var second = new Edition { ForeignEditionId = "edition-2", Monitored = false };

            var book = new Book
            {
                Editions = new List<Edition> { first, second }
            };

            book.GetPreferredEdition().Should().BeSameAs(first);
        }

        [Test]
        public void should_select_first_monitored_edition_when_multiple_are_marked_monitored()
        {
            var firstMonitored = new Edition { ForeignEditionId = "edition-1", Monitored = true };
            var secondMonitored = new Edition { ForeignEditionId = "edition-2", Monitored = true };

            var book = new Book
            {
                Editions = new List<Edition> { firstMonitored, secondMonitored }
            };

            book.GetPreferredEdition().Should().BeSameAs(firstMonitored);
        }
    }
}
