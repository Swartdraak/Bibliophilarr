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
        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

        [TestCase(1)]
        [TestCase(2)]
        public async Task should_serialize_shared_series_writes_without_blocking_a_distinct_refresh(int secondAuthor)
        {
            const string sharedSeriesId = "hardcover:series:42";
            const string unrelatedSeriesId = "hardcover:series:97";
            var series = new ConcurrentDictionary<string, int>();
            var links = new ConcurrentDictionary<(int SeriesId, int BookId), SeriesBookLink>();
            var firstSnapshot = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var secondThread = new TaskCompletionSource<Thread>(TaskCreationOptions.RunContinuationsAsynchronously);
            var secondSnapshot = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseFirst = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var sharedReads = 0;

            var seriesService = Mocker.GetMock<ISeriesService>();
            seriesService.Setup(service => service.GetByAuthorMetadataId(It.IsAny<int>())).Returns(new List<Series>());
            seriesService.Setup(service => service.FindById(It.IsAny<List<string>>())).Returns((List<string> ids) =>
            {
                var snapshot = series.Where(item => ids.Contains(item.Key)).Select(item => NewSeries(item.Key, item.Value)).ToList();
                if (ids.Contains(sharedSeriesId))
                {
                    if (Interlocked.Increment(ref sharedReads) == 1)
                    {
                        firstSnapshot.SetResult();
                        releaseFirst.Task.WaitAsync(4 * Timeout).GetAwaiter().GetResult();
                    }
                    else
                    {
                        secondSnapshot.SetResult(snapshot.Count);
                    }
                }

                return snapshot;
            });
            seriesService.Setup(service => service.InsertMany(It.IsAny<IList<Series>>())).Callback((IList<Series> rows) =>
            {
                foreach (var row in rows)
                {
                    var id = row.ForeignSeriesId == sharedSeriesId ? 7 : 8;
                    if (!series.TryAdd(row.ForeignSeriesId, id))
                    {
                        throw new InvalidOperationException("A series with this foreign ID is already stored.");
                    }

                    row.Id = id;
                }
            });

            var bookService = Mocker.GetMock<IBookService>();
            bookService.Setup(service => service.GetBooksByAuthorMetadataId(It.IsAny<int>()))
                .Returns((int author) => new List<Book> { new Book { Id = author + 10, ForeignBookId = WorkId(author) } });
            var linkService = Mocker.GetMock<ISeriesBookLinkService>();
            linkService.Setup(service => service.GetLinksBySeriesAndAuthor(It.IsAny<int>(), It.IsAny<string>()))
                .Returns((int id, string author) => links.Values.Where(link => link.SeriesId == id && AuthorId(link.BookId - 10) == author).ToList());
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

            var running = new List<Task> { StartRefresh(1, sharedSeriesId) };
            try
            {
                await firstSnapshot.Task.WaitAsync(Timeout);
                running.Add(StartRefresh(secondAuthor, sharedSeriesId, secondThread));
                var contender = await secondThread.Task.WaitAsync(Timeout);

                // Observe the worker actually waiting while the first owns the gate, or fail as soon as it reads an empty snapshot.
                Assert.That(SpinWait.SpinUntil(() => secondSnapshot.Task.IsCompleted || contender.ThreadState.HasFlag(ThreadState.WaitSleepJoin), Timeout), Is.True);
                Assert.That(secondSnapshot.Task.IsCompleted, Is.False, "The shared series was read before the first refresh committed it.");

                var unrelated = StartRefresh(3, unrelatedSeriesId);
                running.Add(unrelated);
                await unrelated.WaitAsync(Timeout);
                Assert.That(series.Keys, Is.EquivalentTo(new[] { unrelatedSeriesId }));
                Assert.That(secondSnapshot.Task.IsCompleted, Is.False, "The shared series was read while the first refresh was still held.");
            }
            finally
            {
                releaseFirst.TrySetResult();
                await Task.WhenAll(running).WaitAsync(Timeout);
            }

            Assert.That(await secondSnapshot.Task.WaitAsync(Timeout), Is.EqualTo(1));
            Assert.That(sharedReads, Is.EqualTo(2));
            Assert.That(series.Count, Is.EqualTo(2));
            var expected = new[] { (7, 11), (7, secondAuthor + 10), (8, 13) }.Distinct();
            Assert.That(links.Keys, Is.EquivalentTo(expected));

            static string WorkId(int author) => $"hardcover:work:{83 + author}";

            static string AuthorId(int author) => $"hardcover:author:{69 + author}";

            static Series NewSeries(string foreignId, int id = 0, int? author = null) => new Series
            {
                Id = id,
                ForeignSeriesId = foreignId,
                Title = "Concurrent refresh fixture",
                LinkItems = author.HasValue ? new List<SeriesBookLink> { new SeriesBookLink { Book = new Book { ForeignBookId = WorkId(author.Value) } } } : new List<SeriesBookLink>()
            };

            Task<bool> StartRefresh(int authorId, string foreignSeriesId, TaskCompletionSource<Thread> started = null)
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
                            Metadata = new AuthorMetadata { Id = authorId, ForeignAuthorId = AuthorId(authorId) },
                            Series = new List<Series> { NewSeries(foreignSeriesId, author: authorId) }
                        };

                        started?.SetResult(Thread.CurrentThread);
                        return refresh.RefreshSeriesInfo(authorId, author.Series.Value, author, false, false, null);
                    },
                    CancellationToken.None,
                    TaskCreationOptions.LongRunning,
                    TaskScheduler.Default);
            }
        }
    }
}
