using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NLog;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.BooksTests
{
    [TestFixture]
    public class RefreshSeriesServiceConcurrencyFixture : CoreTest<RefreshSeriesService>
    {
        [Test]
        public async Task should_read_the_committed_series_when_author_refreshes_overlap()
        {
            const string foreignSeriesId = "hardcover:series:42";
            const string foreignBookId = "hardcover:work:83";
            var series = new ConcurrentDictionary<string, int>();
            var links = new ConcurrentDictionary<(int SeriesId, int BookId), SeriesBookLink>();
            var firstSnapshot = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var secondStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var secondSnapshot = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseFirst = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var reads = 0;

            var seriesService = Mocker.GetMock<ISeriesService>();
            seriesService.Setup(service => service.GetByAuthorMetadataId(1)).Returns(new List<Series>());
            seriesService.Setup(service => service.FindById(It.IsAny<List<string>>())).Returns((List<string> ids) =>
            {
                var snapshot = series.Where(item => ids.Contains(item.Key)).Select(item => NewSeries(item.Value)).ToList();
                if (Interlocked.Increment(ref reads) == 1)
                {
                    firstSnapshot.SetResult();
                    releaseFirst.Task.WaitAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult();
                }
                else
                {
                    secondSnapshot.SetResult();
                }

                return snapshot;
            });
            seriesService.Setup(service => service.InsertMany(It.IsAny<IList<Series>>())).Callback((IList<Series> rows) =>
            {
                foreach (var row in rows)
                {
                    if (!series.TryAdd(row.ForeignSeriesId, 7))
                    {
                        throw new InvalidOperationException("A series with this foreign ID is already stored.");
                    }

                    row.Id = 7;
                }
            });

            var bookService = Mocker.GetMock<IBookService>();
            bookService.Setup(service => service.GetBooksByAuthorMetadataId(1))
                .Returns(new List<Book> { new Book { Id = 11, ForeignBookId = foreignBookId } });
            var linkService = Mocker.GetMock<ISeriesBookLinkService>();
            linkService.Setup(service => service.GetLinksBySeriesAndAuthor(It.IsAny<int>(), It.IsAny<string>()))
                .Returns((int id, string author) => links.Values.Where(link => link.SeriesId == id).ToList());
            linkService.Setup(service => service.InsertMany(It.IsAny<List<SeriesBookLink>>())).Callback((List<SeriesBookLink> rows) =>
            {
                foreach (var row in rows)
                {
                    if (!links.TryAdd((row.SeriesId, row.BookId), row))
                    {
                        throw new InvalidOperationException("A link for this series and work is already stored.");
                    }
                }
            });

            var first = StartRefresh(null);
            try
            {
                await firstSnapshot.Task.WaitAsync(TimeSpan.FromSeconds(5));
                var second = StartRefresh(secondStarted);
                await secondStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

                // The old implementation lets the second refresh read the same empty snapshot.
                // With the fix, allowing the first refresh to finish gives the second the stored series.
                await Task.WhenAny(secondSnapshot.Task, Task.Delay(TimeSpan.FromSeconds(1)));
                releaseFirst.SetResult();
                await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(10));
            }
            finally
            {
                releaseFirst.TrySetResult();
            }

            Assert.That(reads, Is.EqualTo(2));
            Assert.That(series.Count, Is.EqualTo(1));
            Assert.That(links.Keys, Is.EqualTo(new[] { (7, 11) }));

            Series NewSeries(int id = 0) => new Series
            {
                Id = id,
                ForeignSeriesId = foreignSeriesId,
                Title = "Concurrent refresh fixture",
                LinkItems = new List<SeriesBookLink> { new SeriesBookLink { Book = new Book { ForeignBookId = foreignBookId } } }
            };

            Task<bool> StartRefresh(TaskCompletionSource started)
            {
                var refresh = new RefreshSeriesService(
                    bookService.Object,
                    seriesService.Object,
                    linkService.Object,
                    Mocker.GetMock<IRefreshSeriesBookLinkService>().Object,
                    Mocker.GetMock<IAuthorMetadataService>().Object,
                    LogManager.GetCurrentClassLogger());

                return Task.Factory.StartNew(
                    () =>
                    {
                        var author = new Author
                        {
                            Metadata = new AuthorMetadata { Id = 1, ForeignAuthorId = "hardcover:author:69" },
                            Series = new List<Series> { NewSeries() }
                        };

                        started?.SetResult();
                        return refresh.RefreshSeriesInfo(1, author.Series.Value, author, false, false, null);
                    },
                    CancellationToken.None,
                    TaskCreationOptions.LongRunning,
                    TaskScheduler.Default);
            }
        }
    }
}
