using System.Collections.Generic;
using System.Linq;
using Bibliophilarr.Api.V1.Books;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.AuthorStats;
using NzbDrone.Core.Books;
using NzbDrone.Core.DecisionEngine.Specifications;
using NzbDrone.Core.MediaCover;
using NzbDrone.Core.Profiles.Metadata;
using NzbDrone.Core.Profiles.Qualities;
using NzbDrone.Core.Validation;
using NzbDrone.SignalR;

namespace NzbDrone.Api.Test.Books
{
    [TestFixture]
    public class BookFormatMonitoringFixture
    {
        [Test]
        public void format_request_updates_book_preferences_even_without_a_matching_edition()
        {
            const int bookId = 8;
            var book = new Book
            {
                Id = bookId,
                Monitored = true,
                Author = new Author { Id = 13, Monitored = true },
                Editions = new List<Edition> { new Edition { IsEbook = true, Monitored = true } }
            };

            var books = new Mock<IBookService>();
            books.Setup(s => s.GetBooks(It.IsAny<IEnumerable<int>>())).Returns(new List<Book> { book });
            books.Setup(s => s.SetFormatMonitored(It.IsAny<IEnumerable<int>>(), FormatType.Audiobook, false))
                .Callback(() => book.AudiobookMonitoringOverride = false);

            var editions = new Mock<IEditionService>(MockBehavior.Strict);
            var series = new Mock<ISeriesBookLinkService>();
            series.Setup(s => s.GetLinksByBook(It.IsAny<List<int>>())).Returns(new List<SeriesBookLink>());
            var stats = new Mock<IAuthorStatisticsService>();
            stats.Setup(s => s.AuthorStatistics()).Returns(new List<AuthorStatistics>());

            var profiles = new Mock<IAuthorFormatProfileService>();
            profiles.Setup(s => s.GetByAuthorId(13)).Returns(new List<AuthorFormatProfile>
            {
                new AuthorFormatProfile { FormatType = FormatType.Ebook, Monitored = true },
                new AuthorFormatProfile { FormatType = FormatType.Audiobook, Monitored = true }
            });
            var qualityProfiles = Mock.Of<IQualityProfileService>();

            var controller = new BookController(
                Mock.Of<IAuthorService>(),
                books.Object,
                Mock.Of<IAddBookService>(),
                editions.Object,
                series.Object,
                stats.Object,
                Mock.Of<IMapCoversToLocal>(),
                Mock.Of<IUpgradableSpecification>(),
                Mock.Of<IBroadcastSignalRMessage>(),
                profiles.Object,
                qualityProfiles,
                new QualityProfileExistsValidator(qualityProfiles),
                new MetadataProfileExistsValidator(Mock.Of<IMetadataProfileService>()));

            var response = (AcceptedResult)controller.SetBooksMonitored(new BooksMonitoredResource
            {
                BookIds = new List<int> { bookId },
                FormatType = FormatType.Audiobook,
                Monitored = false
            });

            var formats = ((List<BookResource>)response.Value).Single().FormatStatuses;
            formats.Single(status => status.FormatType == FormatType.Ebook).Monitored.Should().BeTrue();
            var audiobook = formats.Single(status => status.FormatType == FormatType.Audiobook);
            audiobook.Monitored.Should().BeFalse();
            audiobook.CanMonitor.Should().BeTrue();
            book.Editions.Value.Single().Monitored.Should().BeTrue();
            editions.VerifyNoOtherCalls();
        }
    }
}
