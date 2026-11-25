using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Moq;
using NLog;
using NUnit.Framework;
using NzbDrone.Core.Books;

namespace NzbDrone.Core.Test.BooksTests
{
    [TestFixture]
    public class SeriesIdentityFixture
    {
        [TestCase(false)]
        [TestCase(true)]
        public void should_revalidate_route_owner_before_reconciling_a_series_alias(bool ownerKeepsChanging)
        {
            const string alias = "openlibrary:series:ol5a:saga";
            const string authorId = "openlibrary:author:OL5A";
            var candidate = MakeSeries(0, alias);
            var author = new Author
            {
                Metadata = new AuthorMetadata { ForeignAuthorId = authorId },
                Series = new List<Series> { candidate }
            };
            var former = MakeSeries(1, "hardcover:series:71");
            var current = MakeSeries(2, "hardcover:series:72");
            var owners = new Queue<Series>(ownerKeepsChanging
                ? new[] { former, current, MakeSeries(3, "hardcover:series:73"), MakeSeries(4, "hardcover:series:74") }
                : new[] { former, current, current });
            var series = new Mock<ISeriesService>();
            series.Setup(service => service.GetByAuthorMetadataId(1)).Returns(new List<Series>());
            series.Setup(service => service.FindById(It.IsAny<List<string>>())).Returns(new List<Series>());
            series.Setup(service => service.FindById(alias)).Returns(() =>
            {
                candidate.ForeignSeriesId.Should().Be(alias);
                candidate.PendingAliases.Should().BeEmpty();
                return owners.Dequeue();
            });
            var books = new Mock<IBookService>();
            books.Setup(service => service.GetBooksByAuthorMetadataId(1)).Returns(new List<Book>());
            var links = new Mock<ISeriesBookLinkService>();
            links.Setup(service => service.GetLinksBySeriesAndAuthor(It.IsAny<int>(), authorId)).Returns(new List<SeriesBookLink>());
            var refresh = new RefreshSeriesService(
                books.Object,
                series.Object,
                links.Object,
                Mock.Of<IRefreshSeriesBookLinkService>(),
                Mock.Of<IAuthorMetadataService>(),
                LogManager.GetCurrentClassLogger());

            if (ownerKeepsChanging)
            {
                Assert.Throws<InvalidOperationException>(() => refresh.RefreshSeriesInfo(1, author.Series.Value, author, false, false, null));
                series.Verify(service => service.UpdateMany(It.IsAny<IList<Series>>()), Times.Never);
                candidate.ForeignSeriesId.Should().Be(alias);
                candidate.PendingAliases.Should().BeEmpty();
            }
            else
            {
                refresh.RefreshSeriesInfo(1, author.Series.Value, author, false, false, null);
                series.Verify(service => service.UpdateMany(It.Is<IList<Series>>(rows =>
                    rows.Count == 1 && rows[0].Id == current.Id && rows[0].ForeignSeriesId == current.ForeignSeriesId &&
                    rows[0].PendingAliases.Contains(alias))), Times.Once);
                candidate.ForeignSeriesId.Should().Be(current.ForeignSeriesId);
            }

            series.Verify(service => service.InsertMany(It.Is<IList<Series>>(rows => rows.Any())), Times.Never);
            owners.Should().BeEmpty();

            static Series MakeSeries(int id, string foreignId) => new Series
            {
                Id = id,
                ForeignSeriesId = foreignId,
                Title = "The Saga",
                LinkItems = new List<SeriesBookLink>()
            };
        }

