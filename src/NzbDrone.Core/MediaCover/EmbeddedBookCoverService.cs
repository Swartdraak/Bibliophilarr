using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using Dapper;
using Microsoft.Win32.SafeHandles;
using NLog;
using NzbDrone.Common.Disk;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Books;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.MediaFiles.BookImport;
using NzbDrone.Core.RootFolders;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;

namespace NzbDrone.Core.MediaCover
{
    public interface IEmbeddedBookCoverService
    {
        bool Recover(int bookId);
        string GetUrl(int bookId);
        string GetPath(int bookId, int fileId, string token);
    }

    public class EmbeddedBookCoverService : IEmbeddedBookCoverService
    {
        private const int MaxCoverBytes = 4 * 1024 * 1024;
        private const long MaxCoverPixels = 16_000_000;

        // Linux x64/arm64, from fcntl.h. Read-only is zero; nonblocking
        // keeps a replacement pipe from stalling before handle verification.
        private const int NoFollow = 0x20000;
        private const int NonBlocking = 0x800;
        private const int CloseOnExec = 0x80000;

        private const string CandidatesSql = """
            SELECT b."CanonicalId", e."Id" AS "EditionId", e."Format", e."IsEbook", e."Images",
                   f."Id" AS "FileId", f."Path", f."Size", f."Modified"
            FROM "Books" b
            JOIN "Editions" e ON e."BookId" = b."Id"
            LEFT JOIN "BookFiles" f ON f."EditionId" = e."Id"
                 AND f."UnmappedReason" IS NULL AND f."CalibreId" = 0
            WHERE b."Id" = @BookId
            """;

        private static readonly object RecoveryLock = new object();

        private readonly IMainDatabase _database;
        private readonly IRootFolderService _roots;
        private readonly IDiskProvider _disk;
        private readonly IConfigFileProvider _config;
        private readonly Logger _logger;
        private readonly string _cacheRoot;

        public EmbeddedBookCoverService(IMainDatabase database,
                                        IRootFolderService roots,
                                        IDiskProvider disk,
                                        IConfigFileProvider config,
                                        IAppFolderInfo appFolder,
                                        Logger logger)
        {
            _database = database;
            _roots = roots;
            _disk = disk;
            _config = config;
            _logger = logger;
            _cacheRoot = Path.Combine(appFolder.GetAppDataPath(), "EmbeddedBookCovers");
        }

        private static bool SupportsSafeOpen =>
            OperatingSystem.IsLinux() && (RuntimeInformation.ProcessArchitecture is Architecture.X64 or Architecture.Arm64);

        public bool Recover(int bookId)
        {
            if (!SupportsSafeOpen || bookId <= 0 || !Monitor.TryEnter(RecoveryLock))
            {
                return false;
            }

            try
            {
                var source = Select(bookId);
                if (source == null || !IsRegisteredLocalPath(source.Path))
                {
                    return false;
                }

                var token = Token(bookId, source);
                var destination = CachePath(bookId, source.FileId, token);
                if (HasImage(destination))
                {
                    return true;
                }

                using var handle = OpenSource(source.Path);
                if (handle == null || !TryGetVersion(handle, source, out var modified))
                {
                    return false;
                }

                using var input = new FileStream(handle, FileAccess.Read);
                var cover = M4bCoverReader.Read(input);
                if (cover == null || !TryGetVersion(handle, source, out var checkedModified) || checkedModified != modified)
                {
                    return false;
                }

                return StageAndPublish(bookId, source, destination, cover, handle, modified);
            }
            catch (Exception error) when (IsRecoverable(error))
            {
                _logger.Debug("Embedded cover recovery did not complete for work {0} ({1}).", bookId, error.GetType().Name);
                return false;
            }
            finally
            {
                Monitor.Exit(RecoveryLock);
            }
        }

