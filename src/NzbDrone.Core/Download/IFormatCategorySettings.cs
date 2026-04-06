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
    }
}
