using System;
using NzbDrone.Common.Extensions;

namespace NzbDrone.Core.MetadataSource.OpenLibrary
{
    public static class OpenLibraryIdNormalizer
    {
        public static string NormalizeWorkId(string value)
        {
            var token = NormalizeBookToken(value);

            return LooksLikeWorkOrEditionId(token) ? token : null;
        }

        public static string NormalizeBookToken(string value)
        {
            if (value.IsNullOrWhiteSpace())
            {
                return null;
            }

            const string workPrefix = "openlibrary:work:";
            const string editionPrefix = "openlibrary:edition:";

            var normalized = value.Trim();

            if (normalized.StartsWith(workPrefix, StringComparison.OrdinalIgnoreCase))
            {
                normalized = normalized.Substring(workPrefix.Length);
            }
            else if (normalized.StartsWith(editionPrefix, StringComparison.OrdinalIgnoreCase))
            {
                normalized = normalized.Substring(editionPrefix.Length);
            }

            var slash = normalized.LastIndexOf('/');
            if (slash >= 0)
            {
                normalized = normalized.Substring(slash + 1);
            }

            return normalized;
        }

        public static string NormalizeAuthorId(string value)
        {
            if (value.IsNullOrWhiteSpace())
            {
                return null;
            }

            const string authorPrefix = "openlibrary:author:";

            var normalized = value.Trim();

            if (normalized.StartsWith(authorPrefix, StringComparison.OrdinalIgnoreCase))
            {
                normalized = normalized.Substring(authorPrefix.Length);
            }

            return LooksLikeAuthorId(normalized)
                ? EnsureToken(normalized, "A")
                : null;
        }

        public static string EnsureToken(string value, string expectedSuffix)
        {
            if (value.IsNullOrWhiteSpace())
            {
                return value;
            }

            var normalized = value.Trim();

            if (normalized.StartsWith("openlibrary:", StringComparison.OrdinalIgnoreCase))
            {
                var lastColon = normalized.LastIndexOf(':');
                normalized = lastColon >= 0 ? normalized.Substring(lastColon + 1) : normalized;
            }

            var slash = normalized.LastIndexOf('/');
            if (slash >= 0)
            {
                normalized = normalized.Substring(slash + 1);
            }

            var token = normalized.StartsWith("OL", StringComparison.OrdinalIgnoreCase) ||
                        normalized.EndsWith(expectedSuffix, StringComparison.OrdinalIgnoreCase)
                ? normalized
                : $"OL{normalized}{expectedSuffix}";

            return token.ToUpperInvariant();
        }

        private static bool LooksLikeWorkOrEditionId(string value)
        {
            return value.IsNotNullOrWhiteSpace() &&
                   value.StartsWith("OL", StringComparison.OrdinalIgnoreCase) &&
                   (value.EndsWith("W", StringComparison.OrdinalIgnoreCase) ||
                    value.EndsWith("M", StringComparison.OrdinalIgnoreCase));
        }

        private static bool LooksLikeAuthorId(string value)
        {
            return value.IsNotNullOrWhiteSpace() &&
                   value.StartsWith("OL", StringComparison.OrdinalIgnoreCase) &&
                   value.EndsWith("A", StringComparison.OrdinalIgnoreCase);
        }
    }
}
