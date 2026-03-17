namespace NzbDrone.Core.MetadataSource
{
    /// <summary>
    /// Base capability contract for all metadata providers.
    /// Providers implement this alongside the specific lookup interfaces
    /// (IProvideAuthorInfo, IProvideBookInfo, ISearchForNewBook, etc.).
    /// Lower Priority = higher preference; 1 = primary, 2 = secondary fallback.
    /// </summary>
    public interface IMetadataProvider
    {
        string ProviderName { get; }
        int Priority { get; }
        bool IsEnabled { get; }

        bool SupportsAuthorSearch { get; }
        bool SupportsBookSearch { get; }
        bool SupportsIsbnLookup { get; }
        bool SupportsSeriesInfo { get; }
        bool SupportsCoverImages { get; }
    }
}
