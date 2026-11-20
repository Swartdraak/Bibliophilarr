using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Abstractions;
using System.Linq;
using System.Text.RegularExpressions;
using NLog;
using NzbDrone.Common;
using NzbDrone.Common.Disk;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Instrumentation;
using NzbDrone.Common.Instrumentation.Extensions;
using NzbDrone.Core.Books;
using NzbDrone.Core.Books.Calibre;
using NzbDrone.Core.DecisionEngine;
using NzbDrone.Core.MediaFiles.BookImport;
using NzbDrone.Core.MediaFiles.Commands;
using NzbDrone.Core.MediaFiles.Events;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.RootFolders;

namespace NzbDrone.Core.MediaFiles
{
    public interface IDiskScanService
    {
        void Scan(List<string> folders = null, FilterFilesType filter = FilterFilesType.Known, bool addNewAuthors = false, List<int> authorIds = null);
        IFileInfo[] GetBookFiles(string path, bool allDirectories = true);
        string[] GetNonBookFiles(string path, bool allDirectories = true);
        List<IFileInfo> FilterFiles(string basePath, IEnumerable<IFileInfo> files);
        List<string> FilterPaths(string basePath, IEnumerable<string> paths);
    }

    public class DiskScanService :
        IDiskScanService,
        IExecute<RescanFoldersCommand>
    {
        public static readonly Regex ExcludedSubFoldersRegex = new Regex(@"(?:\\|\/|^)(?:extras|@eadir|extrafanart|plex versions|\.[^\\/]+)(?:\\|\/)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        public static readonly Regex ExcludedFilesRegex = new Regex(@"^\._|^Thumbs\.db$|^\.DS_store$|\.partial~$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private readonly IDiskProvider _diskProvider;
        private readonly ICalibreProxy _calibre;
        private readonly IMediaFileService _mediaFileService;
        private readonly IMakeImportDecision _importDecisionMaker;
        private readonly IImportApprovedBooks _importApprovedTracks;
        private readonly IAuthorService _authorService;
        private readonly IMediaFileTableCleanupService _mediaFileTableCleanupService;
        private readonly IRootFolderService _rootFolderService;
        private readonly IEventAggregator _eventAggregator;
        private readonly ICommandResultReporter _commandResultReporter;
        private readonly Logger _logger;

        public DiskScanService(IDiskProvider diskProvider,
                               ICalibreProxy calibre,
                               IMediaFileService mediaFileService,
                               IMakeImportDecision importDecisionMaker,
                               IImportApprovedBooks importApprovedTracks,
                               IAuthorService authorService,
                               IRootFolderService rootFolderService,
                               IMediaFileTableCleanupService mediaFileTableCleanupService,
                               IEventAggregator eventAggregator,
                               ICommandResultReporter commandResultReporter,
                               Logger logger)
        {
            _diskProvider = diskProvider;
            _calibre = calibre;

            _mediaFileService = mediaFileService;
            _importDecisionMaker = importDecisionMaker;
            _importApprovedTracks = importApprovedTracks;
            _authorService = authorService;
            _mediaFileTableCleanupService = mediaFileTableCleanupService;
            _rootFolderService = rootFolderService;
            _eventAggregator = eventAggregator;
            _commandResultReporter = commandResultReporter;
            _logger = logger;
        }

        public void Scan(List<string> folders = null, FilterFilesType filter = FilterFilesType.Known, bool addNewAuthors = false, List<int> authorIds = null)
        {
            if (folders == null)
            {
                folders = _rootFolderService.All().Select(x => x.Path).ToList();
            }

            if (authorIds == null)
            {
                authorIds = new List<int>();
            }

            var mediaFileList = new List<IFileInfo>();
            var scannedFolders = new List<string>();

            var musicFilesStopwatch = Stopwatch.StartNew();

            foreach (var folder in folders)
            {
                var loggedFolder = LogSanitizer.Sanitize(folder);

                // We could be scanning a root folder or a subset of a root folder.  If it's a subset,
                // check if the root folder exists before cleaning.
                var rootFolder = _rootFolderService.GetBestRootFolder(folder);

                if (rootFolder == null)
                {
                    _logger.Error("Not scanning {0}, it's not a subdirectory of a defined root folder", loggedFolder);
                    continue;
                }

                var folderExists = _diskProvider.FolderExists(folder);

                if (!folderExists)
                {
                    if (!_diskProvider.FolderExists(rootFolder.Path))
                    {
                        _logger.Warn("Authors' root folder ({0}) doesn't exist.", LogSanitizer.Sanitize(rootFolder.Path));
                        var skippedAuthors = _authorService.GetAuthors(authorIds);
                        skippedAuthors.ForEach(x => _eventAggregator.PublishEvent(new AuthorScanSkippedEvent(x, AuthorScanSkippedReason.RootFolderDoesNotExist)));
                        continue;
                    }

                    if (_diskProvider.FolderEmpty(rootFolder.Path))
                    {
                        _logger.Warn("Authors' root folder ({0}) is empty.", LogSanitizer.Sanitize(rootFolder.Path));
                        var skippedAuthors = _authorService.GetAuthors(authorIds);
                        skippedAuthors.ForEach(x => _eventAggregator.PublishEvent(new AuthorScanSkippedEvent(x, AuthorScanSkippedReason.RootFolderIsEmpty)));
                        continue;
                    }
                }

                if (!folderExists)
                {
                    _logger.Debug("Specified scan folder ({0}) doesn't exist.", loggedFolder);

                    CleanMediaFiles(folder, new List<string>());
                    scannedFolders.Add(folder);
                    continue;
                }

                _logger.ProgressInfo("Scanning {0}", loggedFolder);

                List<IFileInfo> files;
                try
                {
                    // Cleanup needs a complete view of this scope, including nested folders.
                    files = FilterFiles(folder, GetBookFiles(folder, allDirectories: true, ignoreInaccessible: false));
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                {
                    _logger.Warn("Could not enumerate scan folder {0}: {1}. Its existing file records were kept.",
                        loggedFolder,
                        error.GetType().Name);
                    continue;
                }

                scannedFolders.Add(folder);
                if (!files.Any())
                {
                    _logger.Warn("Scan folder {0} is empty.", loggedFolder);
                    continue;
                }

                CleanMediaFiles(folder, files.Select(x => x.FullName).ToList());
                mediaFileList.AddRange(files);
            }

            var incompleteScan = scannedFolders.Count != folders.Count;
            if (incompleteScan)
            {
                _commandResultReporter.Report(CommandResult.Unsuccessful);
                if (scannedFolders.Count == 0)
                {
                    _logger.ProgressInfo("Library scan skipped all {0} requested folders. See System Events for details.", folders.Count);
                    return;
                }
            }

            musicFilesStopwatch.Stop();
            _logger.Trace("Finished enumerating {0} scan folders in {1}.", scannedFolders.Count, musicFilesStopwatch.Elapsed);

            var decisionsStopwatch = Stopwatch.StartNew();

            var config = new ImportDecisionMakerConfig
            {
                Filter = filter,
                IncludeExisting = true,
                AddNewAuthors = addNewAuthors
            };

            var decisions = _importDecisionMaker.GetImportDecisions(mediaFileList, null, null, config);
            decisions.ForEach(decision => decision.Item.IsUnattendedLibraryImport = addNewAuthors);

            decisionsStopwatch.Stop();
            _logger.Debug("Import decisions complete [{0}]", decisionsStopwatch.Elapsed);

            var importStopwatch = Stopwatch.StartNew();
            var importResults = _importApprovedTracks.Import(decisions, false);

            // decisions may have been filtered to just new files.  Anything new and approved will have been inserted.
            // Now we need to make sure anything new but not approved gets inserted
            // Note that knownFiles will include anything imported just now
            var knownFiles = new List<BookFile>();
            scannedFolders.ForEach(x => knownFiles.AddRange(_mediaFileService.GetFilesWithBasePath(x)));

            knownFiles = knownFiles.DistinctBy(file => file.Path, PathEqualityComparer.Instance).ToList();
            var unlinkedDecisions = decisions.ExceptBy(
                decision => decision.Item.Path,
                knownFiles.Where(file => file.EditionId > 0),
                file => file.Path,
                PathEqualityComparer.Instance);
            var unmappedReasons = GetUnmappedReasons(unlinkedDecisions, importResults);
            if (!incompleteScan && unmappedReasons.Values.Any(reason => reason is LibraryImportHoldReason.ImportFailed or LibraryImportHoldReason.ReadError))
            {
                _commandResultReporter.Report(CommandResult.Unsuccessful);
            }

            var newFiles = decisions
                .ExceptBy(x => x.Item.Path, knownFiles, x => x.Path, PathEqualityComparer.Instance)
                .DistinctBy(x => x.Item.Path, PathEqualityComparer.Instance)
                .Select(decision => new BookFile
                {
                    Path = decision.Item.Path,
                    CalibreId = decision.Item.CalibreId,
                    Part = decision.Item.Part,
                    PartCount = decision.Item.PartCount,
                    Size = decision.Item.Size,
                    Modified = decision.Item.Modified,
                    DateAdded = DateTime.UtcNow,
                    Quality = decision.Item.Quality,
                    MediaInfo = decision.Item.FileTrackInfo?.MediaInfo,
                    UnmappedReason = unmappedReasons[decision.Item.Path]
                })
                .ToList();
            _mediaFileService.AddMany(newFiles);

            _logger.Debug($"Inserted {newFiles.Count} new unmatched trackfiles");

            // finally update info on size/modified for existing files
            var updatedFiles = knownFiles
                .Join(decisions,
                      x => x.Path,
                      x => x.Item.Path,
                      (file, decision) => new
                      {
                          File = file,
                          Item = decision.Item
                      },
                      PathEqualityComparer.Instance)
                .Where(x => x.File.Size != x.Item.Size ||
                       Math.Abs((x.File.Modified - x.Item.Modified).TotalSeconds) > 1)
                .Select(x =>
                {
                    x.File.Size = x.Item.Size;
                    x.File.Modified = x.Item.Modified;
                    x.File.MediaInfo = x.Item.FileTrackInfo?.MediaInfo;
                    x.File.Quality = x.Item.Quality;
                    return x.File;
                })
                .ToList();

            _mediaFileService.UpdateScannedFileInfo(updatedFiles);

            var changedReasons = knownFiles.Where(file => file.EditionId == 0 &&
                unmappedReasons.TryGetValue(file.Path, out var reason) && file.UnmappedReason != reason).ToList();
            changedReasons.ForEach(file => file.UnmappedReason = unmappedReasons[file.Path]);
            _mediaFileService.SetUnmappedReasons(changedReasons);

            _logger.Debug($"Updated info for {updatedFiles.Count} known files");

            if (!incompleteScan)
            {
                _authorService.GetAuthors(authorIds).ForEach(CompletedScanning);
            }
            else
            {
                _logger.ProgressInfo("Library scan skipped {0} folders. {1} evaluated files remain unmapped. See System Events for details.",
                    folders.Count - scannedFolders.Count,
                    unmappedReasons.Count);
            }

            importStopwatch.Stop();
            _logger.Debug("Library import finished for {0} scan folders in {1}.", scannedFolders.Count, importStopwatch.Elapsed);
        }

        private Dictionary<string, LibraryImportHoldReason> GetUnmappedReasons(IEnumerable<ImportDecision<LocalBook>> decisions, List<ImportResult> importResults)
        {
            var reasons = new Dictionary<string, LibraryImportHoldReason>(PathEqualityComparer.Instance);
            var importErrors = (importResults ?? new List<ImportResult>())
                .ToLookup(result => result.ImportDecision.Item.Path, PathEqualityComparer.Instance);
            foreach (var decision in decisions.DistinctBy(decision => decision.Item.Path, PathEqualityComparer.Instance))
            {
                var results = importErrors[decision.Item.Path];
                var reason = decision.Approved
                    ? string.Join(", ", results.SelectMany(result => result.Errors).Distinct())
                    : string.Join(", ", decision.Rejections);
                reasons.Add(decision.Item.Path, decision.Rejections.Select(rejection => rejection.HoldReason)
                    .Concat(results.Select(result => result.HoldReason))
                    .FirstOrDefault(code => code.HasValue) ?? LibraryImportHoldReason.ReviewRequired);
                _logger.Debug(
                    "Unmapped library file {0}: {1}",
                    LogSanitizer.Sanitize(decision.Item.Path),
                    reason.IsNullOrWhiteSpace() ? "import did not persist an edition link" : LogSanitizer.Sanitize(reason));
            }

            if (reasons.Count > 0)
            {
                var counts = reasons.Values.GroupBy(reason => reason).OrderBy(group => group.Key)
                    .Select(group => $"{group.Key}={group.Count()}");
                _logger.ProgressInfo("Library scan left {0} evaluated files unmapped. {1}", reasons.Count, string.Join(", ", counts));
            }

            return reasons;
        }

        private void CleanMediaFiles(string folder, List<string> mediaFileList)
        {
            _logger.Debug("Checking tracked files under {0}", LogSanitizer.Sanitize(folder));
            _mediaFileTableCleanupService.Clean(folder, mediaFileList);
        }

        private void CompletedScanning(Author author)
        {
            _logger.Info("Completed scanning disk for {0}", author.Name);
            _eventAggregator.PublishEvent(new AuthorScannedEvent(author));
        }

        public IFileInfo[] GetBookFiles(string path, bool allDirectories = true)
        {
            return GetBookFiles(path, allDirectories, ignoreInaccessible: true);
        }

        private IFileInfo[] GetBookFiles(string path, bool allDirectories, bool ignoreInaccessible)
        {
            IEnumerable<IFileInfo> filesOnDisk;

            var rootFolder = _rootFolderService.GetBestRootFolder(path);

            if (rootFolder != null && rootFolder.IsCalibreLibrary && rootFolder.CalibreSettings != null)
            {
                _logger.Info("Getting book list from Calibre for {0}", LogSanitizer.Sanitize(path));
                var paths = _calibre.GetAllBookFilePaths(rootFolder.CalibreSettings);
                var folderPaths = paths.Where(x => path.IsParentPath(x));

                filesOnDisk = folderPaths.Select(x => _diskProvider.GetFileInfo(x));
            }
            else
            {
                _logger.Debug("Scanning '{0}' for supported media files", LogSanitizer.Sanitize(path));

                filesOnDisk = _diskProvider.GetFileInfos(path, allDirectories, ignoreInaccessible);

                _logger.Trace("{0} files were found in {1}", filesOnDisk.Count(), path);
            }

            var mediaFileList = filesOnDisk.Where(file => MediaFileExtensions.AllExtensions.Contains(file.Extension))
                .ToArray();

            _logger.Debug("{0} book files were found in {1}", mediaFileList.Length, path);

            return mediaFileList;
        }

        public string[] GetNonBookFiles(string path, bool allDirectories = true)
        {
            _logger.Debug("Scanning '{0}' for non-ebook files", path);

            var filesOnDisk = _diskProvider.GetFiles(path, allDirectories).ToList();

            var mediaFileList = filesOnDisk.Where(file => !MediaFileExtensions.AllExtensions.Contains(Path.GetExtension(file)))
                                           .ToList();

            _logger.Trace("{0} files were found in {1}", filesOnDisk.Count, path);
            _logger.Debug("{0} non-ebook files were found in {1}", mediaFileList.Count, path);

            return mediaFileList.ToArray();
        }

        public List<string> FilterPaths(string basePath, IEnumerable<string> paths)
        {
            return paths.Where(file => !ExcludedSubFoldersRegex.IsMatch(basePath.GetRelativePath(file)))
                        .Where(file => !ExcludedFilesRegex.IsMatch(Path.GetFileName(file)))
                        .ToList();
        }

        public List<IFileInfo> FilterFiles(string basePath, IEnumerable<IFileInfo> files)
        {
            return files.Where(file => !ExcludedSubFoldersRegex.IsMatch(basePath.GetRelativePath(file.FullName)))
                        .Where(file => !ExcludedFilesRegex.IsMatch(file.Name))
                        .ToList();
        }

        public void Execute(RescanFoldersCommand message)
        {
            Scan(message.Folders, message.Filter, message.AddNewAuthors, message.AuthorIds);
        }
    }
}
