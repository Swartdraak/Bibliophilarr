using System.Collections.Generic;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Books;

namespace NzbDrone.Core.Test.BooksTests
{
    [TestFixture]
    public class BookEditionSelectorFormatFixture
    {
        [Test]
        public void should_return_null_when_no_editions_match_format()
        {
            var ebookEdition = new Edition { ForeignEditionId = "ebook-1", IsEbook = true, Monitored = true };

            var book = new Book
            {
                Editions = new List<Edition> { ebookEdition }
            };

            book.GetPreferredEdition(FormatType.Audiobook).Should().BeNull();
        }

        [Test]
        public void should_return_null_when_editions_null()
        {
            IEnumerable<Edition> editions = null;
            editions.GetPreferredEdition(FormatType.Ebook).Should().BeNull();
        }

        [Test]
        public void should_return_monitored_ebook_edition_for_ebook_format()
        {
            var ebookMonitored = new Edition { ForeignEditionId = "ebook-1", IsEbook = true, Monitored = true };
            var audioMonitored = new Edition { ForeignEditionId = "audio-1", IsEbook = false, Monitored = true };

            var book = new Book
            {
                Editions = new List<Edition> { ebookMonitored, audioMonitored }
            };

            book.GetPreferredEdition(FormatType.Ebook).Should().BeSameAs(ebookMonitored);
        }

        [Test]
        public void should_return_monitored_audiobook_edition_for_audiobook_format()
        {
            var ebookMonitored = new Edition { ForeignEditionId = "ebook-1", IsEbook = true, Monitored = true };
            var audioMonitored = new Edition { ForeignEditionId = "audio-1", IsEbook = false, Monitored = true };

            var book = new Book
            {
                Editions = new List<Edition> { ebookMonitored, audioMonitored }
            };

            book.GetPreferredEdition(FormatType.Audiobook).Should().BeSameAs(audioMonitored);
        }

        [Test]
        public void should_fallback_to_first_matching_format_when_none_monitored()
        {
            var ebook1 = new Edition { ForeignEditionId = "ebook-1", IsEbook = true, Monitored = false };
            var ebook2 = new Edition { ForeignEditionId = "ebook-2", IsEbook = true, Monitored = false };

            var book = new Book
            {
                Editions = new List<Edition> { ebook1, ebook2 }
            };

            book.GetPreferredEdition(FormatType.Ebook).Should().BeSameAs(ebook1);
        }

        [Test]
        public void should_prefer_monitored_over_first_within_format()
        {
            var ebookFirst = new Edition { ForeignEditionId = "ebook-1", IsEbook = true, Monitored = false };
            var ebookMonitored = new Edition { ForeignEditionId = "ebook-2", IsEbook = true, Monitored = true };

            var book = new Book
            {
                Editions = new List<Edition> { ebookFirst, ebookMonitored }
            };

            book.GetPreferredEdition(FormatType.Ebook).Should().BeSameAs(ebookMonitored);
        }

        [Test]
        public void should_not_return_wrong_format_even_if_monitored()
        {
            var audioMonitored = new Edition { ForeignEditionId = "audio-1", IsEbook = false, Monitored = true };
            var ebookNotMonitored = new Edition { ForeignEditionId = "ebook-1", IsEbook = true, Monitored = false };

            var book = new Book
            {
                Editions = new List<Edition> { audioMonitored, ebookNotMonitored }
            };

            // Requesting ebook format should return the unmonitored ebook, not the monitored audiobook
            book.GetPreferredEdition(FormatType.Ebook).Should().BeSameAs(ebookNotMonitored);
        }

        [Test]
        public void original_overload_should_still_return_any_monitored_regardless_of_format()
        {
            var audioMonitored = new Edition { ForeignEditionId = "audio-1", IsEbook = false, Monitored = true };
            var ebookNotMonitored = new Edition { ForeignEditionId = "ebook-1", IsEbook = true, Monitored = false };

            var book = new Book
            {
                Editions = new List<Edition> { audioMonitored, ebookNotMonitored }
            };

            // Original overload is format-agnostic
            book.GetPreferredEdition().Should().BeSameAs(audioMonitored);
        }
    }
}
