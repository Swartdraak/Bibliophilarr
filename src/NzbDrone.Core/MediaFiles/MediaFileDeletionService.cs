using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions;
using System.Linq;
using System.Net;
using NLog;
using NzbDrone.Common.Disk;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Books;
using NzbDrone.Core.Books.Calibre;
using NzbDrone.Core.Books.Events;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Exceptions;
using NzbDrone.Core.MediaFiles.Events;
using NzbDrone.Core.Messaging;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.RootFolders;

namespace NzbDrone.Core.MediaFiles
{
    public interface IDeleteMediaFiles
    {
        void DeleteTrackFile(Author author, BookFile bookFile);
        void DeleteTrackFile(BookFile bookFile, string subfolder = "");
        void DeleteFilesForExplicitBookRemoval(int bookId);
    }

    public class MediaFileDeletionService : IDeleteMediaFiles,
                                            IHandle<AuthorDeletedEvent>,
                                            IHandleAsync<AuthorDeletedEvent>,
                                            IHandleAsync<BookDeletedEvent>,
                                            IHandle<BookFileDeletedEvent>
    {
        private readonly IDiskProvider _diskProvider;
        private readonly IRecycleBinProvider _recycleBinProvider;
        private readonly IMediaFileService _mediaFileService;
        private readonly IMediaFileRepository _mediaFileRepository;
        private readonly IAuthorService _authorService;
        private readonly IConfigService _configService;
        private readonly IEventAggregator _eventAggregator;
        private readonly IRootFolderService _rootFolderService;
        private readonly ICalibreProxy _calibre;
        private readonly Logger _logger;

        public MediaFileDeletionService(IDiskProvider diskProvider,
                                        IRecycleBinProvider recycleBinProvider,
                                        IMediaFileService mediaFileService,
                                        IMediaFileRepository mediaFileRepository,
                                        IAuthorService authorService,
                                        IConfigService configService,
                                        IEventAggregator eventAggregator,
                                        IRootFolderService rootFolderService,
                                        ICalibreProxy calibre,
                                        Logger logger)
        {
            _diskProvider = diskProvider;
            _recycleBinProvider = recycleBinProvider;
            _mediaFileService = mediaFileService;
            _mediaFileRepository = mediaFileRepository;
            _authorService = authorService;
            _configService = configService;
            _eventAggregator = eventAggregator;
            _rootFolderService = rootFolderService;
            _calibre = calibre;
            _logger = logger;
        }

        public void DeleteTrackFile(Author author, BookFile bookFile)
        {
            var fullPath = bookFile.Path;

            // Use the best matching root folder for the file's actual path, not the author's
            // base path. With dual-format tracking, ebook files may be under a different root
            // folder (e.g. /media/ebooks) than the author's primary path (e.g. /media/audiobooks).
            var fileRootFolder = _rootFolderService.GetBestRootFolder(fullPath);
            var rootFolderPath = fileRootFolder?.Path ?? _diskProvider.GetParentFolder(author.Path);

            if (!_diskProvider.FolderExists(rootFolderPath))
            {
                _logger.Warn("Root folder ({0}) doesn't exist for file {1}.", rootFolderPath, fullPath);
                throw new NzbDroneClientException(HttpStatusCode.Conflict, "Root folder ({0}) doesn't exist.", rootFolderPath);
            }

            if (_diskProvider.GetDirectories(rootFolderPath).Empty())
            {
                _logger.Warn("Root folder ({0}) is empty.", rootFolderPath);
                throw new NzbDroneClientException(HttpStatusCode.Conflict, "Root folder ({0}) is empty.", rootFolderPath);
            }

            // Determine the author folder under this root folder
            var fileParent = _diskProvider.GetParentFolder(fullPath);
            if (rootFolderPath.IsParentPath(fileParent))
            {
                var subfolder = rootFolderPath.GetRelativePath(fileParent);
                DeleteTrackFile(bookFile, subfolder);
            }
            else if (_diskProvider.FolderExists(author.Path))
            {
                var subfolder = _diskProvider.GetParentFolder(author.Path).GetRelativePath(fileParent);
                DeleteTrackFile(bookFile, subfolder);
            }
            else
            {
                // delete from db even if the author folder is missing
                _mediaFileService.Delete(bookFile, DeleteMediaFileReason.Manual);
            }
        }

        public void DeleteTrackFile(BookFile bookFile, string subfolder = "")
        {
            var fullPath = bookFile.Path;

            if (_diskProvider.FileExists(fullPath))
            {
                _logger.Info("Deleting book file: {0}", fullPath);
                DeleteFile(bookFile, subfolder);
            }

            // Delete the track file from the database to clean it up even if the file was already deleted
            _mediaFileService.Delete(bookFile, DeleteMediaFileReason.Manual);

            _eventAggregator.PublishEvent(new DeleteCompletedEvent());
        }

        public void DeleteFilesForExplicitBookRemoval(int bookId)
        {
            var files = _mediaFileService.GetFilesByBook(bookId);
            var selectedIds = files.Select(file => file.Id).ToHashSet();
            var targets = files.OrderBy(file => file.Id).Select(file => (File: file, Root: GetBookFileRoot(file.Path))).ToList();
            var calibreRecords = targets.Where(target => target.Root.IsCalibreLibrary)
                .GroupBy(target => (target.Root.Id, target.File.CalibreId)).ToList();

            // A Calibre deletion removes the entire record, including formats held by another work.
            foreach (var record in calibreRecords)
            {
                ValidateCalibreOwnership(record.First().Root, record.Key.CalibreId, selectedIds);
            }

            foreach (var target in targets)
            {
                ConfirmFileUnchanged(bookId, target.File, target.Root);
            }

            foreach (var target in targets.Where(target => !target.Root.IsCalibreLibrary))
            {
                var root = ConfirmFileUnchanged(bookId, target.File, target.Root);
                if (!_diskProvider.FileExists(target.File.Path))
                {
                    throw MissingBookFile();
                }

                DeleteFile(target.File, rootFolder: root);
                RemoveDeletedFileRecord(target.File);
            }

            foreach (var record in calibreRecords)
            {
                var first = record.First();
                var root = ConfirmFileUnchanged(bookId, first.File, first.Root);
                ValidateCalibreOwnership(root, record.Key.CalibreId, selectedIds);
                foreach (var target in record.Skip(1))
                {
                    ConfirmFileUnchanged(bookId, target.File, target.Root);
                }

                DeleteFile(first.File, rootFolder: root);
                foreach (var target in record)
                {
                    RemoveDeletedFileRecord(target.File);
                }
            }
        }

        private RootFolder GetBookFileRoot(string path)
        {
            var root = !string.IsNullOrWhiteSpace(path) && Path.IsPathFullyQualified(path) &&
                       !path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Any(part => part is "." or "..")
                ? _rootFolderService.GetBestRootFolder(path)
                : null;
            if (root == null || !root.Path.IsParentPath(path) || !_diskProvider.FolderExists(root.Path))
            {
                throw new NzbDroneClientException(HttpStatusCode.Conflict, "A book file is outside an available registered root. Refresh before trying again.");
            }

            // Explicitly configured aliases are allowed. Reject links encountered below that root.
            for (var parent = Path.GetDirectoryName(path); !parent.PathEquals(root.Path); parent = Path.GetDirectoryName(parent))
            {
                RejectLinkedBookPath(_diskProvider.GetDirectoryInfo(parent));
            }

            return root;
        }

        private static void RejectLinkedBookPath(IFileSystemInfo path)
        {
            if (path.LinkTarget != null || (path.Exists && (path.Attributes & FileAttributes.ReparsePoint) != 0))
            {
                throw new NzbDroneClientException(HttpStatusCode.Conflict, "A book file or its parent is linked below the registered root. Review the file location before trying again.");
            }
        }

        private RootFolder ConfirmFileUnchanged(int bookId, BookFile file, RootFolder expectedRoot)
        {
            var root = GetBookFileRoot(file.Path);
            var current = _mediaFileService.GetFileWithPath(file.Path);
            if (root.Id != expectedRoot.Id || !root.Path.PathEquals(expectedRoot.Path) || root.IsCalibreLibrary != expectedRoot.IsCalibreLibrary ||
                (root.IsCalibreLibrary && !SameCalibreLibrary(root.CalibreSettings, expectedRoot.CalibreSettings)) ||
                current?.Id != file.Id || current.EditionId != file.EditionId ||
                current.Edition?.Value?.BookId != bookId || current.CalibreId != file.CalibreId ||
                current.Size != file.Size || current.Modified != file.Modified)
            {
                throw new NzbDroneClientException(HttpStatusCode.Conflict, "A book file changed during deletion. Refresh before trying again.");
            }

            var onDisk = _diskProvider.GetFileInfo(file.Path);
            RejectLinkedBookPath(onDisk);
            if (!root.IsCalibreLibrary && !onDisk.Exists)
            {
                throw MissingBookFile();
            }

            if (onDisk.Exists && (onDisk.Length != file.Size || Math.Abs((onDisk.LastWriteTimeUtc - file.Modified).TotalSeconds) > 1))
            {
                throw new NzbDroneClientException(HttpStatusCode.Conflict, "A book file on disk changed. Rescan it before trying again.");
            }

            return root;
        }

        private static NzbDroneClientException MissingBookFile()
        {
            return new NzbDroneClientException(HttpStatusCode.Conflict, "A tracked book file is unavailable. Restore access or rescan the library before trying again.");
        }

        private static bool SameCalibreLibrary(CalibreSettings current, CalibreSettings expected)
        {
            return current != null && expected != null &&
                   (current.Host, current.Port, current.UseSsl, current.UrlBase, current.Library, current.Username) ==
                   (expected.Host, expected.Port, expected.UseSsl, expected.UrlBase, expected.Library, expected.Username);
        }

        private void ValidateCalibreOwnership(RootFolder root, int calibreId, HashSet<int> selectedIds)
        {
            if (root.CalibreSettings == null || calibreId <= 0 ||
                _mediaFileService.GetFilesWithBasePath(root.Path).Any(file => file.CalibreId == calibreId && !selectedIds.Contains(file.Id)))
            {
                throw new NzbDroneClientException(HttpStatusCode.Conflict, "Could not verify that the Calibre record belongs only to this book.");
            }
        }

        private void RemoveDeletedFileRecord(BookFile file)
        {
            if (_diskProvider.FileExists(file.Path))
            {
                throw new NzbDroneClientException(HttpStatusCode.InternalServerError, "Unable to delete book file");
            }

            if (!_mediaFileRepository.DeleteMatchingFile(file))
            {
                throw new NzbDroneClientException(HttpStatusCode.Conflict, "A book file changed during deletion. Refresh before trying again.");
            }
        }

        private void DeleteFile(BookFile bookFile, string subfolder = "", RootFolder rootFolder = null)
        {
            rootFolder ??= _rootFolderService.GetBestRootFolder(bookFile.Path);
            var isCalibre = rootFolder.IsCalibreLibrary && rootFolder.CalibreSettings != null;

            try
            {
                if (!isCalibre)
                {
                    _recycleBinProvider.DeleteFile(bookFile.Path, subfolder);
                }
                else
                {
                    _calibre.DeleteBook(bookFile, rootFolder.CalibreSettings);
                }
            }
            catch (Exception e)
            {
                _logger.Error(e, "Unable to delete book file");
                throw new NzbDroneClientException(HttpStatusCode.InternalServerError, "Unable to delete book file");
            }
        }

        [EventHandleOrder(EventHandleOrder.First)]
        public void Handle(AuthorDeletedEvent message)
        {
            if (message.DeleteFiles)
            {
                var author = message.Author;

                var rootFolder = _rootFolderService.GetBestRootFolder(message.Author.Path);
                var isCalibre = rootFolder.IsCalibreLibrary && rootFolder.CalibreSettings != null;

                if (isCalibre)
                {
                    // use metadataId instead of authorId so that query works even after author deleted
                    var books = _mediaFileService.GetFilesByAuthorMetadataId(author.AuthorMetadataId);
                    _calibre.DeleteBooks(books, rootFolder.CalibreSettings);
                }
            }
        }

        public void HandleAsync(AuthorDeletedEvent message)
        {
            if (message.DeleteFiles)
            {
                var author = message.Author;

                var rootFolder = _rootFolderService.GetBestRootFolder(message.Author.Path);
                var isCalibre = rootFolder.IsCalibreLibrary && rootFolder.CalibreSettings != null;

                if (!isCalibre)
                {
                    var allAuthors = _authorService.AllAuthorPaths();

                    foreach (var s in allAuthors)
                    {
                        if (s.Key == author.Id)
                        {
                            continue;
                        }

                        if (author.Path.IsParentPath(s.Value))
                        {
                            _logger.Error("Author path: '{0}' is a parent of another author, not deleting files.", author.Path);
                            return;
                        }

                        if (author.Path.PathEquals(s.Value))
                        {
                            _logger.Error("Author path: '{0}' is the same as another author, not deleting files.", author.Path);
                            return;
                        }
                    }

                    if (_diskProvider.FolderExists(message.Author.Path))
                    {
                        _recycleBinProvider.DeleteFolder(message.Author.Path);
                    }

                    _eventAggregator.PublishEvent(new DeleteCompletedEvent());
                }
            }
        }

        public void HandleAsync(BookDeletedEvent message)
        {
            if (message.DeleteFiles && !message.MediaAlreadyRemoved)
            {
                var files = _mediaFileService.GetFilesByBook(message.Book.Id);
                foreach (var file in files)
                {
                    DeleteFile(file);
                }
            }
        }

        [EventHandleOrder(EventHandleOrder.Last)]
        public void Handle(BookFileDeletedEvent message)
        {
            if (message?.BookFile?.Author?.Value == null || message.BookFile.Path.IsNullOrWhiteSpace())
            {
                return;
            }

            if (message.Reason == DeleteMediaFileReason.Upgrade)
            {
                return;
            }

            if (_configService.DeleteEmptyFolders)
            {
                var author = message.BookFile.Author.Value;
                var bookFolder = message.BookFile.Path.GetParentPath();

                if (_diskProvider.GetFiles(author.Path, true).Empty())
                {
                    _diskProvider.DeleteFolder(author.Path, true);
                }
                else if (_diskProvider.GetFiles(bookFolder, true).Empty())
                {
                    _diskProvider.RemoveEmptySubfolders(bookFolder);
                }
            }
        }
    }
}
