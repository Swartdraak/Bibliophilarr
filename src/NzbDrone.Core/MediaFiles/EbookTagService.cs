using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Abstractions;
using System.Linq;
using System.Text.RegularExpressions;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Instrumentation.Extensions;
using NzbDrone.Common.Serializer;
using NzbDrone.Core.Books;
using NzbDrone.Core.Books.Calibre;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.MediaFiles.Azw;
using NzbDrone.Core.MediaFiles.Commands;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.RootFolders;
using PdfSharpCore.Pdf.IO;
using VersOne.Epub;
using VersOne.Epub.Schema;

namespace NzbDrone.Core.MediaFiles
{
    public interface IEBookTagService
    {
        ParsedTrackInfo ReadTags(IFileInfo file);
        void WriteTags(BookFile trackfile, bool newDownload, bool force = false);
        void SyncTags(List<Edition> books);
        List<RetagBookFilePreview> GetRetagPreviewsByAuthor(int authorId);
        List<RetagBookFilePreview> GetRetagPreviewsByBook(int bookId);
        void RetagFiles(RetagFilesCommand message);
        void RetagAuthor(RetagAuthorCommand message);
    }

    public class EBookTagService : IEBookTagService
    {
        private readonly IAuthorService _authorService;
        private readonly IMediaFileService _mediaFileService;
        private readonly IRootFolderService _rootFolderService;
        private readonly IConfigService _configService;
        private readonly ICalibreProxy _calibre;
        private readonly Logger _logger;
        private static readonly Regex IsbnPattern = new Regex(@"(?:97[89])?\d{9}[\dXx]", RegexOptions.Compiled);
        private static readonly Regex AsinPattern = new Regex(@"\b[A-Za-z0-9]{10}\b", RegexOptions.Compiled);

        public EBookTagService(IAuthorService authorService,
            IMediaFileService mediaFileService,
            IRootFolderService rootFolderService,
            IConfigService configService,
            ICalibreProxy calibre,
            Logger logger)
        {
            _authorService = authorService;
            _mediaFileService = mediaFileService;
            _rootFolderService = rootFolderService;
            _configService = configService;
            _calibre = calibre;

            _logger = logger;
        }

        public ParsedTrackInfo ReadTags(IFileInfo file)
        {
            var extension = file.Extension.ToLower();
            _logger.Trace($"Got extension '{extension}'");

            switch (extension)
            {
                case ".pdf":
                    return ReadPdf(file.FullName);
                case ".epub":
                case ".kepub":
                    return ReadEpub(file.FullName);
                case ".azw3":
                case ".mobi":
                    return ReadAzw3(file.FullName);
                default:
                    return Parser.Parser.ParseTitle(file.FullName);
            }
        }

        public void WriteTags(BookFile bookFile, bool newDownload, bool force = false)
        {
            if (!force)
            {
                if (_configService.WriteBookTags == WriteBookTagsType.NewFiles && !newDownload)
                {
                    return;
                }
            }

            _logger.Debug($"Writing tags for {bookFile}");

            WriteTagsInternal(bookFile, _configService.UpdateCovers, _configService.EmbedMetadata);
        }

        public void SyncTags(List<Edition> editions)
        {
            if (_configService.WriteBookTags != WriteBookTagsType.Sync)
            {
                return;
            }

            // get the tracks to update
            foreach (var edition in editions)
            {
                var bookFiles = edition.BookFiles.Value;

                _logger.Debug($"Syncing ebook tags for {edition}");

                foreach (var file in bookFiles.Where(x => x.CalibreId != 0))
                {
                    // populate tracks (which should also have release/book/author set) because
                    // not all of the updates will have been committed to the database yet
                    file.Edition = edition;

                    WriteTagsInternal(file, _configService.UpdateCovers, _configService.EmbedMetadata);
                }
            }
        }

        public List<RetagBookFilePreview> GetRetagPreviewsByAuthor(int authorId)
        {
            var files = _mediaFileService.GetFilesByAuthor(authorId);

            return GetPreviews(files).ToList();
        }

        public List<RetagBookFilePreview> GetRetagPreviewsByBook(int bookId)
        {
            var files = _mediaFileService.GetFilesByBook(bookId);

            return GetPreviews(files).ToList();
        }

        public void RetagFiles(RetagFilesCommand message)
        {
            var author = _authorService.GetAuthor(message.AuthorId);
            var files = _mediaFileService.Get(message.Files);

            _logger.ProgressInfo("Re-tagging {0} ebook files for {1}", files.Count, author.Name);

            foreach (var file in files.Where(x => x.CalibreId != 0))
            {
                WriteTagsInternal(file, message.UpdateCovers, message.EmbedMetadata);
            }

            _logger.ProgressInfo("Selected ebook files re-tagged for {0}", author.Name);
        }

