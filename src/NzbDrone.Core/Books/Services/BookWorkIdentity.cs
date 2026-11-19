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

            var title = left.Title.CleanAuthorName();
            if (title.Length == 0 || title != right.Title.CleanAuthorName())
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

            // January 1 is frequently a provider's placeholder for year-only data.
            // A title match plus a placeholder date is not work identity.
            var date = left.ReleaseDate?.Date;
            return date.HasValue && (date.Value.Month != 1 || date.Value.Day != 1) && date == right.ReleaseDate?.Date ? 1 : 0;
        }

        public static string GetOpenLibraryWorkId(Book book)
        {
            return NormalizeWorkId(book.OpenLibraryWorkId) ?? NormalizeWorkId(book.ForeignBookId);
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

        private static IEnumerable<(string SeriesId, string Position)> GetNumberedSeries(Book book)
        {
            return (book.SeriesLinks?.Value ?? new List<SeriesBookLink>())
                .Where(link => link.IsPrimary && !string.IsNullOrWhiteSpace(link.Position))
                .Select(link => (SeriesId: link.Series?.Value?.ForeignSeriesId, Position: link.Position))
                .Where(link => !string.IsNullOrWhiteSpace(link.SeriesId));
        }
    }
}
