using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Instrumentation;
using NzbDrone.Common.Instrumentation.Extensions;
using NzbDrone.Core.Books.Commands;
using NzbDrone.Core.Books.Events;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Exceptions;
using NzbDrone.Core.History;
using NzbDrone.Core.ImportLists.Exclusions;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.Commands;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.Profiles.Metadata;
using NzbDrone.Core.RootFolders;

namespace NzbDrone.Core.Books
{
    public interface IRefreshAuthorService
    {
    }

    public class RefreshAuthorService : RefreshEntityServiceBase<Author, Book>,
        IRefreshAuthorService,
        IExecute<RefreshAuthorCommand>,
        IExecute<BulkRefreshAuthorCommand>
    {
        private readonly IMetadataProviderOrchestrator _orchestrator;
        private readonly IAuthorService _authorService;
        private readonly IBookService _bookService;
        private readonly IMetadataProfileService _metadataProfileService;
        private readonly IRefreshBookService _refreshBookService;
        private readonly IRefreshSeriesService _refreshSeriesService;
        private readonly IEventAggregator _eventAggregator;
        private readonly IManageCommandQueue _commandQueueManager;
        private readonly ICommandResultReporter _commandResultReporter;
        private readonly IMediaFileService _mediaFileService;
        private readonly IHistoryService _historyService;
        private readonly IRootFolderService _rootFolderService;
        private readonly ICheckIfAuthorShouldBeRefreshed _checkIfAuthorShouldBeRefreshed;
        private readonly IMonitorNewBookService _monitorNewBookService;
        private readonly IConfigService _configService;
        private readonly IImportListExclusionService _importListExclusionService;
        private readonly ICatalogueIdentityRepository _catalogueIdentity;
        private readonly Logger _logger;

        public RefreshAuthorService(IMetadataProviderOrchestrator orchestrator,
                                    IAuthorService authorService,
                                    IAuthorMetadataService authorMetadataService,
                                    IBookService bookService,
                                    IMetadataProfileService metadataProfileService,
                                    IRefreshBookService refreshBookService,
                                    IRefreshSeriesService refreshSeriesService,
                                    IEventAggregator eventAggregator,
                                    IManageCommandQueue commandQueueManager,
                                    ICommandResultReporter commandResultReporter,
                                    IMediaFileService mediaFileService,
                                    IHistoryService historyService,
                                    IRootFolderService rootFolderService,
                                    ICheckIfAuthorShouldBeRefreshed checkIfAuthorShouldBeRefreshed,
                                    IMonitorNewBookService monitorNewBookService,
                                    IConfigService configService,
                                    IImportListExclusionService importListExclusionService,
                                    ICatalogueIdentityRepository catalogueIdentity,
                                    Logger logger)
        : base(logger, authorMetadataService)
        {
            _orchestrator = orchestrator;
            _authorService = authorService;
            _bookService = bookService;
            _metadataProfileService = metadataProfileService;
            _refreshBookService = refreshBookService;
            _refreshSeriesService = refreshSeriesService;
            _eventAggregator = eventAggregator;
            _commandQueueManager = commandQueueManager;
            _commandResultReporter = commandResultReporter;
            _mediaFileService = mediaFileService;
            _historyService = historyService;
            _rootFolderService = rootFolderService;
            _checkIfAuthorShouldBeRefreshed = checkIfAuthorShouldBeRefreshed;
            _monitorNewBookService = monitorNewBookService;
            _configService = configService;
            _importListExclusionService = importListExclusionService;
            _catalogueIdentity = catalogueIdentity;
            _logger = logger;
        }

        private Author GetSkyhookData(string foreignId)
        {
            try
            {
                return _orchestrator.GetAuthorInfo(foreignId);
            }
            catch (AuthorNotFoundException ex)
            {
                // A missing author or a local identifier with no provider result is an expected no-op.
                var message = ex.IsConfirmedMissing
                    ? "The metadata provider returned not found for author id '{0}'. Keeping the existing author and metadata."
                    : "No metadata matched author id '{0}'. Keeping the existing author and metadata.";
                _logger.Warn(message, LogSanitizer.Sanitize(foreignId));
                return null;
            }
            catch (MetadataProviderUnavailableException ex)
            {
                _logger.Warn(ex, "Metadata lookup did not complete for author id '{0}'. Keeping the existing author and metadata.", LogSanitizer.Sanitize(foreignId));
                throw;
            }
        }

        protected override RemoteData GetRemoteData(Author local, List<Author> remote, Author data)
        {
            var result = new RemoteData();

            if (data != null)
            {
                var incumbent = local.Metadata.Value;
                var incoming = data.Metadata.Value;
                if (incumbent.CanonicalId.IsNotNullOrWhiteSpace() && incumbent.ForeignAuthorId != incoming.ForeignAuthorId &&
                    !PreserveCatalogueAuthor(incumbent, incoming))
                {
                    _logger.Warn("Holding an uncorroborated provider identity change for author {0}.", local.Id);
                    return result;
                }

                result.Entity = data;
                result.Metadata = new List<AuthorMetadata> { incoming };
            }

            return result;
        }

        private bool PreserveCatalogueAuthor(AuthorMetadata incumbent, AuthorMetadata incoming)
        {
            const CatalogueEntityKind kind = CatalogueEntityKind.Author;
            var proposed = CatalogueIdentityRepository.ParseLegacySource(kind, incoming.ForeignAuthorId, "corroborated-author-match");
            if (proposed == null)
            {
                return false;
            }

            var owner = _catalogueIdentity.FindByProviderId(kind, proposed.Provider, proposed.ProviderId);
            var incumbentLink = CatalogueIdentityRepository.NormalizeSource(kind, "OpenLibrary", incumbent.OpenLibraryAuthorId, null);
            var incomingLink = CatalogueIdentityRepository.NormalizeSource(kind, "OpenLibrary", incoming.OpenLibraryAuthorId, null);
            var linksAgree = incumbentLink != null && incumbentLink.ProviderId == incomingLink?.ProviderId;
            if (owner != incumbent.Id && (owner.HasValue || !linksAgree))
            {
                return false;
            }

            incoming.PendingSourceAssociations.Add(proposed);
            incoming.ForeignAuthorId = incumbent.ForeignAuthorId;
            incoming.TitleSlug = incumbent.TitleSlug;
            return true;
        }

        protected override bool ShouldDelete(Author local)
        {
            return false;
        }

        protected override void LogProgress(Author local)
        {
            _logger.ProgressInfo("Updating Info for {0}", local.Name);
        }

        protected override bool IsMerge(Author local, Author remote)
        {
            _logger.Trace($"local: {local.AuthorMetadataId} remote: {remote.Metadata.Value.Id}");
            return local.AuthorMetadataId != remote.Metadata.Value.Id;
        }

        protected override UpdateResult UpdateEntity(Author local, Author remote)
        {
            var result = UpdateResult.None;

            if (!local.Metadata.Value.Equals(remote.Metadata.Value))
            {
                result = UpdateResult.UpdateTags;
            }

            local.UseMetadataFrom(remote);
            local.Metadata = remote.Metadata;
            local.Series = remote.Series?.Value ?? new List<Series>();
            local.LastInfoSync = DateTime.UtcNow;

            try
            {
                local.Path = new DirectoryInfo(local.Path).FullName;
                local.Path = local.Path.GetActualCasing();
            }
            catch (Exception e)
            {
                _logger.Warn(e, "Couldn't update author path for " + local.Path);
            }

            return result;
        }

        protected override UpdateResult MoveEntity(Author local, Author remote)
        {
            _logger.Debug($"Updating foreign id for {local} to {remote}");

            // We are moving from one metadata to another (will already have been poplated)
            local.AuthorMetadataId = remote.Metadata.Value.Id;
            local.Metadata = remote.Metadata.Value;

            // Update list exclusion if one exists
            var importExclusion = _importListExclusionService.FindByForeignId(local.Metadata.Value.ForeignAuthorId);

            if (importExclusion != null)
            {
                importExclusion.ForeignId = remote.Metadata.Value.ForeignAuthorId;
                _importListExclusionService.Update(importExclusion);
            }

            // Do the standard update
            UpdateEntity(local, remote);

            // We know we need to update tags as author id has changed
            return UpdateResult.UpdateTags;
        }

        protected override UpdateResult MergeEntity(Author local, Author target, Author remote)
        {
            _logger.Warn($"Author {local} was replaced with {remote} because the original was a duplicate.");

            // Update list exclusion if one exists
            var importExclusionLocal = _importListExclusionService.FindByForeignId(local.Metadata.Value.ForeignAuthorId);

            if (importExclusionLocal != null)
            {
                var importExclusionTarget = _importListExclusionService.FindByForeignId(target.Metadata.Value.ForeignAuthorId);
                if (importExclusionTarget == null)
                {
                    importExclusionLocal.ForeignId = remote.Metadata.Value.ForeignAuthorId;
                    _importListExclusionService.Update(importExclusionLocal);
                }
            }

            // move any books over to the new author and remove the local author
            var books = _bookService.GetBooksByAuthor(local.Id);
            books.ForEach(x => x.AuthorMetadataId = target.AuthorMetadataId);
            _bookService.UpdateMany(books);
            _authorService.DeleteAuthor(local.Id, false);

            // Update history entries to new id
            var items = _historyService.GetByAuthor(local.Id, null);
            items.ForEach(x => x.AuthorId = target.Id);
            _historyService.UpdateMany(items);

            // We know we need to update tags as author id has changed
            return UpdateResult.UpdateTags;
        }

        protected override Author GetEntityByForeignId(Author local)
        {
            return _authorService.FindById(local.ForeignAuthorId);
        }

        protected override void SaveEntity(Author local)
        {
            _authorService.UpdateAuthor(local);
        }

        protected override void DeleteEntity(Author local, bool deleteFiles)
        {
            throw new NotSupportedException("Missing provider metadata cannot delete a library author.");
        }

        protected override List<Book> GetRemoteChildren(Author local, Author remote)
        {
            var filtered = _metadataProfileService.FilterBooks(remote, local.MetadataProfileId);

            var all = filtered.DistinctBy(m => m.ForeignBookId).ToList();
            var ids = all.Select(x => x.ForeignBookId).ToList();
            var excluded = _importListExclusionService.FindByForeignId(ids).Select(x => x.ForeignId).ToList();
            return all.Where(x => !excluded.Contains(x.ForeignBookId)).ToList();
        }

        protected override List<Book> ReconcileRemoteChildren(Author local, List<Book> candidates, List<Book> localBooks)
        {
            if (candidates.Count == 0)
            {
                return candidates;
            }

            var existing = localBooks.Where(x => x.AuthorMetadataId == local.AuthorMetadataId)
                .ToList();

            var existingIds = existing.Select(x => x.ForeignBookId).ToHashSet();
            var canonical = new List<Book>();
            var aliases = new Dictionary<string, Book>();
            foreach (var candidate in candidates.OrderByDescending(x => existingIds.Contains(x.ForeignBookId))
                         .ThenByDescending(x => MetadataQualityScorer.GetBookScoreBreakdown(x).Values.Sum())
                         .ThenBy(x => x.ForeignBookId, StringComparer.Ordinal))
            {
                var stored = BookWorkIdentity.FindUniqueMatch(candidate, existing, out var ambiguous);
                var current = stored != null
                    ? canonical.FirstOrDefault(x => x.ForeignBookId == stored.ForeignBookId)
                    : ambiguous ? null : BookWorkIdentity.FindUniqueMatch(candidate, canonical, out ambiguous);
                if (ambiguous)
                {
                    _logger.Warn("Holding work {0} for review because more than one catalogue entry matches", LogSanitizer.Sanitize(candidate.ForeignBookId));
                    continue;
                }

                if (current != null && !BookWorkIdentity.AreEquivalent(candidate, current))
                {
                    current = null;
                    stored = null;
                }

                if (current == null)
                {
                    current = candidate;
                    if (stored != null && stored.ForeignBookId != candidate.ForeignBookId)
                    {
                        current = new Book
                        {
                            Author = candidate.Author,
                            AuthorMetadata = candidate.AuthorMetadata,
                            Editions = candidate.Editions,
                            SeriesLinks = candidate.SeriesLinks
                        };
                        current.UseMetadataFrom(candidate);
                        current.ForeignBookId = stored.ForeignBookId;
                        current.TitleSlug = stored.TitleSlug;
                        RecordWorkSource(current, stored, candidate);
                    }

                    current.OpenLibraryWorkId ??= BookWorkIdentity.GetOpenLibraryWorkId(candidate);
                    if (stored != null)
                    {
                        current.OpenLibraryWorkId ??= BookWorkIdentity.GetOpenLibraryWorkId(stored);
                    }

                    canonical.Add(current);
                }
                else
                {
                    RecordWorkSource(current, current, candidate);
                    current.OpenLibraryWorkId ??= BookWorkIdentity.GetOpenLibraryWorkId(candidate);
                    current.Editions = (current.Editions?.Value ?? new List<Edition>())
                        .Concat(candidate.Editions?.Value ?? new List<Edition>())
                        .DistinctBy(edition => edition.ForeignEditionId)
                        .ToList();
                }

                if (candidate.ForeignBookId != null)
                {
                    aliases[candidate.ForeignBookId] = current;
                }
            }

            foreach (var series in local.Series?.Value ?? new List<Series>())
            {
                var links = series.LinkItems?.Value ?? new List<SeriesBookLink>();
                foreach (var link in links)
                {
                    var foreignBookId = link.Book?.Value?.ForeignBookId;
                    if (foreignBookId != null && aliases.TryGetValue(foreignBookId, out var work))
                    {
                        link.Book = work;
                    }
                }

                series.LinkItems = links.GroupBy(link => link.Book?.Value?.ForeignBookId)
                    .Select(group => group.OrderByDescending(link => link.IsPrimary)
                        .ThenByDescending(link => link.Position.IsNotNullOrWhiteSpace())
                        .First())
                    .ToList();
            }

            return canonical;
        }

        private static void RecordWorkSource(Book owner, Book corroboratingWork, Book candidate)
        {
            if (owner.ForeignBookId == candidate.ForeignBookId || !BookWorkIdentity.AreCorroboratedForSameAuthor(corroboratingWork, candidate))
            {
                return;
            }

            var source = CatalogueIdentityRepository.ParseLegacySource(CatalogueEntityKind.Work, candidate.ForeignBookId, "corroborated-work-match");
            if (source != null)
            {
                owner.PendingSourceAssociations.Add(source);
            }
        }

        protected override List<Book> GetLocalChildren(Author entity, List<Book> remoteChildren)
        {
            return _bookService.GetBooksForRefresh(entity.AuthorMetadataId,
                                                     remoteChildren.Select(x => x.ForeignBookId).ToList());
        }

        protected override Tuple<Book, List<Book>> GetMatchingExistingChildren(List<Book> existingChildren, Book remote)
        {
            var existingChild = existingChildren.SingleOrDefault(x => x.ForeignBookId == remote.ForeignBookId);
            var mergeChildren = new List<Book>();
            return Tuple.Create(existingChild, mergeChildren);
        }

        protected override void PrepareNewChild(Book child, Author entity)
        {
            child.Author = entity;
            child.AuthorMetadata = entity.Metadata.Value;
            child.AuthorMetadataId = entity.Metadata.Value.Id;
            child.Added = DateTime.UtcNow;
            child.LastInfoSync = DateTime.MinValue;
            child.Monitored = entity.Monitored;
        }

        protected override void PrepareExistingChild(Book local, Book remote, Author entity)
        {
            local.Author = entity;
            local.AuthorMetadata = entity.Metadata.Value;
            local.AuthorMetadataId = entity.Metadata.Value.Id;

            remote.UseDbFieldsFrom(local);
        }

        protected override void ProcessChildren(Author entity, SortedChildren children)
        {
            foreach (var book in children.Added)
            {
                book.Monitored = _monitorNewBookService.ShouldMonitorNewBook(book, children.UpToDate, entity.MonitorNewItems);
            }
        }

        protected override void AddChildren(List<Book> children)
        {
            _bookService.InsertMany(children);
        }

        protected override bool RefreshChildren(SortedChildren localChildren, List<Book> remoteChildren, Author remoteData, bool forceChildRefresh, bool forceUpdateFileTags, DateTime? lastUpdate)
        {
            // Refresh and reparent known editions before considering removal of any old work.
            var updated = _refreshBookService.RefreshBookInfo(localChildren.Future, remoteChildren, remoteData, forceChildRefresh, forceUpdateFileTags, lastUpdate);

            // A bibliography is not proof that omitted works ceased to exist.
            // Only an identified alias with no remaining library state can be retired.
            var retired = new List<Book>();
            foreach (var deleted in localChildren.Deleted)
            {
                if (deleted.AddOptions?.AddType == BookAddType.Manual)
                {
                    _logger.Debug("Keeping manually-added book {0} despite remote removal", deleted);
                    continue;
                }

                if (_mediaFileService.GetFilesByBook(deleted.Id).Any())
                {
                    _logger.Debug("Keeping book {0} with local files despite remote removal", deleted);
                    continue;
                }

                var canonical = BookWorkIdentity.FindUniqueMatch(deleted, remoteChildren, out _);
                if (canonical == null ||
                    deleted.EbookMonitoringOverride.HasValue ||
                    deleted.AudiobookMonitoringOverride.HasValue ||
                    _historyService.GetByBook(deleted.Id, null)?.Any() == true ||
                    _bookService.GetBook(deleted.Id)?.Editions?.Value?.Any() != false)
                {
                    _logger.Debug("Keeping work {0}: it still has state or has no unique canonical match", deleted);
                    continue;
                }

                _logger.Info("Retiring empty work alias {0} in favor of {1}", deleted.Id, canonical.ForeignBookId);
                _bookService.DeleteBook(deleted.Id, false);
                retired.Add(deleted);
            }

            localChildren.Deleted = retired;
            return updated;
        }

        protected override void PublishEntityUpdatedEvent(Author entity)
        {
            _eventAggregator.PublishEvent(new AuthorUpdatedEvent(entity));
        }

        protected override void PublishRefreshCompleteEvent(Author entity)
        {
            // NOTE: Trigger the series update here after author refresh completes.
            var series = entity.Series;

            if (series?.Value == null || !series.Value.Any())
            {
                series = BuildSeriesFromBookLinks(entity);
            }

            _refreshSeriesService.RefreshSeriesInfo(entity.AuthorMetadataId, series, entity, false, false, null);
            _eventAggregator.PublishEvent(new AuthorRefreshCompleteEvent(entity));
        }

        protected override void PublishChildrenUpdatedEvent(Author entity, List<Book> newChildren, List<Book> updateChildren, List<Book> deleteChildren)
        {
            _eventAggregator.PublishEvent(new BookInfoRefreshedEvent(entity, newChildren, updateChildren, deleteChildren));
        }

        private void Rescan(List<int> authorIds, bool isNew, CommandTrigger trigger, bool infoUpdated)
        {
            var rescanAfterRefresh = _configService.RescanAfterRefresh;
            var shouldRescan = true;

            if (isNew)
            {
                _logger.Trace("Forcing rescan. Reason: New author added");
                shouldRescan = true;
            }
            else if (rescanAfterRefresh == RescanAfterRefreshType.Never)
            {
                _logger.Trace("Skipping rescan. Reason: never rescan after refresh");
                shouldRescan = false;
            }
            else if (rescanAfterRefresh == RescanAfterRefreshType.AfterManual && trigger != CommandTrigger.Manual)
            {
                _logger.Trace("Skipping rescan. Reason: not after automatic refreshes");
                shouldRescan = false;
            }
            else if (!infoUpdated)
            {
                _logger.Trace("Skipping rescan. Reason: no metadata updated");
                shouldRescan = false;
            }

            if (shouldRescan)
            {
                // some metadata has updated so rescan unmatched
                // (but don't add new authors to reduce repeated searches against api)
                var folders = _rootFolderService.All().Select(x => x.Path).ToList();

                if (HasQueuedOrStartedMatchedRescan(authorIds, folders))
                {
                    _logger.Trace("Skipping rescan. Reason: matching rescan command already queued or started");
                    return;
                }

                _commandQueueManager.Push(new RescanFoldersCommand(folders, FilterFilesType.Matched, false, authorIds));
            }
        }

        private bool HasQueuedOrStartedMatchedRescan(List<int> authorIds, List<string> folders)
        {
            var commands = _commandQueueManager.All() ?? new List<CommandModel>();
            var expectedFolders = (folders ?? new List<string>()).OrderBy(x => x).ToList();

            // Only match on folders and filter type — authorIds are only used for
            // post-scan AuthorScannedEvent and do not affect the actual disk scan.
            // Matching on authorIds caused each individual author refresh to queue
            // a separate (but functionally identical) rescan command.
            return commands.Any(command =>
                (command.Status == CommandStatus.Queued || command.Status == CommandStatus.Started) &&
                string.Equals(command.Name, nameof(RescanFoldersCommand), StringComparison.Ordinal) &&
                command.Body is RescanFoldersCommand existing &&
                existing.Filter == FilterFilesType.Matched &&
                !existing.AddNewAuthors &&
                (existing.Folders ?? new List<string>()).OrderBy(x => x).SequenceEqual(expectedFolders));
        }

        private static List<Series> BuildSeriesFromBookLinks(Author entity)
        {
            var links = entity?.Books?.Value?
                .Where(b => b?.SeriesLinks?.Value?.Any() == true)
                .SelectMany(b => b.SeriesLinks.Value)
                .Where(l => l?.Series?.Value != null &&
                            l.Series.Value.ForeignSeriesId.IsNotNullOrWhiteSpace())
                .ToList() ?? new List<SeriesBookLink>();

            if (!links.Any())
            {
                return new List<Series>();
            }

            var grouped = links.GroupBy(x => x.Series.Value.ForeignSeriesId, StringComparer.OrdinalIgnoreCase);
            var series = new List<Series>();

            foreach (var group in grouped)
            {
                var first = group.First().Series.Value;
                var dedupedLinks = group
                    .GroupBy(x => x.Book?.Value?.ForeignBookId ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                    .Select(x => x.First())
                    .ToList();

                var hydrated = new Series
                {
                    ForeignSeriesId = first.ForeignSeriesId,
                    ForeignAuthorId = entity.ForeignAuthorId,
                    Title = first.Title,
                    Numbered = dedupedLinks.Any(x => x.Position.IsNotNullOrWhiteSpace()),
                    WorkCount = dedupedLinks.Count,
                    PrimaryWorkCount = dedupedLinks.Count(x => x.IsPrimary),
                    LinkItems = dedupedLinks
                };

                foreach (var link in dedupedLinks)
                {
                    link.Series = hydrated;
                }

                series.Add(hydrated);
            }

            return series;
        }

        private void RefreshSelectedAuthors(List<int> authorIds, bool isNew, CommandTrigger trigger)
        {
            var updated = false;
            var refreshFailed = false;
            var authors = _authorService.GetAuthors(authorIds);

            foreach (var author in authors)
            {
                try
                {
                    var data = GetSkyhookData(author.ForeignAuthorId);
                    if (data != null)
                    {
                        updated |= RefreshEntityInfo(author, null, data, true, false, null);
                    }
                }
                catch (Exception e)
                {
                    refreshFailed = true;
                    _logger.Error(e, "Couldn't refresh info for {0}", author);
                }
            }

            CompleteRefresh(authorIds, isNew, trigger, updated, refreshFailed);
        }

        private void CompleteRefresh(List<int> authorIds, bool isNew, CommandTrigger trigger, bool updated, bool refreshFailed)
        {
            Rescan(authorIds, isNew, trigger, updated);
            if (refreshFailed)
            {
                _commandResultReporter.Report(CommandResult.Unsuccessful);
            }
        }

        public void Execute(BulkRefreshAuthorCommand message)
        {
            RefreshSelectedAuthors(message.AuthorIds, message.AreNewAuthors, message.Trigger);
        }

        public void Execute(RefreshAuthorCommand message)
        {
            var trigger = message.Trigger;
            var isNew = message.IsNewAuthor;

            if (message.AuthorId.HasValue)
            {
                RefreshSelectedAuthors(new List<int> { message.AuthorId.Value }, isNew, trigger);
            }
            else
            {
                var updated = false;
                var refreshFailed = false;
                var authors = _authorService.GetAllAuthors().OrderBy(c => c.Name).ToList();
                var authorIds = authors.Select(x => x.Id).ToList();

                var updatedOpenLibraryAuthors = new HashSet<string>();

                if (message.LastExecutionTime.HasValue && message.LastExecutionTime.Value.AddDays(14) > DateTime.UtcNow)
                {
                    updatedOpenLibraryAuthors = _orchestrator.GetChangedAuthors(message.LastStartTime.Value);
                }

                foreach (var author in authors)
                {
                    var manualTrigger = message.Trigger == CommandTrigger.Manual;

                    if ((updatedOpenLibraryAuthors == null && _checkIfAuthorShouldBeRefreshed.ShouldRefresh(author)) ||
                        (updatedOpenLibraryAuthors != null && updatedOpenLibraryAuthors.Contains(author.ForeignAuthorId)) ||
                        manualTrigger)
                    {
                        try
                        {
                            LogProgress(author);
                            var data = GetSkyhookData(author.ForeignAuthorId);
                            if (data != null)
                            {
                                updated |= RefreshEntityInfo(author, null, data, manualTrigger, false, message.LastStartTime);
                            }
                        }
                        catch (Exception e)
                        {
                            refreshFailed = true;
                            _logger.Error(e, "Couldn't refresh info for {0}", author);
                        }
                    }
                    else
                    {
                        _logger.Info("Skipping refresh of author: {0}", author.Name);
                    }
                }

                CompleteRefresh(authorIds, isNew, trigger, updated, refreshFailed);
            }
        }
    }
}
