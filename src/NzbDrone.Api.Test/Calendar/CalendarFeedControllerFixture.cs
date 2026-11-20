using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Threading.Tasks;
using Bibliophilarr.Api.V1.Calendar;
using Bibliophilarr.Http.Authentication;
using Bibliophilarr.Http.Middleware;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Authentication;
using NzbDrone.Core.Books;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Tags;
using NzbDrone.Http.Authentication;

namespace NzbDrone.Api.Test.Calendar
{
    [TestFixture]
    public class CalendarFeedControllerFixture
    {
        [TestCase("")]
        [TestCase("/books")]
        public async Task calendar_feed_should_require_an_explicit_api_credential_without_challenging_forms(string urlBase)
        {
            const string key = "local-calendar-fixture-key";
            var config = new Mock<IConfigFileProvider>();
            config.SetupGet(service => service.ApiKey).Returns(key);
            config.SetupGet(service => service.AuthenticationMethod).Returns(AuthenticationType.Forms);
            config.SetupGet(service => service.AuthenticationRequired).Returns(AuthenticationRequiredType.Enabled);
            config.SetupGet(service => service.InstanceName).Returns("Calendar test");

            var first = new Author { Id = 1, Metadata = new AuthorMetadata { Name = "Included Author" }, Tags = new HashSet<int> { 7 } };
            var second = new Author { Id = 2, Metadata = new AuthorMetadata { Name = "Excluded Author" }, Tags = new HashSet<int> { 9 } };
            var books = new Mock<IBookService>();
            books.Setup(service => service.BooksBetweenDates(It.IsAny<DateTime>(), It.IsAny<DateTime>(), false))
                .Returns(new List<Book> { ScheduledBook(41, "Included Book", first), ScheduledBook(42, "Excluded Book", second) });
            var authors = new Mock<IAuthorService>();
            authors.Setup(service => service.GetAuthor(first.Id)).Returns(first);
            authors.Setup(service => service.GetAuthor(second.Id)).Returns(second);
            var tags = new Mock<ITagService>();
            tags.Setup(service => service.All()).Returns(new List<Tag>
            {
                new Tag { Id = 7, Label = "selected" },
                new Tag { Id = 9, Label = "other" }
            });

            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Production });
            builder.Logging.ClearProviders();
            builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0));
            builder.Services.AddSingleton(config.Object);
            builder.Services.AddSingleton(books.Object);
            builder.Services.AddSingleton(authors.Object);
            builder.Services.AddSingleton(tags.Object);
            builder.Services.AddSingleton<IAuthorizationPolicyProvider, UiAuthorizationPolicyProvider>();
            builder.Services.AddSingleton<IAuthorizationHandler, UiAuthorizationHandler>();
            builder.Services.AddDataProtection().UseEphemeralDataProtectionProvider();
            builder.Services.AddAuthorization(options =>
            {
                options.FallbackPolicy = new AuthorizationPolicyBuilder("API", "Forms")
                    .RequireAuthenticatedUser().Build();
            });
            builder.Services.AddAppAuthentication();
            builder.Services.AddControllers().ConfigureApplicationPartManager(manager =>
            {
                manager.ApplicationParts.Clear();
                manager.ApplicationParts.Add(new AssemblyPart(typeof(CalendarFeedController).Assembly));
            });

            await using var app = builder.Build();
            if (urlBase.Length != 0)
            {
                app.UsePathBase(urlBase);
            }

            app.UseRouting();
            app.UseAuthentication();
            app.UseMiddleware<ApiWriteAuthenticationMiddleware>();
            app.UseMiddleware<UiAuthenticationMiddleware>();
            app.UseAuthorization();
            app.MapControllers();
            app.MapGet("/calendar", () => Results.Ok()).RequireAuthorization("UI");
            await app.StartAsync();

            using var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false })
            {
                BaseAddress = new Uri(app.Urls.Single())
            };
            var route = urlBase + "/feed/v1/calendar/Bibliophilarr.ics";

            using var subscription = await client.GetAsync(route + "?pastDays=7&futureDays=28&apikey=" + Uri.EscapeDataString(key));
            Assert.That(subscription.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(subscription.Content.Headers.ContentType?.MediaType, Is.EqualTo("text/calendar"));
            Assert.That(await subscription.Content.ReadAsStringAsync(), Does.Contain("BEGIN:VCALENDAR"));

            using var keyedRequest = new HttpRequestMessage(HttpMethod.Get, route);
            keyedRequest.Headers.Add("X-Api-Key", key);
            using var keyed = await client.SendAsync(keyedRequest);
            Assert.That(keyed.StatusCode, Is.EqualTo(HttpStatusCode.OK));

            using var bearerRequest = new HttpRequestMessage(HttpMethod.Get, route);
            bearerRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
            using var bearer = await client.SendAsync(bearerRequest);
            Assert.That(bearer.StatusCode, Is.EqualTo(HttpStatusCode.OK));

            var forms = app.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>().Get("Forms");
            var ticket = new AuthenticationTicket(
                new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("user", "calendar-fixture") }, "Forms")), "Forms");
            var cookie = forms.Cookie.Name + "=" + forms.TicketDataFormat.Protect(ticket);

            foreach (var query in new[] { "", "?apikey=wrong-fixture-key" })
            {
                using var denied = await client.GetAsync(route + query);
                Assert.That(denied.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
                Assert.That(denied.Headers.Location, Is.Null);

                using var formsOnly = new HttpRequestMessage(HttpMethod.Get, route + query);
                formsOnly.Headers.Add("Cookie", cookie);
                using var deniedForms = await client.SendAsync(formsOnly);
                Assert.That(deniedForms.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
                Assert.That(deniedForms.Headers.Location, Is.Null);
            }

            using var signedInUiRequest = new HttpRequestMessage(HttpMethod.Get, urlBase + "/calendar");
            signedInUiRequest.Headers.Add("Cookie", cookie);
            using var signedInUi = await client.SendAsync(signedInUiRequest);
            Assert.That(signedInUi.StatusCode, Is.EqualTo(HttpStatusCode.OK));

            foreach (var path in new[] { "/calendar", "/feed/v1/calendar/other.ics" })
            {
                using var uiRequest = new HttpRequestMessage(HttpMethod.Get, urlBase + path + "?apikey=" + Uri.EscapeDataString(key));
                uiRequest.Headers.Accept.ParseAdd("text/html");
                using var ui = await client.SendAsync(uiRequest);
                Assert.That(ui.StatusCode, Is.EqualTo(HttpStatusCode.Found));
                Assert.That(new Uri(client.BaseAddress, ui.Headers.Location).AbsolutePath, Is.EqualTo(urlBase + "/login"));
            }

            foreach (var query in new[] { "tags=7", "tagList=selected", "tagList=selected&tags=9" })
            {
                using var filtered = await client.GetAsync(route + "?" + query + "&apikey=" + Uri.EscapeDataString(key));
                Assert.That(filtered.StatusCode, Is.EqualTo(HttpStatusCode.OK));
                var calendar = global::Ical.Net.Calendar.Load(await filtered.Content.ReadAsStringAsync());
                Assert.That(calendar.Events.Select(item => item.Summary), Is.EqualTo(new[] { "Included Author - Included Book" }));
            }

            using var unknown = await client.GetAsync(route + "?tags=99&apikey=" + Uri.EscapeDataString(key));
            Assert.That(unknown.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            books.Verify(service => service.BooksBetweenDates(It.IsAny<DateTime>(), It.IsAny<DateTime>(), false), Times.Exactly(6));
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        public void calendar_feed_should_round_trip_all_day_release_events(int monitoredEditions)
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
                Monitored = monitoredEditions > 0,
                Overview = "A release description",
                Title = "Example Book",
                Format = "ebook"
            };
            var audiobook = new Edition
            {
                Monitored = monitoredEditions > 1,
                Overview = "An audiobook description",
                Title = "Example Book",
                Format = "audiobook"
            };

            var book = new Book
            {
                Id = 42,
                Title = "Example Book",
                ReleaseDate = releaseDate,
                Author = new LazyLoaded<Author>(author),
                Editions = new LazyLoaded<List<Edition>>(new List<Edition> { edition, audiobook }),
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

        private static Book ScheduledBook(int id, string title, Author author)
        {
            return new Book
            {
                Id = id,
                Title = title,
                ReleaseDate = DateTime.Today.AddDays(1),
                Author = new LazyLoaded<Author>(author),
                Editions = new LazyLoaded<List<Edition>>(new List<Edition> { new Edition { Monitored = true, Title = title } }),
                Genres = new List<string>()
            };
        }
    }
}
