using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Instrumentation.Extensions;
using NzbDrone.Core.Books.Commands;
using NzbDrone.Core.Books.Events;
using NzbDrone.Core.Exceptions;
using NzbDrone.Core.History;
using NzbDrone.Core.MediaCover;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.RootFolders;

namespace NzbDrone.Core.Books
{
    public interface IRefreshBookService
    {
        bool RefreshBookInfo(Book book, List<Book> remoteBooks, Author remoteData, bool forceUpdateFileTags);
        bool RefreshBookInfo(List<Book> books, List<Book> remoteBooks, Author remoteData, bool forceBookRefresh, bool forceUpdateFileTags, DateTime? lastUpdate);
    }

    public class RefreshBookService : RefreshEntityServiceBase<Book, Edition>,
        IRefreshBookService,
        IExecute<RefreshBookCommand>,
        IExecute<BulkRefreshBookCommand>
    {
        private readonly IBookService _bookService;
        private readonly IAuthorService _authorService;
        private readonly IRootFolderService _rootFolderService;
        private readonly IAddAuthorService _addAuthorService;
        private readonly IEditionService _editionService;
        private readonly IMetadataProviderOrchestrator _orchestrator;
        private readonly IRefreshEditionService _refreshEditionService;
        private readonly IMediaFileService _mediaFileService;
        private readonly IHistoryService _historyService;
        private readonly IEventAggregator _eventAggregator;
        private readonly ICheckIfBookShouldBeRefreshed _checkIfBookShouldBeRefreshed;
        private readonly IMapCoversToLocal _mediaCoverService;
        private readonly Logger _logger;
        private readonly ConcurrentDictionary<string, DateTime> _pendingDeleteMisses = new ConcurrentDictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);

        [ThreadStatic]
        private static bool _suppressDeletesForCurrentRun;

        public RefreshBookService(IBookService bookService,
                                  IAuthorService authorService,
                                  IRootFolderService rootFolderService,
                                  IAddAuthorService addAuthorService,
                                  IEditionService editionService,
                                  IAuthorMetadataService authorMetadataService,
                                  IMetadataProviderOrchestrator orchestrator,
                                  IRefreshEditionService refreshEditionService,
                                  IMediaFileService mediaFileService,
                                  IHistoryService historyService,
                                  IEventAggregator eventAggregator,
                                  ICheckIfBookShouldBeRefreshed checkIfBookShouldBeRefreshed,
                                  IMapCoversToLocal mediaCoverService,
                                  Logger logger)
        : base(logger, authorMetadataService)
        {
            _bookService = bookService;
            _authorService = authorService;
            _rootFolderService = rootFolderService;
            _addAuthorService = addAuthorService;
            _editionService = editionService;
            _orchestrator = orchestrator;
            _refreshEditionService = refreshEditionService;
            _mediaFileService = mediaFileService;
            _historyService = historyService;
            _eventAggregator = eventAggregator;
            _checkIfBookShouldBeRefreshed = checkIfBookShouldBeRefreshed;
            _mediaCoverService = mediaCoverService;
            _logger = logger;
        }

        private Author GetSkyhookData(Book book)
        {
            try
            {
                var tuple = _orchestrator.GetBookInfo(book.ForeignBookId);
                var author = _orchestrator.GetAuthorInfo(tuple.Item1);
                var newbook = tuple.Item2;

                newbook.Author = author;
                newbook.AuthorMetadata = author.Metadata.Value;
                newbook.AuthorMetadataId = book.AuthorMetadataId;
                newbook.AuthorMetadata.Value.Id = book.AuthorMetadataId;

                author.Books = new List<Book> { newbook };
                return author;
            }
            catch (BookNotFoundException)
            {
                _logger.Warn("Metadata provider could not find book '{0}' (id: {1}). " +
                             "The book will be kept with existing metadata. " +
                             "This may occur when a provider removes a work entry. " +
                             "Consider manually updating the book's metadata or re-identifying the file.",
                             book.Title,
                             book.ForeignBookId);
            }

            return null;
        }

