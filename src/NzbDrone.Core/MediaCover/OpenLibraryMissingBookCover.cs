using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using NzbDrone.Core.Books;
using NzbDrone.Core.MetadataSource.OpenLibrary;

namespace NzbDrone.Core.MediaCover
{
    internal static class OpenLibraryMissingBookCover
    {
        private static readonly Regex WorkId = new Regex(@"\Aopenlibrary:work:OL[1-9][0-9]*W\z", RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private static readonly Regex EditionId = new Regex(@"\Aopenlibrary:edition:OL[1-9][0-9]*M\z", RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private static readonly Regex RetryFileSuffix = new Regex(@"\A\.ol-retry-[0-9a-f]{64}\.jpg\z", RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private static readonly Regex CoverUrl = new Regex(@"\Ahttps://covers\.openlibrary\.org/b/id/[1-9][0-9]{0,18}-L\.jpg(?:\?default=false)?\z", RegexOptions.Compiled | RegexOptions.CultureInvariant);

        public static bool IsValidRetryFileSuffix(string suffix) => suffix != null && RetryFileSuffix.IsMatch(suffix);

        public static bool IsValidCoverUrl(string url) => url != null && CoverUrl.IsMatch(url);

        public static MediaCover GetSingleCover(IEnumerable<MediaCover> images)
        {
            var covers = images?.Where(image => image?.CoverType == MediaCoverTypes.Cover && !string.IsNullOrWhiteSpace(image.Url))
                .DistinctBy(image => image.Url).Take(2).ToList();
            return covers?.Count == 1 ? covers[0] : null;
        }

        public static Edition SelectUnambiguousEdition(IList<Edition> editions)
        {
            if (editions == null)
            {
                return null;
            }

            // Database result order is not part of the preferred edition contract.
            var monitored = editions.Where(edition => edition.Monitored).Take(2).ToList();
            return monitored.Count == 1 ? monitored[0] : editions.Count == 1 ? editions[0] : null;
        }

        public static (int EditionId, string CoverUrl, string FileSuffix)? SelectArtwork(Book book, Edition selected)
        {
            if (selected is not { Id: > 0 } || selected.BookId != book.Id)
            {
                return null;
            }

            if (!string.IsNullOrWhiteSpace(book.OpenLibraryWorkId) &&
                book.ForeignBookId != $"openlibrary:work:{OpenLibraryIdNormalizer.NormalizeWorkId(book.OpenLibraryWorkId)}")
            {
                return null;
            }

            var cover = GetSingleCover(selected.Images);
            var suffix = GetRetryFileSuffix(book.ForeignBookId, selected.ForeignEditionId, cover?.Url);
            return suffix == null ? null : (selected.Id, cover.Url, suffix);
        }

        public static string GetRetryFileSuffix(string workId, string editionId, string coverUrl)
        {
            const string workPrefix = "openlibrary:work:";
            if (workId == null || editionId == null || coverUrl == null ||
                !WorkId.IsMatch(workId) || !IsValidCoverUrl(coverUrl))
            {
                return null;
            }

            if (editionId != workId && editionId != $"OL-work-{workId[workPrefix.Length..]}" && !EditionId.IsMatch(editionId))
            {
                return null;
            }

            var fingerprint = $"{workId}\n{editionId}\n{coverUrl}".SHA256Hash();
            return $".ol-retry-{fingerprint}.jpg";
        }
    }
}
