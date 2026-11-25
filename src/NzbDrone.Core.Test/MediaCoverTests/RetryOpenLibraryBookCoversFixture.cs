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
        private sealed class TickClock : TimeProvider
        {
            public long ElapsedTicks { get; set; }
            public override long TimestampFrequency => TimeSpan.TicksPerSecond;
            public override long GetTimestamp() => ElapsedTicks;
        }

        private TickClock _clock;

        [SetUp]
        public void Setup()
        {
            _clock = new TickClock();
            Mocker.SetConstant<TimeProvider>(_clock);
            Mocker.GetMock<IConfigService>().SetupGet(settings => settings.EnableOpenLibraryProvider).Returns(true);
            Mocker.GetMock<IOpenLibraryBookCoverCache>().Setup(cache => cache.IsMissing(It.IsAny<int>(), It.IsAny<string>())).Returns(true);
            Mocker.GetMock<IBookRepository>()
                .Setup(repository => repository.GetArtworkRetryPage(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<bool>()))
                .Returns(new List<Book>());
            Mocker.GetMock<IAuthorRepository>().Setup(repository => repository.GetArtworkRetryPage(It.IsAny<int>(), It.IsAny<int>())).Returns(new List<Author>());
            Mocker.GetMock<IOpenLibraryAuthorPortraitCache>().Setup(cache => cache.IsMissing(It.IsAny<int>(), It.IsAny<string>())).Returns(true);
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
            Mocker.GetMock<IAuthorRepository>().VerifyNoOtherCalls();
            Mocker.GetMock<IOpenLibraryAuthorPortraitCache>().VerifyNoOtherCalls();
            Mocker.GetMock<IOpenLibraryBookCoverCache>().Verify(
                cache => cache.TryRecover(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Func<bool>>(), It.IsAny<TimeSpan>(), It.IsAny<Func<bool>>()),
                Times.Never);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void should_prioritize_file_attached_covers_while_every_lane_gets_its_turn(bool oneAttemptPerTick)
        {
            var attached = Enumerable.Range(101, 5).Select(StoredBook).ToList();
            var bibliography = Enumerable.Range(1, 5).Select(StoredBook).ToList();
            var editions = attached.Concat(bibliography).Select(book => StoredEdition(book)).ToList();
            var author = PortraitAuthor(51);
            var bookRepository = Mocker.GetMock<IBookRepository>();
            bookRepository.Setup(repository => repository.GetArtworkRetryPage(It.IsAny<int>(), 48, true))
                .Returns<int, int, bool>((after, _, _) => attached.Where(book => book.Id > after).ToList());
            bookRepository.Setup(repository => repository.GetArtworkRetryPage(It.IsAny<int>(), 16, false))
                .Returns<int, int, bool>((after, _, _) => bibliography.Where(book => book.Id > after).ToList());
            Mocker.GetMock<IAuthorRepository>().Setup(repository => repository.GetArtworkRetryPage(0, 64))
                .Returns(new List<Author> { author });
            Mocker.GetMock<IEditionService>().Setup(service => service.GetEditionsByBook(It.IsAny<IEnumerable<int>>()))
                .Returns<IEnumerable<int>>(ids => editions.Where(edition => ids.Contains(edition.BookId)).ToList());

            var attempted = new List<int>();
            OpenLibraryCoverRecovery Record(int id)
            {
                attempted.Add(id);
                if (oneAttemptPerTick)
                {
                    _clock.ElapsedTicks += TimeSpan.FromSeconds(10).Ticks;
                }

                return OpenLibraryCoverRecovery.Recovered;
            }

            var bookCache = Mocker.GetMock<IOpenLibraryBookCoverCache>();
            bookCache.Setup(cache => cache.TryRecover(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Func<bool>>(), It.IsAny<TimeSpan>(), It.IsAny<Func<bool>>()))
                .Returns<int, string, string, Func<bool>, TimeSpan, Func<bool>>((id, _, _, _, _, _) => Record(id));
            Mocker.GetMock<IOpenLibraryAuthorPortraitCache>()
                .Setup(cache => cache.TryRecover(51, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Func<bool>>(), It.IsAny<TimeSpan>(), It.IsAny<Func<bool>>()))
                .Returns(() => Record(51));

            Subject.Execute(new RetryOpenLibraryBookCoversCommand());
            if (oneAttemptPerTick)
            {
                attempted.Should().Equal(101);
                bookCache.SetupGet(cache => cache.IsCoolingDown).Returns(true);
                Subject.Execute(new RetryOpenLibraryBookCoversCommand());
                attempted.Should().Equal(101);
                bookCache.SetupGet(cache => cache.IsCoolingDown).Returns(false);
                for (var tick = 0; tick < 3; tick++)
                {
                    Subject.Execute(new RetryOpenLibraryBookCoversCommand());
                }

                bookRepository.Verify(repository => repository.GetArtworkRetryPage(101, 48, true), Times.Once);
            }

            attempted.Should().Equal(101, 1, 102, 51);
        }

        [Test]
        public void should_not_retry_the_same_book_if_a_file_is_attached_between_the_lane_reads()
        {
            var moved = StoredBook(71);
            var catalogueReady = false;
            var fileAttached = false;
            var repository = Mocker.GetMock<IBookRepository>();
            repository.Setup(book => book.GetArtworkRetryPage(0, 48, true))
                .Returns(() => fileAttached ? new List<Book> { moved } : new List<Book>());
            repository.Setup(book => book.GetArtworkRetryPage(0, 16, false))
                .Returns(() => catalogueReady && !fileAttached ? new List<Book> { moved } : new List<Book>());
            Mocker.GetMock<IEditionService>().Setup(service => service.GetEditionsByBook(It.IsAny<IEnumerable<int>>()))
                .Returns(new List<Edition> { StoredEdition(moved) });
            var cache = Mocker.GetMock<IOpenLibraryBookCoverCache>();
            cache.Setup(service => service.TryRecover(moved.Id, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Func<bool>>(), It.IsAny<TimeSpan>(), It.IsAny<Func<bool>>()))
                .Returns(() =>
                {
                    fileAttached = true;

                    // There is deliberately no saved cache entry to suppress a second request.
                    return OpenLibraryCoverRecovery.Skipped;
                });

            Subject.Execute(new RetryOpenLibraryBookCoversCommand());
            catalogueReady = true;
            Subject.Execute(new RetryOpenLibraryBookCoversCommand());

            fileAttached.Should().BeTrue();
            repository.Verify(book => book.GetArtworkRetryPage(0, 48, true), Times.Exactly(2));
            repository.Verify(book => book.GetArtworkRetryPage(0, 16, false), Times.Exactly(2));
            cache.Verify(service => service.TryRecover(moved.Id, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Func<bool>>(), It.IsAny<TimeSpan>(), It.IsAny<Func<bool>>()), Times.Once);
        }

        [Test]
        public void should_charge_archive_hops_against_the_same_four_request_tick()
        {
            var books = Enumerable.Range(1, 5).Select(StoredBook).ToList();
            var attached = Enumerable.Range(101, 5).Select(StoredBook).ToList();
            var editions = attached.Concat(books).Select(book => StoredEdition(book)).ToList();
            Mocker.GetMock<IBookRepository>().Setup(repository => repository.GetArtworkRetryPage(0, 16, false)).Returns(books);
            Mocker.GetMock<IBookRepository>().Setup(repository => repository.GetArtworkRetryPage(0, 48, true)).Returns(attached);
            Mocker.GetMock<IEditionService>().Setup(service => service.GetEditionsByBook(It.IsAny<IEnumerable<int>>())).Returns(editions);
            var attempted = new List<int>();
            Mocker.GetMock<IOpenLibraryBookCoverCache>()
                .Setup(cache => cache.TryRecover(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Func<bool>>(), It.IsAny<TimeSpan>(), It.IsAny<Func<bool>>()))
                .Returns<int, string, string, Func<bool>, TimeSpan, Func<bool>>((id, _, _, _, _, reserveRedirect) =>
                {
                    attempted.Add(id);
                    reserveRedirect().Should().BeTrue();
                    return OpenLibraryCoverRecovery.Recovered;
                });

            Subject.Execute(new RetryOpenLibraryBookCoversCommand());

            attempted.Should().Equal(101, 1);
        }

        [Test]
        public void should_share_four_requests_with_one_portrait_and_resume_both_cursors_after_cooldown()
        {
            var books = Enumerable.Range(1, 6).Select(StoredBook).ToList();
            var editions = books.Select(book => StoredEdition(book)).ToList();
            var authors = new List<Author> { PortraitAuthor(41), PortraitAuthor(42) };
            var bookRepository = Mocker.GetMock<IBookRepository>();
            bookRepository.Setup(repository => repository.GetArtworkRetryPage(0, 16, false)).Returns(books);
            bookRepository.Setup(repository => repository.GetArtworkRetryPage(3, 16, false)).Returns(books.Skip(3).ToList());
            var authorRepository = Mocker.GetMock<IAuthorRepository>();
            authorRepository.Setup(repository => repository.GetArtworkRetryPage(0, 64)).Returns(authors);
            authorRepository.Setup(repository => repository.GetArtworkRetryPage(41, 64)).Returns(authors.Skip(1).ToList());
            Mocker.GetMock<IEditionService>().Setup(service => service.GetEditionsByBook(It.IsAny<IEnumerable<int>>())).Returns(editions);

            var attempted = new List<int>();
            var bookCache = Mocker.GetMock<IOpenLibraryBookCoverCache>();
            bookCache.SetupSequence(cache => cache.IsCoolingDown).Returns(true).Returns(false).Returns(false);
            bookCache.Setup(cache => cache.TryRecover(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Func<bool>>(), It.IsAny<TimeSpan>(), It.IsAny<Func<bool>>()))
                .Callback<int, string, string, Func<bool>, TimeSpan, Func<bool>>((id, _, _, _, _, _) => attempted.Add(id))
                .Returns(OpenLibraryCoverRecovery.Recovered);

            Mocker.GetMock<IOpenLibraryAuthorPortraitCache>()
                .Setup(cache => cache.TryRecover(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Func<bool>>(), It.IsAny<TimeSpan>(), It.IsAny<Func<bool>>()))
                .Returns<int, string, string, Func<bool>, TimeSpan, Func<bool>>((id, _, _, stillSelected, timeout, _) =>
                {
                    stillSelected().Should().BeTrue();
                    timeout.Should().Be(TimeSpan.FromSeconds(5));
                    attempted.Add(id);
                    return OpenLibraryCoverRecovery.Recovered;
                });

            Subject.Execute(new RetryOpenLibraryBookCoversCommand());
            attempted.Should().BeEmpty();
            bookRepository.VerifyNoOtherCalls();
            authorRepository.VerifyNoOtherCalls();

            Subject.Execute(new RetryOpenLibraryBookCoversCommand());
            attempted.Should().Equal(1, 41, 2, 3);
            Subject.Execute(new RetryOpenLibraryBookCoversCommand());
            attempted.Should().Equal(1, 41, 2, 3, 4, 42, 5, 6);
            bookRepository.Verify(repository => repository.GetArtworkRetryPage(0, 16, false), Times.Once);
            bookRepository.Verify(repository => repository.GetArtworkRetryPage(3, 16, false), Times.Once);
            authorRepository.Verify(repository => repository.GetArtworkRetryPage(0, 64), Times.Once);
            authorRepository.Verify(repository => repository.GetArtworkRetryPage(41, 64), Times.Once);
        }

        [Test]
        public void should_stop_after_slow_or_deferred_attempts_and_retry_an_author_without_advancing_its_cursor()
        {
            var book = StoredBook(1);
            var nextBook = StoredBook(2);
            var edition = StoredEdition(book);
            var author = PortraitAuthor(41);
            var bookRepository = Mocker.GetMock<IBookRepository>();
            bookRepository.Setup(repository => repository.GetArtworkRetryPage(0, 16, false)).Returns(new List<Book> { book, nextBook });
            bookRepository.Setup(repository => repository.GetArtworkRetryPage(1, 16, false)).Returns(new List<Book>());
            var authorRepository = Mocker.GetMock<IAuthorRepository>();
            authorRepository.Setup(repository => repository.GetArtworkRetryPage(0, 64)).Returns(new List<Author> { author });
            Mocker.GetMock<IEditionService>().Setup(service => service.GetEditionsByBook(It.IsAny<IEnumerable<int>>())).Returns(new List<Edition> { edition });

            var bookCache = Mocker.GetMock<IOpenLibraryBookCoverCache>();
            var recovered = false;
            bookCache.Setup(cache => cache.IsMissing(1, It.IsAny<string>())).Returns(() => !recovered);
            bookCache.Setup(cache => cache.TryRecover(1, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Func<bool>>(), It.IsAny<TimeSpan>(), It.IsAny<Func<bool>>()))
                .Returns(() =>
                {
                    recovered = true;
                    _clock.ElapsedTicks += TimeSpan.FromSeconds(10).Ticks;
                    return OpenLibraryCoverRecovery.Recovered;
                });
            var portraitCache = Mocker.GetMock<IOpenLibraryAuthorPortraitCache>();
            portraitCache.SetupSequence(cache => cache.TryRecover(41, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Func<bool>>(), It.IsAny<TimeSpan>(), It.IsAny<Func<bool>>()))
                .Returns(OpenLibraryCoverRecovery.Deferred)
                .Returns(OpenLibraryCoverRecovery.Recovered);

            var subject = new RetryOpenLibraryBookCoversService(
                Mocker.GetMock<IConfigService>().Object,
                bookRepository.Object,
                Mocker.GetMock<IEditionService>().Object,
                Mocker.GetMock<IOpenLibraryBookCoverCache>().Object,
                new NzbDrone.Common.Cache.CacheManager(),
                Mocker.GetMock<ICatalogueIdentityRepository>().Object,
                authorRepository.Object,
                portraitCache.Object,
                _clock);
            subject.Execute(new RetryOpenLibraryBookCoversCommand());
            authorRepository.VerifyNoOtherCalls();
            subject.Execute(new RetryOpenLibraryBookCoversCommand());
            portraitCache.Verify(cache => cache.TryRecover(41, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Func<bool>>(), It.IsAny<TimeSpan>(), It.IsAny<Func<bool>>()), Times.Once);
            bookRepository.Verify(repository => repository.GetArtworkRetryPage(1, 16, false), Times.Once);
            subject.Execute(new RetryOpenLibraryBookCoversCommand());
            portraitCache.Verify(cache => cache.TryRecover(41, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Func<bool>>(), It.IsAny<TimeSpan>(), It.IsAny<Func<bool>>()), Times.Exactly(2));
            authorRepository.Verify(repository => repository.GetArtworkRetryPage(0, 64), Times.Exactly(2));
        }

        [TestCase("https://covers.openlibrary.org/a/id/19-L.jpg", "OL17A", "primary-provider-id", false, true)]
        [TestCase("https://covers.openlibrary.org/a/id/19-L.jpg?default=false", "openlibrary:author:OL17A", "legacy-provider-id", false, true)]
        [TestCase("https://covers.openlibrary.org/b/id/19-L.jpg", "OL17A", "primary-provider-id", false, false)]
        [TestCase("https://covers.openlibrary.org:443/a/id/19-L.jpg", "OL17A", "primary-provider-id", false, false)]
        [TestCase("https://covers.openlibrary.org/a/id/19-L.jpg?extra=true", "OL17A", "primary-provider-id", false, false)]
        [TestCase("https://covers.openlibrary.org/a/id/19-L.jpg", "OL18A", "primary-provider-id", false, false)]
        [TestCase("https://covers.openlibrary.org/a/id/19-L.jpg", "OL17A", "legacy-linked-id", false, false)]
        [TestCase("https://covers.openlibrary.org/a/id/19-L.jpg", "OL17A", "primary-provider-id", true, false)]
        public void should_admit_only_an_exact_primary_author_portrait(
            string url, string sourceId, string evidence, bool ambiguous, bool shouldRecover)
        {
            var author = PortraitAuthor(17);
            var poster = author.Metadata.Value.Images.Single();
            poster.Url = url;
            poster.SourceId = sourceId;
            var sources = new List<CatalogueSourceAssociation>
            {
                new CatalogueSourceAssociation { Provider = "OpenLibrary", ProviderId = "OL17A", Evidence = evidence }
            };
            if (ambiguous)
            {
                sources.Add(new CatalogueSourceAssociation { Provider = "OpenLibrary", ProviderId = "OL18A", Evidence = evidence });
            }

            Mocker.GetMock<ICatalogueIdentityRepository>()
                .Setup(repository => repository.GetSources(CatalogueEntityKind.Author, author.AuthorMetadataId)).Returns(sources);
            Mocker.GetMock<IAuthorRepository>()
                .Setup(repository => repository.GetArtworkRetryPage(0, 64)).Returns(new List<Author> { author });
            Mocker.GetMock<IBookRepository>()
                .Setup(repository => repository.GetArtworkRetryPage(0, 16, false)).Returns(new List<Book>());

            Subject.Execute(new RetryOpenLibraryBookCoversCommand());

            Mocker.GetMock<IOpenLibraryAuthorPortraitCache>()
                .Verify(cache => cache.TryRecover(author.Id, url, It.IsAny<string>(), It.IsAny<Func<bool>>(), It.IsAny<TimeSpan>(), It.IsAny<Func<bool>>()),
                    shouldRecover ? Times.Once() : Times.Never());
        }

        [Test]
        public void should_batch_editions_and_resume_the_primary_key_scan_after_four_candidates()
        {
            var books = Enumerable.Range(1, 6).Select(StoredBook).ToList();
            var editions = books.Select(book => StoredEdition(book)).ToList();
            var repo = Mocker.GetMock<IBookRepository>();
            repo.Setup(repository => repository.GetArtworkRetryPage(0, 16, false)).Returns(books);
            repo.Setup(repository => repository.GetArtworkRetryPage(4, 16, false)).Returns(books.Skip(4).ToList());
            var editionService = Mocker.GetMock<IEditionService>();
            editionService.Setup(service => service.GetEditionsByBook(It.IsAny<IEnumerable<int>>())).Returns(editions);
            var attempted = new List<int>();
            Mocker.GetMock<IOpenLibraryBookCoverCache>()
                .Setup(cache => cache.TryRecover(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Func<bool>>(), It.IsAny<TimeSpan>(), It.IsAny<Func<bool>>()))
                .Callback<int, string, string, Func<bool>, TimeSpan, Func<bool>>((id, _, _, _, timeout, _) =>
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

        [Test]
        public void should_wrap_when_the_last_book_uses_the_last_available_attempt()
        {
            var books = Enumerable.Range(1, 4).Select(StoredBook).ToList();
            var repository = Mocker.GetMock<IBookRepository>();
            repository.Setup(repo => repo.GetArtworkRetryPage(0, 16, false)).Returns(books);
            Mocker.GetMock<IEditionService>().Setup(service => service.GetEditionsByBook(It.IsAny<IEnumerable<int>>()))
                .Returns(books.Select(book => StoredEdition(book)).ToList());
            Mocker.GetMock<IOpenLibraryBookCoverCache>()
                .Setup(cache => cache.TryRecover(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Func<bool>>(), It.IsAny<TimeSpan>(), It.IsAny<Func<bool>>()))
                .Returns(OpenLibraryCoverRecovery.Recovered);

            Subject.Execute(new RetryOpenLibraryBookCoversCommand());
            Subject.Execute(new RetryOpenLibraryBookCoversCommand());

            repository.Verify(repo => repo.GetArtworkRetryPage(0, 16, false), Times.Exactly(2));
            repository.Verify(repo => repo.GetArtworkRetryPage(0, 48, true), Times.Exactly(2));
            repository.VerifyNoOtherCalls();
        }

        [TestCase(OpenLibraryCoverRecovery.Deferred, 0, 1)]
        [TestCase(OpenLibraryCoverRecovery.DeferredAfterRequest, 1, 2)]
        public void should_resume_at_an_unattempted_cover_and_advance_past_an_upstream_cooldown(
            OpenLibraryCoverRecovery firstResult, int nextCursor, int nextBookId)
        {
            var books = new List<Book> { StoredBook(1), StoredBook(2) };
            var editions = books.Select(book => StoredEdition(book)).ToList();
            var repo = Mocker.GetMock<IBookRepository>();
            repo.Setup(repository => repository.GetArtworkRetryPage(0, 16, false)).Returns(books);
            repo.Setup(repository => repository.GetArtworkRetryPage(1, 16, false)).Returns(books.Skip(1).ToList());
            Mocker.GetMock<IEditionService>().Setup(service => service.GetEditionsByBook(It.IsAny<IEnumerable<int>>()))
                .Returns(editions);
            var attempted = new List<int>();
            var cache = Mocker.GetMock<IOpenLibraryBookCoverCache>();
            cache.SetupSequence(service => service.IsCoolingDown).Returns(false).Returns(true).Returns(false);
            cache.Setup(service => service.TryRecover(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Func<bool>>(), It.IsAny<TimeSpan>(), It.IsAny<Func<bool>>()))
                .Returns<int, string, string, Func<bool>, TimeSpan, Func<bool>>((id, _, _, _, _, _) =>
                {
                    attempted.Add(id);
                    return attempted.Count == 1 ? firstResult : OpenLibraryCoverRecovery.Recovered;
                });

            Subject.Execute(new RetryOpenLibraryBookCoversCommand());
            Subject.Execute(new RetryOpenLibraryBookCoversCommand());
            attempted.Should().Equal(1);
            repo.Verify(repository => repository.GetArtworkRetryPage(0, 16, false), Times.Once);

            Subject.Execute(new RetryOpenLibraryBookCoversCommand());
            attempted[1].Should().Be(nextBookId);
            repo.Verify(repository => repository.GetArtworkRetryPage(nextCursor, 16, false), nextCursor == 0 ? Times.Exactly(2) : Times.Once());
        }

        [Test]
        public void should_retry_a_deferred_selection_from_the_current_catalogue_after_recreating_the_handler()
        {
            var book = StoredBook(3);
            var edition = StoredEdition(book);
            var repo = Mocker.GetMock<IBookRepository>();
            repo.Setup(repository => repository.GetArtworkRetryPage(0, 16, false)).Returns(new List<Book> { book });
            repo.Setup(repository => repository.Find(book.Id)).Returns(book);
            var editionService = Mocker.GetMock<IEditionService>();
            editionService.Setup(service => service.GetEditionsByBook(It.IsAny<IEnumerable<int>>())).Returns(new List<Edition> { edition });
            editionService.SetupSequence(service => service.GetEditionsByBook(book.Id))
                .Returns(new List<Edition> { edition })
                .Returns(new List<Edition> { StoredEdition(book, 99) });
            var called = 0;
            var cache = Mocker.GetMock<IOpenLibraryBookCoverCache>();
            cache.Setup(service => service.TryRecover(book.Id, edition.Images[0].Url, It.IsAny<string>(), It.IsAny<Func<bool>>(), It.IsAny<TimeSpan>(), It.IsAny<Func<bool>>()))
                .Returns<int, string, string, Func<bool>, TimeSpan, Func<bool>>((_, _, _, stillSelected, _, _) =>
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
                Mocker.GetMock<ICatalogueIdentityRepository>().Object,
                Mocker.GetMock<IAuthorRepository>().Object,
                Mocker.GetMock<IOpenLibraryAuthorPortraitCache>().Object,
                _clock);
            restarted.Execute(new RetryOpenLibraryBookCoversCommand());

            called.Should().Be(2);
            repo.Verify(repository => repository.GetArtworkRetryPage(0, 16, false), Times.Exactly(2));
        }

        [Test]
        public async Task should_not_run_a_requeued_tick_while_the_cancelled_handler_is_still_recovering()
        {
            var book = StoredBook(65);
            var edition = StoredEdition(book);
            var repo = Mocker.GetMock<IBookRepository>();
            repo.Setup(repository => repository.GetArtworkRetryPage(0, 16, false)).Returns(new List<Book> { book });
            Mocker.GetMock<IEditionService>().Setup(service => service.GetEditionsByBook(It.IsAny<IEnumerable<int>>()))
                .Returns(new List<Edition> { edition });

            using var release = new ManualResetEventSlim();
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var requests = 0;
            Mocker.GetMock<IOpenLibraryBookCoverCache>()
                .Setup(cache => cache.TryRecover(book.Id, edition.Images[0].Url, It.IsAny<string>(), It.IsAny<Func<bool>>(), It.IsAny<TimeSpan>(), It.IsAny<Func<bool>>()))
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
                repo.Verify(repository => repository.GetArtworkRetryPage(0, 16, false), Times.Once);
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
            Mocker.GetMock<IBookRepository>().Setup(repo => repo.GetArtworkRetryPage(0, 16, false)).Returns(new List<Book> { cached, ambiguous });
            Mocker.GetMock<IEditionService>().Setup(service => service.GetEditionsByBook(It.IsAny<IEnumerable<int>>()))
                .Returns(new List<Edition> { StoredEdition(cached), StoredEdition(ambiguous), StoredEdition(ambiguous, 99) });
            var cache = Mocker.GetMock<IOpenLibraryBookCoverCache>();
            cache.Setup(service => service.IsMissing(cached.Id, It.IsAny<string>())).Returns(false);

            Subject.Execute(new RetryOpenLibraryBookCoversCommand());

            cache.Verify(service => service.IsMissing(cached.Id, It.IsAny<string>()), Times.Once);
            cache.Verify(service => service.IsMissing(ambiguous.Id, It.IsAny<string>()), Times.Never);
            cache.Verify(service => service.TryRecover(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Func<bool>>(), It.IsAny<TimeSpan>(), It.IsAny<Func<bool>>()), Times.Never);
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

            Mocker.GetMock<IBookRepository>().Setup(repo => repo.GetArtworkRetryPage(0, 16, false)).Returns(new List<Book> { book });
            Mocker.GetMock<IEditionService>().Setup(service => service.GetEditionsByBook(It.IsAny<IEnumerable<int>>()))
                .Returns(new List<Edition> { localEdition, providerEdition });
            Mocker.GetMock<ICatalogueIdentityRepository>()
                .Setup(repo => repo.FindByProviderId(CatalogueEntityKind.Work, "OpenLibrary", "OL43W"))
                .Returns(book.Id);

            Subject.Execute(new RetryOpenLibraryBookCoversCommand());

            Mocker.GetMock<IOpenLibraryBookCoverCache>().Verify(
                cache => cache.TryRecover(book.Id, cover.Url, It.IsAny<string>(), It.IsAny<Func<bool>>(), It.IsAny<TimeSpan>(), It.IsAny<Func<bool>>()),
                Times.Once);
        }

        private static Book StoredBook(int id) => new Book { Id = id, ForeignBookId = $"openlibrary:work:OL{id}W" };

        private Author PortraitAuthor(int id)
        {
            var token = $"OL{id}A";
            var author = new Author
            {
                Id = id,
                AuthorMetadataId = id + 100,
                Metadata = new AuthorMetadata
                {
                    Id = id + 100,
                    CanonicalId = Guid.NewGuid().ToString("N"),
                    ForeignAuthorId = $"openlibrary:author:{token}",
                    OpenLibraryAuthorId = token,
                    Images = new List<MediaCover.MediaCover>
                    {
                        new MediaCover.MediaCover(MediaCoverTypes.Poster, $"https://covers.openlibrary.org/a/id/{id}-L.jpg")
                        {
                            SourceProvider = "OpenLibrary",
                            SourceId = token
                        }
                    }
                }
            };
            Mocker.GetMock<IAuthorRepository>().Setup(repository => repository.Find(id)).Returns(author);
            Mocker.GetMock<ICatalogueIdentityRepository>().Setup(repository => repository.GetSources(CatalogueEntityKind.Author, author.AuthorMetadataId))
                .Returns(new[] { new CatalogueSourceAssociation { Provider = "OpenLibrary", ProviderId = token, Evidence = "primary-provider-id" } });
            return author;
        }

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
