using System;
using System.Collections.Generic;
using Bibliophilarr.Api.V1.Books;
using Bibliophilarr.Api.V1.Calendar;
using Microsoft.AspNetCore.Mvc;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.AuthorStats;
using NzbDrone.Core.Books;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.DecisionEngine.Specifications;
using NzbDrone.Core.MediaCover;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.Events;
using NzbDrone.Core.Profiles.Metadata;
using NzbDrone.Core.Profiles.Qualities;
using NzbDrone.Core.Tags;
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
            var qualityProfileValidator = new QualityProfileExistsValidator(Mock.Of<IQualityProfileService>());
            var metadataProfileValidator = new MetadataProfileExistsValidator(Mock.Of<IMetadataProfileService>());

            var controller = new BookController(
                Mock.Of<IAuthorService>(),
                Mock.Of<IBookService>(),
                Mock.Of<IAddBookService>(),
                Mock.Of<IEditionService>(),
                Mock.Of<ISeriesBookLinkService>(),
                Mock.Of<IAuthorStatisticsService>(),
                Mock.Of<IMapCoversToLocal>(),
                Mock.Of<IUpgradableSpecification>(),
                Mock.Of<IBroadcastSignalRMessage>(),
                Mock.Of<IAuthorFormatProfileService>(),
                Mock.Of<IQualityProfileService>(),
                qualityProfileValidator,
                metadataProfileValidator);

            Assert.DoesNotThrow(() => controller.Handle(new BookFileDeletedEvent(null, DeleteMediaFileReason.Manual)));
        }

        [Test]
        public void calendar_feed_should_serialize_all_day_release_events()
        {
            var author = new Author
            {
                Id = 10,
                Metadata = new LazyLoaded<AuthorMetadata>(new AuthorMetadata { Name = "Test Author" }),
                Tags = new HashSet<int>()
            };

            var book = new Book
            {
                Id = 42,
                Title = "Test Book",
                ReleaseDate = new DateTime(2025, 03, 15, 0, 0, 0, DateTimeKind.Local),
                Genres = new List<string> { "Fiction" },
                Author = new LazyLoaded<Author>(author),
                Editions = new LazyLoaded<List<Edition>>(new List<Edition>
                {
                    new Edition { Monitored = true, Overview = "A test overview." }
                })
            };

            var bookService = new Mock<IBookService>();
            bookService.Setup(s => s.BooksBetweenDates(It.IsAny<DateTime>(), It.IsAny<DateTime>(), false))
                .Returns(new List<Book> { book });

            var authorService = new Mock<IAuthorService>();
            authorService.Setup(s => s.GetAuthor(book.AuthorId)).Returns(author);

            var tagService = new Mock<ITagService>();
            var controller = new CalendarFeedController(bookService.Object, authorService.Object, tagService.Object);

            var result = controller.GetCalendarFeed() as ContentResult;

            Assert.That(result, Is.Not.Null);
            Assert.That(result.ContentType, Is.EqualTo("text/calendar"));
            Assert.That(result.Content, Does.Contain("BEGIN:VCALENDAR"));
            Assert.That(result.Content, Does.Contain("SUMMARY:Test Author - Test Book"));
            Assert.That(result.Content, Does.Contain("DTSTART;VALUE=DATE:20250315"));
        }
    }
}