        public void RetagAuthor(RetagAuthorCommand message)
        {
            _logger.Debug("Re-tagging all ebook files for selected authors");
            var authorsToRename = _authorService.GetAuthors(message.AuthorIds);

            foreach (var author in authorsToRename)
            {
                var files = _mediaFileService.GetFilesByAuthor(author.Id);

                _logger.ProgressInfo("Re-tagging all ebook files for author: {0}", author.Name);

                foreach (var file in files.Where(x => x.CalibreId != 0))
                {
                    WriteTagsInternal(file, message.UpdateCovers, message.EmbedMetadata);
                }

                _logger.ProgressInfo("All ebook files re-tagged for {0}", author.Name);
            }
        }

        private void WriteTagsInternal(BookFile file, bool updateCover, bool embedMetadata)
        {
            if (file.CalibreId == 0)
            {
                _logger.Trace($"No calibre id for {file.Path}, skipping writing tags");
            }

            var rootFolder = _rootFolderService.GetBestRootFolder(file.Path);

            if (rootFolder == null)
            {
                throw new Exception($"File '{file.Path}' is not in a root folder.");
            }

            _calibre.SetFields(file, rootFolder.CalibreSettings, updateCover, embedMetadata);
        }

        private IEnumerable<RetagBookFilePreview> GetPreviews(List<BookFile> files)
        {
            var calibreFiles = files.Where(x => x.CalibreId > 0).OrderBy(x => x.Edition.Value.Title).ToList();

            var rootFolderPairs = calibreFiles.Select(x => Tuple.Create(x, _rootFolderService.GetBestRootFolder(x.Path)));

            var rootFolderGroups = rootFolderPairs.GroupBy(x => x.Item2.Path);

            var calibreBooks = new List<CalibreBook>();
            foreach (var group in rootFolderGroups)
            {
                var rootFolder = group.First().Item2;
                var books = _calibre.GetBooks(group.Select(x => x.Item1.CalibreId).ToList(), rootFolder.CalibreSettings);
                calibreBooks.AddRange(books);
            }

            var dict = calibreBooks.ToDictionary(x => x.Id);

            foreach (var file in calibreFiles)
            {
                var edition = file.Edition.Value;
                var book = edition.Book.Value;
                var serieslink = book.SeriesLinks.Value.OrderBy(x => x.SeriesPosition).FirstOrDefault(x => x.Series.Value.Title.IsNotNullOrWhiteSpace());

                var series = serieslink?.Series.Value;
                double? seriesIndex = null;
                if (double.TryParse(serieslink?.Position, out var index))
                {
                    _logger.Trace($"Parsed {serieslink?.Position} as {index}");
                    seriesIndex = index;
                }

                var oldTags = dict[file.CalibreId];

                var textInfo = CultureInfo.InvariantCulture.TextInfo;
                var genres = book.Genres.Select(x => textInfo.ToTitleCase(x.Replace('-', ' '))).ToList();

                var newTags = new CalibreBook
                {
                    Title = edition.Title,
                    Authors = new List<string> { file.Author.Value.Name },
                    PubDate = book.ReleaseDate,
                    Publisher = edition.Publisher,
                    Languages = new List<string> { edition.Language.CanonicalizeLanguage() },
                    Tags = genres,
                    Comments = edition.Overview,
                    Rating = (int)(edition.Ratings.Value * 2) / 2.0,
                    Identifiers = new Dictionary<string, string>
                    {
                        { "isbn", edition.Isbn13 },
                        { "asin", edition.Asin },
                        { "openlibrary", edition.ForeignEditionId }
                    },
                    Series = series?.Title,
                    Position = seriesIndex
                };

                var diff = oldTags.Diff(newTags);

                if (diff.Any())
                {
                    yield return new RetagBookFilePreview
                    {
                        AuthorId = file.Author.Value.Id,
                        BookId = file.Edition.Value.Id,
                        BookFileId = file.Id,
                        Path = file.Path,
                        Changes = diff
                    };
                }
            }
        }

