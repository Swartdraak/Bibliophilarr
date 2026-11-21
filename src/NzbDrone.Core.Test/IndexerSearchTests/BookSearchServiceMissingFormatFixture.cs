using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Moq;
using NLog;
using NUnit.Framework;
using NzbDrone.Common.Serializer;
using NzbDrone.Core.Books;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.DecisionEngine;
using NzbDrone.Core.Download;
using NzbDrone.Core.IndexerSearch;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.Queue;

namespace NzbDrone.Core.Test.IndexerSearchTests
{
    [TestFixture]
    public class BookSearchServiceMissingFormatFixture
    {
        private const int BookId = 41;
        private const int AuthorId = 22;
        private Mock<ISearchForReleases> _search;
        private Mock<IBookService> _books;
        private Mock<IProcessDownloadDecisions> _downloads;
        private Mock<IConfigService> _config;
        private Mock<IAuthorFormatProfileService> _profiles;
        private Mock<IQueueService> _queue;
        private Mock<IBookCutoffService> _cutoff;
        private Book _book;
        private AuthorFormatProfile _profile;
        private BookSearchService _subject;

        [SetUp]
        public void SetUp()
        {
            _search = new Mock<ISearchForReleases>(MockBehavior.Strict);
            _books = new Mock<IBookService>();
            _downloads = new Mock<IProcessDownloadDecisions>(MockBehavior.Strict);
            _config = new Mock<IConfigService>();
            _profiles = new Mock<IAuthorFormatProfileService>();
            _queue = new Mock<IQueueService>();
            _queue.Setup(service => service.GetQueue()).Returns(new List<Queue.Queue>());
            _cutoff = new Mock<IBookCutoffService>(MockBehavior.Strict);

            _profile = new AuthorFormatProfile { Monitored = true };
            _book = new Book
            {
                Id = BookId,
                Author = new Author { Id = AuthorId, Monitored = true },
                Monitored = true,
                BookFiles = new List<BookFile>()
            };

            _books.Setup(service => service.GetBook(BookId)).Returns(_book);
            _config.SetupGet(config => config.EnableDualFormatTracking).Returns(true);
            _profiles.Setup(service => service.GetByAuthorIdAndFormat(AuthorId, It.IsAny<FormatType>())).Returns(() => _profile);

            _subject = new BookSearchService(
                _search.Object,
                _books.Object,
                _cutoff.Object,
                _queue.Object,
                _downloads.Object,
                _config.Object,
                _profiles.Object,
                LogManager.GetCurrentClassLogger());
        }

        [TestCase(FormatType.Ebook)]
        [TestCase(FormatType.Audiobook)]
        public void should_forward_only_the_requested_missing_format_without_requiring_a_known_edition(FormatType target)
        {
            var other = target == FormatType.Ebook ? FormatType.Audiobook : FormatType.Ebook;
            var targetQuality = target == FormatType.Ebook ? Quality.EPUB : Quality.M4B;
            var otherQuality = other == FormatType.Ebook ? Quality.EPUB : Quality.M4B;
            _book.BookFiles.Value.Add(new BookFile { Quality = new QualityModel(otherQuality) });
            var resolved = Decision(target, targetQuality);
            var parsed = Decision(null, targetQuality);
            var all = new List<DownloadDecision>
            {
                resolved,
                Decision(target, otherQuality),
                Decision(other, targetQuality),
                Decision(null, otherQuality),
                Decision(null, null),
                Decision(null, Quality.Unknown),
                Decision(FormatType.Ebook, Quality.Unknown),
                parsed
            };

            _search.Setup(service => service.BookSearch(BookId, true, true, false, true)).ReturnsAsync(all);
            _downloads.Setup(service => service.ProcessDecisions(It.IsAny<List<DownloadDecision>>())).ReturnsAsync(EmptyResult());

            _subject.Execute(new BookSearchCommand(new List<int> { BookId, BookId })
            {
                Trigger = CommandTrigger.Manual,
                MissingFormat = target
            });

            _search.Verify(service => service.BookSearch(BookId, true, true, false, true), Times.Once);
            _profiles.Verify(service => service.GetByAuthorIdAndFormat(AuthorId, target), Times.Exactly(2));
            _downloads.Verify(service => service.ProcessDecisions(It.Is<List<DownloadDecision>>(items =>
                items.SequenceEqual(new[] { resolved, parsed }))), Times.Once);
        }

