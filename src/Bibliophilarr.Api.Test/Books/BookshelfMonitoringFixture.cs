using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Bibliophilarr.Api.V1.Bookshelf;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Books;

namespace NzbDrone.Api.Test.Books
{
    [TestFixture]
    public class BookshelfMonitoringFixture
    {
        [TestCase("", null, false)]
        [TestCase(",\"monitoringOptions\":null", null, false)]
        [TestCase(",\"monitoringOptions\":{}", null, false)]
        [TestCase(",\"monitoringOptions\":{\"monitor\":null}", null, false)]
        [TestCase(",\"monitoringOptions\":{\"monitor\":0}", MonitorTypes.All, false)]
        [TestCase(",\"monitoringOptions\":{\"monitor\":6}", MonitorTypes.None, false)]
        [TestCase(",\"monitoringOptions\":{\"booksToMonitor\":[\"chosen-book\"]}", MonitorTypes.All, true)]
        public void should_apply_existing_book_monitoring_only_when_requested(string options, MonitorTypes? expected, bool selectBook)
        {
            var author = new Author { Id = 8, Monitored = true, MonitorNewItems = NewItemMonitorTypes.None };
            var authors = new Mock<IAuthorService>();
            authors.Setup(service => service.GetAuthors(It.Is<IEnumerable<int>>(ids => ids.SequenceEqual(new[] { author.Id }))))
                .Returns(new List<Author> { author });
            var monitored = new Mock<IBookMonitoredService>();
            MonitoringOptions applied = null;
            monitored.Setup(service => service.SetBookMonitoredStatus(author, It.IsAny<MonitoringOptions>()))
                .Callback<Author, MonitoringOptions>((_, value) => applied = value);
            var controller = new BookshelfController(authors.Object, monitored.Object);
            var request = JsonSerializer.Deserialize<BookshelfResource>(
                $"{{\"authors\":[{{\"id\":8}}],\"monitorNewItems\":2{options}}}",
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            controller.UpdateAll(request).Should().BeOfType<AcceptedResult>();

            monitored.Verify(service => service.SetBookMonitoredStatus(author, It.IsAny<MonitoringOptions>()), Times.Once());
            (applied != null).Should().Be(expected.HasValue);
            (applied?.Monitor).Should().Be(expected);
            if (selectBook)
            {
                applied.BooksToMonitor.Should().Equal("chosen-book");
            }

            author.MonitorNewItems.Should().Be(NewItemMonitorTypes.New);
            author.Monitored.Should().Be(expected != MonitorTypes.None);
        }
    }
}