        protected override RemoteData GetRemoteData(Book local, List<Book> remote, Author data)
        {
            var result = new RemoteData();

            var book = remote.SingleOrDefault(x => x.ForeignBookId == local.ForeignBookId);

            if (book == null)
            {
                data = GetSkyhookData(local);

                if (data?.Books?.Value == null)
                {
                    return result;
                }

                book = data.Books.Value.SingleOrDefault(x => x.ForeignBookId == local.ForeignBookId);
            }

            result.Entity = book;
            if (result.Entity != null)
            {
                result.Entity.Id = local.Id;
                ClearPendingDelete(local.ForeignBookId);
            }

            return result;
        }

        protected override void EnsureNewParent(Book local, Book remote)
        {
            // Make sure the appropriate author exists (it could be that a book changes parent)
            // The authorMetadata entry will be in the db but make sure a corresponding author is too
            // so that the book doesn't just disappear.

            // NOTE filter by metadata id before hitting database
            _logger.Trace($"Ensuring parent author exists [{remote.AuthorMetadata.Value.ForeignAuthorId}]");

            var newAuthorForeignId = remote.AuthorMetadata.Value.ForeignAuthorId;
            var oldAuthorForeignId = local.AuthorMetadata?.Value?.ForeignAuthorId;

            // Guard: if the remote author differs from the local author, this is
            // likely a co-authored book where cached_contributors[0] returned a
            // different person (e.g. an editor like John Joseph Adams).  Do NOT
            // auto-add an entirely new author that the user never requested.
            // Instead, keep the book under its current author.
            if (oldAuthorForeignId.IsNotNullOrWhiteSpace() &&
                !string.Equals(newAuthorForeignId, oldAuthorForeignId, StringComparison.OrdinalIgnoreCase))
            {
                var newAuthor = _authorService.FindById(newAuthorForeignId);
                if (newAuthor == null)
                {
                    _logger.Debug(
                        "Skipping parent author change for '{0}': remote says [{1}] but local is [{2}] and the remote author is not in the library.",
                        local.Title,
                        newAuthorForeignId,
                        oldAuthorForeignId);

                    // Override the remote book's metadata back to the local author
                    // so downstream processing keeps the book in place.
                    remote.AuthorMetadata = local.AuthorMetadata;
                    remote.AuthorMetadataId = local.AuthorMetadataId;
                    return;
                }
            }

            var existingAuthor = _authorService.FindById(newAuthorForeignId);

            if (existingAuthor == null)
            {
                var oldAuthor = local.Author.Value;
                var addAuthor = new Author
                {
                    Metadata = remote.AuthorMetadata.Value,
                    MetadataProfileId = oldAuthor.MetadataProfileId,
                    QualityProfileId = oldAuthor.QualityProfileId,
                    RootFolderPath = _rootFolderService.GetBestRootFolderPath(oldAuthor.Path),
                    Monitored = oldAuthor.Monitored,
                    Tags = oldAuthor.Tags
                };
                _logger.Debug($"Adding missing parent author {addAuthor}");
                _addAuthorService.AddAuthor(addAuthor);
            }
        }

        protected override bool ShouldDelete(Book local)
        {
            if (_suppressDeletesForCurrentRun)
            {
                _logger.Warn("Suppressing delete for {0} due to degraded metadata-provider window", local);
                return false;
            }

            // not manually added and has no files
            var eligible = local.AddOptions.AddType != BookAddType.Manual &&
                           !_mediaFileService.GetFilesByBook(local.Id).Any();

            if (!eligible)
            {
                return false;
            }

            var key = local.ForeignBookId;
            if (string.IsNullOrWhiteSpace(key))
            {
                return true;
            }

            if (_pendingDeleteMisses.TryAdd(key, DateTime.UtcNow))
            {
                _logger.Warn("Marking {0} stale after first metadata miss; hard delete requires a repeat miss", local);
                return false;
            }

            ClearPendingDelete(key);
            return true;
        }