        [TestCase("file")]
        [TestCase("work")]
        [TestCase("author")]
        [TestCase("format override")]
        [TestCase("disabled profile")]
        [TestCase("absent profile")]
        [TestCase("deleted")]
        public void should_skip_an_ineligible_work_without_searching(string blocker)
        {
            switch (blocker)
            {
                case "file":
                    _book.BookFiles.Value.Add(new BookFile { Quality = new QualityModel(Quality.M4B) });
                    break;
                case "work":
                    _book.Monitored = false;
                    break;
                case "author":
                    _book.Author.Value.Monitored = false;
                    break;
                case "format override":
                    _book.AudiobookMonitoringOverride = false;
                    break;
                case "disabled profile":
                    _profile.Monitored = false;
                    break;
                case "absent profile":
                    _profile = null;
                    break;
                case "deleted":
                    _books.Setup(service => service.GetBook(BookId)).Throws(new ModelNotFoundException(typeof(Book), BookId));
                    break;
            }

            _subject.Execute(AudioSearch());

            _search.VerifyNoOtherCalls();
            _downloads.VerifyNoOtherCalls();
        }

        [Test]
        public void should_not_grab_when_the_file_arrives_during_search()
        {
            var audiobook = Decision(FormatType.Audiobook, Quality.M4B);
            var afterImport = new Book
            {
                Id = BookId,
                Author = _book.Author,
                Monitored = true,
                BookFiles = new List<BookFile> { new BookFile { Quality = new QualityModel(Quality.M4B) } }
            };

            _books.SetupSequence(service => service.GetBook(BookId)).Returns(_book).Returns(afterImport);
            _search.Setup(service => service.BookSearch(BookId, true, true, false, true))
                .ReturnsAsync(new List<DownloadDecision> { audiobook });

            _subject.Execute(AudioSearch());

            _search.Verify(service => service.BookSearch(BookId, true, true, false, true), Times.Once);
            _downloads.VerifyNoOtherCalls();
        }

        [Test]
        public void should_identify_a_legacy_file_without_quality_by_its_path()
        {
            _book.BookFiles.Value.Add(new BookFile { Path = "/library/existing.m4b" });
            var ebook = Decision(FormatType.Ebook, Quality.EPUB);
            _search.Setup(service => service.BookSearch(BookId, true, true, false, true))
                .ReturnsAsync(new List<DownloadDecision> { ebook });
            _downloads.Setup(service => service.ProcessDecisions(It.IsAny<List<DownloadDecision>>())).ReturnsAsync(EmptyResult());

            _subject.Execute(AudioSearch());
            _search.VerifyNoOtherCalls();
            _downloads.VerifyNoOtherCalls();

            var command = AudioSearch();
            command.MissingFormat = FormatType.Ebook;
            _subject.Execute(command);

            _search.Verify(service => service.BookSearch(BookId, true, true, false, true), Times.Once);
            _downloads.Verify(service => service.ProcessDecisions(It.Is<List<DownloadDecision>>(items =>
                items.Count == 1 && items[0] == ebook)), Times.Once);
        }

        [Test]
        public void should_not_forward_a_result_set_without_the_requested_format()
        {
            _search.Setup(service => service.BookSearch(BookId, true, true, false, true))
                .ReturnsAsync(new List<DownloadDecision> { Decision(FormatType.Ebook, Quality.EPUB) });

            _subject.Execute(AudioSearch());

            _search.Verify(service => service.BookSearch(BookId, true, true, false, true), Times.Once);
            _books.Verify(service => service.GetBook(BookId), Times.Once);
            _profiles.Verify(service => service.GetByAuthorIdAndFormat(AuthorId, FormatType.Audiobook), Times.Once);
            _downloads.VerifyNoOtherCalls();
        }