        private ParsedTrackInfo ReadEpub(string file)
        {
            _logger.Trace($"Reading {file}");
            var result = new ParsedTrackInfo
            {
                Quality = new QualityModel
                {
                    Quality = Quality.EPUB,
                    QualityDetectionSource = QualityDetectionSource.TagLib
                }
            };

            try
            {
                using (var bookRef = EpubReader.OpenBook(file))
                {
                    result.Authors = bookRef.AuthorList;
                    result.BookTitle = bookRef.Title;

                    var meta = bookRef.Schema.Package.Metadata;

                    _logger.Trace(meta.ToJson());

                    result.Isbn = GetIsbn(meta?.Identifiers);
                    result.Asin = meta?.Identifiers?.FirstOrDefault(x => x.Scheme?.ToLower().Contains("asin") ?? false)?.Identifier;
                    result.Language = meta?.Languages?.FirstOrDefault();
                    result.Publisher = meta?.Publishers?.FirstOrDefault();
                    result.Disambiguation = meta?.Description;
                    result.SeriesTitle = meta?.MetaItems?.FirstOrDefault(x => x.Name == "calibre:series")?.Content;
                    result.SeriesIndex = meta?.MetaItems?.FirstOrDefault(x => x.Name == "calibre:series_index")?.Content;

                    result.BookTitleConfidence = result.BookTitle.IsNotNullOrWhiteSpace() ? 0.92 : 0.0;
                    result.AuthorConfidence = result.Authors?.Any() == true ? 0.92 : 0.0;
                    result.IsbnConfidence = result.Isbn.IsNotNullOrWhiteSpace() ? 0.97 : 0.0;
                    result.AsinConfidence = result.Asin.IsNotNullOrWhiteSpace() ? 0.9 : 0.0;
                    result.SeriesConfidence = result.SeriesTitle.IsNotNullOrWhiteSpace() ? 0.8 : 0.0;
                    result.PublisherConfidence = result.Publisher.IsNotNullOrWhiteSpace() ? 0.8 : 0.0;
                    result.LanguageConfidence = result.Language.IsNotNullOrWhiteSpace() ? 0.75 : 0.0;
                }

                NormalizeParsedMetadata(result);
                ApplyFilenameFallback(file, result, 0.5);
            }
            catch (Exception e)
            {
                _logger.Warn(e, "Corrupt or unreadable EPUB: '{0}'. Tag parsing failed; using filename fallback. Repair or re-export via Calibre to restore full metadata.", file);
                result.Quality.QualityDetectionSource = QualityDetectionSource.Extension;
                ApplyFilenameFallback(file, result, 0.88);
                LogFallbackOutcome(file, result, "EPUB");
            }

            _logger.Trace($"Got:\n{result.ToJson()}");

            return result;
        }

        private ParsedTrackInfo ReadAzw3(string file)
        {
            _logger.Trace($"Reading {file}");
            var result = new ParsedTrackInfo();

            try
            {
                var book = new Azw3File(file);
                result.Authors = book.Authors;
                result.BookTitle = book.Title;
                result.Asin = book.Asin;
                result.Language = book.Language;
                result.Disambiguation = book.Description;
                result.Publisher = book.Publisher;
                result.Label = book.Imprint;
                result.Source = book.Source;

                result.BookTitleConfidence = result.BookTitle.IsNotNullOrWhiteSpace() ? 0.9 : 0.0;
                result.AuthorConfidence = result.Authors?.Any() == true ? 0.9 : 0.0;
                result.AsinConfidence = result.Asin.IsNotNullOrWhiteSpace() ? 0.92 : 0.0;
                result.PublisherConfidence = result.Publisher.IsNotNullOrWhiteSpace() ? 0.7 : 0.0;
                result.LanguageConfidence = result.Language.IsNotNullOrWhiteSpace() ? 0.7 : 0.0;

                result.Quality = new QualityModel
                {
                    Quality = book.Version <= 6 ? Quality.MOBI : Quality.AZW3,
                    QualityDetectionSource = QualityDetectionSource.TagLib
                };

                NormalizeParsedMetadata(result);
                ApplyFilenameFallback(file, result, 0.55);
            }
            catch (Exception e)
            {
                var azwFormat = Path.GetExtension(file).Equals(".mobi", StringComparison.OrdinalIgnoreCase) ? "MOBI" : "AZW3";
                _logger.Warn(e, "Corrupt or unreadable {0}: '{1}'. Tag parsing failed; using filename fallback. Convert or repair via Calibre to restore full metadata.", azwFormat, file);
                result.Quality = new QualityModel
                {
                    Quality = Path.GetExtension(file) == ".mobi" ? Quality.MOBI : Quality.AZW3,
                    QualityDetectionSource = QualityDetectionSource.Extension
                };
                ApplyFilenameFallback(file, result, 0.88);
                LogFallbackOutcome(file, result, azwFormat);
            }

            _logger.Trace($"Got {result.ToJson()}");

            return result;
        }

