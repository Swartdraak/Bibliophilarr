using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Common.Instrumentation.Extensions;
using NzbDrone.Core.Books;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.DecisionEngine;
using NzbDrone.Core.Download;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.Queue;

namespace NzbDrone.Core.IndexerSearch
{
    internal class BookSearchService : IExecute<BookSearchCommand>,
                               IExecute<MissingBookSearchCommand>,
                               IExecute<CutoffUnmetBookSearchCommand>
    {
        private readonly ISearchForReleases _releaseSearchService;
        private readonly IBookService _bookService;
        private readonly IBookCutoffService _bookCutoffService;
        private readonly IQueueService _queueService;
        private readonly IProcessDownloadDecisions _processDownloadDecisions;
        private readonly IConfigService _configService;
        private readonly IAuthorFormatProfileService _formatProfileService;
        private readonly Logger _logger;

        public BookSearchService(ISearchForReleases releaseSearchService,
            IBookService bookService,
            IBookCutoffService bookCutoffService,
            IQueueService queueService,
            IProcessDownloadDecisions processDownloadDecisions,
            IConfigService configService,
            IAuthorFormatProfileService formatProfileService,
            Logger logger)
        {
            _releaseSearchService = releaseSearchService;
            _bookService = bookService;
            _bookCutoffService = bookCutoffService;
            _queueService = queueService;
            _processDownloadDecisions = processDownloadDecisions;
            _configService = configService;
            _formatProfileService = formatProfileService;
            _logger = logger;
        }

        private async Task SearchForBulkBooks(List<Book> books, bool userInvokedSearch)
        {
            _logger.ProgressInfo("Performing missing search for {0} books", books.Count);
            var downloadedCount = 0;

            foreach (var book in books.OrderBy(a => a.LastSearchTime ?? DateTime.MinValue))
            {
                List<DownloadDecision> decisions;

                try
                {
                    decisions = await _releaseSearchService.BookSearch(book.Id, false, userInvokedSearch, false, _configService.EnableDualFormatTracking);
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Unable to search for book: [{0}]", book);
                    continue;
                }

                var processed = await _processDownloadDecisions.ProcessDecisions(decisions);

                downloadedCount += processed.Grabbed.Count;
            }

            _logger.ProgressInfo("Completed search for {0} books. {1} reports downloaded.", books.Count, downloadedCount);
        }

        public void Execute(BookSearchCommand message)
        {
            if (message.MissingFormat is FormatType format)
            {
                ValidateMissingFormat(format);
                SearchMissingFormat(message.BookIds, message.Trigger, format).GetAwaiter().GetResult();
                return;
            }

            foreach (var bookId in message.BookIds)
            {
                var decisions = _releaseSearchService.BookSearch(bookId, false, message.Trigger == CommandTrigger.Manual, false).GetAwaiter().GetResult();
                var processed = _processDownloadDecisions.ProcessDecisions(decisions).GetAwaiter().GetResult();

                _logger.ProgressInfo("Book search completed. {0} reports downloaded.", processed.Grabbed.Count);
            }
        }

        private async Task SearchMissingFormat(IEnumerable<int> bookIds, CommandTrigger trigger, FormatType format)
        {
            var downloadedCount = 0;

            foreach (var bookId in bookIds?.Distinct() ?? Enumerable.Empty<int>())
            {
                List<DownloadDecision> matching;

                try
                {
                    if (!IsMissingMonitoredFormat(bookId, format))
                    {
                        continue;
                    }

                    var decisions = await _releaseSearchService.BookSearch(
                        bookId,
                        missingOnly: true,
                        userInvokedSearch: trigger == CommandTrigger.Manual,
                        interactiveSearch: false,
                        monitoredBooksOnly: _configService.EnableDualFormatTracking);
                    matching = decisions.Where(decision => RemoteBook.GetReleaseFormat(decision.RemoteBook) == format).ToList();

                    // A file may have arrived, or monitoring may have changed, during the indexer search.
                    if (matching.Count == 0 || !IsMissingMonitoredFormat(bookId, format))
                    {
                        continue;
                    }
                }
                catch (ModelNotFoundException)
                {
                    _logger.Debug("Skipping missing-format search for removed book {0}.", bookId);
                    continue;
                }

                var processed = await _processDownloadDecisions.ProcessDecisions(matching);
                downloadedCount += processed.Grabbed.Count;
            }

            _logger.ProgressInfo("Missing {0} search completed. {1} reports downloaded.", format, downloadedCount);
        }

        private bool IsMissingMonitoredFormat(int bookId, FormatType format)
        {
            var book = _bookService.GetBook(bookId);
            if (!book.Monitored || book.Author?.Value is not { Monitored: true } author || book.BookFiles?.Value is not { } files)
            {
                return false;
            }

            if (!_configService.EnableDualFormatTracking)
            {
                return book.Editions?.Value?.Any(edition => edition.Monitored &&
                    edition.MatchesFormat(format) && files.All(file => file.EditionId != edition.Id)) == true;
            }

            return book.IsFormatMonitored(format) &&
                   _formatProfileService.GetByAuthorIdAndFormat(author.Id, format)?.Monitored == true &&
                   !files.Any(file => Quality.GetFormatType(file.Quality?.Quality) == format);
        }

        private static void ValidateMissingFormat(FormatType format)
        {
            if (format != FormatType.Ebook && format != FormatType.Audiobook)
            {
                throw new ArgumentOutOfRangeException(nameof(BookSearchCommand.MissingFormat), format, "Unsupported book format.");
            }
        }

        public void Execute(MissingBookSearchCommand message)
        {
            if (message.MissingFormat is FormatType format)
            {
                ValidateMissingFormat(format);
                var missingIds = GetMissingFormatBookIds(format, message.AuthorId);
                SearchMissingFormat(missingIds, message.Trigger, format).GetAwaiter().GetResult();
                return;
            }

            List<Book> books;
            var dualFormatEnabled = _configService.EnableDualFormatTracking;

            if (message.AuthorId.HasValue)
            {
                var authorId = message.AuthorId.Value;

                var pagingSpec = new PagingSpec<Book>
                {
                    Page = 1,
                    PageSize = 100000,
                    SortDirection = SortDirection.Ascending,
                    SortKey = "Id"
                };

                AddMonitoredFilter(pagingSpec);

                books = _bookService.BooksWithoutFiles(pagingSpec, null, dualFormatEnabled).Records.Where(e => e.AuthorId.Equals(authorId)).ToList();
            }
            else
            {
                var pagingSpec = new PagingSpec<Book>
                {
                    Page = 1,
                    PageSize = 100000,
                    SortDirection = SortDirection.Ascending,
                    SortKey = "Id"
                };

                AddMonitoredFilter(pagingSpec);

                books = _bookService.BooksWithoutFiles(pagingSpec, null, dualFormatEnabled).Records.ToList();
            }

            var queue = _queueService.GetQueue().Where(q => q.Book != null).Select(q => q.Book.Id);
            var missing = books.Where(e => !queue.Contains(e.Id)).ToList();

            SearchForBulkBooks(missing, message.Trigger == CommandTrigger.Manual).GetAwaiter().GetResult();
        }

        private IEnumerable<int> GetMissingFormatBookIds(FormatType format, int? authorId)
        {
            var queued = _queueService.GetQueue()
                .Where(item => item.Book != null && RemoteBook.GetReleaseFormat(item.RemoteBook, item.Quality?.Quality) == format)
                .Select(item => item.Book.Id)
                .ToHashSet();
            var paging = new PagingSpec<Book>
            {
                Page = 1,
                PageSize = 500,
                SortDirection = SortDirection.Ascending,
                SortKey = "Id"
            };
            AddMonitoredFilter(paging);

            if (authorId is int id)
            {
                paging.FilterExpressions.Add(book => book.Author.Value.Id == id);
            }

            var lastId = 0;
            paging.FilterExpressions.Add(book => book.Id > lastId);
            var dualFormatEnabled = _configService.EnableDualFormatTracking;
            var missing = new List<Book>();
            List<Book> page;

            do
            {
                page = _bookService.BooksWithoutFiles(paging, format, dualFormatEnabled).Records;
                missing.AddRange(page.Where(book => !queued.Contains(book.Id)));
                lastId = page.LastOrDefault()?.Id ?? lastId;
            }
            while (page.Count == paging.PageSize);

            return missing.OrderBy(book => book.LastSearchTime ?? DateTime.MinValue).Select(book => book.Id);
        }

        public void Execute(CutoffUnmetBookSearchCommand message)
        {
            var pagingSpec = new PagingSpec<Book>
            {
                Page = 1,
                PageSize = 100000,
                SortDirection = SortDirection.Ascending,
                SortKey = "Id"
            };

            AddMonitoredFilter(pagingSpec);

            var books = _bookCutoffService.BooksWhereCutoffUnmet(pagingSpec).Records.ToList();

            var queue = _queueService.GetQueue().Where(q => q.Book != null).Select(q => q.Book.Id);
            var cutoffUnmet = books.Where(e => !queue.Contains(e.Id)).ToList();

            SearchForBulkBooks(cutoffUnmet, message.Trigger == CommandTrigger.Manual).GetAwaiter().GetResult();
        }

        private static void AddMonitoredFilter(PagingSpec<Book> pagingSpec)
        {
            pagingSpec.FilterExpressions.Add(v => v.Monitored == true && v.Author.Value.Monitored == true);
        }
    }
}
