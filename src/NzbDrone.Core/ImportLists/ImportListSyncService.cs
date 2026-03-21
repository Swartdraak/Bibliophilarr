using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Instrumentation.Extensions;
using NzbDrone.Core.Books;
using NzbDrone.Core.Books.Commands;
using NzbDrone.Core.ImportLists.Exclusions;
using NzbDrone.Core.IndexerSearch;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.ImportLists
{
    public class ImportListSyncService : IExecute<ImportListSyncCommand>
    {
        private readonly IImportListFactory _importListFactory;
        private readonly IImportListExclusionService _importListExclusionService;
        private readonly IFetchAndParseImportList _listFetcherAndParser;
        private readonly IMetadataProviderOrchestrator _metadataOrchestrator;
        private readonly IMetadataQueryNormalizationService _queryNormalizationService;
        private readonly IAuthorService _authorService;
        private readonly IBookService _bookService;
        private readonly IEditionService _editionService;
        private readonly IAddAuthorService _addAuthorService;
        private readonly IAddBookService _addBookService;
        private readonly IEventAggregator _eventAggregator;
        private readonly IManageCommandQueue _commandQueueManager;
        private readonly Logger _logger;

        public ImportListSyncService(IImportListFactory importListFactory,
                                     IImportListExclusionService importListExclusionService,
                                     IFetchAndParseImportList listFetcherAndParser,
                                     IMetadataProviderOrchestrator metadataOrchestrator,
                                     IMetadataQueryNormalizationService queryNormalizationService,
                                     IAuthorService authorService,
                                     IBookService bookService,
                                     IEditionService editionService,
                                     IAddAuthorService addAuthorService,
                                     IAddBookService addBookService,
                                     IEventAggregator eventAggregator,
                                     IManageCommandQueue commandQueueManager,
                                     Logger logger)
        {
            _importListFactory = importListFactory;
            _importListExclusionService = importListExclusionService;
            _listFetcherAndParser = listFetcherAndParser;
            _metadataOrchestrator = metadataOrchestrator;
            _queryNormalizationService = queryNormalizationService;
            _authorService = authorService;
            _bookService = bookService;
            _editionService = editionService;
            _addAuthorService = addAuthorService;
            _addBookService = addBookService;
            _eventAggregator = eventAggregator;
            _commandQueueManager = commandQueueManager;
            _logger = logger;
        }

        private List<Book> SyncAll()
        {
            if (_importListFactory.AutomaticAddEnabled().Empty())
            {
                _logger.Debug("No import lists with automatic add enabled");

                return new List<Book>();
            }

            _logger.ProgressInfo("Starting Import List Sync");

            var listItems = _listFetcherAndParser.Fetch().ToList();

            return ProcessListItems(listItems);
        }

        private List<Book> SyncList(ImportListDefinition definition)
        {
            _logger.ProgressInfo($"Starting Import List Refresh for List {definition.Name}");

            var listItems = _listFetcherAndParser.FetchSingleList(definition).ToList();

            return ProcessListItems(listItems);
        }

        private List<Book> ProcessListItems(List<ImportListItemInfo> items)
        {
            var processed = new List<Book>();
            var authorsToAdd = new List<Author>();
            var booksToAdd = new List<Book>();

            if (items.Count == 0)
            {
                _logger.ProgressInfo("No list items to process");

                return new List<Book>();
            }

            _logger.ProgressInfo("Processing {0} list items", items.Count);

            var reportNumber = 1;

            var listExclusions = _importListExclusionService.All();

            foreach (var report in items)
            {
                _logger.ProgressTrace("Processing list item {0}/{1}", reportNumber, items.Count);

                reportNumber++;

                var importList = _importListFactory.Get(report.ImportListId);

                if (report.Book.IsNotNullOrWhiteSpace() || report.EditionOpenLibraryId.IsNotNullOrWhiteSpace())
                {
                    if (report.EditionOpenLibraryId.IsNullOrWhiteSpace() || report.AuthorOpenLibraryId.IsNullOrWhiteSpace() || report.BookOpenLibraryId.IsNullOrWhiteSpace())
                    {
                        MapBookReport(report);
                    }

                    ProcessBookReport(importList, report, listExclusions, booksToAdd, authorsToAdd);
                }
                else if (report.Author.IsNotNullOrWhiteSpace() || report.AuthorOpenLibraryId.IsNotNullOrWhiteSpace())
                {
                    if (report.AuthorOpenLibraryId.IsNullOrWhiteSpace())
                    {
                        MapAuthorReport(report);
                    }

                    ProcessAuthorReport(importList, report, listExclusions, authorsToAdd);
                }
            }

            var addedAuthors = _addAuthorService.AddAuthors(authorsToAdd, false);
            var addedBooks = _addBookService.AddBooks(booksToAdd, false);

            var message = string.Format($"Import List Sync Completed. Items found: {items.Count}, Authors added: {authorsToAdd.Count}, Books added: {booksToAdd.Count}");

            _logger.ProgressInfo(message);

            var toRefresh = addedAuthors.Select(x => x.Id).Concat(addedBooks.Select(x => x.Author.Value.Id)).Distinct().ToList();
            if (toRefresh.Any())
            {
                _commandQueueManager.Push(new BulkRefreshAuthorCommand(toRefresh, true));
            }

            return processed;
        }

        private void MapBookReport(ImportListItemInfo report)
        {
            if (report.AuthorOpenLibraryId.IsNotNullOrWhiteSpace() && report.BookOpenLibraryId.IsNotNullOrWhiteSpace())
            {
                return;
            }

            if (report.EditionOpenLibraryId.IsNotNullOrWhiteSpace())
            {
                // check the local DB
                var edition = _editionService.GetEditionByForeignEditionId(report.EditionOpenLibraryId);

                if (edition != null)
                {
                    var book = edition.Book.Value;
                    report.BookOpenLibraryId = book.ForeignBookId;
                    report.Book = edition.Title;
                    report.Author ??= book.AuthorMetadata.Value.Name;
                    report.AuthorOpenLibraryId ??= book.AuthorMetadata.Value.ForeignAuthorId;
                    return;
                }

                var editionResults = _metadataOrchestrator.SearchByExternalId("openlibrary", report.EditionOpenLibraryId);
                var remoteBook = editionResults.FirstOrDefault();

                if (remoteBook == null)
                {
                    _logger.Debug($"Nothing found for edition [{report.EditionOpenLibraryId}]");
                    report.EditionOpenLibraryId = null;
                    return;
                }

                _logger.Trace($"Mapped {report.EditionOpenLibraryId} to [{remoteBook.ForeignBookId}] {remoteBook.Title}");

                report.BookOpenLibraryId = remoteBook.ForeignBookId;
                report.Book = remoteBook.Title;
                report.Author ??= remoteBook.AuthorMetadata.Value.Name;
                report.AuthorOpenLibraryId ??= remoteBook.AuthorMetadata.Value.ForeignAuthorId;
            }
            else if (report.BookOpenLibraryId.IsNotNullOrWhiteSpace())
            {
                var mappedBook = _metadataOrchestrator.GetBookInfo(report.BookOpenLibraryId);

                report.BookOpenLibraryId = mappedBook.Item2.ForeignBookId;
                report.Book = mappedBook.Item2.Title;
                report.AuthorOpenLibraryId = mappedBook.Item3.First().ForeignAuthorId;
            }
            else
            {
                var searchResults = SearchBooksWithVariants(report.Book, report.Author, false);
                var mappedBook = searchResults.FirstOrDefault();

                if (mappedBook == null)
                {
                    _logger.Trace($"Nothing found for {report.Author} - {report.Book}");
                    return;
                }

                _logger.Trace($"Mapped Book {report.Book} by Author {report.Author} to [{mappedBook.ForeignBookId}] {mappedBook.Title}");

                report.BookOpenLibraryId = mappedBook.ForeignBookId;
                report.Book = mappedBook.Title;
                report.Author ??= mappedBook.AuthorMetadata.Value.Name;
                report.AuthorOpenLibraryId ??= mappedBook.AuthorMetadata.Value.ForeignAuthorId;
                report.EditionOpenLibraryId = mappedBook.ForeignEditionId;
            }
        }

        private void ProcessBookReport(ImportListDefinition importList, ImportListItemInfo report, List<ImportListExclusion> listExclusions, List<Book> booksToAdd, List<Author> authorsToAdd)
        {
            // Check to see if book in DB
            var existingBook = _bookService.FindById(report.BookOpenLibraryId);

            // Check to see if book excluded
            var excludedBook = listExclusions.SingleOrDefault(s => s.ForeignId == report.BookOpenLibraryId);

            // Check to see if author excluded
            var excludedAuthor = listExclusions.SingleOrDefault(s => s.ForeignId == report.AuthorOpenLibraryId);

            if (excludedBook != null)
            {
                _logger.Debug("{0} [{1}] Rejected due to list exclusion", report.EditionOpenLibraryId, report.Book);
                return;
            }

            if (excludedAuthor != null)
            {
                _logger.Debug("{0} [{1}] Rejected due to list exclusion for parent author", report.EditionOpenLibraryId, report.Book);
                return;
            }

            if (existingBook != null)
            {
                _logger.Debug("{0} [{1}] Rejected, Book Exists in DB.  Ensuring Book and Author monitored.", report.EditionOpenLibraryId, report.Book);

                if (importList.ShouldMonitorExisting && importList.ShouldMonitor != ImportListMonitorType.None)
                {
                    if (!existingBook.Monitored)
                    {
                        _bookService.SetBookMonitored(existingBook.Id, true);

                        if (importList.ShouldMonitor == ImportListMonitorType.SpecificBook)
                        {
                            _commandQueueManager.Push(new BookSearchCommand(new List<int> { existingBook.Id }));
                        }
                    }

                    var existingAuthor = existingBook.Author.Value;
                    var doSearch = false;

                    if (importList.ShouldMonitor == ImportListMonitorType.EntireAuthor)
                    {
                        if (existingAuthor.Books.Value.Any(x => !x.Monitored))
                        {
                            doSearch = true;
                            _bookService.SetMonitored(existingAuthor.Books.Value.Select(x => x.Id), true);
                        }
                    }

                    if (!existingAuthor.Monitored)
                    {
                        doSearch = true;
                        existingAuthor.Monitored = true;
                        _authorService.UpdateAuthor(existingAuthor);
                    }

                    if (doSearch)
                    {
                        _commandQueueManager.Push(new MissingBookSearchCommand(existingAuthor.Id));
                    }
                }

                return;
            }

            // Append Book if not already in DB or already on add list
            if (booksToAdd.All(s => s.ForeignBookId != report.BookOpenLibraryId))
            {
                var monitored = importList.ShouldMonitor != ImportListMonitorType.None;

                var toAddAuthor = new Author
                {
                    Monitored = monitored,
                    MonitorNewItems = importList.MonitorNewItems,
                    RootFolderPath = importList.RootFolderPath,
                    QualityProfileId = importList.ProfileId,
                    MetadataProfileId = importList.MetadataProfileId,
                    Tags = importList.Tags,
                    AddOptions = new AddAuthorOptions
                    {
                        SearchForMissingBooks = importList.ShouldSearch,
                        Monitored = monitored,
                        Monitor = monitored ? MonitorTypes.All : MonitorTypes.None
                    }
                };

                if (report.AuthorOpenLibraryId != null && report.Author != null)
                {
                    toAddAuthor = ProcessAuthorReport(importList, report, listExclusions, authorsToAdd);
                }

                var toAdd = new Book
                {
                    ForeignBookId = report.BookOpenLibraryId,
                    Monitored = monitored,
                    AnyEditionOk = true,
                    Editions = new List<Edition>(),
                    Author = toAddAuthor,
                    AddOptions = new AddBookOptions
                    {
                        // Only search for new book for existing authors
                        // New author searches are triggered by SearchForMissingBooks
                        SearchForNewBook = importList.ShouldSearch && toAddAuthor.Id > 0
                    }
                };

                if (report.EditionOpenLibraryId.IsNotNullOrWhiteSpace())
                {
                    toAdd.Editions.Value.Add(new Edition
                    {
                        ForeignEditionId = report.EditionOpenLibraryId,
                        Monitored = true
                    });
                }

                if (importList.ShouldMonitor == ImportListMonitorType.SpecificBook && toAddAuthor.AddOptions != null)
                {
                    Debug.Assert(toAddAuthor.Id == 0, "new author added but ID is not 0");
                    toAddAuthor.AddOptions.BooksToMonitor.Add(toAdd.ForeignBookId);
                }

                booksToAdd.Add(toAdd);
            }
        }

        private void MapAuthorReport(ImportListItemInfo report)
        {
            var searchResults = SearchBooksWithVariants(report.Author, null, false);
            var mappedBook = searchResults.FirstOrDefault();

            if (mappedBook == null)
            {
                _logger.Trace($"Nothing found for {report.Author}");
                return;
            }

            _logger.Trace($"Mapped {report.Author} to [{mappedBook.AuthorMetadata.Value.Name}]");

            report.Author = mappedBook.AuthorMetadata.Value.Name;
            report.AuthorOpenLibraryId = mappedBook.AuthorMetadata.Value.ForeignAuthorId;
        }

        private List<Book> SearchBooksWithVariants(string title, string author, bool getAllEditions)
        {
            var titleVariants = (_queryNormalizationService.BuildTitleVariants(title) ?? new List<string>())
                .Where(x => x.IsNotNullOrWhiteSpace())
                .Distinct()
                .ToList();

            if (!titleVariants.Any() && title.IsNotNullOrWhiteSpace())
            {
                titleVariants.Add(title);
            }

            var authorVariants = (_queryNormalizationService.ExpandAuthorAliases(new[] { author }) ?? new List<string>())
                .Where(x => x.IsNotNullOrWhiteSpace())
                .Distinct()
                .ToList();

            if (!authorVariants.Any() && author.IsNotNullOrWhiteSpace())
            {
                authorVariants.Add(author);
            }

            if (!authorVariants.Any())
            {
                authorVariants.Add(null);
            }

            foreach (var titleVariant in titleVariants)
            {
                foreach (var authorVariant in authorVariants)
                {
                    var results = _metadataOrchestrator.SearchForNewBook(titleVariant, authorVariant, getAllEditions);
                    if (results.Any())
                    {
                        return results;
                    }
                }
            }

            return new List<Book>();
        }

        private Author ProcessAuthorReport(ImportListDefinition importList, ImportListItemInfo report, List<ImportListExclusion> listExclusions, List<Author> authorsToAdd)
        {
            if (report.AuthorOpenLibraryId == null)
            {
                return null;
            }

            // Check to see if author in DB
            var existingAuthor = _authorService.FindById(report.AuthorOpenLibraryId);

            // Check to see if author excluded
            var excludedAuthor = listExclusions.SingleOrDefault(s => s.ForeignId == report.AuthorOpenLibraryId);

            // Check to see if author in import
            var existingImportAuthor = authorsToAdd.Find(i => i.ForeignAuthorId == report.AuthorOpenLibraryId);

            if (excludedAuthor != null)
            {
                _logger.Debug("{0} [{1}] Rejected due to list exclusion", report.AuthorOpenLibraryId, report.Author);
                return null;
            }

            if (existingAuthor != null)
            {
                _logger.Debug("{0} [{1}] Rejected, Author Exists in DB.  Ensuring Author monitored", report.AuthorOpenLibraryId, report.Author);

                if (importList.ShouldMonitorExisting && !existingAuthor.Monitored)
                {
                    existingAuthor.Monitored = true;
                    _authorService.UpdateAuthor(existingAuthor);
                }

                return existingAuthor;
            }

            if (existingImportAuthor != null)
            {
                _logger.Debug("{0} [{1}] Rejected, Author Exists in Import.", report.AuthorOpenLibraryId, report.Author);

                return existingImportAuthor;
            }

            var monitored = importList.ShouldMonitor != ImportListMonitorType.None;

            var toAdd = new Author
            {
                Metadata = new AuthorMetadata
                {
                    ForeignAuthorId = report.AuthorOpenLibraryId,
                    Name = report.Author
                },
                Monitored = monitored,
                MonitorNewItems = importList.MonitorNewItems,
                RootFolderPath = importList.RootFolderPath,
                QualityProfileId = importList.ProfileId,
                MetadataProfileId = importList.MetadataProfileId,
                Tags = importList.Tags,
                AddOptions = new AddAuthorOptions
                {
                    SearchForMissingBooks = importList.ShouldSearch,
                    Monitored = monitored,
                    Monitor = monitored ? MonitorTypes.All : MonitorTypes.None
                }
            };

            authorsToAdd.Add(toAdd);

            return toAdd;
        }

        public void Execute(ImportListSyncCommand message)
        {
            var processed = message.DefinitionId.HasValue ? SyncList(_importListFactory.Get(message.DefinitionId.Value)) : SyncAll();

            _eventAggregator.PublishEvent(new ImportListSyncCompleteEvent(processed));
        }
    }
}
