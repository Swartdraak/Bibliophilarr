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
                Mock.Of<IBookCutoffService>(),
                Mock.Of<IQueueService>(),
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
        public void should_not_forward_a_result_set_without_the_requested_format()
        {
            _search.Setup(service => service.BookSearch(BookId, true, true, false, true))
                .ReturnsAsync(new List<DownloadDecision> { Decision(FormatType.Ebook, Quality.EPUB) });

            _subject.Execute(AudioSearch());

            _search.Verify(service => service.BookSearch(BookId, true, true, false, true), Times.Once);
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
        public void should_not_search_when_dual_format_tracking_is_disabled()
        {
            _config.SetupGet(config => config.EnableDualFormatTracking).Returns(false);

            _subject.Execute(AudioSearch());

            _books.VerifyNoOtherCalls();
            _search.VerifyNoOtherCalls();
            _downloads.VerifyNoOtherCalls();
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

        private static DownloadDecision Decision(FormatType? resolved, Quality quality)
        {
            return new DownloadDecision(new RemoteBook
            {
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
