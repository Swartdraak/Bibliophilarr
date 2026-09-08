using System;
using System.Collections.Generic;
using System.Linq;
using Bibliophilarr.Api.V1.Calendar;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Tags;

namespace NzbDrone.Api.Test.Calendar
{
    [TestFixture]
    public class CalendarFeedControllerFixture
    {
        [Test]
        public void calendar_feed_should_round_trip_all_day_release_events()
        {
            var author = new Author
            {
                Metadata = new AuthorMetadata
                {
                    Name = "Author Name"
                },
                Tags = new HashSet<int>()
            };

            var releaseDate = new DateTime(2025, 3, 15, 0, 0, 0, DateTimeKind.Unspecified);
            var edition = new Edition
            {
                Monitored = true,
                Overview = "A release description",
                Title = "Example Book"
            };

            var book = new Book
            {
                Id = 42,
                Title = "Example Book",
                ReleaseDate = releaseDate,
                Author = new LazyLoaded<Author>(author),
                Editions = new LazyLoaded<List<Edition>>(new List<Edition> { edition }),
                Genres = new List<string> { "Fiction" }
            };

            var bookService = new Mock<IBookService>();
            bookService.Setup(s => s.BooksBetweenDates(It.IsAny<DateTime>(), It.IsAny<DateTime>(), false))
                .Returns(new List<Book> { book });

            var authorService = new Mock<IAuthorService>();
            authorService.Setup(s => s.GetAuthor(book.AuthorId)).Returns(author);

            var tagService = new Mock<ITagService>();
            var controller = new CalendarFeedController(bookService.Object, authorService.Object, tagService.Object);

            var result = controller.GetCalendarFeed();
            var content = result as Microsoft.AspNetCore.Mvc.ContentResult;
            Assert.NotNull(content);
            Assert.AreEqual("text/calendar", content.ContentType);

            var calendarText = content.Content;
            Assert.That(calendarText, Does.Contain("DTSTART;VALUE=DATE:20250315"));
            Assert.That(calendarText, Does.Contain("DTEND;VALUE=DATE:20250316"));
            Assert.That(calendarText, Does.Contain("SUMMARY:Author Name - Example Book"));
            Assert.That(calendarText, Does.Contain("DESCRIPTION:A release description"));

            var parsed = global::Ical.Net.Calendar.Load(calendarText);
            Assert.That(parsed, Is.Not.Null);
            Assert.That(parsed.Events, Is.Not.Empty);

            var parsedEvent = parsed.Events.Single();
            Assert.That(parsedEvent.Start.Value.Date, Is.EqualTo(new DateTime(2025, 3, 15)));
            Assert.That(parsedEvent.End.Value.Date, Is.EqualTo(new DateTime(2025, 3, 16)));
            Assert.That(parsedEvent.IsAllDay, Is.True);
            Assert.That(parsedEvent.Start.HasTime, Is.False);
            Assert.That(parsedEvent.End.HasTime, Is.False);
            Assert.That(parsedEvent.Summary, Is.EqualTo("Author Name - Example Book"));
            Assert.That(parsedEvent.Description, Is.EqualTo("A release description"));
        }

        [Test]
        public void calendar_feed_should_not_shift_utc_release_dates_across_day_boundaries()
        {
            var author = new Author
            {
                Metadata = new AuthorMetadata
                {
                    Name = "UTC Author"
                },
                Tags = new HashSet<int>()
            };

            var releaseDate = new DateTime(2025, 3, 15, 23, 30, 0, DateTimeKind.Utc);
            var edition = new Edition
            {
                Monitored = true,
                Overview = "Utc release description",
                Title = "UTC Book"
            };

            var book = new Book
            {
                Id = 43,
                Title = "UTC Book",
                ReleaseDate = releaseDate,
                Author = new LazyLoaded<Author>(author),
                Editions = new LazyLoaded<List<Edition>>(new List<Edition> { edition }),
                Genres = new List<string> { "Fiction" }
            };

            var bookService = new Mock<IBookService>();
            bookService.Setup(s => s.BooksBetweenDates(It.IsAny<DateTime>(), It.IsAny<DateTime>(), false))
                .Returns(new List<Book> { book });

            var authorService = new Mock<IAuthorService>();
            authorService.Setup(s => s.GetAuthor(book.AuthorId)).Returns(author);

            var controller = new CalendarFeedController(bookService.Object, authorService.Object, Mock.Of<ITagService>());
            var result = controller.GetCalendarFeed() as Microsoft.AspNetCore.Mvc.ContentResult;

            Assert.That(result, Is.Not.Null);
            Assert.That(result.Content, Does.Contain("DTSTART;VALUE=DATE:20250315"));
            Assert.That(result.Content, Does.Not.Contain("DTSTART;VALUE=DATE:20250316"));
        }
    }
}