        [Test]
        public void should_search_remaining_eligible_works_after_a_queued_book_is_removed()
        {
            const int removedBookId = 42;
            var command = AudioSearch();
            command.BookIds.Insert(0, removedBookId);
            var audiobook = Decision(FormatType.Audiobook, Quality.M4B);
            _books.Setup(service => service.GetBook(removedBookId)).Throws(new ModelNotFoundException(typeof(Book), removedBookId));
            _search.Setup(service => service.BookSearch(BookId, true, true, false, true))
                .ReturnsAsync(new List<DownloadDecision> { audiobook });
            _downloads.Setup(service => service.ProcessDecisions(It.IsAny<List<DownloadDecision>>())).ReturnsAsync(EmptyResult());

            _subject.Execute(command);

            _search.Verify(service => service.BookSearch(BookId, true, true, false, true), Times.Once);
            _downloads.Verify(service => service.ProcessDecisions(It.Is<List<DownloadDecision>>(items =>
                items.Count == 1 && items[0] == audiobook)), Times.Once);
        }

        [Test]
        public void should_not_search_without_a_matching_monitored_legacy_edition()
        {
            _config.SetupGet(config => config.EnableDualFormatTracking).Returns(false);
            _book.Editions = new List<Edition> { new Edition { Id = 8, Monitored = false } };

            _subject.Execute(AudioSearch());

            _books.Verify(service => service.GetBook(BookId), Times.Once);
            _search.VerifyNoOtherCalls();
            _downloads.VerifyNoOtherCalls();
        }

        [Test]
        public void should_search_a_missing_monitored_legacy_edition_without_format_profiles()
        {
            _config.SetupGet(config => config.EnableDualFormatTracking).Returns(false);
            _book.Editions = new List<Edition> { new Edition { Id = 8, Monitored = true } };
            _book.BookFiles.Value.Add(new BookFile { EditionId = 9, Quality = new QualityModel(Quality.EPUB) });
            var audio = Decision(FormatType.Audiobook, Quality.M4B);
            _search.Setup(service => service.BookSearch(BookId, true, true, false, false))
                .ReturnsAsync(new List<DownloadDecision> { Decision(FormatType.Ebook, Quality.EPUB), audio });
            _downloads.Setup(service => service.ProcessDecisions(It.IsAny<List<DownloadDecision>>())).ReturnsAsync(EmptyResult());

            _subject.Execute(AudioSearch());

            _search.Verify(service => service.BookSearch(BookId, true, true, false, false), Times.Once);
            _downloads.Verify(service => service.ProcessDecisions(It.Is<List<DownloadDecision>>(items =>
                items.SequenceEqual(new[] { audio }))), Times.Once);
            _profiles.VerifyNoOtherCalls();
        }

        [Test]
        public void should_scope_all_missing_books_and_ignore_other_formats_in_the_queue()
        {
            const int audioQueuedBookId = 42;
            var audioQueuedBook = new Book { Id = audioQueuedBookId, Author = _book.Author, Monitored = true };
            _books.Setup(service => service.BooksWithoutFiles(It.IsAny<PagingSpec<Book>>(), FormatType.Audiobook, true))
                .Returns((PagingSpec<Book> paging, FormatType? _, bool _) =>
                {
                    paging.Records = new List<Book> { _book, audioQueuedBook };
                    paging.TotalRecords = paging.Records.Count;
                    return paging;
                });
            _queue.Setup(service => service.GetQueue()).Returns(new List<Queue.Queue>
            {
                new Queue.Queue { Book = _book, Quality = new QualityModel(Quality.EPUB) },
                new Queue.Queue { Book = audioQueuedBook, Quality = new QualityModel(Quality.M4B) }
            });
            var audio = Decision(FormatType.Audiobook, Quality.M4B);
            _search.Setup(service => service.BookSearch(BookId, true, true, false, true))
                .ReturnsAsync(new List<DownloadDecision> { Decision(FormatType.Ebook, Quality.EPUB), audio });
            _downloads.Setup(service => service.ProcessDecisions(It.IsAny<List<DownloadDecision>>())).ReturnsAsync(EmptyResult());
            var command = STJson.Deserialize<MissingBookSearchCommand>("""{"missingFormat":"audiobook"}""");
            command.Trigger = CommandTrigger.Manual;

            _subject.Execute(command);

            _books.Verify(service => service.BooksWithoutFiles(It.IsAny<PagingSpec<Book>>(), FormatType.Audiobook, true), Times.Once);
            _search.Verify(service => service.BookSearch(BookId, true, true, false, true), Times.Once);
            _search.VerifyNoOtherCalls();
            _downloads.Verify(service => service.ProcessDecisions(It.Is<List<DownloadDecision>>(items =>
                items.SequenceEqual(new[] { audio }))), Times.Once);
        }

