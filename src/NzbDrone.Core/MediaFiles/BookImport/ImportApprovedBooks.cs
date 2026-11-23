using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using NLog;
using NzbDrone.Common;
using NzbDrone.Common.Disk;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Instrumentation.Extensions;
using NzbDrone.Core.Books;
using NzbDrone.Core.Books.Calibre;
using NzbDrone.Core.Books.Commands;
using NzbDrone.Core.Books.Events;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.DecisionEngine;
using NzbDrone.Core.Download;
using NzbDrone.Core.Exceptions;
using NzbDrone.Core.Extras;
using NzbDrone.Core.History;
using NzbDrone.Core.MediaFiles.Events;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.MetadataSource.OpenLibrary;
using NzbDrone.Core.Parser;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.RootFolders;

namespace NzbDrone.Core.MediaFiles.BookImport
{
    public interface IImportApprovedBooks
    {
        List<ImportResult> Import(List<ImportDecision<LocalBook>> decisions, bool replaceExisting, DownloadClientItem downloadClientItem = null, ImportMode importMode = ImportMode.Auto);
    }

    public class ImportApprovedBooks : IImportApprovedBooks
    {
        private const string AudioPartRetryHold = "The previously occupied audiobook part could not be linked safely";
        private const string LocalEditionIdPrefix = "bibliophilarr:edition:";
        private static readonly RegexReplace PadNumbers = new RegexReplace(@"\d+", n => n.Value.PadLeft(9, '0'), RegexOptions.Compiled);
        private static readonly Regex ScopedAuthorIdRegex = new Regex("^(openlibrary|googlebooks|inventaire|hardcover):author:[^:]+$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private readonly IUpgradeMediaFiles _bookFileUpgrader;
        private readonly IMediaFileService _mediaFileService;
        private readonly IMetadataTagService _metadataTagService;
        private readonly IAuthorService _authorService;
        private readonly IAddAuthorService _addAuthorService;
        private readonly IBookService _bookService;
        private readonly IEditionService _editionService;
        private readonly IRootFolderService _rootFolderService;
        private readonly IRecycleBinProvider _recycleBinProvider;
        private readonly IExtraService _extraService;
        private readonly IDiskProvider _diskProvider;
        private readonly IHistoryService _historyService;
        private readonly IEventAggregator _eventAggregator;
        private readonly IManageCommandQueue _commandQueueManager;
        private readonly IMonitorNewBookService _monitorNewBookService;
        private readonly IConfigService _configService;
        private readonly Logger _logger;

        public ImportApprovedBooks(IUpgradeMediaFiles bookFileUpgrader,
                                   IMediaFileService mediaFileService,
                                   IMetadataTagService metadataTagService,
                                   IAuthorService authorService,
                                   IAddAuthorService addAuthorService,
                                   IBookService bookService,
                                   IEditionService editionService,
                                   IRootFolderService rootFolderService,
                                   IRecycleBinProvider recycleBinProvider,
                                   IExtraService extraService,
                                   IDiskProvider diskProvider,
                                   IHistoryService historyService,
                                   IEventAggregator eventAggregator,
                                   IManageCommandQueue commandQueueManager,
                                   IMonitorNewBookService monitorNewBookService,
                                   IConfigService configService,
                                   Logger logger)
        {
            _bookFileUpgrader = bookFileUpgrader;
            _mediaFileService = mediaFileService;
            _metadataTagService = metadataTagService;
            _authorService = authorService;
            _addAuthorService = addAuthorService;
            _bookService = bookService;
            _editionService = editionService;
            _rootFolderService = rootFolderService;
            _recycleBinProvider = recycleBinProvider;
            _extraService = extraService;
            _diskProvider = diskProvider;
            _historyService = historyService;
            _eventAggregator = eventAggregator;
            _commandQueueManager = commandQueueManager;
            _monitorNewBookService = monitorNewBookService;
            _configService = configService;
            _logger = logger;
        }

        public List<ImportResult> Import(List<ImportDecision<LocalBook>> decisions, bool replaceExisting, DownloadClientItem downloadClientItem = null, ImportMode importMode = ImportMode.Auto)
        {
            var importResults = new List<ImportResult>();
            var allImportedTrackFiles = new List<BookFile>();
            var allOldTrackFiles = new List<BookFile>();
            var addedAuthors = new List<Author>();
            var addedBooks = new List<Book>();

            var audiobookPartRetries = GetAudiobookPartRetries(decisions, replaceExisting);
            foreach (var decision in audiobookPartRetries.Keys.Where(d => !HasPersistedAudiobookTarget(d.Item)))
            {
                decision.Reject(new Rejection(AudioPartRetryHold, holdReason: LibraryImportHoldReason.DuplicateInScan));
            }

            foreach (var decision in decisions.Where(d => d.Approved && (d.Item.Author == null || d.Item.Book == null || d.Item.Edition == null)))
            {
                decision.Reject(new Rejection("Missing author, book, or edition for import", RejectionType.Temporary));
            }

            RejectIncompatibleFormats(decisions);

            var bookDecisions = decisions.Where(e => e.Approved && !audiobookPartRetries.ContainsKey(e))
                .GroupBy(e => new
                {
                    ForeignBookId = e.Item.Book.ForeignBookId,
                    ForeignEditionId = e.Item.Edition.ForeignEditionId,
                    LocalBookId = e.Item.Book.ForeignBookId.IsNullOrWhiteSpace() ? e.Item.Book.Id : 0,
                    LocalEditionId = e.Item.Edition.ForeignEditionId.IsNullOrWhiteSpace() ? e.Item.Edition.Id : 0,
                    FileFormat = ImportFormat.ForPath(e.Item.Path)
                })
                .SelectMany(group => PartitionDigitalVariants(group.ToList(), group.Key.FileFormat)
                    .Select(partition => (Decisions: partition, group.Key.FileFormat)))
                .ToList();

            var iDecision = 1;
            foreach (var bookDecision in bookDecisions)
            {
                var decisionList = bookDecision.Decisions;
                _logger.ProgressInfo("Importing book {0}/{1} {2}", iDecision++, bookDecisions.Count, decisionList[0].Item.Book);

                try
                {
                    var author = EnsureAuthorAdded(decisionList, addedAuthors);

                    var book = author == null ? null : EnsureBookAdded(decisionList, addedBooks);
                    var edition = book == null ? null : EnsureImportEdition(book, decisionList, bookDecision.FileFormat);

                    if (edition == null)
                    {
                        RejectBook(decisionList);
                        continue;
                    }

                    foreach (var decision in decisionList.Where(d => d.Approved && !ImportFormat.CanLink(d.Item.Path, d.Item.Edition)))
                    {
                        decision.Reject(new Rejection("The selected edition cannot store this file's format", RejectionType.Temporary));
                    }

                    decisionList = decisionList.Where(d => d.Approved).ToList();
                    if (decisionList.Count == 0)
                    {
                        continue;
                    }

                    // if (replaceExisting)
                    // {
                    //     RemoveExistingTrackFiles(author, book);
                    // }

                    // Make sure part numbers are populated for audiobooks
                    // If all audio files and all part numbers are zero, set them by filename order
                    if (decisionList.All(b => MediaFileExtensions.AudioExtensions.Contains(Path.GetExtension(b.Item.Path)) && b.Item.Part == 0))
                    {
                        var part = 1;
                        foreach (var d in decisionList.OrderBy(x => PadNumbers.Replace(x.Item.Path)))
                        {
                            d.Item.Part = part++;
                        }
                    }

                    // set the correct release to be monitored before importing the new files
                    var newRelease = decisionList[0].Item.Edition;
                    _logger.Debug("Updating release to {0}", newRelease);

                    if (_configService.EnableDualFormatTracking)
                    {
                        // Only un-monitor editions of the same format type, preserving
                        // monitoring of the other format's editions.
                        book.Editions = _editionService.SetMonitoredByFormat(newRelease);
                    }
                    else
                    {
                        book.Editions = _editionService.SetMonitored(newRelease);
                    }

                    // Publish book edited event.
                    // Deliberately don't put in the old book since we don't want to trigger an AuthorScan.
                    _eventAggregator.PublishEvent(new BookEditedEvent(book, book));
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Failed to prepare book {0} for import; continuing with remaining books", decisionList[0].Item.Book);
                    RejectBook(decisionList);
                }
            }

            var qualifiedImports = decisions.Where(c => c.Approved)
                .GroupBy(c => c.Item.Author.Id, (i, s) => s
                         .OrderByDescending(c => c.Item.Quality, new QualityModelComparer(s.First().Item.Author.QualityProfile))
                         .ThenByDescending(c => c.Item.Size))
                .SelectMany(c => c)
                .ToList();

            _logger.ProgressInfo("Importing {0} files", qualifiedImports.Count);
            _logger.Debug("Importing {0} files. Replace existing: {1}", qualifiedImports.Count, replaceExisting);

            var filesToAdd = new List<BookFile>(qualifiedImports.Count);
            var trackImportedEvents = new List<TrackImportedEvent>(qualifiedImports.Count);

            foreach (var importDecision in qualifiedImports)
            {
                var localTrack = importDecision.Item;
                var oldFiles = new List<BookFile>();

                try
                {
                    var format = Quality.GetFormatType(localTrack.Quality?.Quality);
                    var alreadyImported = importResults.FirstOrDefault(result =>
                        result.Result == ImportResultType.Imported &&
                        result.ImportDecision.Item.Book.Id == localTrack.Book.Id &&
                        result.ImportDecision.Item.Edition?.Id == localTrack.Edition?.Id &&
                        result.ImportDecision.Item.Part == localTrack.Part &&
                        Quality.GetFormatType(result.ImportDecision.Item.Quality?.Quality) == format);

                    if (alreadyImported != null)
                    {
                        importResults.Add(HasUnverifiedEbookIsbn(localTrack, alreadyImported.ImportDecision.Item)
                            ? new ImportResult(importDecision, LibraryImportHoldReason.UnverifiedEditionIdentifier, "The embedded ISBN does not identify the occupied ebook edition")
                            : new ImportResult(importDecision, LibraryImportHoldReason.DuplicateInScan, "Another file in this scan already occupies this edition and part for the same media format"));
                        continue;
                    }

                    if (audiobookPartRetries.TryGetValue(importDecision, out var heldFile))
                    {
                        if (heldFile == null || localTrack.Part <= 0 || localTrack.Part != heldFile.Part ||
                            localTrack.RawTaggedPart != localTrack.Part ||
                            !_mediaFileService.TryLinkUnmappedAudiobookPart(heldFile, localTrack.Edition))
                        {
                            importResults.Add(new ImportResult(importDecision, LibraryImportHoldReason.DuplicateInScan, AudioPartRetryHold));
                            continue;
                        }

                        localTrack.Book.Author = localTrack.Author;
                        heldFile.Author = localTrack.Author;
                        allImportedTrackFiles.Add(heldFile);
                        trackImportedEvents.Add(new TrackImportedEvent(localTrack, heldFile, oldFiles, false, downloadClientItem));
                        importResults.Add(new ImportResult(importDecision));
                        continue;
                    }

                    localTrack.Book.Author = localTrack.Author;

                    if (!replaceExisting)
                    {
                        // A move, reassignment, or hardlinked copy can change a tracked file.
                        var previousFile = _mediaFileService.GetFileWithPath(localTrack.Path.CleanFilePath());

                        if (previousFile?.Id > 0)
                        {
                            if (localTrack.ExistingFile &&
                                (previousFile.EditionId == 0
                                    ? _mediaFileService.TryLinkUnmappedFile(previousFile, localTrack.Edition)
                                    : _mediaFileService.TryRepairUnsupportedEditionLink(previousFile, localTrack.Edition)))
                            {
                                previousFile.Author = localTrack.Author;
                                allImportedTrackFiles.Add(previousFile);
                                trackImportedEvents.Add(new TrackImportedEvent(localTrack, previousFile, oldFiles, false, downloadClientItem));
                                importResults.Add(new ImportResult(importDecision));
                                continue;
                            }

                            var preservedId = previousFile.Id;
                            _logger.ProgressInfo(
                                "Skipping duplicate import of {0} for {1}: existing book file {2} is preserved (replaceExistingFiles=false)",
                                localTrack.Path,
                                localTrack.Book,
                                preservedId);

                            importResults.Add(new ImportResult(
                                importDecision,
                                $"Skipped: file already imported as book file {previousFile.Id} (original record preserved, replaceExistingFiles=false)"));

                            continue;
                        }
                    }

                    var bookFile = new BookFile
                    {
                        Path = localTrack.Path.CleanFilePath(),
                        CalibreId = localTrack.CalibreId,
                        Part = localTrack.Part,
                        PartCount = localTrack.PartCount,
                        Size = localTrack.Size,
                        Modified = localTrack.Modified,
                        DateAdded = DateTime.UtcNow,
                        ReleaseGroup = localTrack.ReleaseGroup,
                        Quality = localTrack.Quality,
                        MediaInfo = localTrack.FileTrackInfo.MediaInfo,
                        EditionId = localTrack.Edition.Id,
                        Author = localTrack.Author,
                        Edition = localTrack.Edition
                    };

                    if (downloadClientItem?.DownloadId.IsNotNullOrWhiteSpace() == true)
                    {
                        var grabHistory = _historyService.FindByDownloadId(downloadClientItem.DownloadId)
                            .OrderByDescending(h => h.Date)
                            .FirstOrDefault(h => h.EventType == EntityHistoryEventType.Grabbed);

                        if (Enum.TryParse(grabHistory?.Data.GetValueOrDefault("indexerFlags"), true, out IndexerFlags flags))
                        {
                            bookFile.IndexerFlags = flags;
                        }
                    }
                    else
                    {
                        bookFile.IndexerFlags = localTrack.IndexerFlags;
                    }

                    bool copyOnly;
                    switch (importMode)
                    {
                        default:
                        case ImportMode.Auto:
                            copyOnly = downloadClientItem != null && !downloadClientItem.CanMoveFiles;
                            break;
                        case ImportMode.Move:
                            copyOnly = false;
                            break;
                        case ImportMode.Copy:
                            copyOnly = true;
                            break;
                    }

                    if (!localTrack.ExistingFile)
                    {
                        bookFile.SceneName = GetSceneReleaseName(downloadClientItem);

                        var moveResult = _bookFileUpgrader.UpgradeBookFile(bookFile, localTrack, copyOnly, replaceExisting);
                        if (moveResult.PreservedFile != null)
                        {
                            var preservedId = moveResult.PreservedFile.Id;
                            _logger.ProgressInfo("Skipping import: existing book file {0} is preserved", preservedId);
                            importResults.Add(new ImportResult(importDecision, $"Skipped: existing book file {preservedId} is preserved (replaceExistingFiles=false)"));
                            continue;
                        }

                        oldFiles = moveResult.OldFiles;
                    }
                    else
                    {
                        // Delete existing files from the DB mapped to this path
                        var previousFile = _mediaFileService.GetFileWithPath(bookFile.Path);

                        if (previousFile != null)
                        {
                            if (!replaceExisting)
                            {
                                importResults.Add(new ImportResult(importDecision, $"Skipped: existing book file {previousFile.Id} is preserved (replaceExistingFiles=false)"));
                                continue;
                            }

                            _mediaFileService.Delete(previousFile, DeleteMediaFileReason.ManualOverride);

                            if (bookFile.CalibreId == 0 && previousFile.CalibreId != 0)
                            {
                                bookFile.CalibreId = previousFile.CalibreId;
                            }
                        }

                        _metadataTagService.WriteTags(bookFile, false);
                    }

                    filesToAdd.Add(bookFile);
                    importResults.Add(new ImportResult(importDecision));

                    if (!localTrack.ExistingFile)
                    {
                        _extraService.ImportTrack(localTrack, bookFile, copyOnly);
                    }

                    allImportedTrackFiles.Add(bookFile);
                    allOldTrackFiles.AddRange(oldFiles);

                    // create all the import events here, but we can't publish until the trackfiles have been
                    // inserted and ids created
                    trackImportedEvents.Add(new TrackImportedEvent(localTrack, bookFile, oldFiles, !localTrack.ExistingFile, downloadClientItem));
                }
                catch (RootFolderNotFoundException e)
                {
                    _logger.Warn(e, "Couldn't import book " + localTrack);
                    _eventAggregator.PublishEvent(new TrackImportFailedEvent(e, localTrack, !localTrack.ExistingFile, downloadClientItem));

                    importResults.Add(new ImportResult(importDecision, LibraryImportHoldReason.ImportFailed, "Failed to import book, root folder missing."));
                }
                catch (DestinationAlreadyExistsException e)
                {
                    _logger.Warn(e, "Couldn't import book " + localTrack);
                    importResults.Add(new ImportResult(importDecision, LibraryImportHoldReason.ImportFailed, "Failed to import book, destination already exists."));
                }
                catch (UnauthorizedAccessException e)
                {
                    _logger.Warn(e, "Couldn't import book " + localTrack);
                    _eventAggregator.PublishEvent(new TrackImportFailedEvent(e, localTrack, !localTrack.ExistingFile, downloadClientItem));

                    importResults.Add(new ImportResult(importDecision, LibraryImportHoldReason.ImportFailed, "Failed to import book, permissions error"));
                }
                catch (RecycleBinException e)
                {
                    _logger.Warn(e, "Couldn't import book " + localTrack);
                    _eventAggregator.PublishEvent(new TrackImportFailedEvent(e, localTrack, !localTrack.ExistingFile, downloadClientItem));

                    importResults.Add(new ImportResult(importDecision, LibraryImportHoldReason.ImportFailed, "Failed to import book, unable to move existing file to the Recycle Bin."));
                }
                catch (CalibreException e)
                {
                    _logger.Warn(e, "Couldn't import book " + localTrack);

                    importResults.Add(new ImportResult(importDecision, LibraryImportHoldReason.ImportFailed, "Failed to import book, error communicating with Calibre.  Check log for details."));
                }
                catch (Exception e)
                {
                    _logger.Warn(e, "Couldn't import book " + localTrack);
                    importResults.Add(new ImportResult(importDecision, LibraryImportHoldReason.ImportFailed, "Failed to import book."));
                }
            }

            var watch = new System.Diagnostics.Stopwatch();
            watch.Start();
            _mediaFileService.AddMany(filesToAdd);
            _logger.Debug("Inserted new trackfiles in {0}ms", watch.ElapsedMilliseconds);

            // now that trackfiles have been inserted and ids generated, publish the import events
            foreach (var trackImportedEvent in trackImportedEvents)
            {
                _eventAggregator.PublishEvent(trackImportedEvent);
            }

            var bookImports = importResults.Where(e => e.Result == ImportResultType.Imported)
                .GroupBy(e => (e.ImportDecision.Item.Book.Id, e.ImportDecision.Item.Edition.Id));

            foreach (var bookImport in bookImports)
            {
                var imported = bookImport.First().ImportDecision.Item;
                _eventAggregator.PublishEvent(new BookImportedEvent(
                    imported.Author,
                    imported.Book,
                    allImportedTrackFiles.Where(s => s.EditionId == imported.Edition.Id).ToList(),
                    allOldTrackFiles.Where(s => s.EditionId == imported.Edition.Id).ToList(),
                    replaceExisting,
                    downloadClientItem));
            }

            //Adding all the rejected decisions
            importResults.AddRange(decisions.Where(c => !c.Approved)
                                            .Select(d => new ImportResult(d,
                                                audiobookPartRetries.ContainsKey(d) ? LibraryImportHoldReason.DuplicateInScan : null,
                                                d.Rejections.Select(r => r.Reason).ToArray())));

            foreach (var result in importResults.Where(result => result.Result != ImportResultType.Imported &&
                                                                 audiobookPartRetries.ContainsKey(result.ImportDecision)))
            {
                result.PreserveExistingUnmappedReason = true;
            }

            // Refresh any authors we added
            if (addedAuthors.Any())
            {
                var distinctAuthorIds = addedAuthors.Select(x => x.Id).Distinct().ToList();
                _commandQueueManager.Push(new BulkRefreshAuthorCommand(distinctAuthorIds, true));
            }

            var addedAuthorMetadataIds = addedAuthors.Select(x => x.AuthorMetadataId).ToHashSet();
            var booksToRefresh = addedBooks.Where(x => !addedAuthorMetadataIds.Contains(x.AuthorMetadataId)).ToList();

            if (booksToRefresh.Any())
            {
                _logger.Debug("Refreshing info for {0} new books", booksToRefresh.Count);
                _commandQueueManager.Push(new BulkRefreshBookCommand(booksToRefresh.Select(x => x.Id).ToList()));
            }

            return importResults;
        }

        private static void RejectIncompatibleFormats(IEnumerable<ImportDecision<LocalBook>> decisions)
        {
            foreach (var decision in decisions.Where(d => d.Approved && !ImportFormat.IsCompatible(d.Item.Path, d.Item.Edition)))
            {
                decision.Reject(new Rejection("File format does not match the selected edition", RejectionType.Permanent));
            }
        }

        private Author EnsureAuthorAdded(List<ImportDecision<LocalBook>> decisions, List<Author> addedAuthors)
        {
            var author = decisions.First().Item.Author;
            var normalizedForeignAuthorId = NormalizeForeignAuthorId(author.ForeignAuthorId);

            if (normalizedForeignAuthorId.IsNotNullOrWhiteSpace())
            {
                author.ForeignAuthorId = normalizedForeignAuthorId;
                author.Metadata.Value.ForeignAuthorId = normalizedForeignAuthorId;
            }

            if (author.Id == 0)
            {
                var dbAuthor = _authorService.FindById(author.ForeignAuthorId);

                if (dbAuthor == null)
                {
                    if (decisions.Any(decision => decision.Item.IsUnattendedLibraryImport) &&
                        ImportDecisionMaker.NeedsReviewForNewSyntheticAuthor(author, _authorService))
                    {
                        foreach (var decision in decisions)
                        {
                            decision.Reject(new Rejection(
                                ImportDecisionMaker.UnverifiedAuthorReviewMessage,
                                holdReason: LibraryImportHoldReason.AuthorIdentityConfirmationRequired));
                        }

                        return null;
                    }

                    _logger.Debug("Adding remote author {0}", author);

                    var path = decisions.First().Item.Path;
                    var rootFolder = _rootFolderService.GetBestRootFolder(path);

                    author.RootFolderPath = rootFolder.Path;
                    author.MetadataProfileId = rootFolder.DefaultMetadataProfileId;
                    author.QualityProfileId = rootFolder.DefaultQualityProfileId;
                    author.Monitored = rootFolder.DefaultMonitorOption != MonitorTypes.None;
                    author.MonitorNewItems = rootFolder.DefaultNewItemMonitorOption;
                    author.Tags = rootFolder.DefaultTags;
                    author.AddOptions = new AddAuthorOptions
                    {
                        SearchForMissingBooks = false,
                        Monitored = author.Monitored,
                        Monitor = rootFolder.DefaultMonitorOption
                    };

                    if (rootFolder.IsCalibreLibrary)
                    {
                        // calibre has author / book / files
                        author.Path = path.GetParentPath().GetParentPath();
                    }

                    if (!ValidateAuthorImportPreflight(author, rootFolder, decisions))
                    {
                        return null;
                    }

                    try
                    {
                        dbAuthor = _addAuthorService.AddAuthor(author, false);

                        // this looks redundant but is necessary to get the LazyLoads populated
                        dbAuthor = _authorService.GetAuthor(dbAuthor.Id);
                        addedAuthors.Add(dbAuthor);
                    }
                    catch (Exception e)
                    {
                        _logger.Error(e, "Failed to add author {0}", author);
                        var bibliographyUnavailable = e is MetadataProviderUnavailableException { Operation: MetadataProviderOrchestrator.AuthorInfoOperation };
                        var reason = bibliographyUnavailable ? LibraryImportHoldReason.AuthorBibliographyUnavailable : (LibraryImportHoldReason?)null;
                        var message = bibliographyUnavailable
                            ? "The metadata provider could not load the author's bibliography. Rescan to retry."
                            : "Failed to add missing author";
                        foreach (var decision in decisions)
                        {
                            decision.Reject(new Rejection(message, RejectionType.Temporary, reason));
                        }

                        return null;
                    }
                }

                // Put in the newly loaded author
                foreach (var decision in decisions)
                {
                    decision.Item.Author = dbAuthor;
                    decision.Item.Book.Author = dbAuthor;
                    decision.Item.Book.AuthorMetadataId = dbAuthor.AuthorMetadataId;
                }

                author = dbAuthor;
            }

            return author;
        }

        private static string NormalizeForeignAuthorId(string foreignAuthorId)
        {
            var normalizedAuthorId = OpenLibraryIdNormalizer.NormalizeAuthorId(foreignAuthorId);

            return normalizedAuthorId.IsNotNullOrWhiteSpace()
                ? $"openlibrary:author:{normalizedAuthorId}"
                : foreignAuthorId;
        }

        private bool ValidateAuthorImportPreflight(Author author, RootFolder rootFolder, List<ImportDecision<LocalBook>> decisions)
        {
            var foreignAuthorId = author?.Metadata?.Value?.ForeignAuthorId;

            if (foreignAuthorId.IsNullOrWhiteSpace())
            {
                RejectAuthorImport(decisions, "Missing or invalid author identifier in import payload");
                return false;
            }

            var normalizedOpenLibraryAuthorId = OpenLibraryIdNormalizer.NormalizeAuthorId(foreignAuthorId);
            var isScopedProviderId = ScopedAuthorIdRegex.IsMatch(foreignAuthorId);

            if (!isScopedProviderId && normalizedOpenLibraryAuthorId.IsNullOrWhiteSpace())
            {
                RejectAuthorImport(decisions, $"Unsupported author identifier format: '{foreignAuthorId}'");
                return false;
            }

            if (rootFolder?.Path.IsNotNullOrWhiteSpace() == true &&
                author?.Path.IsNotNullOrWhiteSpace() == true &&
                rootFolder.Path.Equals(author.Path, StringComparison.OrdinalIgnoreCase))
            {
                RejectAuthorImport(decisions, $"Author path '{author.Path}' conflicts with root folder path");
                return false;
            }

            return true;
        }

        private static void RejectAuthorImport(IEnumerable<ImportDecision<LocalBook>> decisions, string reason)
        {
            foreach (var decision in decisions)
            {
                decision.Reject(new Rejection(reason, RejectionType.Permanent));
            }
        }

        private Book EnsureBookAdded(List<ImportDecision<LocalBook>> decisions, List<Book> addedBooks)
        {
            var book = decisions.First().Item.Book;

            if (book.Id == 0)
            {
                var selectedEdition = decisions[0].Item.Edition;
                var dbBook = _bookService.FindById(book.ForeignBookId);
                if (dbBook != null && book.AuthorMetadataId > 0 && dbBook.AuthorMetadataId != book.AuthorMetadataId)
                {
                    foreach (var decision in decisions)
                    {
                        decision.Reject(new Rejection("The matched work belongs to a different author", RejectionType.Temporary));
                    }

                    return null;
                }

                if (dbBook == null && selectedEdition.ForeignEditionId.IsNotNullOrWhiteSpace())
                {
                    var storedEdition = _editionService.GetEditionByForeignEditionId(selectedEdition.ForeignEditionId);
                    if (storedEdition != null)
                    {
                        dbBook = _bookService.GetBook(storedEdition.BookId);
                        if (dbBook == null || book.AuthorMetadataId == 0 || dbBook.AuthorMetadataId != book.AuthorMetadataId)
                        {
                            RejectBook(decisions);
                            return null;
                        }
                    }
                }

                if (dbBook == null && book.AuthorMetadataId > 0)
                {
                    var candidates = _bookService.GetBooksByAuthorMetadataId(book.AuthorMetadataId) ?? new List<Book>();
                    dbBook = BookWorkIdentity.FindUniqueMatch(book, candidates, out var ambiguous);
                    if (ambiguous)
                    {
                        foreach (var decision in decisions)
                        {
                            decision.Reject(new Rejection("More than one existing work matches this book", RejectionType.Temporary, LibraryImportHoldReason.CompetingEditions));
                        }

                        return null;
                    }
                }

                if (dbBook == null)
                {
                    _logger.Debug("Adding remote book {0}", book);

                    if (book.AuthorMetadataId == 0)
                    {
                        throw new InvalidOperationException("Cannot insert book with AuthorMetadataId = 0");
                    }

                    try
                    {
                        NormalizeBookForInsert(book);
                        var existingBooks = _bookService.GetBooksByAuthor(book.Author.Value.Id);
                        book.Monitored = _monitorNewBookService.ShouldMonitorNewBook(book, existingBooks, book.Author.Value.MonitorNewItems);
                        book.Added = DateTime.UtcNow;
                        _bookService.InsertMany(new List<Book> { book });
                        addedBooks.Add(book);

                        book.Editions.Value.ForEach(x => x.BookId = book.Id);
                        _editionService.InsertMany(book.Editions.Value);

                        dbBook = _bookService.FindById(book.ForeignBookId);
                    }
                    catch (Exception e)
                    {
                        _logger.Error(e, "Failed to add book {0}", book);
                        RejectBook(decisions);

                        return null;
                    }
                }

                var edition = dbBook.Editions.Value.ExclusiveOrDefault(x =>
                    selectedEdition.ForeignEditionId.IsNotNullOrWhiteSpace()
                        ? x.ForeignEditionId == selectedEdition.ForeignEditionId
                        : selectedEdition.Id > 0 && x.Id == selectedEdition.Id);

                if (edition == null && selectedEdition.ForeignEditionId.IsNullOrWhiteSpace())
                {
                    RejectBook(decisions);
                    return null;
                }

                // Another format can be prepared first. Defer a missing selected
                // edition to EnsureEditionAdded on the same persisted work.
                foreach (var decision in decisions)
                {
                    decision.Item.Book = dbBook;
                    decision.Item.Edition = edition ?? selectedEdition;
                }

                book = dbBook;
            }

            return book;
        }

        private static bool HasPersistedAudiobookTarget(LocalBook track)
        {
            return track.Author is { Id: > 0 } && track.Book is { Id: > 0 } &&
                   track.Edition is { Id: > 0 } edition && edition.BookId == track.Book.Id &&
                   ImportFormat.ForEdition(edition) == FormatType.Audiobook;
        }

        private Dictionary<ImportDecision<LocalBook>, BookFile> GetAudiobookPartRetries(List<ImportDecision<LocalBook>> decisions, bool replaceExisting)
        {
            var result = new Dictionary<ImportDecision<LocalBook>, BookFile>();
            if (replaceExisting)
            {
                return result;
            }

            var candidates = decisions.Where(decision => decision.Approved && decision.Item.IsLibraryScan &&
                decision.Item.ExistingFile && ImportFormat.ForPath(decision.Item.Path) == FormatType.Audiobook).ToList();
            if (candidates.Count == 0)
            {
                return result;
            }

            var heldPaths = _mediaFileService.GetUnmappedFiles()
                .Where(file => file.Id > 0 && file.Path.IsNotNullOrWhiteSpace() && file.UnmappedReason == LibraryImportHoldReason.DuplicateInScan)
                .Select(file => file.Path)
                .ToHashSet(PathEqualityComparer.Instance);
            var retries = candidates.Where(candidate => heldPaths.Contains(candidate.Item.Path)).ToList();
            if (retries.Count == 0)
            {
                return result;
            }

            // A different stored spelling can refer to the same path. Include linked rows
            // and require a unique retained hold before attempting any write.
            var paths = retries.Select(candidate => candidate.Item.Path).Distinct(PathEqualityComparer.Instance).ToList();
            var storedByPath = _mediaFileService.GetFileWithPath(paths).ToLookup(file => file.Path, PathEqualityComparer.Instance);
            foreach (var candidate in retries)
            {
                var rows = storedByPath[candidate.Item.Path].Take(2).ToList();
                var held = rows.Count == 1 && rows[0].EditionId == 0 && rows[0].UnmappedReason == LibraryImportHoldReason.DuplicateInScan ? rows[0] : null;
                result.Add(candidate, held);
            }

            return result;
        }

        private static IEnumerable<List<ImportDecision<LocalBook>>> PartitionDigitalVariants(List<ImportDecision<LocalBook>> decisions, FormatType? format)
        {
            if (!format.HasValue || !decisions.Any(decision => ImportFormat.NeedsDigitalEdition(decision.Item.Edition)))
            {
                yield return decisions;
                yield break;
            }

            var tagged = decisions.Select(decision => (Decision: decision, Identity: GetFileEditionIdentity(decision.Item.FileTrackInfo))).ToList();
            if (format == FormatType.Ebook && tagged.Count(item => IsCompleteFile(item.Decision.Item)) > 1)
            {
                foreach (var item in tagged.Where(item => !item.Identity.HasIdentifier && IsCompleteFile(item.Decision.Item)))
                {
                    item.Decision.Reject(new Rejection("Competing complete ebook files require verified edition identifiers", RejectionType.Temporary, LibraryImportHoldReason.CompetingEditions));
                }

                tagged.RemoveAll(item => !item.Decision.Approved);
                if (tagged.Count == 0)
                {
                    yield break;
                }

                decisions = tagged.Select(item => item.Decision).ToList();
            }

            var identities = tagged.Select(item => item.Identity).Where(identity => identity.HasIdentifier).Distinct().ToList();
            var matchingTags = identities.Select(identity => identity.Asin).Where(value => value != null).Distinct().Count() <= 1 &&
                               identities.Select(identity => identity.Isbn).Where(value => value != null).Distinct().Count() <= 1;
            var connectedTags = identities.Count <= 1 || identities.Any(identity => identity.HasBothIdentifiers);
            if (matchingTags && (connectedTags || format == FormatType.Audiobook))
            {
                // Multipart audio often has partial identifiers on different files.
                yield return decisions;
                yield break;
            }

            var variants = identities.Where(identity => identity.HasBothIdentifiers ||
                !identities.Any(other => other.HasBothIdentifiers && identity.Fits(other))).ToList();
            var partitions = variants.ToDictionary(identity => identity, _ => new List<ImportDecision<LocalBook>>());
            foreach (var item in tagged)
            {
                var matches = variants.Where(variant => item.Identity.Fits(variant)).Take(2).ToList();
                if (matches.Count == 1)
                {
                    partitions[matches[0]].Add(item.Decision);
                }
                else
                {
                    item.Decision.Reject(new Rejection("The file does not identify one of the competing digital editions", RejectionType.Temporary, LibraryImportHoldReason.CompetingEditions));
                }
            }

            foreach (var partition in partitions.Values.Where(items => items.Count > 0))
            {
                yield return partition;
            }
        }

        private static bool IsCompleteFile(LocalBook book)
        {
            return book.PartCount <= 1 && book.Part <= 1;
        }

        private static bool HasUnverifiedEbookIsbn(LocalBook file, LocalBook retained)
        {
            if (!file.ExistingFile || !retained.ExistingFile || ImportFormat.ForPath(file.Path) != FormatType.Ebook ||
                !IsCompleteFile(file) || !IsCompleteFile(retained) || file.Size <= 0 || retained.Size <= 0)
            {
                return false;
            }

            var isbn = GetFileEditionIdentity(file.FileTrackInfo).Isbn;
            return isbn != null && !Isbn.AreEquivalent(isbn, file.Edition.Isbn13) && isbn != GetFileEditionIdentity(retained.FileTrackInfo).Isbn;
        }

        private static FileEditionIdentity GetFileEditionIdentity(ParsedTrackInfo tags)
        {
            var asin = tags?.Asin?.Trim();
            return new FileEditionIdentity(
                tags?.AsinConfidence >= 0.9 && asin?.Length == 10 && asin.All(char.IsAsciiLetterOrDigit) ? asin.ToUpperInvariant() : null,
                tags?.IsbnConfidence >= 0.9 ? Isbn.AsIsbn13(tags.Isbn) : null);
        }

        private Edition EnsureImportEdition(Book book, List<ImportDecision<LocalBook>> decisions, FormatType? fileFormat)
        {
            var selected = EnsureEditionAdded(decisions);
            if (!fileFormat.HasValue || !ImportFormat.NeedsDigitalEdition(selected))
            {
                return selected;
            }

            var format = fileFormat.Value;
            var ebook = format == FormatType.Ebook;
            var identities = decisions.Select(decision => GetFileEditionIdentity(decision.Item.FileTrackInfo)).ToList();
            var identity = new FileEditionIdentity(
                identities.Select(item => item.Asin).FirstOrDefault(value => value != null),
                identities.Select(item => item.Isbn).FirstOrDefault(value => value != null));
            var compatible = (_editionService.GetEditionsByBook(book.Id) ?? new List<Edition>())
                .Where(edition => edition.Id > 0 && edition.BookId == book.Id &&
                    edition.IsEbook == ebook && ImportFormat.ForEdition(edition) == format)
                .ToList();
            var matches = identity.HasIdentifier
                ? compatible.Where(edition => MatchesVerifiedIdentifiers(edition, identity.Asin, identity.Isbn)).Take(2).ToList()
                : new List<Edition>();
            if (matches.Count > 1 || (!identity.HasIdentifier && compatible.Take(2).Count() > 1))
            {
                foreach (var decision in decisions)
                {
                    decision.Reject(new Rejection("More than one compatible digital edition requires file identity evidence", RejectionType.Temporary, LibraryImportHoldReason.CompetingEditions));
                }

                return null;
            }

            var edition = matches.SingleOrDefault() ?? GetOrAddLocalDigitalEdition(book, ebook, identity);
            if (edition == null || edition.Id <= 0 || edition.BookId != book.Id ||
                ImportFormat.ForEdition(edition) != format || edition.IsEbook != ebook)
            {
                return null;
            }

            foreach (var decision in decisions)
            {
                decision.Item.Edition = edition;
            }

            return edition;
        }

        private static bool MatchesVerifiedIdentifiers(Edition edition, string asin, string isbn)
        {
            var compareAsin = asin != null && edition.Asin.IsNotNullOrWhiteSpace();
            var compareIsbn = isbn != null && edition.Isbn13.IsNotNullOrWhiteSpace();
            return (compareAsin || compareIsbn) &&
                   (!compareAsin || string.Equals(asin, edition.Asin, StringComparison.OrdinalIgnoreCase)) &&
                   (!compareIsbn || Isbn.AreEquivalent(isbn, edition.Isbn13));
        }

        private Edition GetOrAddLocalDigitalEdition(Book book, bool ebook, FileEditionIdentity fileIdentity)
        {
            var identity = Guid.TryParseExact(book.CanonicalId, "N", out var canonical)
                ? canonical.ToString("N")
                : book.Id.ToString(CultureInfo.InvariantCulture);
            var key = LocalEditionIdPrefix + identity + (ebook ? ":ebook" : ":audiobook");
            if (fileIdentity.HasIdentifier)
            {
                var evidence = Encoding.ASCII.GetBytes($"{fileIdentity.Asin}:{fileIdentity.Isbn}");
                key += ":" + Convert.ToHexStringLower(SHA256.HashData(evidence));
            }

            var existing = _editionService.GetEditionByForeignEditionId(key);
            if (existing != null)
            {
                return existing;
            }

            var edition = new Edition
            {
                ForeignEditionId = key,
                BookId = book.Id,
                Book = book,
                Title = book.Title,
                Format = ebook ? "Ebook" : "Audiobook",
                IsEbook = ebook
            };
            NormalizeEditionForInsert(edition, book);

            try
            {
                _editionService.InsertMany(new List<Edition> { edition });
            }
            catch
            {
                // Another approved import may have created the same local edition.
                existing = _editionService.GetEditionByForeignEditionId(key);
                if (existing == null)
                {
                    throw;
                }
            }

            return existing ?? edition;
        }

        private Edition EnsureEditionAdded(List<ImportDecision<LocalBook>> decisions)
        {
            var book = decisions.First().Item.Book;
            var edition = decisions.First().Item.Edition;

            if (edition.Id == 0)
            {
                var dbEdition = _editionService.GetEditionByForeignEditionId(edition.ForeignEditionId);

                if (dbEdition == null)
                {
                    _logger.Debug("Adding remote edition {0}", edition);

                    try
                    {
                        NormalizeEditionForInsert(edition, book);
                        edition.BookId = book.Id;
                        edition.Monitored = false;
                        _editionService.InsertMany(new List<Edition> { edition });

                        dbEdition = _editionService.GetEditionByForeignEditionId(edition.ForeignEditionId);
                    }
                    catch (Exception e)
                    {
                        _logger.Error(e, "Failed to add edition {0}", edition);
                        RejectBook(decisions);

                        return null;
                    }
                }

                edition = dbEdition;
            }

            if (edition != null && edition.BookId != book.Id)
            {
                foreach (var decision in decisions)
                {
                    decision.Reject(new Rejection("The selected edition belongs to a different work", RejectionType.Temporary));
                }

                return null;
            }

            foreach (var decision in decisions)
            {
                decision.Item.Edition = edition;
            }

            return edition;
        }

        private static void NormalizeBookForInsert(Book book)
        {
            if (book == null)
            {
                return;
            }

            if (book.CleanTitle.IsNullOrWhiteSpace())
            {
                book.CleanTitle = book.Title;
            }

            if (book.TitleSlug.IsNullOrWhiteSpace())
            {
                book.TitleSlug = FirstNonEmpty(book.ForeignBookId, book.OpenLibraryWorkId, book.CleanTitle, book.Title)?.ToUrlSlug();
            }

            var editions = book.Editions?.Value;
            if (editions == null)
            {
                return;
            }

            foreach (var edition in editions)
            {
                NormalizeEditionForInsert(edition, book);
            }
        }

        private static void NormalizeEditionForInsert(Edition edition, Book book)
        {
            if (edition == null)
            {
                return;
            }

            if (edition.Title.IsNullOrWhiteSpace())
            {
                edition.Title = book?.Title;
            }

            if (edition.TitleSlug.IsNullOrWhiteSpace())
            {
                edition.TitleSlug = FirstNonEmpty(edition.ForeignEditionId, edition.Title, book?.TitleSlug, book?.ForeignBookId)?.ToUrlSlug();
            }
        }

        private static string FirstNonEmpty(params string[] values)
        {
            return values.FirstOrDefault(x => x.IsNotNullOrWhiteSpace())?.Trim();
        }

        private void RejectBook(List<ImportDecision<LocalBook>> decisions)
        {
            foreach (var decision in decisions.Where(decision => decision.Approved))
            {
                decision.Reject(new Rejection("Failed to prepare book for import", RejectionType.Temporary));
            }
        }

        private void RemoveExistingTrackFiles(Author author, Book book)
        {
            var rootFolder = _diskProvider.GetParentFolder(author.Path);
            var previousFiles = _mediaFileService.GetFilesByBook(book.Id);

            _logger.Debug("Deleting {0} existing files for {1}", previousFiles.Count, book);

            foreach (var previousFile in previousFiles)
            {
                var subfolder = rootFolder.GetRelativePath(_diskProvider.GetParentFolder(previousFile.Path));
                if (_diskProvider.FileExists(previousFile.Path))
                {
                    _logger.Debug("Removing existing book file: {0}", previousFile);
                    _recycleBinProvider.DeleteFile(previousFile.Path, subfolder);
                }

                _mediaFileService.Delete(previousFile, DeleteMediaFileReason.Upgrade);
            }
        }

        private string GetSceneReleaseName(DownloadClientItem downloadClientItem)
        {
            if (downloadClientItem != null)
            {
                var title = Parser.Parser.RemoveFileExtension(downloadClientItem.Title);

                var parsedTitle = Parser.Parser.ParseBookTitle(title);

                if (parsedTitle != null)
                {
                    return title;
                }
            }

            return null;
        }

        private readonly struct FileEditionIdentity
        {
            public FileEditionIdentity(string asin, string isbn)
            {
                Asin = asin;
                Isbn = isbn;
            }

            public string Asin { get; }
            public string Isbn { get; }
            public bool HasIdentifier => Asin != null || Isbn != null;
            public bool HasBothIdentifiers => Asin != null && Isbn != null;

            public bool Fits(FileEditionIdentity variant)
            {
                return HasIdentifier && (Asin == null || Asin == variant.Asin) && (Isbn == null || Isbn == variant.Isbn);
            }
        }
    }
}