        private ParsedTrackInfo ReadPdf(string file)
        {
            _logger.Trace($"Reading {file}");
            var result = new ParsedTrackInfo
            {
                Quality = new QualityModel
                {
                    Quality = Quality.PDF,
                    QualityDetectionSource = QualityDetectionSource.TagLib
                }
            };

            try
            {
                var book = PdfReader.Open(file, PdfDocumentOpenMode.InformationOnly);
                if (book.Info != null)
                {
                    result.Authors = new List<string> { book.Info.Author };
                    result.BookTitle = book.Info.Title;
                    result.Publisher = book.Info.Creator;

                    result.BookTitleConfidence = result.BookTitle.IsNotNullOrWhiteSpace() ? 0.65 : 0.0;
                    result.AuthorConfidence = result.Authors?.Any() == true ? 0.65 : 0.0;
                    result.PublisherConfidence = result.Publisher.IsNotNullOrWhiteSpace() ? 0.55 : 0.0;

                    _logger.Trace(book.Info.ToJson());
                }

                NormalizeParsedMetadata(result);
                ApplyFilenameFallback(file, result, 0.72);
            }
            catch (Exception e)
            {
                _logger.Warn(e, "Corrupt or unreadable PDF: '{0}'. Tag parsing failed; using filename fallback. Re-export from source or repair via Calibre to restore full metadata.", file);
                result.Quality.QualityDetectionSource = QualityDetectionSource.Extension;
                ApplyFilenameFallback(file, result, 0.88);
                LogFallbackOutcome(file, result, "PDF");
            }

            _logger.Trace($"Got:\n{result.ToJson()}");

            return result;
        }

        public string GetIsbn(IEnumerable<EpubMetadataIdentifier> ids)
        {
            if (ids == null)
            {
                return null;
            }

            var candidates = ids.Select(x => StripIsbn(x?.Identifier))
                .Where(x => x != null)
                .OrderByDescending(x => x.Length);

            return candidates.FirstOrDefault(x => x.StartsWith("978"))
                ?? candidates.FirstOrDefault(x => x.StartsWith("979"))
                ?? candidates.FirstOrDefault();
        }

        private void ApplyFilenameFallback(string file, ParsedTrackInfo result, double fieldConfidence)
        {
            var parsed = Parser.Parser.ParseTitle(file);
            if (parsed == null)
            {
                return;
            }

            var parsedAuthors = NormalizeAuthors(parsed.Authors);
            var parsedTitle = NormalizeMetadataText(parsed.BookTitle);
            var parsedIsbn = NormalizeIsbn(parsed.Isbn) ?? TryExtractIsbnFromText(file);
            var parsedAsin = NormalizeAsin(parsed.Asin) ?? TryExtractAsinFromText(file);

            if (parsedAuthors.Any() && (result.Authors == null || !result.Authors.Any() || result.AuthorConfidence < fieldConfidence))
            {
                result.Authors = parsedAuthors;
                result.AuthorConfidence = fieldConfidence;
            }

            if (parsedTitle.IsNotNullOrWhiteSpace() && (result.BookTitle.IsNullOrWhiteSpace() || result.BookTitleConfidence < fieldConfidence))
            {
                result.BookTitle = parsedTitle;
                result.BookTitleConfidence = fieldConfidence;
            }

            if (parsedIsbn.IsNotNullOrWhiteSpace() && (result.Isbn.IsNullOrWhiteSpace() || result.IsbnConfidence < fieldConfidence))
            {
                result.Isbn = parsedIsbn;
                result.IsbnConfidence = fieldConfidence;
            }

            if (parsedAsin.IsNotNullOrWhiteSpace() && (result.Asin.IsNullOrWhiteSpace() || result.AsinConfidence < fieldConfidence))
            {
                result.Asin = parsedAsin;
                result.AsinConfidence = fieldConfidence;
            }

            NormalizeParsedMetadata(result);
        }

        private void NormalizeParsedMetadata(ParsedTrackInfo result)
        {
            result.BookTitle = NormalizeMetadataText(result.BookTitle);
            result.Authors = NormalizeAuthors(result.Authors);
            result.SeriesTitle = NormalizeMetadataText(result.SeriesTitle);
            result.Publisher = NormalizeMetadataText(result.Publisher);
            result.Language = NormalizeLanguage(result.Language);
            result.Isbn = NormalizeIsbn(result.Isbn) ?? TryExtractIsbnFromText(result.Disambiguation);
            result.Asin = NormalizeAsin(result.Asin) ?? TryExtractAsinFromText(result.Disambiguation);
        }