        protected override void LogProgress(Book local)
        {
            _logger.ProgressInfo("Updating Info for {0}", local.Title);
        }

        protected override bool IsMerge(Book local, Book remote)
        {
            return local.ForeignBookId != remote.ForeignBookId;
        }

        protected override UpdateResult UpdateEntity(Book local, Book remote)
        {
            UpdateResult result;

            remote.UseDbFieldsFrom(local);

            if (local.Title != (remote.Title ?? "Unknown") ||
                local.ForeignBookId != remote.ForeignBookId ||
                local.AuthorMetadata.Value.ForeignAuthorId != remote.AuthorMetadata.Value.ForeignAuthorId)
            {
                result = UpdateResult.UpdateTags;
            }
            else if (!local.Equals(remote))
            {
                result = UpdateResult.Standard;
            }
            else
            {
                result = UpdateResult.None;
            }

            // Force update and fetch covers if images have changed so that we can write them into tags
            // if (remote.Images.Any() && !local.Images.SequenceEqual(remote.Images))
            // {
            //     _mediaCoverService.EnsureBookCovers(remote);
            //     result = UpdateResult.UpdateTags;
            // }
            local.UseMetadataFrom(remote);

            local.AuthorMetadataId = remote.AuthorMetadata.Value.Id;
            local.LastInfoSync = DateTime.UtcNow;

            return result;
        }

        protected override UpdateResult MergeEntity(Book local, Book target, Book remote)
        {
            _logger.Warn($"Book {local} was merged with {remote} because the original was a duplicate.");

            // Update book ids for trackfiles
            var files = _mediaFileService.GetFilesByBook(local.Id);
            files.ForEach(x => x.EditionId = target.Editions.Value.Single(e => e.Monitored).Id);
            _mediaFileService.Update(files);

            // Update book ids for history
            var items = _historyService.GetByBook(local.Id, null);
            items.ForEach(x => x.BookId = target.Id);
            _historyService.UpdateMany(items);

            // Finally delete the old book
            _bookService.DeleteMany(new List<Book> { local });

            return UpdateResult.UpdateTags;
        }

        protected override Book GetEntityByForeignId(Book local)
        {
            return _bookService.FindById(local.ForeignBookId);
        }

        protected override void SaveEntity(Book local)
        {
            // Use UpdateMany to avoid firing the book edited event
            _bookService.UpdateMany(new List<Book> { local });
        }

        protected override void DeleteEntity(Book local, bool deleteFiles)
        {
            _bookService.DeleteBook(local.Id, deleteFiles);
        }

        protected override List<Edition> GetRemoteChildren(Book local, Book remote)
        {
            return remote.Editions.Value.DistinctBy(m => m.ForeignEditionId).ToList();
        }

        protected override List<Edition> GetLocalChildren(Book entity, List<Edition> remoteChildren)
        {
            return _editionService.GetEditionsForRefresh(entity.Id, remoteChildren.Select(x => x.ForeignEditionId).ToList());
        }

        protected override Tuple<Edition, List<Edition>> GetMatchingExistingChildren(List<Edition> existingChildren, Edition remote)
        {
            var existingChild = existingChildren.SingleOrDefault(x => x.ForeignEditionId == remote.ForeignEditionId);
            return Tuple.Create(existingChild, new List<Edition>());
        }

        protected override void PrepareNewChild(Edition child, Book entity)
        {
            child.BookId = entity.Id;
            child.Book = entity;
        }

        protected override void PrepareExistingChild(Edition local, Edition remote, Book entity)
        {
            local.BookId = entity.Id;
            local.Book = entity;

            remote.UseDbFieldsFrom(local);
        }

        protected override void AddChildren(List<Edition> children)
        {
            // NOTE: Intentionally empty — children are added in RefreshChildren to control monitored status.
        }

