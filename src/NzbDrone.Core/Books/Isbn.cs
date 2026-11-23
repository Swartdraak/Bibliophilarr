using System;
using System.Linq;

namespace NzbDrone.Core.Books
{
    internal static class Isbn
    {
        public static bool AreEquivalent(string first, string second)
        {
            var canonical = AsIsbn13(first);
            return canonical != null && canonical == AsIsbn13(second);
        }

        public static string AsIsbn13(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            var isbn = new string(value.Where(character => character != '-' && !char.IsWhiteSpace(character))
                .ToArray()).ToUpperInvariant();

            if ((isbn.StartsWith("978", StringComparison.Ordinal) || isbn.StartsWith("979", StringComparison.Ordinal)) &&
                IsValid13(isbn))
            {
                return isbn;
            }

            if (!IsValid10(isbn))
            {
                return null;
            }

            // ISBN-10 has a corresponding ISBN-13 only under the 978 prefix.
            var body = "978" + isbn.Substring(0, 9);
            return body + Isbn13Checksum(body);
        }

        private static char Isbn10Checksum(string isbn)
        {
            var sum = 0;
            for (var i = 0; i < 9; i++)
            {
                sum += (isbn[i] - '0') * (10 - i);
            }

            var result = sum % 11;

            if (result == 0)
            {
                return '0';
            }
            else if (result == 1)
            {
                return 'X';
            }

            return (char)('0' + 11 - result);
        }

        private static char Isbn13Checksum(string isbn)
        {
            var result = 0;
            for (var i = 0; i < 12; i++)
            {
                result += (isbn[i] - '0') * ((i % 2 == 0) ? 1 : 3);
            }

            result %= 10;

            return result == 0 ? '0' : (char)('0' + 10 - result);
        }

        public static bool IsValid10(string isbn)
        {
            return isbn.Length == 10 && isbn.Take(9).All(char.IsAsciiDigit) && isbn[9] == Isbn10Checksum(isbn);
        }

        public static bool IsValid13(string isbn)
        {
            return isbn.Length == 13 && isbn.All(char.IsAsciiDigit) && isbn[12] == Isbn13Checksum(isbn);
        }
    }
}
