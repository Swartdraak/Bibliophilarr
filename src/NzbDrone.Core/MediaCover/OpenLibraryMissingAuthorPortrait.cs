using System;
using System.Linq;
using System.Text.RegularExpressions;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Books;

namespace NzbDrone.Core.MediaCover
{
    internal static class OpenLibraryMissingAuthorPortrait
    {
        private static readonly Regex AuthorToken = new Regex(@"\AOL[1-9][0-9]*A\z", RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private static readonly Regex PortraitUrl = new Regex(@"\Ahttps://covers\.openlibrary\.org/a/id/[1-9][0-9]{0,18}-L\.jpg(?:\?default=false)?\z", RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private static readonly Regex RetryFileSuffix = new Regex(@"\A\.ol-author-retry-[0-9a-f]{64}\.jpg\z", RegexOptions.Compiled | RegexOptions.CultureInvariant);

        public static bool IsValidPortraitUrl(string url) => url != null && PortraitUrl.IsMatch(url);

        public static bool IsValidRetryFileSuffix(string suffix) => suffix != null && RetryFileSuffix.IsMatch(suffix);

        public static bool IsRetryPortraitFile(string fileName) =>
            fileName?.StartsWith("poster", StringComparison.Ordinal) == true && IsValidRetryFileSuffix(fileName[6..]);

        public static (int MetadataId, string SourceId, string Url, string FileSuffix)? SelectArtwork(Author author)
        {
            var metadata = author?.Metadata?.Value;
            if (author is not { Id: > 0, AuthorMetadataId: > 0 } || metadata?.Id != author.AuthorMetadataId ||
                !Guid.TryParseExact(metadata.CanonicalId, "N", out var canonical) || canonical == Guid.Empty)
            {
                return null;
            }

            var primary = NormalizeSourceId(metadata.ForeignAuthorId);
            if (primary == null ||
                (!string.IsNullOrWhiteSpace(metadata.OpenLibraryAuthorId) && NormalizeSourceId(metadata.OpenLibraryAuthorId) != primary))
            {
                return null;
            }

            var posters = metadata.Images?.Where(image => image?.CoverType == MediaCoverTypes.Poster && !string.IsNullOrWhiteSpace(image.Url))
                .DistinctBy(image => (image.Url, image.SourceProvider, NormalizeSourceId(image.SourceId))).Take(2).ToList();
            if (posters?.Count != 1 || posters[0].SourceProvider != "OpenLibrary" ||
                NormalizeSourceId(posters[0].SourceId) != primary || !IsValidPortraitUrl(posters[0].Url))
            {
                return null;
            }

            var url = posters[0].Url;
            var fingerprint = FormattableString.Invariant($"{author.Id}\n{metadata.Id}\n{canonical:N}\n{primary}\n{url}").SHA256Hash();
            return (metadata.Id, primary, url, $".ol-author-retry-{fingerprint}.jpg");
        }

        public static bool HasVerifiedOwner(ICatalogueIdentityRepository identities, int metadataId, string sourceId)
        {
            var source = identities.GetSources(CatalogueEntityKind.Author, metadataId)?
                .Where(association => association.Provider == "OpenLibrary").ExclusiveOrDefault();
            return source != null && source.ProviderId == sourceId &&
                source.Evidence is CatalogueIdentityRepository.PrimaryProviderEvidence or CatalogueIdentityRepository.LegacyProviderEvidence;
        }

        public static string NormalizeSourceId(string sourceId)
        {
            var source = CatalogueIdentityRepository.NormalizeSource(CatalogueEntityKind.Author, "OpenLibrary", sourceId, null);
            return source != null && AuthorToken.IsMatch(source.ProviderId) ? source.ProviderId : null;
        }
    }
}
