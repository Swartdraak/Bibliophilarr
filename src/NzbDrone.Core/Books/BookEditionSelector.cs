using System;
using System.Collections.Generic;
using System.Linq;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.MediaCover;

namespace NzbDrone.Core.Books
{
    public static class BookEditionSelector
    {
        public static Edition GetPreferredEdition(this Book book)
        {
            return GetPreferredEdition(book?.Editions?.Value);
        }

        public static Edition GetPreferredEdition(this IEnumerable<Edition> editions)
        {
            if (editions == null)
            {
                return null;
            }

            var editionList = editions as IList<Edition> ?? editions.ToList();
            return editionList.FirstOrDefault(x => x.Monitored) ?? editionList.FirstOrDefault();
        }

        public static List<MediaCover.MediaCover> GetBookImages(this IList<Edition> editions)
        {
            var preferred = editions.GetPreferredEdition();
            var images = preferred?.Images?.Where(image => image != null && image.Url.IsNotNullOrWhiteSpace())
                         .DistinctBy(image => (image.CoverType, image.Url)).ToList()
                         ?? new List<MediaCover.MediaCover>();

            if (editions == null)
            {
                return images;
            }

            var coverUrls = images.Where(image => image.CoverType == MediaCoverTypes.Cover)
                .Select(image => image.Url)
                .ToHashSet(StringComparer.Ordinal);
            const int maxCoverCandidates = 3;
            var fallback = editions
                .Where(edition => edition != preferred && edition?.Images != null)
                .SelectMany(edition => edition.Images)
                .Where(image => image?.CoverType == MediaCoverTypes.Cover &&
                    Uri.TryCreate(image.Url, UriKind.Absolute, out var url) && url.Scheme is "http" or "https" &&
                    coverUrls.Add(image.Url))
                .Take(maxCoverCandidates - coverUrls.Count);
            images.AddRange(fallback);

            return images;
        }

        public static string GetBookOverview(this IList<Edition> editions)
        {
            var preferredOverview = editions.GetPreferredEdition()?.Overview;
            return preferredOverview.IsNotNullOrWhiteSpace()
                ? preferredOverview
                : editions?.Select(edition => edition?.Overview).FirstOrDefault(overview => overview.IsNotNullOrWhiteSpace());
        }

        public static Edition GetPreferredEdition(this Book book, FormatType formatType)
        {
            return GetPreferredEdition(book?.Editions?.Value, formatType);
        }

        public static Edition GetPreferredEdition(this IEnumerable<Edition> editions, FormatType formatType)
        {
            if (editions == null)
            {
                return null;
            }

            var formatEditions = editions.Where(e => e.MatchesFormat(formatType)).ToList();

            if (!formatEditions.Any())
            {
                return null;
            }

            return formatEditions.FirstOrDefault(x => x.Monitored) ?? formatEditions.FirstOrDefault();
        }
    }
}
