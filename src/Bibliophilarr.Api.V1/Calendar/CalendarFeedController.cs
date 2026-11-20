using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Bibliophilarr.Http;
using Ical.Net;
using Ical.Net.CalendarComponents;
using Ical.Net.DataTypes;
using Ical.Net.Serialization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Books;
using NzbDrone.Core.Tags;

namespace Bibliophilarr.Api.V1.Calendar
{
    [V1FeedController("calendar")]
    [Authorize(Policy = "API")]
    public class CalendarFeedController : Controller
    {
        private readonly IBookService _bookService;
        private readonly IAuthorService _authorService;
        private readonly ITagService _tagService;

        public CalendarFeedController(IBookService bookService, IAuthorService authorService, ITagService tagService)
        {
            _bookService = bookService;
            _authorService = authorService;
            _tagService = tagService;
        }

        [HttpGet("Bibliophilarr.ics")]
        public IActionResult GetCalendarFeed(int pastDays = 7, int futureDays = 28, string tagList = "", bool unmonitored = false, [FromQuery] string tags = "")
        {
            var start = DateTime.Today.AddDays(-pastDays);
            var end = DateTime.Today.AddDays(futureDays);
            var requestedTags = tagList.IsNotNullOrWhiteSpace() ? tagList : tags;
            var tagIds = new HashSet<int>();

            if (requestedTags.IsNotNullOrWhiteSpace())
            {
                var availableTags = _tagService.All();
                foreach (var requestedTag in requestedTags.Split(',', StringSplitOptions.TrimEntries))
                {
                    var tag = int.TryParse(requestedTag, NumberStyles.None, CultureInfo.InvariantCulture, out var id)
                        ? availableTags.Find(candidate => candidate.Id == id)
                        : availableTags.Find(candidate => candidate.Label == requestedTag);
                    if (tag == null)
                    {
                        return BadRequest("The calendar subscription includes an unknown tag.");
                    }

                    tagIds.Add(tag.Id);
                }
            }

            var books = _bookService.BooksBetweenDates(start, end, unmonitored);
            var calendar = new Ical.Net.Calendar
            {
                ProductId = "-//bibliophilarr.com//Bibliophilarr//EN"
            };

            var calendarName = "Bibliophilarr Book Schedule";
            calendar.AddProperty(new CalendarProperty("NAME", calendarName));
            calendar.AddProperty(new CalendarProperty("X-WR-CALNAME", calendarName));

            foreach (var book in books.OrderBy(v => v.ReleaseDate.Value))
            {
                var author = _authorService.GetAuthor(book.AuthorId); // Temp fix NOTE: Figure out why Book.Author is not populated during BooksBetweenDates Query

                if (tagIds.Count != 0 && tagIds.None(author.Tags.Contains))
                {
                    continue;
                }

                var occurrence = calendar.Create<CalendarEvent>();
                occurrence.Uid = "Bibliophilarr_book_" + book.Id;

                occurrence.Description = book.GetPreferredEdition()?.Overview;
                occurrence.Categories = book.Genres;

                var releaseDate = book.ReleaseDate.Value.Date;
                occurrence.Start = new CalDateTime(DateTime.SpecifyKind(releaseDate, DateTimeKind.Unspecified), false);
                occurrence.End = new CalDateTime(DateTime.SpecifyKind(releaseDate.AddDays(1), DateTimeKind.Unspecified), false);

                occurrence.Summary = $"{author.Name} - {book.Title}";
            }

            var serializer = (IStringSerializer)new SerializerFactory().Build(calendar.GetType(), new SerializationContext());
            var icalendar = serializer.SerializeToString(calendar);

            return Content(icalendar, "text/calendar");
        }
    }
}
