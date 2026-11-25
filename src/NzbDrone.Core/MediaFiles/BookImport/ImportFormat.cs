using System.IO;
using NzbDrone.Core.Books;
using NzbDrone.Core.Qualities;

namespace NzbDrone.Core.MediaFiles.BookImport
{
    public static class ImportFormat
    {
        internal static FormatType? ForPath(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return null;
            }

            var quality = MediaFileExtensions.GetQualityForExtension(Path.GetExtension(path));
            return quality == Quality.Unknown ? null : Quality.GetFormatType(quality);
        }

        public static bool IsCompatible(string path, Edition edition)
        {
            var fileFormat = ForPath(path);
            var editionFormat = ForEdition(edition);
            return !fileFormat.HasValue || !editionFormat.HasValue || fileFormat == editionFormat;
        }

        public static FormatType? ForEdition(Edition edition, bool requireExplicitFormat = false)
        {
            if (edition == null)
            {
                return null;
            }

            var label = edition.Format?.Trim().TrimStart('.').ToLowerInvariant();
            var format = label switch
            {
                "audio" or "audiobook" or "audio book" or "audio cd" or "audio cassette" or "audible audio" or "mp3 cd" => FormatType.Audiobook,
                "ebook" or "e-book" or "electronic book" or "kindle edition" or "nook" => FormatType.Ebook,
                null or "" => null,
                _ => ForPath("." + label)
            };

            // A missing or generic label with IsEbook=false does not establish audio.
            return format ?? (!requireExplicitFormat && edition.IsEbook ? FormatType.Ebook : null);
        }
    }
}