        public string GetUrl(int bookId)
        {
            if (!SupportsSafeOpen || bookId <= 0)
            {
                return null;
            }

            try
            {
                if (!_disk.FolderExists(CacheDirectory(bookId)))
                {
                    return null;
                }

                var source = Select(bookId);
                if (source == null || !IsRegisteredLocalPath(source.Path))
                {
                    return null;
                }

                var token = Token(bookId, source);
                return HasImage(CachePath(bookId, source.FileId, token))
                    ? FormattableString.Invariant($"{_config.UrlBase}/api/v1/embeddedbookcover/{bookId}/{source.FileId}/{token}")
                    : null;
            }
            catch (Exception error) when (IsRecoverable(error))
            {
                return null;
            }
        }

        public string GetPath(int bookId, int fileId, string token)
        {
            if (!SupportsSafeOpen || bookId <= 0 || fileId <= 0 ||
                token?.Length != 64 || !token.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f'))
            {
                return null;
            }

            try
            {
                var path = CachePath(bookId, fileId, token);
                if (!HasImage(path))
                {
                    return null;
                }

                var source = Select(bookId);
                return source?.FileId == fileId && Token(bookId, source) == token && IsRegisteredLocalPath(source.Path)
                    ? path
                    : null;
            }
            catch (Exception error) when (IsRecoverable(error))
            {
                return null;
            }
        }

        private Candidate Select(int bookId)
        {
            using var connection = _database.OpenConnection();
            return Select(connection, null, bookId);
        }

        private static Candidate Select(IDbConnection connection, IDbTransaction transaction, int bookId)
        {
            var rows = connection.Query<Candidate>(CandidatesSql, new { BookId = bookId }, transaction).ToList();
            if (rows.Any(row => row.Images?.Any(image => image?.CoverType == MediaCoverTypes.Cover && !string.IsNullOrWhiteSpace(image.Url)) == true))
            {
                return null;
            }

            var supported = rows.Where(row => row.FileId > 0 && row.Size > 0 && row.EditionId > 0 &&
                Guid.TryParseExact(row.CanonicalId, "N", out var canonical) && canonical != Guid.Empty &&
                string.Equals(Path.GetExtension(row.Path), ".m4b", StringComparison.OrdinalIgnoreCase) &&
                !row.IsEbook &&
                ImportFormat.ForEdition(new Edition { Format = row.Format }, requireExplicitFormat: true) == FormatType.Audiobook)
                .Take(2)
                .ToList();
            return supported.Count == 1 ? supported[0] : null;
        }

        private bool StageAndPublish(int bookId, Candidate source, string destination, byte[] cover, SafeFileHandle handle, DateTime modified)
        {
            if (cover.Length == 0 || cover.Length > MaxCoverBytes)
            {
                return false;
            }

            using var bytes = new MemoryStream(cover, writable: false);
            var options = new DecoderOptions { SkipMetadata = true, MaxFrames = 1 };
            var info = Image.Identify(options, bytes);
            if ((info.Metadata.DecodedImageFormat != JpegFormat.Instance && info.Metadata.DecodedImageFormat != PngFormat.Instance) ||
                (long)info.Width * info.Height <= 1 || (long)info.Width * info.Height > MaxCoverPixels)
            {
                return false;
            }

            bytes.Position = 0;
            using var image = Image.Load(options, bytes);
            using var jpeg = new MemoryStream(new byte[MaxCoverBytes], writable: true);
            jpeg.SetLength(0);
            image.Save(jpeg, new JpegEncoder { Quality = 80, SkipMetadata = true });

            var folder = CacheDirectory(bookId);
            _disk.CreateFolder(folder);
            var staged = Path.Combine(folder, $".{Guid.NewGuid():N}.tmp");
            try
            {
                using (var output = new FileStream(staged, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    jpeg.Position = 0;
                    jpeg.CopyTo(output);
                }

                if (!TryGetVersion(handle, source, out var latestModified) || latestModified != modified ||
                    !IsRegisteredLocalPath(source.Path))
                {
                    return false;
                }

                return Publish(bookId, source, staged, destination);
            }
            finally
            {
                if (_disk.FileExists(staged))
                {
                    _disk.DeleteFile(staged);
                }
            }
        }

        private bool Publish(int bookId, Candidate source, string staged, string destination)
        {
            var postgres = _database.DatabaseType == DatabaseType.PostgreSQL;
            using var connection = _database.OpenConnection();
            using var transaction = connection.BeginTransaction(postgres ? IsolationLevel.ReadCommitted : IsolationLevel.Serializable);

            if (postgres)
            {
                // These legacy edges lack foreign keys. Row locks alone would not
                // protect against a competing edition/file inserted after selection.
                connection.Execute("""LOCK TABLE "Books", "Editions", "BookFiles" IN SHARE MODE NOWAIT""", transaction: transaction);
            }

            var current = Select(connection, transaction, bookId);
            if (current == null || !string.Equals(Token(bookId, current), Token(bookId, source), StringComparison.Ordinal))
            {
                return false;
            }

            if (HasImage(destination))
            {
                return true;
            }

            // The transaction protects the fresh read through the atomic move.
            // It has no database changes to commit.
            _disk.MoveFile(staged, destination, overwrite: false);
            return true;
        }

        private bool IsRegisteredLocalPath(string path)
        {
            if (!Path.IsPathFullyQualified(path) || !string.Equals(Path.GetFullPath(path), path, StringComparison.Ordinal))
            {
                return false;
            }

            var root = _roots.GetBestRootFolder(path);
            if (root == null || root.IsCalibreLibrary || !Path.IsPathFullyQualified(root.Path))
            {
                return false;
            }

            var relative = Path.GetRelativePath(root.Path, path);
            return relative != "." && relative != ".." && !Path.IsPathRooted(relative) &&
                !relative.StartsWith("../", StringComparison.Ordinal);
        }

        private static SafeFileHandle OpenSource(string path)
        {
            for (var component = path; component != null; component = Path.GetDirectoryName(component))
            {
                if ((File.GetAttributes(component) & FileAttributes.ReparsePoint) != 0)
                {
                    return null;
                }
            }

            var descriptor = Open(path, NoFollow | NonBlocking | CloseOnExec);
            return descriptor < 0 ? null : new SafeFileHandle(new IntPtr(descriptor), ownsHandle: true);
        }

        private static bool TryGetVersion(SafeFileHandle handle, Candidate source, out DateTime modified)
        {
            modified = default;
            var fd = FormattableString.Invariant($"/proc/self/fd/{handle.DangerousGetHandle().ToInt64()}");
            if (File.ResolveLinkTarget(fd, returnFinalTarget: false)?.FullName != source.Path ||
                RandomAccess.GetLength(handle) != source.Size)
            {
                return false;
            }

            modified = File.GetLastWriteTimeUtc(handle);
            return Math.Abs(modified.Ticks - source.Modified.Ticks) < TimeSpan.TicksPerMillisecond;
        }

        private bool HasImage(string path) => _disk.FileExists(path) && _disk.GetFileSize(path) > 0;

        private string CacheDirectory(int bookId) => Path.Combine(_cacheRoot, bookId.ToString(CultureInfo.InvariantCulture));

        private string CachePath(int bookId, int fileId, string token) =>
            Path.Combine(CacheDirectory(bookId), FormattableString.Invariant($"{fileId}-{token}.jpg"));

        private static string Token(int bookId, Candidate source)
        {
            var value = FormattableString.Invariant($"{bookId}|{source.CanonicalId}|{source.EditionId}|{source.FileId}|{source.Size}|{source.Modified.Ticks}|{source.Format}|{source.Path}");
            return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
        }

        private static bool IsRecoverable(Exception error) =>
            error is IOException or UnauthorizedAccessException or ArgumentException or ImageFormatException or
                DbException or NotSupportedException or InvalidOperationException or DllNotFoundException or EntryPointNotFoundException;

        [DllImport("libc", EntryPoint = "open")]
        private static extern int Open([MarshalAs(UnmanagedType.LPUTF8Str)] string path, int flags);

        private sealed class Candidate
        {
            public string CanonicalId { get; set; }
            public int EditionId { get; set; }
            public string Format { get; set; }
            public bool IsEbook { get; set; }
            public List<MediaCover> Images { get; set; }
            public int FileId { get; set; }
            public string Path { get; set; }
            public long Size { get; set; }
            public DateTime Modified { get; set; }
        }
    }
}
