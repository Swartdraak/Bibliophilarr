namespace NzbDrone.Core.DecisionEngine
{
    public enum LibraryImportHoldReason
    {
        ReviewRequired = 1,
        NoAcceptedWork = 2,
        LowConfidence = 3,
        ExistingFilePreferred = 4,
        SameSizeAsExisting = 5,
        CompetingEditions = 6,
        ReadError = 7,
        DuplicateInScan = 8,
        ConflictingIdentity = 9,
        ImportFailed = 10,
        ProviderIdentityConflict = 11,
        UnverifiedEditionIdentifier = 12
    }
}
