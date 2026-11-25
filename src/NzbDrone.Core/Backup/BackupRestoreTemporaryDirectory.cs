using System;
using System.IO;
using NLog;
using NzbDrone.Common.Disk;

namespace NzbDrone.Core.Backup
{
    public sealed class BackupRestoreTemporaryDirectory : IDisposable
    {
        private readonly IDiskProvider _diskProvider;
        private readonly Logger _logger;

        public BackupRestoreTemporaryDirectory(IDiskProvider diskProvider, Logger logger)
        {
            _diskProvider = diskProvider;
            _logger = logger;
            Path = Directory.CreateTempSubdirectory("bibliophilarr_backup_restore_").FullName;
        }

        public string Path { get; }

        public void Dispose()
        {
            try
            {
                _diskProvider.DeleteFolder(Path, true);
            }
            catch (DirectoryNotFoundException)
            {
                // The temporary directory is already gone.
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _logger.Warn(ex, "Unable to clean up a temporary backup restore directory");
            }
        }
    }
}