        [Test]
        public void should_continue_after_a_full_page_with_no_eligible_downloads()
        {
            var unrelatedAuthor = new Author { Id = AuthorId + 1, Monitored = true };
            var candidates = Enumerable.Range(1, 500)
                .Select(id => new Book { Id = id, Author = _book.Author, Monitored = true })
                .Append(new Book { Id = 501, Author = unrelatedAuthor, Monitored = true })
                .Append(new Book { Id = 502, Author = _book.Author, Monitored = true })
                .ToList();
            _book.Id = 502;
            _books.Setup(service => service.GetBook(It.IsAny<int>())).Returns((int id) =>
                id == _book.Id ? _book : throw new ModelNotFoundException(typeof(Book), id));
            _books.Setup(service => service.BooksWithoutFiles(It.IsAny<PagingSpec<Book>>(), FormatType.Audiobook, true))
                .Returns((PagingSpec<Book> paging, FormatType? _, bool _) =>
                {
                    var filters = paging.FilterExpressions.Select(filter => filter.Compile()).ToList();
                    paging.Records = candidates.Where(book => filters.All(filter => filter(book)))
                        .Take(paging.PageSize).ToList();
                    return paging;
                });
            var audio = Decision(FormatType.Audiobook, Quality.M4B);
            _search.Setup(service => service.BookSearch(502, true, true, false, true))
                .ReturnsAsync(new List<DownloadDecision> { audio });
            _downloads.Setup(service => service.ProcessDecisions(It.IsAny<List<DownloadDecision>>())).ReturnsAsync(EmptyResult());

            _subject.Execute(new MissingBookSearchCommand(AuthorId)
            {
                Trigger = CommandTrigger.Manual,
                MissingFormat = FormatType.Audiobook
            });

            _books.Verify(service => service.BooksWithoutFiles(It.IsAny<PagingSpec<Book>>(), FormatType.Audiobook, true), Times.Exactly(2));
            _books.Verify(service => service.GetBook(501), Times.Never);
            _search.Verify(service => service.BookSearch(502, true, true, false, true), Times.Once);
            _search.VerifyNoOtherCalls();
        }

        [Test]
        public void should_fail_on_an_unsupported_format_without_falling_back()
        {
            var command = AudioSearch();
            command.MissingFormat = (FormatType)99;

            Action execute = () => _subject.Execute(command);

            execute.Should().Throw<ArgumentOutOfRangeException>();
            _books.VerifyNoOtherCalls();
            _search.VerifyNoOtherCalls();
            _downloads.VerifyNoOtherCalls();
        }

        [Test]
        public void should_keep_the_legacy_command_book_search_behavior()
        {
            var command = STJson.Deserialize<BookSearchCommand>("""{"bookIds":[41]}""");
            var releases = new List<DownloadDecision> { Decision(FormatType.Ebook, Quality.EPUB), Decision(FormatType.Audiobook, Quality.M4B) };
            _search.Setup(service => service.BookSearch(BookId, false, false, false)).ReturnsAsync(releases);
            _downloads.Setup(service => service.ProcessDecisions(releases)).ReturnsAsync(EmptyResult());

            _subject.Execute(command);

            command.MissingFormat.Should().BeNull();
            _search.Verify(service => service.BookSearch(BookId, false, false, false), Times.Once);
            _downloads.Verify(service => service.ProcessDecisions(releases), Times.Once);
            _books.VerifyNoOtherCalls();
            _profiles.VerifyNoOtherCalls();
        }

