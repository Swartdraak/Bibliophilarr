using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Processes;
using NzbDrone.Common.Serializer;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.MediaFiles
{
    public interface IEmbeddedAudioTagFallbackReader
    {
        EmbeddedAudioTagFallbackResult ReadTags(string path);
    }

    public class EmbeddedAudioTagFallbackResult
    {
        public ParsedTrackInfo TrackInfo { get; set; }
        public string FormatHint { get; set; }
        public double BookTitleConfidence { get; set; }
        public double AuthorConfidence { get; set; }
    }

    public class EmbeddedAudioTagFallbackReader : IEmbeddedAudioTagFallbackReader
    {
        private static readonly Regex StructuralTitleRegex = new Regex(@"^(?:bundle|world|set|disc|cd|part|track|chapter|volume)\b(?:[\s._-]*(?:\d+|[ivxlcdm]+))?$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private readonly IProcessProvider _processProvider;
        private readonly Logger _logger;

        public EmbeddedAudioTagFallbackReader(IProcessProvider processProvider, Logger logger)
        {
            _processProvider = processProvider;
            _logger = logger;
        }

        public EmbeddedAudioTagFallbackResult ReadTags(string path)
        {
            var args = "-v error -show_entries format_tags=title,artist,album_artist,album -of json \"" + path + "\"";
            var output = _processProvider.StartAndCapture("ffprobe", args);

            if (output.ExitCode != 0)
            {
                _logger.Trace("ffprobe fallback was not available for {0}", path);
                return null;
            }

            var payload = output.Standard.Select(x => x.Content)
                .Where(x => x.IsNotNullOrWhiteSpace())
                .ConcatToString("\n");

            if (payload.IsNullOrWhiteSpace())
            {
                return null;
            }

            try
            {
                var parsed = Json.Deserialize<FfprobeFormatResponse>(payload);
                var tags = parsed?.Format?.Tags;
                if (tags == null)
                {
                    return null;
                }

                var authors = SplitAuthors(tags.GetValueOrDefault("album_artist") ?? tags.GetValueOrDefault("artist"));
                var title = tags.GetValueOrDefault("title");
                var bookTitle = tags.GetValueOrDefault("album") ?? title;

                if (bookTitle.IsNullOrWhiteSpace() && !authors.Any())
                {
                    return null;
                }

                var formatHint = GetFormatHint(path);
                var bookTitleConfidence = formatHint.BookTitleConfidence;
                var authorConfidence = formatHint.AuthorConfidence;

                if (LooksStructural(title) || LooksStructural(bookTitle))
                {
                    bookTitleConfidence *= 0.25;
                }

                if (!authors.Any())
                {
                    authorConfidence = 0.0;
                }

                return new EmbeddedAudioTagFallbackResult
                {
                    FormatHint = formatHint.Name,
                    BookTitleConfidence = bookTitleConfidence,
                    AuthorConfidence = authorConfidence,
                    TrackInfo = new ParsedTrackInfo
                    {
                        Title = title,
                        BookTitle = bookTitle,
                        Authors = authors,
                        TrackNumbers = new int[0],
                        IdentitySource = formatHint.Name,
                        BookTitleConfidence = bookTitleConfidence,
                        AuthorConfidence = authorConfidence
                    }
                };
            }
            catch (System.Exception e)
            {
                _logger.Trace(e, "Unable to parse ffprobe tag payload for {0}", path);
                return null;
            }
        }

        private static List<string> SplitAuthors(string raw)
        {
            if (raw.IsNullOrWhiteSpace())
            {
                return new List<string>();
            }

            var normalized = raw.Replace(" and ", ";").Replace(" & ", ";").Replace("/", ";");
            return normalized.Split(';')
                .Select(x => x.Trim())
                .Where(x => x.IsNotNullOrWhiteSpace())
                .Distinct()
                .ToList();
        }

        private static bool LooksStructural(string value)
        {
            return value.IsNotNullOrWhiteSpace() && StructuralTitleRegex.IsMatch(value.Trim());
        }

        private static EmbeddedFormatHint GetFormatHint(string path)
        {
            var extension = System.IO.Path.GetExtension(path)?.ToLowerInvariant();

            switch (extension)
            {
                case ".m4b":
                case ".m4a":
                case ".aac":
                case ".aa":
                case ".aax":
                    return new EmbeddedFormatHint("ffprobe:aac-family", 0.92, 0.88);
                case ".flac":
                case ".opus":
                case ".ogg":
                    return new EmbeddedFormatHint("ffprobe:container-tags", 0.72, 0.7);
                case ".mp3":
                case ".mp2":
                case ".wma":
                case ".ape":
                    return new EmbeddedFormatHint("ffprobe:legacy-tags", 0.48, 0.6);
                default:
                    return new EmbeddedFormatHint("ffprobe:generic", 0.55, 0.6);
            }
        }

        private class EmbeddedFormatHint
        {
            public EmbeddedFormatHint(string name, double bookTitleConfidence, double authorConfidence)
            {
                Name = name;
                BookTitleConfidence = bookTitleConfidence;
                AuthorConfidence = authorConfidence;
            }

            public string Name { get; }
            public double BookTitleConfidence { get; }
            public double AuthorConfidence { get; }
        }

        private class FfprobeFormatResponse
        {
            public FfprobeFormatResource Format { get; set; }
        }

        private class FfprobeFormatResource
        {
            public Dictionary<string, string> Tags { get; set; }
        }
    }
}
