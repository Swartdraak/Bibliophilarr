using System;
using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;
using NLog;
using NzbDrone.Common.Disk;
using NzbDrone.Common.Exceptions;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Instrumentation;

namespace NzbDrone.Common.EnvironmentInfo
{
    public interface IAppFolderFactory
    {
        void Register();
    }

    public class AppFolderFactory : IAppFolderFactory
    {
        private readonly IAppFolderInfo _appFolderInfo;
        private readonly IDiskProvider _diskProvider;
        private readonly IStartupContext _startupContext;
        private readonly Logger _logger;

        public AppFolderFactory(IAppFolderInfo appFolderInfo,
                                IStartupContext startupContext,
                                IDiskProvider diskProvider)
        {
            _appFolderInfo = appFolderInfo;
            _diskProvider = diskProvider;
            _startupContext = startupContext;
            _logger = NzbDroneLogger.GetLogger(this);
        }

        public void Register()
        {
            try
            {
                if (OsInfo.IsWindows && !_startupContext.Args.ContainsKey(StartupContext.APPDATA))
                {
                    _diskProvider.EnsurePrivateFolder(_appFolderInfo.AppDataFolder);
                }
                else
                {
                    _diskProvider.EnsureFolder(_appFolderInfo.AppDataFolder);
                }
            }
            catch (UnauthorizedAccessException)
            {
                throw new BibliophilarrStartupException("Cannot create AppFolder, Access to the path {0} is denied", _appFolderInfo.AppDataFolder);
            }

            if (!_diskProvider.FolderWritable(_appFolderInfo.AppDataFolder))
            {
                throw new BibliophilarrStartupException("AppFolder {0} is not writable", _appFolderInfo.AppDataFolder);
            }

            InitializeMonoApplicationData();
        }

        public static void EnsurePrivateWindowsFolder(string path)
        {
            if (!OperatingSystem.IsWindows())
            {
                return;
            }

            var directory = new DirectoryInfo(path);
            if (directory.Exists)
            {
                return;
            }

            using var identity = WindowsIdentity.GetCurrent(TokenAccessLevels.Query);
            var userSid = identity.User ?? throw new UnauthorizedAccessException("Cannot identify the Windows account for the data directory.");
            var systemSid = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
            var administratorsSid = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
            const InheritanceFlags inheritance = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;

            var security = new DirectorySecurity();
            security.SetAccessRuleProtection(true, false);
            security.AddAccessRule(new FileSystemAccessRule(userSid, FileSystemRights.Modify, inheritance, PropagationFlags.None, AccessControlType.Allow));
            security.AddAccessRule(new FileSystemAccessRule(systemSid, FileSystemRights.FullControl, inheritance, PropagationFlags.None, AccessControlType.Allow));
            security.AddAccessRule(new FileSystemAccessRule(administratorsSid, FileSystemRights.FullControl, inheritance, PropagationFlags.None, AccessControlType.Allow));

            // Apply the DACL at creation, before the app writes configuration or credentials.
            directory.Create(security);

            if (!directory.GetAccessControl(AccessControlSections.Access).AreAccessRulesProtected)
            {
                throw new UnauthorizedAccessException($"Could not create the Windows data directory with private permissions: {path}");
            }
        }

        private void InitializeMonoApplicationData()
        {
            if (OsInfo.IsWindows)
            {
                return;
            }

            try
            {
                // It seems that DoNotVerify is the mono behaviour even though .net docs specify a blank string
                // should be returned if the data doesn't exist.  For compatibility with .net core, explicitly
                // set DoNotVerify (which makes sense given we're explicitly checking that the folder exists)
                var configHome = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData, Environment.SpecialFolderOption.DoNotVerify);
                if (configHome.IsNullOrWhiteSpace() ||
                    configHome == "/.config" ||
                    (configHome.EndsWith("/.config") && !_diskProvider.FolderExists(configHome.GetParentPath())) ||
                    !_diskProvider.FolderExists(configHome))
                {
                    // Tell mono/netcore to use appData/.config as ApplicationData folder.
                    Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", Path.Combine(_appFolderInfo.AppDataFolder, ".config"));
                }

                var dataHome = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.DoNotVerify);
                if (dataHome.IsNullOrWhiteSpace() ||
                    dataHome == "/.local/share" ||
                    (dataHome.EndsWith("/.local/share") && !_diskProvider.FolderExists(dataHome.GetParentPath().GetParentPath())) ||
                    !_diskProvider.FolderExists(dataHome))
                {
                    // Tell mono/netcore to use appData/.config/share as LocalApplicationData folder.
                    Environment.SetEnvironmentVariable("XDG_DATA_HOME", Path.Combine(_appFolderInfo.AppDataFolder, ".config/share"));
                }
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Failed to initialize the mono config directory.");
            }
        }
    }
}
