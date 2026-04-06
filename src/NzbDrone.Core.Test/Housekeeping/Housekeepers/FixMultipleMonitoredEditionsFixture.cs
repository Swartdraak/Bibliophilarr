using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Housekeeping.Housekeepers;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Housekeeping.Housekeepers
{
    [TestFixture]
    public class FixMultipleMonitoredEditionsFixture : DbTest<FixMultipleMonitoredEditions, Edition>
    {
        private void SetDualFormatFlag(bool enabled)
        {
            Mocker.GetMock<IConfigService>()
                .Setup(s => s.EnableDualFormatTracking)
                .Returns(enabled);
        }

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
        public void flag_off_should_unmonitor_duplicate_for_same_book()
        {
            SetDualFormatFlag(false);

            InsertEdition(bookId: 1, isEbook: true, monitored: true);
            InsertEdition(bookId: 1, isEbook: false, monitored: true);

            Subject.Clean();

            // With flag off, only one monitored edition per book is allowed.
            // Housekeeping will un-monitor one of them.
            AllStoredModels.Should().Contain(e => e.Monitored);
        }

        [Test]
        public void flag_off_single_monitored_should_not_be_touched()
        {
            SetDualFormatFlag(false);

            var monitored = InsertEdition(bookId: 1, isEbook: true, monitored: true);
            InsertEdition(bookId: 1, isEbook: false, monitored: false);

            Subject.Clean();

            var result = Db.All<Edition>();
            result.Should().ContainSingle(e => e.Monitored)
                .Which.Id.Should().Be(monitored.Id);
        }

        [Test]
        public void flag_on_should_preserve_both_formats_monitored()
        {
            SetDualFormatFlag(true);

            var ebook = InsertEdition(bookId: 1, isEbook: true, monitored: true);
            var audiobook = InsertEdition(bookId: 1, isEbook: false, monitored: true);

            Subject.Clean();

            var result = Db.All<Edition>();
            result.Should().HaveCount(2);
            result.Should().OnlyContain(e => e.Monitored,
                "both formats should remain monitored when dual-format tracking is enabled");
        }

        [Test]
        public void flag_on_should_unmonitor_duplicate_within_same_format()
        {
            SetDualFormatFlag(true);

            InsertEdition(bookId: 1, isEbook: true, monitored: true);
            InsertEdition(bookId: 1, isEbook: true, monitored: true);

            Subject.Clean();

            // Two ebook editions both monitored — housekeeping should un-monitor one
            var result = Db.All<Edition>();
            result.Should().HaveCount(2);
            result.Should().ContainSingle(e => e.Monitored && e.IsEbook);
        }

        [Test]
        public void flag_on_should_not_touch_different_books()
        {
            SetDualFormatFlag(true);

            InsertEdition(bookId: 1, isEbook: true, monitored: true);
            InsertEdition(bookId: 2, isEbook: true, monitored: true);

            Subject.Clean();

            var result = Db.All<Edition>();
            result.Should().HaveCount(2);
            result.Should().OnlyContain(e => e.Monitored,
                "editions for different books should not affect each other");
        }
    }
}
