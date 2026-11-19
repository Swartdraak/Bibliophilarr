using System.Collections.Generic;
using System.Linq;
using Bibliophilarr.Api.V1.Books;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.AuthorStats;
using NzbDrone.Core.Books;
using NzbDrone.Core.DecisionEngine.Specifications;
using NzbDrone.Core.MediaCover;
using NzbDrone.Core.Profiles.Qualities;
using NzbDrone.SignalR;

namespace NzbDrone.Api.Test.Books
{
    [TestFixture]
    public class BookFormatMonitoringProjectionFixture
    {
        [TestCase(true, true, true, true)]
        [TestCase(false, true, true, false)]
        [TestCase(true, false, true, false)]
        [TestCase(true, true, false, false)]
        public void should_apply_the_same_monitoring_gates_to_single_and_batch_resources(
            bool authorMonitored, bool bookMonitored, bool formatMonitored, bool expected)
        {
            var book = new Book
            {
                Id = 1,
                Monitored = bookMonitored,
                Author = new Author { Id = 12, Monitored = authorMonitored },
                Editions = new List<Edition> { new Edition { IsEbook = true, Monitored = true } }
            };

            var profiles = new Mock<IAuthorFormatProfileService>();
            profiles.Setup(service => service.GetByAuthorId(12)).Returns(new List<AuthorFormatProfile>
            {
                new AuthorFormatProfile
                {
                    AuthorId = 12,
                    FormatType = FormatType.Ebook,
                    Monitored = formatMonitored
                },
                new AuthorFormatProfile
                {
                    AuthorId = 12,
                    FormatType = FormatType.Audiobook,
                    Monitored = formatMonitored
                }
            });

            var seriesLinks = new Mock<ISeriesBookLinkService>();
            seriesLinks.Setup(service => service.GetLinksByBook(It.IsAny<List<int>>()))
                .Returns(new List<SeriesBookLink>());
            var authorStatistics = new Mock<IAuthorStatisticsService>();
            authorStatistics.Setup(service => service.AuthorStatistics())
                .Returns(new List<AuthorStatistics>());

            var controller = new ProjectionController(profiles.Object, seriesLinks.Object, authorStatistics.Object);
            var single = controller.Project(book);
            var batch = controller.ProjectMany(book);

            Assert.That(single.FormatStatuses.Select(status => status.Monitored), Is.EqualTo(new[] { expected, expected }));
            Assert.That(batch.FormatStatuses.Select(status => status.Monitored), Is.EqualTo(new[] { expected, expected }));
            Assert.That(single.Monitored, Is.EqualTo(bookMonitored));
            Assert.That(batch.Monitored, Is.EqualTo(bookMonitored));
        }

        private class ProjectionController : BookControllerWithSignalR
        {
            public ProjectionController(
                IAuthorFormatProfileService profiles,
                ISeriesBookLinkService seriesLinks,
                IAuthorStatisticsService authorStatistics)
                : base(
                    Mock.Of<IBookService>(),
                    seriesLinks,
                    authorStatistics,
                    Mock.Of<IMapCoversToLocal>(),
                    Mock.Of<IUpgradableSpecification>(),
                    Mock.Of<IBroadcastSignalRMessage>(),
                    profiles,
                    Mock.Of<IQualityProfileService>())
            {
            }

            public BookResource Project(Book book)
            {
                return MapToResource(book, false);
            }

            public BookResource ProjectMany(Book book)
            {
                return MapToResource(new List<Book> { book }, false).Single();
            }
        }
    }
}
