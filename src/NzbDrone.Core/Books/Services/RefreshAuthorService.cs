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
using NzbDrone.Core.MediaCover;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.Commands;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.MetadataSource.OpenLibrary;
using NzbDrone.Core.Parser;
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
        private readonly IEditionService _editionService;
        private readonly IMetadataProfileService _metadataProfileService;
        private readonly IRefreshBookService _refreshBookService;
        private readonly IRefreshSeriesService _refreshSeriesService;
        private readonly IEventAggregator _eventAggregator;
        private readonly IManageCommandQueue _commandQueueManager;
        private readonly ICommandResultReporter _commandResultReporter;
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
                                    IEditionService editionService,
                                    IMetadataProfileService metadataProfileService,
                                    IRefreshBookService refreshBookService,
                                    IRefreshSeriesService refreshSeriesService,
                                    IEventAggregator eventAggregator,
                                    IManageCommandQueue commandQueueManager,
                                    ICommandResultReporter commandResultReporter,
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
            _editionService = editionService;
            _metadataProfileService = metadataProfileService;
            _refreshBookService = refreshBookService;
            _refreshSeriesService = refreshSeriesService;
            _eventAggregator = eventAggregator;
            _commandQueueManager = commandQueueManager;
            _commandResultReporter = commandResultReporter;
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
                var primaryProviderAuthorId = incoming.ForeignAuthorId;
                if (Guid.TryParseExact(incumbent.CanonicalId, "N", out _) && incumbent.ForeignAuthorId != incoming.ForeignAuthorId &&
                    !PreserveCatalogueAuthor(incumbent, incoming))
                {
                    _logger.Warn("Holding an uncorroborated provider identity change for author {0}.", local.Id);
                    return result;
                }

                EnrichFromVerifiedAuthorProfile(incumbent, incoming, primaryProviderAuthorId);
                result.Entity = data;
                result.Metadata = new List<AuthorMetadata> { incoming };
            }

            return result;
        }

        private void EnrichFromVerifiedAuthorProfile(AuthorMetadata incumbent, AuthorMetadata incoming, string primaryProviderAuthorId)
        {
            const string provider = "OpenLibrary";
            var missingBiography = string.IsNullOrWhiteSpace(incumbent.Overview) && string.IsNullOrWhiteSpace(incoming.Overview);
            var missingPortrait = incumbent.Images?.Any(IsPortrait) != true && incoming.Images?.Any(IsPortrait) != true;
            if (!missingBiography && !missingPortrait)
            {
                return;
            }

            var verifiedId = GetVerifiedOpenLibraryAuthorId(incumbent, incoming, primaryProviderAuthorId);
            if (verifiedId == null)
            {
                return;
            }

            var profile = _orchestrator.GetAuthorProfileMetadata(provider, verifiedId);
            if (profile == null || OpenLibraryIdNormalizer.NormalizeAuthorId(profile.OpenLibraryAuthorId) != verifiedId ||
                _catalogueIdentity.FindByProviderId(CatalogueEntityKind.Author, provider, verifiedId) != incumbent.Id)
            {
                return;
            }

            if (missingBiography && !string.IsNullOrWhiteSpace(profile.Overview))
            {
                incoming.Overview = profile.Overview;
            }

            if (missingPortrait && profile.Images != null)
            {
                incoming.Images ??= new List<MediaCover.MediaCover>();
                incoming.Images.AddRange(profile.Images.Where(IsPortrait));
            }
        }

        private string GetVerifiedOpenLibraryAuthorId(AuthorMetadata incumbent, AuthorMetadata incoming, string primaryProviderAuthorId)
        {
            const CatalogueEntityKind kind = CatalogueEntityKind.Author;
            const string provider = "OpenLibrary";
            if (incumbent.Id <= 0 || CatalogueIdentityRepository.ParseLegacySource(kind, primaryProviderAuthorId, null)?.Provider == provider)
            {
                return null;
            }

            var sources = _catalogueIdentity.GetSources(kind, incumbent.Id)?
                .Where(source => source.Provider == provider)
                .ToList();
            if (sources?.Count != 1 || sources[0].Evidence != AuthorCanonicalizationService.BibliographyEvidence)
            {
                return null;
            }

            var verifiedId = OpenLibraryIdNormalizer.NormalizeAuthorId(sources[0].ProviderId);
            var existingIds = new[] { incumbent.OpenLibraryAuthorId, incoming.OpenLibraryAuthorId };
            return verifiedId != null && existingIds.Where(id => id.IsNotNullOrWhiteSpace())
                .All(id => OpenLibraryIdNormalizer.NormalizeAuthorId(id) == verifiedId) ? verifiedId : null;
        }

        private static bool IsPortrait(MediaCover.MediaCover image) =>
            image?.CoverType == MediaCoverTypes.Poster && !string.IsNullOrWhiteSpace(image.Url);

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
            incoming.UseDbFieldsFrom(incumbent);
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

            var all = filtered.Concat(GetSupplementalBibliography(local, remote, filtered))
                .DistinctBy(m => m.ForeignBookId).ToList();
            var ids = all.Select(x => x.ForeignBookId).ToList();
            var excluded = _importListExclusionService.FindByForeignId(ids).Select(x => x.ForeignId).ToList();
            return all.Where(x => !excluded.Contains(x.ForeignBookId)).ToList();
        }

        private IEnumerable<Book> GetSupplementalBibliography(Author local, Author primary, List<Book> retainedPrimary)
        {
            const CatalogueEntityKind authorKind = CatalogueEntityKind.Author;
            if (CatalogueIdentityRepository.ParseLegacySource(authorKind, primary.ForeignAuthorId, null)?.Provider != "Hardcover")
            {
                return Enumerable.Empty<Book>();
            }

            var metadata = local.Metadata.Value;
            var verifiedId = GetVerifiedOpenLibraryAuthorId(metadata, primary.Metadata.Value, primary.ForeignAuthorId);
            if (verifiedId == null || _catalogueIdentity.FindByProviderId(authorKind, "OpenLibrary", verifiedId) != metadata.Id)
            {
                return Enumerable.Empty<Book>();
            }

            Author alternate;
            try
            {
                alternate = _orchestrator.GetAuthorInfo($"openlibrary:author:{verifiedId}");
            }
            catch (AuthorNotFoundException)
            {
                return Enumerable.Empty<Book>();
            }
            catch (MetadataProviderUnavailableException)
            {
                _logger.Warn("The alternate bibliography for author {0} could not be read. The primary bibliography remains available.", local.Id);
                return Enumerable.Empty<Book>();
            }

            var source = CatalogueIdentityRepository.ParseLegacySource(authorKind, alternate?.Metadata?.Value?.ForeignAuthorId, null);
            if (source?.Provider != "OpenLibrary" || source.ProviderId != verifiedId ||
                alternate.Metadata.Value.OpenLibraryAuthorId != verifiedId ||
                _catalogueIdentity.FindByProviderId(authorKind, "OpenLibrary", verifiedId) != metadata.Id)
            {
                return Enumerable.Empty<Book>();
            }

            // An exact title collision without corroboration is a review case. An
            // alternate bibliography is never evidence to update an incumbent work here.
            var primaryWorks = primary.Books?.Value ?? retainedPrimary;
            var known = primaryWorks.Concat(_bookService.GetBooksByAuthorMetadataId(metadata.Id)).ToList();
            var knownTitles = known.Select(GetBibliographyTitle).ToHashSet(StringComparer.Ordinal);
            var knownWorkIds = known.Select(BookWorkIdentity.GetOpenLibraryWorkId)
                .Where(id => id != null).ToHashSet(StringComparer.Ordinal);
            var proposed = _metadataProfileService.FilterBooks(alternate, local.MetadataProfileId)
                .Where(book => IsVerifiedBibliographyWork(book, verifiedId))
                .GroupBy(GetBibliographyTitle, StringComparer.Ordinal)
                .Where(group => group.Key.Length != 0 && group.Count() == 1 && !knownTitles.Contains(group.Key))
                .Select(group => group.Single())
                .Where(book => !knownWorkIds.Contains(BookWorkIdentity.GetOpenLibraryWorkId(book)) && _bookService.FindById(book.ForeignBookId) == null)
                .ToList();

            if (proposed.Count == 0)
            {
                return proposed;
            }

            var proposedEditionIds = proposed.SelectMany(GetEditionIds).ToList();
            var reservedEditionIds = primaryWorks.SelectMany(GetEditionIds)
                .Concat(proposedEditionIds.GroupBy(id => id).Where(group => group.Count() > 1).Select(group => group.Key))
                .ToHashSet(StringComparer.Ordinal);
            if (proposedEditionIds.Count != 0)
            {
                reservedEditionIds.UnionWith(_editionService.GetEditionsForRefresh(0, proposedEditionIds.Distinct().ToList())
                    .Select(edition => edition.ForeignEditionId));
            }

            var accepted = proposed.Where(book => !GetEditionIds(book).Any(reservedEditionIds.Contains)).ToList();
            _logger.Debug("Accepted {0} unclaimed works from the alternate bibliography for author {1}.", accepted.Count, local.Id);
            return accepted;
        }

        private static IEnumerable<string> GetEditionIds(Book book) =>
            (book.Editions?.Value ?? new List<Edition>()).Select(edition => edition.ForeignEditionId)
                .Where(id => !string.IsNullOrWhiteSpace(id)).Distinct();

        private static string GetBibliographyTitle(Book book) => book.Title?.Split(':', 2)[0].CleanAuthorName() ?? string.Empty;

        private static bool IsVerifiedBibliographyWork(Book book, string verifiedId)
        {
            var source = CatalogueIdentityRepository.ParseLegacySource(CatalogueEntityKind.Work, book?.ForeignBookId, null);
            return source?.Provider == "OpenLibrary" && book.VerifiedOpenLibraryAuthorId == verifiedId &&
                source.ProviderId == BookWorkIdentity.GetOpenLibraryWorkId(book);
        }

        protected override List<Book> ReconcileRemoteChildren(Author local, List<Book> candidates, List<Book> localBooks)
        {
            if (candidates.Count == 0)
            {
                return candidates;
            }

            var foreignOwnedIds = localBooks.Where(x => x.AuthorMetadataId != local.AuthorMetadataId)
                .Select(x => x.ForeignBookId)
                .ToHashSet(StringComparer.Ordinal);

            var sharedCredits = candidates.Where(book => book.CreditedHardcoverAuthorId.HasValue && foreignOwnedIds.Contains(book.ForeignBookId))
                .GroupBy(book => book.CreditedHardcoverAuthorId.Value);
            foreach (var credits in sharedCredits)
            {
                _bookService.RecordHardcoverAuthorCredits(local.AuthorMetadataId, credits.Key, credits.Select(book => book.ForeignBookId).ToList());
            }

            // Cross-author exact hits are evidence of a conflict, never editable children.
            localBooks.RemoveAll(x => x.AuthorMetadataId != local.AuthorMetadataId);
            var existing = localBooks;

            var existingIds = existing.Select(x => x.ForeignBookId).ToHashSet();
            var canonical = new List<Book>();
            var aliases = new Dictionary<string, Book>();
            foreach (var candidate in candidates.OrderByDescending(x => existingIds.Contains(x.ForeignBookId))
                         .ThenByDescending(x => x.PendingSourceAssociations.Any(source => source.Evidence == CatalogueIdentityRepository.HardcoverWorkMappingEvidence))
                         .ThenByDescending(x => MetadataQualityScorer.GetBookScoreBreakdown(x).Values.Sum())
                         .ThenBy(x => x.ForeignBookId, StringComparer.Ordinal))
            {
                if (foreignOwnedIds.Contains(candidate.ForeignBookId))
                {
                    _logger.Warn("Holding catalogue work {0} for review because it belongs to another author.",
                        LogSanitizer.Sanitize(candidate.ForeignBookId));
                    continue;
                }

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
                    current.PendingSourceAssociations.AddRange(candidate.PendingSourceAssociations);
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
            var inserted = _bookService.InsertBibliographyWorks(children);
            if (!ReferenceEquals(inserted, children))
            {
                _logger.Debug("Stored {0} of {1} proposed bibliography works.", inserted.Count, children.Count);
                children.Clear();
                children.AddRange(inserted);
            }
        }

        protected override bool RefreshChildren(SortedChildren localChildren, List<Book> remoteChildren, Author remoteData, bool forceChildRefresh, bool forceUpdateFileTags, DateTime? lastUpdate)
        {
            // A bibliography can omit works. Retire an alias through explicit reconciliation
            // so its canonical URL and local catalogue state can be transferred atomically.
            var updated = _refreshBookService.RefreshBookInfo(localChildren.Future, remoteChildren, remoteData, forceChildRefresh, forceUpdateFileTags, lastUpdate);
            localChildren.Deleted.Clear();
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
                    Description = group.Select(link => link.Series.Value.Description).FirstOrDefault(description => description.IsNotNullOrWhiteSpace()),
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
