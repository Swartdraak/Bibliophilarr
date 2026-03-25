using System;
using System.IO;
using System.Security.Cryptography;
using NLog;
using NzbDrone.Common.Disk;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Common.Extensions;

namespace NzbDrone.Update.UpdateEngine
{
    public interface IBackupAppData
    {
        void Backup();
        bool VerifyBackup();
    }

    public class BackupAppData : IBackupAppData
    {
        private readonly IAppFolderInfo _appFolderInfo;
        private readonly IDiskTransferService _diskTransferService;
        private readonly IDiskProvider _diskProvider;
        private readonly Logger _logger;

        public BackupAppData(IAppFolderInfo appFolderInfo,
                             IDiskProvider diskProvider,
                             IDiskTransferService diskTransferService,
                             Logger logger)
        {
            _appFolderInfo = appFolderInfo;
            _diskProvider = diskProvider;
            _diskTransferService = diskTransferService;
            _logger = logger;
        }

        public void Backup()
        {
            _logger.Info("Backing up appdata (database/config)");
            var backupFolderAppData = _appFolderInfo.GetUpdateBackUpAppDataFolder();

            if (_diskProvider.FolderExists(backupFolderAppData))
            {
                _diskProvider.EmptyFolder(backupFolderAppData);
            }
            else
            {
                _diskProvider.CreateFolder(backupFolderAppData);
            }

            try
            {
                var backupConfigFile = _appFolderInfo.GetUpdateBackupConfigFile();
                var backupDatabaseFile = _appFolderInfo.GetUpdateBackupDatabase();

                _diskTransferService.TransferFile(_appFolderInfo.GetConfigPath(), backupConfigFile, TransferMode.Copy);
                _diskTransferService.TransferFile(_appFolderInfo.GetDatabase(), backupDatabaseFile, TransferMode.Copy);

                WriteChecksumFile(backupConfigFile, backupFolderAppData);
                WriteChecksumFile(backupDatabaseFile, backupFolderAppData);

                _logger.Info("Backup completed with checksum verification files");
            }
            catch (Exception e)
            {
                _logger.Error(e, "Couldn't create a data backup");
            }
        }

        public bool VerifyBackup()
        {
            var backupFolderAppData = _appFolderInfo.GetUpdateBackUpAppDataFolder();
            var backupConfigFile = _appFolderInfo.GetUpdateBackupConfigFile();
            var backupDatabaseFile = _appFolderInfo.GetUpdateBackupDatabase();

            var configValid = VerifyChecksumFile(backupConfigFile, backupFolderAppData);
            var databaseValid = VerifyChecksumFile(backupDatabaseFile, backupFolderAppData);

            if (!configValid || !databaseValid)
            {
                _logger.Error("Backup verification failed — one or more files are corrupted");
                return false;
            }

            _logger.Info("Backup verification passed");
            return true;
        }

        private void WriteChecksumFile(string filePath, string backupFolder)
        {
            if (!_diskProvider.FileExists(filePath))
            {
                return;
            }

            var hash = ComputeSha256(filePath);
            var checksumPath = filePath + ".sha256";
            File.WriteAllText(checksumPath, hash);
            _logger.Debug("Wrote checksum for {0}: {1}", Path.GetFileName(filePath), hash);
        }

        private bool VerifyChecksumFile(string filePath, string backupFolder)
        {
            if (!_diskProvider.FileExists(filePath))
            {
                _logger.Warn("Backup file missing: {0}", filePath);
                return false;
            }

            var checksumPath = filePath + ".sha256";
            if (!_diskProvider.FileExists(checksumPath))
            {
                _logger.Warn("Checksum file missing for {0} — cannot verify integrity", filePath);
                return false;
            }

            var expectedHash = File.ReadAllText(checksumPath).Trim();
            var actualHash = ComputeSha256(filePath);

            if (!string.Equals(expectedHash, actualHash, StringComparison.OrdinalIgnoreCase))
            {
                _logger.Error("Checksum mismatch for {0}: expected {1}, got {2}", Path.GetFileName(filePath), expectedHash, actualHash);
                return false;
            }

            return true;
        }

        private static string ComputeSha256(string filePath)
        {
            using (var stream = File.OpenRead(filePath))
            using (var sha256 = SHA256.Create())
            {
                var hashBytes = sha256.ComputeHash(stream);
                return BitConverter.ToString(hashBytes).Replace("-", string.Empty).ToLowerInvariant();
            }
        }
    }
}
