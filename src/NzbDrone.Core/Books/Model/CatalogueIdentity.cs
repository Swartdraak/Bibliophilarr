using System.Collections.Generic;

namespace NzbDrone.Core.Books
{
    public enum CatalogueEntityKind
    {
        Author,
        Work,
        Series
    }

    public sealed class CatalogueSourceAssociation
    {
        public string Provider { get; set; }
        public string ProviderId { get; set; }
        public string Evidence { get; set; }
    }

    public interface ICatalogueIdentityModel
    {
        string CanonicalId { get; set; }
        CatalogueEntityKind CatalogueKind { get; }
        string LegacyForeignId { get; }
        string LegacyTitleSlug { get; }
        List<CatalogueSourceAssociation> PendingSourceAssociations { get; }
    }
}
