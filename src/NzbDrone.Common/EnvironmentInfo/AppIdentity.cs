using System;

namespace NzbDrone.Common.EnvironmentInfo
{
    public static class AppIdentity
    {
        public static string InternalName { get; } =
            string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("APP_INTERNAL_NAME"))
                ? "Readarr"
                : Environment.GetEnvironmentVariable("APP_INTERNAL_NAME");

        public static string DisplayName { get; } =
            string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("APP_DISPLAY_NAME"))
                ? "Bibliophilarr"
                : Environment.GetEnvironmentVariable("APP_DISPLAY_NAME");

        public static string ServiceName { get; } =
            string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("APP_SERVICE_NAME"))
                ? InternalName
                : Environment.GetEnvironmentVariable("APP_SERVICE_NAME");

        public static string ConsoleProcessName { get; } = $"{InternalName}.Console";
        public static string UpdateProcessName { get; } = $"{InternalName}.Update";

        public static string UpdateSandboxFolderName { get; } = $"{InternalName.ToLowerInvariant()}_update";
        public static string UpdatePackageFolderName { get; } = InternalName;
        public static string UpdateBackupFolderName { get; } = $"{InternalName.ToLowerInvariant()}_backup";
        public static string UpdateBackupAppDataFolderName { get; } = $"{InternalName.ToLowerInvariant()}_appdata_backup";
    }
}