        [TestCase(false)]
        [TestCase(true)]
        public void prunes_omitted_links_only_with_verified_membership(bool complete)
        {
            var provider = complete ? "hardcover" : "openlibrary";
            var authorId = complete ? "hardcover:author:14" : "openlibrary:author:OL14A";
            var seriesId = complete ? "hardcover:series:42" : "openlibrary:series:ol14a:saga";
            var books = Enumerable.Range(1, 3).Select(id => new Book
            {
                Id = id,
                ForeignBookId = complete ? $"{provider}:work:{id}" : $"{provider}:work:OL{id}W"
            }).ToList();

            var retained = new SeriesBookLink { Id = 17, BookId = 1, SeriesId = 7, Position = "1", SeriesPosition = 1, IsPrimary = true };
            var omitted = new SeriesBookLink { Id = 18, BookId = 2, SeriesId = 7, Position = "2", SeriesPosition = 2, IsPrimary = true };
            var existing = new Series { Id = 7, ForeignSeriesId = seriesId, Title = "The Saga" };
            var incoming = new Series
            {
                ForeignSeriesId = seriesId,
                Title = existing.Title,
                LinkItems = new List<SeriesBookLink>
                {
                    new SeriesBookLink { Book = books[0], Position = "1.5", SeriesPosition = 1, IsPrimary = true },
                    new SeriesBookLink { Book = books[2], Position = "3", SeriesPosition = 3, IsPrimary = true }
                }
            };
            var author = new Author
            {
                Metadata = new AuthorMetadata { ForeignAuthorId = authorId },
                RemoteSeriesComplete = complete ? true : null,
                Series = new List<Series> { incoming }
            };

            var bookService = new Mock<IBookService>();
            bookService.Setup(service => service.GetBooksByAuthorMetadataId(14)).Returns(books);
            var seriesService = new Mock<ISeriesService>();
            seriesService.Setup(service => service.GetByAuthorMetadataId(14)).Returns(new List<Series> { existing });
            seriesService.Setup(service => service.FindById(It.IsAny<List<string>>())).Returns(new List<Series> { existing });
            var links = new Mock<ISeriesBookLinkService>();
            links.Setup(service => service.GetLinksBySeriesAndAuthor(7, authorId)).Returns(new List<SeriesBookLink> { retained, omitted });
            var logger = LogManager.GetCurrentClassLogger();
            var refresh = new RefreshSeriesService(
                bookService.Object,
                seriesService.Object,
                links.Object,
                new RefreshSeriesBookLinkService(links.Object, logger),
                Mock.Of<IAuthorMetadataService>(),
                logger);

            refresh.RefreshSeriesInfo(14, author.Series.Value, author, false, false, null).Should().BeTrue();

            links.Verify(service => service.InsertMany(It.Is<List<SeriesBookLink>>(rows =>
                rows.Count == 1 && rows[0].SeriesId == 7 && rows[0].BookId == 3 && rows[0].Position == "3")), Times.Once);
            links.Verify(service => service.UpdateMany(It.Is<List<SeriesBookLink>>(rows =>
                rows.Count == 1 && rows[0].Id == retained.Id && rows[0].Position == "1.5")), Times.Once);
            links.Verify(service => service.DeleteMany(It.Is<List<SeriesBookLink>>(rows => rows.Any())),
                complete ? Times.Once() : Times.Never());
            if (complete)
            {
                links.Verify(service => service.DeleteMany(It.Is<List<SeriesBookLink>>(rows =>
                    rows.Count == 1 && rows[0].Id == omitted.Id)), Times.Once);
            }

            seriesService.Verify(service => service.Delete(It.IsAny<int>()), Times.Never);
        }

        [TestCase("openlibrary:series:ol5a:saga", "2", true, true)]
        [TestCase("openlibrary:series:ol5a:saga", "3", true, false)]
        [TestCase("openlibrary:series:ol5a:saga", "2", false, false)]
        [TestCase("hardcover:series:22", "2", true, false)]
        public void requires_independently_matching_member_positions(string providerId, string secondPosition, bool secondPrimary, bool matches)
        {
            var existing = new Series
            {
                Title = "The Saga",
                ForeignSeriesId = "hardcover:series:11",
                LinkItems = new List<SeriesBookLink>
                {
                    new SeriesBookLink { BookId = 1, Position = "1", IsPrimary = true },
                    new SeriesBookLink { BookId = 2, Position = "2", IsPrimary = true }
                }
            };
            var incoming = new Series
            {
                Title = "The Saga",
                ForeignSeriesId = providerId,
                LinkItems = new List<SeriesBookLink>
                {
                    new SeriesBookLink { Book = new Book { Id = 1 }, Position = "1", IsPrimary = true },
                    new SeriesBookLink { Book = new Book { Id = 2 }, Position = secondPosition, IsPrimary = secondPrimary }
                }
            };

            RefreshSeriesService.SeriesIdentityMatches(existing, incoming).Should().Be(matches);
        }
    }
}
