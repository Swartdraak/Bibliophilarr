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
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.BookImport;
using NzbDrone.Core.Messaging.Commands;
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
                SearchMissingFormat(message, format).GetAwaiter().GetResult();
                return;
            }

            foreach (var bookId in message.BookIds)
            {
                var decisions = _releaseSearchService.BookSearch(bookId, false, message.Trigger == CommandTrigger.Manual, false).GetAwaiter().GetResult();
                var processed = _processDownloadDecisions.ProcessDecisions(decisions).GetAwaiter().GetResult();

                _logger.ProgressInfo("Book search completed. {0} reports downloaded.", processed.Grabbed.Count);
            }
        }

        private async Task SearchMissingFormat(BookSearchCommand message, FormatType format)
        {
            if (format != FormatType.Ebook && format != FormatType.Audiobook)
            {
                throw new ArgumentOutOfRangeException(nameof(message.MissingFormat), format, "Unsupported book format.");
            }

            var downloadedCount = 0;

            foreach (var bookId in message.BookIds?.Distinct() ?? Enumerable.Empty<int>())
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
                        userInvokedSearch: message.Trigger == CommandTrigger.Manual,
                        interactiveSearch: false,
                        monitoredBooksOnly: true);
                    matching = decisions.Where(decision => decision.RemoteBook?.MatchesKnownFormat(format) == true).ToList();

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
            if (!_configService.EnableDualFormatTracking)
            {
                return false;
            }

            var book = _bookService.GetBook(bookId);
            return book.IsFormatMonitored(format) &&
                   book.Author?.Value is { Monitored: true } author &&
                   _formatProfileService.GetByAuthorIdAndFormat(author.Id, format)?.Monitored == true &&
                   book.BookFiles?.Value is { } files &&
                   !files.Any(file => GetFileFormat(file) == format);
        }

        private static FormatType? GetFileFormat(BookFile file)
        {
            return ImportFormat.ForPath(file.Path) ?? GetKnownQualityFormat(file.Quality?.Quality);
        }

        private static FormatType? GetKnownQualityFormat(Quality quality)
        {
            return quality == null || quality == Quality.Unknown ? null : Quality.GetFormatType(quality);
        }

        public void Execute(MissingBookSearchCommand message)
        {
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

        public void Execute(CutoffUnmetBookSearchCommand message)
        {
            if (message.FormatType is FormatType format)
            {
                if (format != FormatType.Ebook && format != FormatType.Audiobook)
                {
                    throw new ArgumentOutOfRangeException(nameof(message.FormatType), format, "Unsupported book format.");
                }

                SearchCutoffFormat(message, format).GetAwaiter().GetResult();
                return;
            }

            if (message.BookIds != null)
            {
                throw new ArgumentException("A format is required for selected cutoff searches.", nameof(message.FormatType));
            }

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

        private async Task SearchCutoffFormat(CutoffUnmetBookSearchCommand message, FormatType format)
        {
            var bookIds = message.BookIds?.Distinct().ToList() ?? GetCutoffBookIds(format, message.AuthorId);
            var downloadedCount = 0;
            foreach (var bookId in bookIds)
            {
                if (HasQueuedFormat(bookId, format) || !CutoffStillUnmet(bookId, format, message.AuthorId))
                {
                    continue;
                }

                List<DownloadDecision> matching;
                try
                {
                    var decisions = await _releaseSearchService.BookSearch(
                        bookId,
                        missingOnly: false,
                        userInvokedSearch: message.Trigger == CommandTrigger.Manual,
                        interactiveSearch: false,
                        monitoredBooksOnly: _configService.EnableDualFormatTracking);
                    matching = decisions.Where(decision =>
                        (decision.Approved || decision.TemporarilyRejected) &&
                        decision.RemoteBook?.MatchesKnownFormat(format) == true &&
                        decision.RemoteBook.Books is { Count: > 0 } books && books.All(book => book.Id == bookId)).ToList();
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Unable to search for a {0} cutoff upgrade for book {1}.", format, bookId);
                    continue;
                }

                // Files, monitoring and the author format profile may have changed while searching indexers.
                if (matching.Count == 0 || HasQueuedFormat(bookId, format) || !CutoffStillUnmet(bookId, format, message.AuthorId))
                {
                    continue;
                }

                var processed = await _processDownloadDecisions.ProcessDecisions(matching);
                downloadedCount += processed.Grabbed.Count;
            }

            _logger.ProgressInfo("{0} cutoff upgrade search completed. {1} reports downloaded.", format, downloadedCount);
        }

        private List<int> GetCutoffBookIds(FormatType format, int? authorId)
        {
            var books = new List<Book>();
            var paging = CutoffPaging(authorId, 500);
            var lastId = 0;
            paging.FilterExpressions.Add(book => book.Id > lastId);

            List<Book> page;
            do
            {
                page = _bookCutoffService.BooksWhereCutoffUnmet(paging, format, true).Records;
                books.AddRange(page);
                lastId = page.LastOrDefault()?.Id ?? lastId;
            }
            while (page.Count == paging.PageSize);

            return books.OrderBy(book => book.LastSearchTime ?? DateTime.MinValue).Select(book => book.Id).ToList();
        }

        private bool HasQueuedFormat(int bookId, FormatType format)
        {
            return _queueService.GetQueue().Any(item =>
            {
                var remote = item.RemoteBook;
                var queuedFormat = GetKnownQualityFormat(item.Quality?.Quality) ?? GetKnownQualityFormat(remote?.ParsedBookInfo?.Quality?.Quality);
                return item.Book?.Id == bookId && queuedFormat == format && (remote?.ResolvedFormatType ?? queuedFormat) == format;
            });
        }

        private bool CutoffStillUnmet(int bookId, FormatType format, int? authorId)
        {
            var paging = CutoffPaging(authorId, 1);
            paging.FilterExpressions.Add(book => book.Id == bookId);
            return _bookCutoffService.BooksWhereCutoffUnmet(paging, format, true).Records.Count != 0;
        }

        private static PagingSpec<Book> CutoffPaging(int? authorId, int pageSize)
        {
            var paging = new PagingSpec<Book>
            {
                Page = 1,
                PageSize = pageSize,
                SortDirection = SortDirection.Ascending,
                SortKey = "Id"
            };

            if (authorId is int id)
            {
                paging.FilterExpressions.Add(book => book.Author.Value.Id == id);
            }

            return paging;
        }

        private static void AddMonitoredFilter(PagingSpec<Book> pagingSpec)
        {
            pagingSpec.FilterExpressions.Add(v => v.Monitored == true && v.Author.Value.Monitored == true);
        }
    }
}
