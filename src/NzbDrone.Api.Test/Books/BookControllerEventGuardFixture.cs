using System.Collections.Generic;
using Bibliophilarr.Api.V1.Books;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.AuthorStats;
using NzbDrone.Core.Books;
using NzbDrone.Core.DecisionEngine.Specifications;
using NzbDrone.Core.MediaCover;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.Events;
using NzbDrone.Core.Profiles.Metadata;
using NzbDrone.Core.Profiles.Qualities;
using NzbDrone.Core.Validation;
using NzbDrone.SignalR;

namespace NzbDrone.Api.Test.Books
{
    [TestFixture]
    public class BookControllerEventGuardFixture
    {
        [Test]
        public void handle_book_file_deleted_should_ignore_missing_payload_without_throwing()
        {
            var controller = CreateController(Mock.Of<IBookService>(), Mock.Of<IBroadcastSignalRMessage>());

            Assert.DoesNotThrow(() => controller.Handle(new BookFileDeletedEvent(null, DeleteMediaFileReason.Manual)));
        }

        [Test]
        public void handle_book_imported_should_fetch_the_persisted_projection_before_broadcasting()
        {
            var author = new Author { Id = 5, Metadata = new AuthorMetadata { Name = "Fixture Author" } };
            var eventBook = new Book { Id = 12, Author = author };
            var bookService = new Mock<IBookService>();
            bookService.Setup(service => service.GetBook(12)).Returns(new Book { Id = 12, Author = author });
            var broadcaster = new Mock<IBroadcastSignalRMessage>();
            broadcaster.SetupGet(service => service.IsConnected).Returns(true);
            var controller = CreateController(bookService.Object, broadcaster.Object);

            controller.Handle(new BookImportedEvent(author, eventBook, new List<BookFile>(), new List<BookFile>(), false, null));

            bookService.Verify(service => service.GetBook(12), Times.Once);
            broadcaster.Verify(service => service.BroadcastMessage(It.IsAny<SignalRMessage>()), Times.Once);
        }

        private static BookController CreateController(IBookService bookService, IBroadcastSignalRMessage broadcaster)
        {
            var qualityProfileValidator = new QualityProfileExistsValidator(Mock.Of<IQualityProfileService>());
            var metadataProfileValidator = new MetadataProfileExistsValidator(Mock.Of<IMetadataProfileService>());

            return new BookController(
                Mock.Of<IAuthorService>(),
                bookService,
                Mock.Of<IAddBookService>(),
                Mock.Of<IEditionService>(),
                Mock.Of<ISeriesBookLinkService>(),
                Mock.Of<IAuthorStatisticsService>(),
                Mock.Of<IMapCoversToLocal>(),
                Mock.Of<IUpgradableSpecification>(),
                broadcaster,
                Mock.Of<IAuthorFormatProfileService>(),
                Mock.Of<IQualityProfileService>(),
                qualityProfileValidator,
                metadataProfileValidator);
        }
    }
}
