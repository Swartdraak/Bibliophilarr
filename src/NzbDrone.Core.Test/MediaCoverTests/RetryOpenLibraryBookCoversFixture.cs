using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.MediaCover;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MediaCoverTests
{
    [TestFixture]
    public class RetryOpenLibraryBookCoversFixture : CoreTest<RetryOpenLibraryBookCoversService>
    {
        [SetUp]
        public void Setup()
        {
            Mocker.GetMock<IConfigService>().SetupGet(settings => settings.EnableOpenLibraryProvider).Returns(true);
            Mocker.GetMock<IOpenLibraryBookCoverCache>().Setup(cache => cache.IsMissing(It.IsAny<int>(), It.IsAny<string>())).Returns(true);
        }

        [TestCase(false, false)]
        [TestCase(true, true)]
        public void should_not_discover_or_request_covers_when_the_provider_is_disabled_or_cooling_down(bool enabled, bool coolingDown)
        {
            Mocker.GetMock<IConfigService>().SetupGet(settings => settings.EnableOpenLibraryProvider).Returns(enabled);
            Mocker.GetMock<IOpenLibraryBookCoverCache>().SetupGet(cache => cache.IsCoolingDown).Returns(coolingDown);

            Subject.Execute(new RetryOpenLibraryBookCoversCommand());

            Mocker.GetMock<IBookRepository>().VerifyNoOtherCalls();
            Mocker.GetMock<IEditionService>().VerifyNoOtherCalls();
            Mocker.GetMock<IOpenLibraryBookCoverCache>().Verify(
                cache => cache.TryRecover(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Func<bool>>(), It.IsAny<TimeSpan>()),
                Times.Never);
        }

        [Test]
        public void should_batch_editions_and_resume_the_primary_key_scan_after_four_candidates()
        {
            var books = Enumerable.Range(1, 6).Select(StoredBook).ToList();
            var editions = books.Select(book => StoredEdition(book)).ToList();
            var repo = Mocker.GetMock<IBookRepository>();
            repo.Setup(repository => repository.GetArtworkRetryPage(0, 64)).Returns(books);
            repo.Setup(repository => repository.GetArtworkRetryPage(4, 64)).Returns(books.Skip(4).ToList());
            var editionService = Mocker.GetMock<IEditionService>();
            editionService.Setup(service => service.GetEditionsByBook(It.IsAny<IEnumerable<int>>())).Returns(editions);
            var attempted = new List<int>();
            Mocker.GetMock<IOpenLibraryBookCoverCache>()
                .Setup(cache => cache.TryRecover(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Func<bool>>(), It.IsAny<TimeSpan>()))
                .Callback<int, string, string, Func<bool>, TimeSpan>((id, _, _, _, timeout) =>
                {
                    timeout.Should().BeGreaterThan(TimeSpan.Zero).And.BeLessThanOrEqualTo(TimeSpan.FromSeconds(5));
                    attempted.Add(id);
                })
                .Returns(OpenLibraryCoverRecovery.Recovered);

            Subject.Execute(new RetryOpenLibraryBookCoversCommand());
            attempted.Should().Equal(1, 2, 3, 4);

            Subject.Execute(new RetryOpenLibraryBookCoversCommand());
            attempted.Should().Equal(1, 2, 3, 4, 5, 6);
            editionService.Verify(service => service.GetEditionsByBook(It.Is<IEnumerable<int>>(ids => ids.SequenceEqual(new[] { 1, 2, 3, 4, 5, 6 }))), Times.Once);
            editionService.Verify(service => service.GetEditionsByBook(It.Is<IEnumerable<int>>(ids => ids.SequenceEqual(new[] { 5, 6 }))), Times.Once);
            editionService.VerifyNoOtherCalls();
        }

        [TestCase(OpenLibraryCoverRecovery.Deferred, 0, 1)]
        [TestCase(OpenLibraryCoverRecovery.DeferredAfterRequest, 1, 2)]
        public void should_resume_at_an_unattempted_cover_and_advance_past_an_upstream_cooldown(
            OpenLibraryCoverRecovery firstResult, int nextCursor, int nextBookId)
        {
            var books = new List<Book> { StoredBook(1), StoredBook(2) };
            var editions = books.Select(book => StoredEdition(book)).ToList();
            var repo = Mocker.GetMock<IBookRepository>();
            repo.Setup(repository => repository.GetArtworkRetryPage(0, 64)).Returns(books);
            repo.Setup(repository => repository.GetArtworkRetryPage(1, 64)).Returns(books.Skip(1).ToList());
            Mocker.GetMock<IEditionService>().Setup(service => service.GetEditionsByBook(It.IsAny<IEnumerable<int>>()))
                .Returns(editions);
            var attempted = new List<int>();
            var cache = Mocker.GetMock<IOpenLibraryBookCoverCache>();
            cache.SetupSequence(service => service.IsCoolingDown).Returns(false).Returns(true).Returns(false);
            cache.Setup(service => service.TryRecover(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Func<bool>>(), It.IsAny<TimeSpan>()))
                .Returns<int, string, string, Func<bool>, TimeSpan>((id, _, _, _, _) =>
                {
                    attempted.Add(id);
                    return attempted.Count == 1 ? firstResult : OpenLibraryCoverRecovery.Recovered;
                });

            Subject.Execute(new RetryOpenLibraryBookCoversCommand());
            Subject.Execute(new RetryOpenLibraryBookCoversCommand());
            attempted.Should().Equal(1);
            repo.Verify(repository => repository.GetArtworkRetryPage(0, 64), Times.Once);

            Subject.Execute(new RetryOpenLibraryBookCoversCommand());
            attempted[1].Should().Be(nextBookId);
            repo.Verify(repository => repository.GetArtworkRetryPage(nextCursor, 64), nextCursor == 0 ? Times.Exactly(2) : Times.Once());
        }

        [Test]
        public void should_retry_a_deferred_selection_from_the_current_catalogue_after_recreating_the_handler()
        {
            var book = StoredBook(3);
            var edition = StoredEdition(book);
            var repo = Mocker.GetMock<IBookRepository>();
            repo.Setup(repository => repository.GetArtworkRetryPage(0, 64)).Returns(new List<Book> { book });
            repo.Setup(repository => repository.Find(book.Id)).Returns(book);
            var editionService = Mocker.GetMock<IEditionService>();
            editionService.Setup(service => service.GetEditionsByBook(It.IsAny<IEnumerable<int>>())).Returns(new List<Edition> { edition });
            editionService.SetupSequence(service => service.GetEditionsByBook(book.Id))
                .Returns(new List<Edition> { edition })
                .Returns(new List<Edition> { StoredEdition(book, 99) });
            var called = 0;
            var cache = Mocker.GetMock<IOpenLibraryBookCoverCache>();
            cache.Setup(service => service.TryRecover(book.Id, edition.Images[0].Url, It.IsAny<string>(), It.IsAny<Func<bool>>(), It.IsAny<TimeSpan>()))
                .Returns<int, string, string, Func<bool>, TimeSpan>((_, _, _, stillSelected, _) =>
                {
                    if (++called == 1)
                    {
                        return OpenLibraryCoverRecovery.Deferred;
                    }

                    stillSelected().Should().BeTrue();
                    stillSelected().Should().BeFalse();
                    return OpenLibraryCoverRecovery.Skipped;
                });

            Subject.Execute(new RetryOpenLibraryBookCoversCommand());

            var restarted = new RetryOpenLibraryBookCoversService(
                Mocker.GetMock<IConfigService>().Object,
                repo.Object,
                editionService.Object,
                cache.Object,
                new NzbDrone.Common.Cache.CacheManager(),
                Mocker.GetMock<ICatalogueIdentityRepository>().Object);
            restarted.Execute(new RetryOpenLibraryBookCoversCommand());

            called.Should().Be(2);
            repo.Verify(repository => repository.GetArtworkRetryPage(0, 64), Times.Exactly(2));
        }

        [Test]
        public async Task should_not_run_a_requeued_tick_while_the_cancelled_handler_is_still_recovering()
        {
            var book = StoredBook(65);
            var edition = StoredEdition(book);
            var repo = Mocker.GetMock<IBookRepository>();
            repo.Setup(repository => repository.GetArtworkRetryPage(0, 64)).Returns(new List<Book> { book });
            Mocker.GetMock<IEditionService>().Setup(service => service.GetEditionsByBook(It.IsAny<IEnumerable<int>>()))
                .Returns(new List<Edition> { edition });

            using var release = new ManualResetEventSlim();
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var requests = 0;
            Mocker.GetMock<IOpenLibraryBookCoverCache>()
                .Setup(cache => cache.TryRecover(book.Id, edition.Images[0].Url, It.IsAny<string>(), It.IsAny<Func<bool>>(), It.IsAny<TimeSpan>()))
                .Returns(() =>
                {
                    if (Interlocked.Increment(ref requests) == 1)
                    {
                        entered.TrySetResult();
                        release.Wait(TimeSpan.FromSeconds(5)).Should().BeTrue();
                    }

                    return OpenLibraryCoverRecovery.Deferred;
                });

            var handler = Subject;
            var first = Task.Run(() => handler.Execute(new RetryOpenLibraryBookCoversCommand()));
            try
            {
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));

                // The command queue has marked the first command cancelled and can dispatch its replacement.
                await Task.Run(() => handler.Execute(new RetryOpenLibraryBookCoversCommand())).WaitAsync(TimeSpan.FromSeconds(2));
                requests.Should().Be(1);
                repo.Verify(repository => repository.GetArtworkRetryPage(0, 64), Times.Once);
            }
            finally
            {
                release.Set();
                await first;
            }

            handler.Execute(new RetryOpenLibraryBookCoversCommand());
            requests.Should().Be(2);
        }

        [Test]
        public void should_decline_conflicting_edition_selection_and_existing_cache()
        {
            var cached = StoredBook(1);
            var ambiguous = StoredBook(2);
            Mocker.GetMock<IBookRepository>().Setup(repo => repo.GetArtworkRetryPage(0, 64)).Returns(new List<Book> { cached, ambiguous });
            Mocker.GetMock<IEditionService>().Setup(service => service.GetEditionsByBook(It.IsAny<IEnumerable<int>>()))
                .Returns(new List<Edition> { StoredEdition(cached), StoredEdition(ambiguous), StoredEdition(ambiguous, 99) });
            var cache = Mocker.GetMock<IOpenLibraryBookCoverCache>();
            cache.Setup(service => service.IsMissing(cached.Id, It.IsAny<string>())).Returns(false);

            Subject.Execute(new RetryOpenLibraryBookCoversCommand());

            cache.Verify(service => service.IsMissing(cached.Id, It.IsAny<string>()), Times.Once);
            cache.Verify(service => service.IsMissing(ambiguous.Id, It.IsAny<string>()), Times.Never);
            cache.Verify(service => service.TryRecover(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Func<bool>>(), It.IsAny<TimeSpan>()), Times.Never);
        }

        [Test]
        public void should_retry_the_verified_work_cover_for_a_locally_identified_audiobook()
        {
            var book = StoredBook(43);
            book.CanonicalId = "1234567890abcdedabcd1234567890ac";
            book.OpenLibraryWorkId = "OL43W";
            var localEdition = new Edition
            {
                Id = 101,
                BookId = book.Id,
                ForeignEditionId = $"bibliophilarr:edition:{book.CanonicalId}:audiobook",
                Format = "Audiobook",
                Monitored = true
            };

            var providerEdition = StoredEdition(book, 2);
            providerEdition.Monitored = false;
            providerEdition.Links = new List<Links> { new Links { Url = "https://openlibrary.org/works/OL43W" } };
            var cover = providerEdition.Images.Single();
            cover.SourceProvider = "OpenLibrary";
            cover.SourceId = "OL43W";

            Mocker.GetMock<IBookRepository>().Setup(repo => repo.GetArtworkRetryPage(0, 64)).Returns(new List<Book> { book });
            Mocker.GetMock<IEditionService>().Setup(service => service.GetEditionsByBook(It.IsAny<IEnumerable<int>>()))
                .Returns(new List<Edition> { localEdition, providerEdition });
            Mocker.GetMock<ICatalogueIdentityRepository>()
                .Setup(repo => repo.FindByProviderId(CatalogueEntityKind.Work, "OpenLibrary", "OL43W"))
                .Returns(book.Id);

            Subject.Execute(new RetryOpenLibraryBookCoversCommand());

            Mocker.GetMock<IOpenLibraryBookCoverCache>().Verify(
                cache => cache.TryRecover(book.Id, cover.Url, It.IsAny<string>(), It.IsAny<Func<bool>>(), It.IsAny<TimeSpan>()),
                Times.Once);
        }

        private static Book StoredBook(int id) => new Book { Id = id, ForeignBookId = $"openlibrary:work:OL{id}W" };

        private static Edition StoredEdition(Book book, int coverId = 1)
        {
            return new Edition
            {
                Id = (book.Id * 100) + coverId,
                BookId = book.Id,
                ForeignEditionId = book.ForeignBookId,
                Monitored = true,
                Images = new List<MediaCover.MediaCover>
                {
                    new MediaCover.MediaCover(MediaCoverTypes.Cover, $"https://covers.openlibrary.org/b/id/{coverId}-L.jpg")
                }
            };
        }
    }
}