        private static string NormalizeMetadataText(string value)
        {
            if (value.IsNullOrWhiteSpace())
            {
                return null;
            }

            return Regex.Replace(value.Trim(), @"\s+", " ");
        }

        private static List<string> NormalizeAuthors(IEnumerable<string> authors)
        {
            return authors?.Where(a => a.IsNotNullOrWhiteSpace())
                .Select(a => NormalizeMetadataText(a))
                .Where(a => a.IsNotNullOrWhiteSpace())
                .Distinct()
                .ToList() ?? new List<string>();
        }

        private static string NormalizeLanguage(string value)
        {
            var normalized = NormalizeMetadataText(value);
            return normalized.IsNullOrWhiteSpace() ? null : normalized.CanonicalizeLanguage();
        }

        private string NormalizeIsbn(string value)
        {
            if (value.IsNullOrWhiteSpace())
            {
                return null;
            }

            var direct = StripIsbn(value);
            if (direct != null)
            {
                return direct;
            }

            var match = IsbnPattern.Match(value);
            if (!match.Success)
            {
                return null;
            }

            return StripIsbn(match.Value);
        }

        private static string NormalizeAsin(string value)
        {
            if (value.IsNullOrWhiteSpace())
            {
                return null;
            }

            var candidate = new string(value.Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
            if (candidate.Length != 10)
            {
                return null;
            }

            return candidate;
        }

        private string TryExtractIsbnFromText(string text)
        {
            if (text.IsNullOrWhiteSpace())
            {
                return null;
            }

            foreach (Match match in IsbnPattern.Matches(text))
            {
                var isbn = StripIsbn(match.Value);
                if (isbn != null)
                {
                    return isbn;
                }
            }

            return null;
        }

        private string TryExtractAsinFromText(string text)
        {
            if (text.IsNullOrWhiteSpace())
            {
                return null;
            }

            foreach (Match match in AsinPattern.Matches(text))
            {
                var asin = NormalizeAsin(match.Value);
                if (asin != null)
                {
                    return asin;
                }
            }

            return null;
        }

        private void LogFallbackOutcome(string file, ParsedTrackInfo result, string format)
        {
            var hasTitle = !result.BookTitle.IsNullOrWhiteSpace();
            var hasAuthors = result.Authors != null && result.Authors.Any();
            if (hasTitle || hasAuthors)
            {
                _logger.Info("{0} fallback metadata from filename for '{1}': title='{2}', authors='{3}'",
                    format,
                    Path.GetFileName(file),
                    result.BookTitle ?? string.Empty,
                    hasAuthors ? string.Join(", ", result.Authors) : "<none>");
            }
            else
            {
                _logger.Warn(
                    "No fallback metadata could be extracted from filename '{0}'. " +
                    "Rename to 'Title - Author.{1}' format to aid automatic matching.",
                    Path.GetFileName(file),
                    format.ToLower());
            }
        }

        private string GetIsbnChars(string input)
        {
            if (input == null)
            {
                return null;
            }

            return new string(input.Where(c => char.IsDigit(c) || c == 'X' || c == 'x').ToArray());
        }

        private string StripIsbn(string input)
        {
            var isbn = GetIsbnChars(input);

            if (isbn == null)
            {
                return null;
            }
            else if ((isbn.Length == 10 && ValidateIsbn10(isbn)) ||
                (isbn.Length == 13 && ValidateIsbn13(isbn)))
            {
                return isbn;
            }

            return null;
        }

        private static char Isbn10Checksum(string isbn)
        {
            var sum = 0;
            for (var i = 0; i < 9; i++)
            {
                sum += int.Parse(isbn[i].ToString()) * (10 - i);
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

            return (11 - result).ToString()[0];
        }

        private static char Isbn13Checksum(string isbn)
        {
            var result = 0;
            for (var i = 0; i < 12; i++)
            {
                result += int.Parse(isbn[i].ToString()) * ((i % 2 == 0) ? 1 : 3);
            }

            result %= 10;

            return result == 0 ? '0' : (10 - result).ToString()[0];
        }

        private static bool ValidateIsbn10(string isbn)
        {
            return ulong.TryParse(isbn.Substring(0, 9), out _) && isbn[9] == Isbn10Checksum(isbn);
        }

        private static bool ValidateIsbn13(string isbn)
        {
            return ulong.TryParse(isbn, out _) && isbn[12] == Isbn13Checksum(isbn);
        }
    }
}