        [Test]
        public void should_deduplicate_only_commands_for_the_same_format_and_books()
        {
            var ebook = new BookSearchCommand(new List<int> { BookId }) { MissingFormat = FormatType.Ebook };
            var unscoped = new BookSearchCommand(new List<int> { BookId });

            CommandEqualityComparer.Instance.Equals(AudioSearch(), AudioSearch()).Should().BeTrue();
            CommandEqualityComparer.Instance.Equals(AudioSearch(), ebook).Should().BeFalse();
            CommandEqualityComparer.Instance.Equals(AudioSearch(), unscoped).Should().BeFalse();
        }

        private static BookSearchCommand AudioSearch()
        {
            var command = STJson.Deserialize<BookSearchCommand>("""{"bookIds":[41],"missingFormat":"audiobook"}""");
            command.Trigger = CommandTrigger.Manual;
            return command;
        }

        [TestCase(FormatType.Ebook, true)]
        [TestCase(FormatType.Ebook, false)]
        [TestCase(FormatType.Audiobook, true)]
        [TestCase(FormatType.Audiobook, false)]
        public void should_only_forward_cutoff_upgrades_in_the_selected_format(FormatType target, bool allBooks)
        {
            var other = target == FormatType.Ebook ? FormatType.Audiobook : FormatType.Ebook;
            var targetQuality = target == FormatType.Ebook ? Quality.AZW3 : Quality.M4B;
            var otherQuality = target == FormatType.Ebook ? Quality.M4B : Quality.AZW3;
            var sameFormatQueued = new Book { Id = 42, Author = _book.Author };
            SetCutoffBooks(target, () => new[] { _book, sameFormatQueued });
            _queue.Setup(service => service.GetQueue()).Returns(new List<Queue.Queue>
            {
                new Queue.Queue { Book = _book, Quality = new QualityModel(otherQuality) },
                new Queue.Queue { Book = sameFormatQueued, Quality = new QualityModel(targetQuality) }
            });
            var upgrade = Decision(target, targetQuality, _book);
            var delayed = new DownloadDecision(Decision(null, targetQuality, _book).RemoteBook, new Rejection("delay", RejectionType.Temporary));
            var rejected = new DownloadDecision(upgrade.RemoteBook, new Rejection("blocked"));
            _search.Setup(service => service.BookSearch(BookId, false, true, false, true)).ReturnsAsync(new List<DownloadDecision>
            {
                upgrade,
                delayed,
                rejected,
                Decision(other, otherQuality, _book),
                Decision(target, otherQuality, _book),
                Decision(other, targetQuality, _book),
                Decision(target, Quality.Unknown, _book),
                Decision(target, null, _book),
                Decision(target, targetQuality, _book, sameFormatQueued)
            });
            _downloads.Setup(service => service.ProcessDecisions(It.IsAny<List<DownloadDecision>>())).ReturnsAsync(EmptyResult());

            _subject.Execute(new CutoffUnmetBookSearchCommand
            {
                Trigger = CommandTrigger.Manual,
                FormatType = target,
                BookIds = allBooks ? null : new List<int> { BookId, BookId, 42, 44 }
            });

            _search.Verify(service => service.BookSearch(BookId, false, true, false, true), Times.Once);
            _search.VerifyNoOtherCalls();
            _downloads.Verify(service => service.ProcessDecisions(It.Is<List<DownloadDecision>>(items =>
                items.SequenceEqual(new[] { upgrade, delayed }))), Times.Once);
        }

