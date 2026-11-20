using System;
using System.Collections.Generic;
using System.Linq;
using NzbDrone.Core.MetadataSource.OpenLibrary;
using NzbDrone.Core.Parser;

namespace NzbDrone.Core.Books
{
    internal static class BookWorkIdentity
    {
        // The caller supplies candidates belonging to the same canonical author.
        public static Book FindUniqueMatch(Book incoming, IEnumerable<Book> candidates, out bool ambiguous)
        {
            var matches = candidates
                .Select(book => (Book: book, Strength: MatchStrength(incoming, book)))
                .Where(candidate => candidate.Strength > 0)
                .OrderByDescending(candidate => candidate.Strength)
                .Take(2)
                .ToList();

            ambiguous = matches.Count == 2 && matches[0].Strength == matches[1].Strength;
            return ambiguous ? null : matches.FirstOrDefault().Book;
        }

        public static bool AreEquivalent(Book left, Book right)
        {
            return MatchStrength(left, right) > 0;
        }

        public static bool AreCorroborated(Book left, Book right)
        {
            return left.AuthorMetadataId > 0 && left.AuthorMetadataId == right.AuthorMetadataId &&
                   AreCorroboratedForSameAuthor(left, right);
        }

        public static bool AreCorroboratedForSameAuthor(Book left, Book right)
        {
            var leftWorkId = GetOpenLibraryWorkId(left);
            var rightWorkId = GetOpenLibraryWorkId(right);
            return (leftWorkId == null || rightWorkId == null || leftWorkId == rightWorkId) && MatchStrength(left, right) >= 2;
        }

        private static int MatchStrength(Book left, Book right)
        {
            if (!string.IsNullOrWhiteSpace(left.ForeignBookId) &&
                string.Equals(left.ForeignBookId, right.ForeignBookId, StringComparison.Ordinal))
            {
                return 5;
            }

            var leftWorkId = GetOpenLibraryWorkId(left);
            var rightWorkId = GetOpenLibraryWorkId(right);
            if (leftWorkId != null && rightWorkId != null)
            {
                return leftWorkId == rightWorkId ? 4 : 0;
            }

            if (HasHardcoverWorkMapping(left, right) || HasHardcoverWorkMapping(right, left))
            {
                return 4;
            }

            var title = left.Title.CleanAuthorName();
            if (title.Length == 0 || (title != right.Title.CleanAuthorName() &&
                !HasNumberedSeriesSubtitle(left, right) && !HasNumberedSeriesSubtitle(right, left)))
            {
                return 0;
            }

            var leftEditions = left.Editions?.Value;
            var rightEditions = right.Editions?.Value;
            if (leftEditions != null && rightEditions != null &&
                (ShareIdentifier(leftEditions, rightEditions, edition => edition.Isbn13, 13) ||
                 ShareIdentifier(leftEditions, rightEditions, edition => edition.Asin, 10)))
            {
                return 3;
            }

            var leftSeries = GetNumberedSeries(left);
            var rightSeries = GetNumberedSeries(right);
            var sharedSeries = leftSeries.Join(
                rightSeries,
                item => item.SeriesId,
                item => item.SeriesId,
                (first, second) => first.Position == second.Position).ToList();
            if (sharedSeries.Count > 0)
            {
                return sharedSeries.All(matches => matches) ? 2 : 0;
            }

            // Matching titles and dates alone cannot establish work identity.
            return 0;
        }

        public static string GetOpenLibraryWorkId(Book book)
        {
            return NormalizeWorkId(book.OpenLibraryWorkId) ?? NormalizeWorkId(book.ForeignBookId);
        }

        private static bool HasHardcoverWorkMapping(Book owner, Book alias)
        {
            if (owner.PendingSourceAssociations.Count == 0)
            {
                return false;
            }

            var source = CatalogueIdentityRepository.ParseLegacySource(CatalogueEntityKind.Work, alias.ForeignBookId, null);
            return source?.Provider == "Hardcover" && owner.PendingSourceAssociations.Any(claim =>
                claim.Provider == source.Provider && claim.ProviderId == source.ProviderId &&
                claim.Evidence == CatalogueIdentityRepository.HardcoverWorkMappingEvidence);
        }

        private static string NormalizeWorkId(string value)
        {
            var token = OpenLibraryIdNormalizer.NormalizeWorkId(value);
            return token?.EndsWith("W", StringComparison.OrdinalIgnoreCase) == true ? token.ToUpperInvariant() : null;
        }

        private static bool ShareIdentifier(List<Edition> left, List<Edition> right, Func<Edition, string> select, int length)
        {
            var identifiers = left.Select(select).Where(value => value?.Length == length);
            return identifiers.Intersect(right.Select(select), StringComparer.OrdinalIgnoreCase).Any();
        }

        private static bool HasNumberedSeriesSubtitle(Book subtitleWork, Book plainWork)
        {
            var parts = subtitleWork.Title?.Split(':', 2);
            if (parts?.Length != 2 || parts[0].CleanAuthorName() != plainWork.Title.CleanAuthorName())
            {
                return false;
            }

            var suffix = parts[1].Trim();
            const string bookMarker = " Book ";
            const string volumeMarker = " Volume ";
            var marker = suffix.Contains(bookMarker, StringComparison.OrdinalIgnoreCase) ? bookMarker : volumeMarker;
            var markerAt = suffix.LastIndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (markerAt < 0)
            {
                return false;
            }

            var seriesName = NormalizeSeriesName(suffix[..markerAt]);
            var numberStart = markerAt + marker.Length;
            var position = suffix[numberStart..].Trim();
            return seriesName.Length > 0 && GetNumberedSeries(subtitleWork).Any(left =>
                left.Position == position && seriesName == NormalizeSeriesName(left.SeriesTitle) &&
                GetNumberedSeries(plainWork).Any(right => left.SeriesId == right.SeriesId && left.Position == right.Position));
        }

        private static string NormalizeSeriesName(string value)
        {
            var title = value?.Trim(' ', ',', '-') ?? string.Empty;
            if (title.StartsWith("The ", StringComparison.OrdinalIgnoreCase))
            {
                title = title[4..];
            }

            if (title.EndsWith(" Series", StringComparison.OrdinalIgnoreCase))
            {
                title = title[..^7];
            }

            return title.CleanAuthorName();
        }

        private static IEnumerable<(string SeriesId, string Position, string SeriesTitle)> GetNumberedSeries(Book book)
        {
            return (book.SeriesLinks?.Value ?? new List<SeriesBookLink>())
                .Where(link => link.IsPrimary && !string.IsNullOrWhiteSpace(link.Position))
                .Select(link => (Series: link.Series?.Value, link.Position))
                .Select(link => (SeriesId: link.Series?.ForeignSeriesId, link.Position, SeriesTitle: link.Series?.Title))
                .Where(link => !string.IsNullOrWhiteSpace(link.SeriesId));
        }
    }
}
