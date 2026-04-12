using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Instrumentation;
using NzbDrone.Core.Books;
using NzbDrone.Core.Books.Calibre;
using NzbDrone.Core.Parser;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.MediaFiles.BookImport.Identification
{
    public static class DistanceCalculator
    {
        private static readonly Logger Logger = NzbDroneLogger.GetLogger(typeof(DistanceCalculator));

        public static readonly List<string> VariousAuthorIds = new List<string> { "89ad4ac3-39f7-470e-963a-56509c546377" };

        private static readonly RegexReplace StripSeriesRegex = new RegexReplace(@"\([^\)].+?\)$", string.Empty, RegexOptions.Compiled);

        private static readonly RegexReplace CleanTitleCruft = new RegexReplace(@"\((?:unabridged)\)", string.Empty, RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex VolumeNumberRegex = new Regex(
            @"(?:vol(?:ume)?|book|part|b|v|#)[\s._-]*(\d+)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // Matches collection / box-set title patterns so the identification
        // service can penalise these editions when the file title is clearly
        // for a standalone work (prevents "Lights Out" matching a collection
        // set that embeds the title in a longer string).
        private static readonly Regex CollectionSetRegex = new Regex(
            @"\b(?:\d+[\s-]*books?\s+collection|collection\s+set|box\s*set|boxed\s+set|omnibus\s+edition)\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly List<string> EbookFormats = new List<string> { "Kindle Edition", "Nook", "ebook" };

        private static readonly List<string> AudiobookFormats = new List<string> { "Audiobook", "Audio CD", "Audio Cassette", "Audible Audio", "CD-ROM", "MP3 CD" };

        public static Distance BookDistance(List<LocalBook> localTracks, Edition edition)
        {
            var dist = new Distance();

            // the most common list of authors reported by a file
            var fileAuthors = localTracks.Select(x => x.FileTrackInfo.Authors.Where(a => a.IsNotNullOrWhiteSpace()).ToList())
                .GroupBy(x => x.ConcatToString())
                .OrderByDescending(x => x.Count())
                .First()
                .First();

            var authors = GetAuthorVariants(fileAuthors);

            var authorConfidence = GetAuthorConfidence(localTracks);
            var authorKey = authorConfidence < 0.7 ? "author_low_confidence" : "author";

            dist.AddString(authorKey, authors, edition.Book.Value.AuthorMetadata.Value.Name);
            Logger.Trace("author: '{0}' vs '{1}' ({2:0.00}); {3}", authors.ConcatToString("' or '"), edition.Book.Value.AuthorMetadata.Value.Name, authorConfidence, dist.NormalizedDistance());

            var title = localTracks.MostCommon(x => x.FileTrackInfo.BookTitle) ?? "";
            var titleOptions = new List<string> { edition.Title };
            if (titleOptions[0].Contains("#"))
            {
                titleOptions.Add(StripSeriesRegex.Replace(titleOptions[0]));
            }

            var (maintitle, _) = edition.Title.SplitBookTitle(edition.Book.Value.AuthorMetadata.Value.Name);
            if (!titleOptions.Contains(maintitle))
            {
                titleOptions.Add(maintitle);
            }

            if (edition.Book.Value.SeriesLinks?.Value?.Any() ?? false)
            {
                foreach (var l in edition.Book.Value.SeriesLinks.Value)
                {
                    if (l.Series?.Value?.Title?.IsNotNullOrWhiteSpace() ?? false)
                    {
                        titleOptions.Add($"{l.Series.Value.Title} {l.Position} {edition.Title}");
                        titleOptions.Add($"{l.Series.Value.Title} Book {l.Position} {edition.Title}");
                        titleOptions.Add($"{edition.Title} {l.Series.Value.Title} {l.Position}");
                        titleOptions.Add($"{edition.Title} {l.Series.Value.Title} Book {l.Position}");
                    }
                }
            }

            var fileTitles = new[] { title, CleanTitleCruft.Replace(title) }.Distinct().ToList();

            var titleConfidence = GetBookTitleConfidence(localTracks);
            var titleKey = titleConfidence < 0.7 ? "book_low_confidence" : "book";

            dist.AddString(titleKey, fileTitles, titleOptions);
            Logger.Trace("book: '{0}' vs '{1}' ({2:0.00}); {3}", fileTitles.ConcatToString("' or '"), titleOptions.ConcatToString("' or '"), titleConfidence, dist.NormalizedDistance());

            // Penalise collection/box-set editions when the file title isn't
            // itself a collection.  Without this, a long collection title that
            // embeds a standalone book name can accidentally score better than
            // the actual standalone edition due to normalisation effects.
            var editionIsSet = CollectionSetRegex.IsMatch(edition.Title);
            var fileIsSet = fileTitles.Any(t => CollectionSetRegex.IsMatch(t));
            if (editionIsSet && !fileIsSet)
            {
                dist.AddBool("collection_set", true);
                Logger.Trace("collection_set penalty applied for edition '{0}'; {1}", edition.Title, dist.NormalizedDistance());
            }

            // Volume/series number comparison: prevents stripping from collapsing all
            // volumes to the same base title and losing volume-specific matching.
            // Check both metadata tags and file paths for volume numbers. The file
            // path is preferred when both exist and disagree, because metadata tags
            // may have been overwritten by a prior incorrect import.
            var metadataVolume = ExtractVolumeNumber(title);
            int? pathVolume = null;

            if (localTracks.Any())
            {
                var samplePath = localTracks.First().Path;
                if (samplePath.IsNotNullOrWhiteSpace())
                {
                    pathVolume = ExtractVolumeNumber(Path.GetFileNameWithoutExtension(samplePath))
                                 ?? ExtractVolumeNumber(Path.GetFileName(Path.GetDirectoryName(samplePath)));
                }
            }

            var fileVolume = pathVolume ?? metadataVolume;
            var editionVolume = ExtractVolumeNumber(edition.Title);

            if (fileVolume.HasValue && editionVolume.HasValue)
            {
                if (fileVolume.Value != editionVolume.Value)
                {
                    dist.AddBool("volume_mismatch", true);
                    Logger.Trace("volume_mismatch: file={0} vs edition={1}; {2}", fileVolume.Value, editionVolume.Value, dist.NormalizedDistance());
                }
            }
            else if (!fileVolume.HasValue && editionVolume.HasValue)
            {
                dist.AddBool("volume_absent_vs_present", true);
                Logger.Trace("volume_absent_vs_present: file has no volume, edition has {0}; {1}", editionVolume.Value, dist.NormalizedDistance());
            }

            var isbn = localTracks.MostCommon(x => x.FileTrackInfo.Isbn);
            if (isbn.IsNotNullOrWhiteSpace() && edition.Isbn13.IsNotNullOrWhiteSpace())
            {
                dist.AddBool("isbn", isbn != edition.Isbn13);
                Logger.Trace("isbn: '{0}' vs '{1}'; {2}", isbn, edition.Isbn13, dist.NormalizedDistance());
            }
            else if (isbn.IsNullOrWhiteSpace() != edition.Isbn13.IsNullOrWhiteSpace())
            {
                dist.AddBool("isbn_missing", true);
                Logger.Trace("isbn: '{0}' vs '{1}'; {2}", isbn, edition.Isbn13, dist.NormalizedDistance());
            }

            var asin = localTracks.MostCommon(x => x.FileTrackInfo.Asin);
            if (asin.IsNotNullOrWhiteSpace() && edition.Asin.IsNotNullOrWhiteSpace())
            {
                dist.AddBool("asin", asin != edition.Asin);
                Logger.Trace("asin: '{0}' vs '{1}'; {2}", asin, edition.Asin, dist.NormalizedDistance());
            }
            else if (asin.IsNullOrWhiteSpace() != edition.Asin.IsNullOrWhiteSpace())
            {
                dist.AddBool("asin_missing", true);
                Logger.Trace("asin: '{0}' vs '{1}'; {2}", asin, edition.Asin, dist.NormalizedDistance());
            }

            // Year
            var localYear = localTracks.MostCommon(x => x.FileTrackInfo.Year);
            if (localYear > 0 && edition.ReleaseDate.HasValue)
            {
                var bookYear = edition.ReleaseDate?.Year ?? 0;
                if (localYear == bookYear)
                {
                    dist.Add("year", 0.0);
                }
                else
                {
                    var remoteYear = bookYear;
                    var diff = Math.Abs(localYear - remoteYear);
                    var diff_max = Math.Abs(DateTime.Now.Year - remoteYear);
                    dist.AddRatio("year", diff, diff_max);
                }

                Logger.Trace($"year: {localYear} vs {edition.ReleaseDate?.Year}; {dist.NormalizedDistance()}");
            }

            // Language - only if set for both the local book and remote edition
            var localLanguage = localTracks.MostCommon(x => x.FileTrackInfo.Language).CanonicalizeLanguage();
            var editionLanguage = edition.Language.CanonicalizeLanguage();
            if (localLanguage.IsNotNullOrWhiteSpace() && editionLanguage.IsNotNullOrWhiteSpace())
            {
                dist.AddBool("language", localLanguage != editionLanguage);
                Logger.Trace($"language: {localLanguage} vs {editionLanguage}; {dist.NormalizedDistance()}");
            }

            // Publisher - only if set for both the local book and remote edition
            var localPublisher = localTracks.MostCommon(x => x.FileTrackInfo.Publisher);
            var editionPublisher = edition.Publisher;
            if (localPublisher.IsNotNullOrWhiteSpace() && editionPublisher.IsNotNullOrWhiteSpace())
            {
                dist.AddString("publisher", localPublisher, editionPublisher);
                Logger.Trace($"publisher: {localPublisher} vs {editionPublisher}; {dist.NormalizedDistance()}");
            }

            // try to tilt it towards the correct "type" of release
            var isAudio = MediaFileExtensions.AudioExtensions.Contains(localTracks.First().Path.GetPathExtension());

            if (edition.Format.IsNotNullOrWhiteSpace())
            {
                if (!isAudio)
                {
                    // text books should prefer ebook formats
                    dist.AddBool("ebook_format", !EbookFormats.Contains(edition.Format, StringComparer.OrdinalIgnoreCase));

                    // text books should not match audio entries
                    dist.AddBool("wrong_format", AudiobookFormats.Contains(edition.Format, StringComparer.OrdinalIgnoreCase));
                }
                else
                {
                    // audio books should prefer audio formats
                    dist.AddBool("audio_format", !AudiobookFormats.Contains(edition.Format, StringComparer.OrdinalIgnoreCase));
                }
            }

            return dist;
        }

        public static List<string> GetAuthorVariants(List<string> fileAuthors)
        {
            var authors = new List<string>(fileAuthors);

            if (fileAuthors.Count == 1)
            {
                authors.AddRange(SplitAuthor(fileAuthors[0]));
            }

            foreach (var author in fileAuthors)
            {
                if (author.Contains(','))
                {
                    var split = author.Split(',', 2).Select(x => x.Trim());
                    if (!split.First().Contains(' '))
                    {
                        authors.Add(split.Reverse().ConcatToString(" "));
                    }
                }
            }

            return authors;
        }

        private static List<string> SplitAuthor(string input)
        {
            var seps = new[] { ';', '/' };
            foreach (var sep in seps)
            {
                if (input.Contains(sep))
                {
                    return input.Split(sep).Select(x => x.Trim()).ToList();
                }
            }

            var andSeps = new List<string> { " and ", " & " };
            foreach (var sep in andSeps)
            {
                if (input.Contains(sep))
                {
                    var result = new List<string>();
                    foreach (var s in input.Split(sep).Select(x => x.Trim()))
                    {
                        var s2 = SplitAuthor(s);
                        if (s2.Any())
                        {
                            result.AddRange(s2);
                        }
                        else
                        {
                            result.Add(s);
                        }
                    }

                    return result;
                }
            }

            if (input.Contains(','))
            {
                var split = input.Split(',').Select(x => x.Trim()).ToList();
                if (split[0].Contains(' '))
                {
                    return split;
                }
            }

            return new List<string>();
        }

        private static double GetBookTitleConfidence(List<LocalBook> localTracks)
        {
            return localTracks.Select(x => x.FileTrackInfo?.BookTitleConfidence ?? 1.0)
                .DefaultIfEmpty(1.0)
                .Average();
        }

        private static double GetAuthorConfidence(List<LocalBook> localTracks)
        {
            return localTracks.Select(x => x.FileTrackInfo?.AuthorConfidence ?? 1.0)
                .DefaultIfEmpty(1.0)
                .Average();
        }

        /// <summary>
        /// Extracts the first volume/part/book number from a title string.
        /// Matches patterns like "Volume 2", "Vol. 3", "Book 1", "Part 4", "#5".
        /// </summary>
        internal static int? ExtractVolumeNumber(string title)
        {
            if (title.IsNullOrWhiteSpace())
            {
                return null;
            }

            var match = VolumeNumberRegex.Match(title);
            if (match.Success && int.TryParse(match.Groups[1].Value, out var volume))
            {
                return volume;
            }

            return null;
        }
    }
}
