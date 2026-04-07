using NzbDrone.Core.Books;

namespace NzbDrone.Core.Download
{
    public interface IFormatCategorySettings
    {
        string EbookCategory { get; set; }
        string AudiobookCategory { get; set; }
    }

    public static class FormatCategorySettingsExtensions
    {
        public static string GetCategoryForFormat(this IFormatCategorySettings settings, string defaultCategory, FormatType? formatType)
        {
            if (formatType == null)
            {
                return defaultCategory;
            }

            var formatCategory = formatType == FormatType.Ebook
                ? settings.EbookCategory
                : settings.AudiobookCategory;

            return string.IsNullOrWhiteSpace(formatCategory) ? defaultCategory : formatCategory;
        }

        /// <summary>
        /// Returns true if the given category matches any of the configured categories
        /// (default, ebook, or audiobook).
        /// </summary>
        public static bool MatchesAnyCategory(this IFormatCategorySettings settings, string defaultCategory, string itemCategory)
        {
            if (itemCategory == defaultCategory)
            {
                return true;
            }

            if (!string.IsNullOrWhiteSpace(settings.EbookCategory) && itemCategory == settings.EbookCategory)
            {
                return true;
            }

            if (!string.IsNullOrWhiteSpace(settings.AudiobookCategory) && itemCategory == settings.AudiobookCategory)
            {
                return true;
            }

            return false;
        }
    }
}