        private void MonitorSingleEdition(SortedChildren children)
        {
            children.Old.ForEach(x => x.Monitored = false);
            var monitored = children.Future.Where(x => x.Monitored).ToList();

            if (monitored.Count == 1)
            {
                return;
            }

            if (monitored.Count == 0)
            {
                monitored = children.Future;
            }

            if (monitored.Count == 0)
            {
                // there are no future children so nothing to do
                return;
            }

            var toMonitor = monitored.OrderByDescending(x => x.Id > 0 ? _mediaFileService.GetFilesByEdition(x.Id).Count : 0)
                .ThenByDescending(x => x.Ratings.Popularity).First();

            monitored.ForEach(x => x.Monitored = false);
            toMonitor.Monitored = true;

            // force update of anything we've messed with
            var extraToUpdate = children.UpToDate.Where(x => monitored.Contains(x));
            children.UpToDate = children.UpToDate.Except(extraToUpdate).ToList();
            children.Updated.AddRange(extraToUpdate);

            Debug.Assert(!children.Future.Any() || children.Future.Count(x => x.Monitored) == 1, "one edition monitored");
        }

        protected override bool RefreshChildren(SortedChildren localChildren, List<Edition> remoteChildren, Author remoteData, bool forceChildRefresh, bool forceUpdateFileTags, DateTime? lastUpdate)
        {
            // make sure only one of the releases ends up monitored
            MonitorSingleEdition(localChildren);

            localChildren.All.ForEach(x => _logger.Trace($"release: {x} monitored: {x.Monitored}"));

            _editionService.InsertMany(localChildren.Added);

            return _refreshEditionService.RefreshEditionInfo(localChildren.Added, localChildren.Updated, localChildren.Merged, localChildren.Deleted, localChildren.UpToDate, remoteChildren, forceUpdateFileTags);
        }

        protected override void PublishEntityUpdatedEvent(Book entity)
        {
            // Fetch fresh from DB so all lazy loads are available
            _eventAggregator.PublishEvent(new BookUpdatedEvent(_bookService.GetBook(entity.Id)));
        }

        public bool RefreshBookInfo(List<Book> books, List<Book> remoteBooks, Author remoteData, bool forceBookRefresh, bool forceUpdateFileTags, DateTime? lastUpdate)
        {
            var updated = false;
            var previousSuppression = _suppressDeletesForCurrentRun;
            _suppressDeletesForCurrentRun = remoteData == null && (remoteBooks == null || remoteBooks.Count == 0);

            try
            {
                foreach (var book in books)
                {
                    if (forceBookRefresh || _checkIfBookShouldBeRefreshed.ShouldRefresh(book))
                    {
                        updated |= RefreshBookInfo(book, remoteBooks, remoteData, forceUpdateFileTags);
                    }
                    else
                    {
                        _logger.Debug("Skipping refresh of book: {0}", book.Title);
                    }
                }
            }
            finally
            {
                _suppressDeletesForCurrentRun = previousSuppression;
            }

            return updated;
        }

        public bool RefreshBookInfo(Book book, List<Book> remoteBooks, Author remoteData, bool forceUpdateFileTags)
        {
            return RefreshEntityInfo(book, remoteBooks, remoteData, true, forceUpdateFileTags, null);
        }

        public bool RefreshBookInfo(Book book)
        {
            var data = GetSkyhookData(book);

            if (data?.Books?.Value == null)
            {
                _logger.Error("Could not refresh info for {0}", book);
                return false;
            }

            return RefreshBookInfo(book, data.Books, data, false);
        }

        public void Execute(BulkRefreshBookCommand message)
        {
            var books = _bookService.GetBooks(message.BookIds);

            foreach (var book in books)
            {
                RefreshBookInfo(book);
            }
        }

        public void Execute(RefreshBookCommand message)
        {
            if (message.BookId.HasValue)
            {
                var book = _bookService.GetBook(message.BookId.Value);

                RefreshBookInfo(book);
            }
        }

        private void ClearPendingDelete(string foreignBookId)
        {
            if (string.IsNullOrWhiteSpace(foreignBookId))
            {
                return;
            }

            _pendingDeleteMisses.TryRemove(foreignBookId, out _);
        }
    }
}
