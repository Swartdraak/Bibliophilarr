using NUnit.Framework;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.Events;
using NzbDrone.Core.Notifications;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.NotificationTests
{
    [TestFixture]
    public class NotificationServiceBookFileDeletedEventGuardFixture : CoreTest<NotificationService>
    {
        [Test]
        public void should_ignore_missing_book_file_payload_without_throwing()
        {
            Assert.DoesNotThrow(() => Subject.Handle(new BookFileDeletedEvent(null, DeleteMediaFileReason.Manual)));
        }
    }

    [TestFixture]
    public class MediaFileDeletionServiceBookFileDeletedEventGuardFixture : CoreTest<Core.MediaFiles.MediaFileDeletionService>
    {
        [Test]
        public void should_ignore_missing_book_file_payload_without_throwing()
        {
            Assert.DoesNotThrow(() => Subject.Handle(new BookFileDeletedEvent(null, DeleteMediaFileReason.Manual)));
        }
    }
}
