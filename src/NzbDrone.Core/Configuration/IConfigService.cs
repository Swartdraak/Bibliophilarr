using System.Collections.Generic;
using NzbDrone.Common.Http.Proxy;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.Security;

namespace NzbDrone.Core.Configuration
{
    public interface IConfigService
    {
        void SaveConfigDictionary(Dictionary<string, object> configValues);

        bool IsDefined(string key);

        //Download Client
        string DownloadClientWorkingFolders { get; set; }
        int DownloadClientHistoryLimit { get; set; }

        //Completed/Failed Download Handling (Download client)
        bool EnableCompletedDownloadHandling { get; set; }
        bool AutoRedownloadFailed { get; set; }
        bool AutoRedownloadFailedFromInteractiveSearch { get; set; }

        //Media Management
        bool AutoUnmonitorPreviouslyDownloadedBooks { get; set; }
        string RecycleBin { get; set; }
        int RecycleBinCleanupDays { get; set; }
        ProperDownloadTypes DownloadPropersAndRepacks { get; set; }
        bool CreateEmptyAuthorFolders { get; set; }
        bool DeleteEmptyFolders { get; set; }
        FileDateType FileDate { get; set; }
        bool SkipFreeSpaceCheckWhenImporting { get; set; }
        int MinimumFreeSpaceWhenImporting { get; set; }
        bool CopyUsingHardlinks { get; set; }
        bool ImportExtraFiles { get; set; }
        string ExtraFileExtensions { get; set; }
        bool WatchLibraryForChanges { get; set; }
        RescanAfterRefreshType RescanAfterRefresh { get; set; }
        AllowFingerprinting AllowFingerprinting { get; set; }

        //Permissions (Media Management)
        bool SetPermissionsLinux { get; set; }
        string ChmodFolder { get; set; }
        string ChownGroup { get; set; }

        //Indexers
        int Retention { get; set; }
        int RssSyncInterval { get; set; }
        int MaximumSize { get; set; }
        int MinimumAge { get; set; }

        //UI
        int FirstDayOfWeek { get; set; }
        string CalendarWeekColumnHeader { get; set; }

        string ShortDateFormat { get; set; }
        string LongDateFormat { get; set; }
        string TimeFormat { get; set; }
        bool ShowRelativeDates { get; set; }
        bool EnableColorImpairedMode { get; set; }
        int UILanguage { get; set; }

        //Internal
        bool CleanupMetadataImages { get; set; }

        string PlexClientIdentifier { get; }

        //Metadata
        string MetadataSource { get; set; }
        WriteAudioTagsType WriteAudioTags { get; set; }
        bool ScrubAudioTags { get; set; }
        WriteBookTagsType WriteBookTags { get; set; }
        bool UpdateCovers { get; set; }
        bool EmbedMetadata { get; set; }
        bool EnableInventaireFallback { get; set; }
        bool EnableGoogleBooksFallback { get; set; }
        string GoogleBooksApiKey { get; set; }
        bool EnableHardcoverFallback { get; set; }
        string HardcoverApiToken { get; set; }
        int HardcoverRequestTimeoutSeconds { get; set; }
        int IsbnContextFallbackLimit { get; set; }
        bool EnableMetadataConflictStrategyVariants { get; set; }
        string MetadataAuthorAliases { get; set; }
        string MetadataTitleStripPatterns { get; set; }

        //Forms Auth
        string RijndaelPassphrase { get; }
        string HmacPassphrase { get; }
        string RijndaelSalt { get; }
        string HmacSalt { get; }

        //Proxy
        bool ProxyEnabled { get; }
        ProxyType ProxyType { get; }
        string ProxyHostname { get; }
        int ProxyPort { get; }
        string ProxyUsername { get; }
        string ProxyPassword { get; }
        string ProxyBypassFilter { get; }
        bool ProxyBypassLocalAddresses { get; }

        // Backups
        string BackupFolder { get; }
        int BackupInterval { get; }
        int BackupRetention { get; }

        CertificateValidationType CertificateValidation { get; }
        string ApplicationUrl { get; }

        // Metadata Providers
        bool EnableOpenLibraryProvider { get; set; }
        bool EnableGoogleBooksProvider { get; set; }
        bool EnableInventaireProvider { get; set; }
        string MetadataProviderPriorityOrder { get; set; }
        int MetadataProviderTimeoutSeconds { get; set; }
        int MetadataProviderRetryBudget { get; set; }
        int MetadataProviderCircuitBreakerThreshold { get; set; }
        int MetadataProviderCircuitBreakerDurationSeconds { get; set; }
        int OpenLibrarySearchTimeoutSeconds { get; set; }
        int OpenLibraryIsbnTimeoutSeconds { get; set; }
        int OpenLibraryWorkTimeoutSeconds { get; set; }
        int OpenLibrarySearchRetryBudget { get; set; }
        int OpenLibraryIsbnRetryBudget { get; set; }
        int OpenLibraryWorkRetryBudget { get; set; }
    }
}
