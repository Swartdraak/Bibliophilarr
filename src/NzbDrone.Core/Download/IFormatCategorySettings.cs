using System.Collections.Generic;
using System.Linq;
using NzbDrone.Core.Books;

namespace NzbDrone.Core.Download
{
    public interface IFormatCategorySettings
    {
        string EbookCategory { get; set; }
        string AudiobookCategory { get; set; }
        string EbookImportedCategory { get; set; }
        string AudiobookImportedCategory { get; set; }
    }

    public static class FormatCategorySettingsExtensions
    {
        public static string GetCategoryForFormat(this IFormatCategorySettings settings, FormatType? formatType)
        {
            if (formatType == FormatType.Audiobook)
            {
                return settings.AudiobookCategory;
            }

            return settings.EbookCategory;
        }

        /// <summary>
        /// Returns all non-empty configured categories (ebook, audiobook, and their imported variants).
        /// </summary>
        public static IEnumerable<string> GetAllCategories(this IFormatCategorySettings settings)
        {
            if (!string.IsNullOrWhiteSpace(settings.EbookCategory))
            {
                yield return settings.EbookCategory;
            }

            if (!string.IsNullOrWhiteSpace(settings.AudiobookCategory))
            {
                yield return settings.AudiobookCategory;
            }

            if (!string.IsNullOrWhiteSpace(settings.EbookImportedCategory))
            {
                yield return settings.EbookImportedCategory;
            }

            if (!string.IsNullOrWhiteSpace(settings.AudiobookImportedCategory))
            {
                yield return settings.AudiobookImportedCategory;
            }
        }

        /// <summary>
        /// Returns true if any non-empty imported category is configured.
        /// </summary>
        public static bool HasImportedCategory(this IFormatCategorySettings settings)
        {
            return !string.IsNullOrWhiteSpace(settings.EbookImportedCategory) ||
                   !string.IsNullOrWhiteSpace(settings.AudiobookImportedCategory);
        }

        /// <summary>
        /// Returns true if the given category matches any of the configured categories
        /// (ebook, audiobook, or their imported variants).
        /// </summary>
        public static bool MatchesAnyCategory(this IFormatCategorySettings settings, string itemCategory)
        {
            return settings.GetAllCategories().Contains(itemCategory);
        }
    }
}