        [Test]
        public void should_stop_forwarding_when_file_or_monitoring_no_longer_matches_after_search()
        {
            var stillBelowCutoff = true;
            SetCutoffBooks(FormatType.Ebook, () => stillBelowCutoff ? new[] { _book } : Array.Empty<Book>());
            _search.Setup(service => service.BookSearch(BookId, false, true, false, true)).Returns(() =>
            {
                stillBelowCutoff = false;
                return System.Threading.Tasks.Task.FromResult(new List<DownloadDecision> { Decision(FormatType.Ebook, Quality.AZW3, _book) });
            });

            _subject.Execute(new CutoffUnmetBookSearchCommand { Trigger = CommandTrigger.Manual, FormatType = FormatType.Ebook });

            _search.Verify(service => service.BookSearch(BookId, false, true, false, true), Times.Once);
            _cutoff.Verify(service => service.BooksWhereCutoffUnmet(It.IsAny<PagingSpec<Book>>(), FormatType.Ebook, true), Times.Exactly(3));
            _downloads.VerifyNoOtherCalls();
        }

        [Test]
        public void should_preserve_old_unscoped_cutoff_dispatch()
        {
            _cutoff.Setup(service => service.BooksWhereCutoffUnmet(It.IsAny<PagingSpec<Book>>())).Returns((PagingSpec<Book> paging) =>
            {
                paging.Records = new List<Book> { _book };
                return paging;
            });
            var reports = new List<DownloadDecision> { Decision(FormatType.Ebook, Quality.AZW3, _book), Decision(FormatType.Audiobook, Quality.M4B, _book) };
            _search.Setup(service => service.BookSearch(BookId, false, true, false, true)).ReturnsAsync(reports);
            _downloads.Setup(service => service.ProcessDecisions(reports)).ReturnsAsync(EmptyResult());

            _subject.Execute(new CutoffUnmetBookSearchCommand { Trigger = CommandTrigger.Manual });

            _downloads.Verify(service => service.ProcessDecisions(reports), Times.Once);
        }

        [Test]
        public void should_compare_all_cutoff_scope_fields_and_not_search_an_empty_selection()
        {
            var ebook = STJson.Deserialize<CutoffUnmetBookSearchCommand>("""{"formatType":"ebook"}""");
            var audio = new CutoffUnmetBookSearchCommand { FormatType = FormatType.Audiobook };
            var selected = new CutoffUnmetBookSearchCommand { FormatType = FormatType.Ebook, BookIds = new List<int> { BookId } };
            var empty = new CutoffUnmetBookSearchCommand { FormatType = FormatType.Ebook, BookIds = new List<int>() };

            CommandEqualityComparer.Instance.Equals(ebook, new CutoffUnmetBookSearchCommand()).Should().BeFalse();
            CommandEqualityComparer.Instance.Equals(ebook, audio).Should().BeFalse();
            CommandEqualityComparer.Instance.Equals(ebook, selected).Should().BeFalse();
            CommandEqualityComparer.Instance.Equals(ebook, new CutoffUnmetBookSearchCommand { FormatType = FormatType.Ebook }).Should().BeTrue();

            _subject.Execute(empty);

            _cutoff.VerifyNoOtherCalls();
            _search.VerifyNoOtherCalls();
            _downloads.VerifyNoOtherCalls();
        }

        private void SetCutoffBooks(FormatType format, Func<Book[]> getBooks)
        {
            _cutoff.Setup(service => service.BooksWhereCutoffUnmet(It.IsAny<PagingSpec<Book>>(), format, true))
                .Returns((PagingSpec<Book> paging, FormatType _, bool _) =>
                {
                    var filters = paging.FilterExpressions.Select(filter => filter.Compile()).ToList();
                    paging.Records = getBooks().Where(book => filters.All(filter => filter(book))).Take(paging.PageSize).ToList();
                    return paging;
                });
        }

        private static DownloadDecision Decision(FormatType? resolved, Quality quality, params Book[] books)
        {
            return new DownloadDecision(new RemoteBook
            {
                Release = new ReleaseInfo { Title = $"{resolved} {quality}" },
                Books = books.ToList(),
                ResolvedFormatType = resolved,
                ParsedBookInfo = quality == null ? null : new ParsedBookInfo { Quality = new QualityModel(quality) }
            });
        }

        private static ProcessedDecisions EmptyResult()
        {
            return new ProcessedDecisions(new List<DownloadDecision>(), new List<DownloadDecision>(), new List<DownloadDecision>());
        }
    }
}
