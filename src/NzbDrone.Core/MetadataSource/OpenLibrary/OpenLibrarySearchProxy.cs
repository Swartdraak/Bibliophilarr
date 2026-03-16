using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Http;
using NzbDrone.Core.Books;

namespace NzbDrone.Core.MetadataSource.OpenLibrary
{
    public interface IOpenLibrarySearchProxy
    {
        List<Book> Search(string query);

        /// <summary>
        /// Look up a book by ISBN-10 or ISBN-13 using the dedicated Open Library ISBN endpoint,
        /// with a search API fallback. Returns null when the ISBN yields no result.
        /// </summary>
        Book LookupByIsbn(string isbn);

        /// <summary>
        /// Attempt to locate a book by ASIN via the Open Library search API.
        /// Open Library has no native ASIN support; this is a best-effort query that
        /// returns null when no unambiguous single-result match is found.
        /// </summary>
        Book LookupByAsin(string asin);
    }

    public class OpenLibrarySearchProxy : IOpenLibrarySearchProxy
    {
        private static readonly Regex Isbn13Regex = new Regex(@"^97[89]\d{10}$", RegexOptions.Compiled);
        private static readonly Regex Isbn10Regex = new Regex(@"^\d{9}[\dXx]$", RegexOptions.Compiled);
        private static readonly Regex AsinRegex = new Regex(@"^B0[0-9A-Z]{8}$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex YearInDateRegex = new Regex(@"\b(1[6-9]\d{2}|20\d{2})\b", RegexOptions.Compiled);

        private readonly IHttpClient _httpClient;
        private readonly Logger _logger;
        private readonly IHttpRequestBuilderFactory _requestBuilder;
        private readonly IHttpRequestBuilderFactory _isbnRequestBuilder;

        public OpenLibrarySearchProxy(IHttpClient httpClient, Logger logger)
        {
            _httpClient = httpClient;
            _logger = logger;

            _requestBuilder = new HttpRequestBuilder("https://openlibrary.org/search.json")
                .Accept(HttpAccept.Json)
                .CreateFactory();

            _isbnRequestBuilder = new HttpRequestBuilder("https://openlibrary.org")
                .Accept(HttpAccept.Json)
                .CreateFactory();
        }

        public List<Book> Search(string query)
        {
            if (query.IsNullOrWhiteSpace())
            {
                return new List<Book>();
            }

            var request = _requestBuilder.Create()
                .AddQueryParam("q", query.Trim())
                .AddQueryParam("limit", "10")
                .Build();

            try
            {
                var response = _httpClient.Get<OpenLibrarySearchResponse>(request);
                var docs = response.Resource?.Docs ?? new List<OpenLibrarySearchDoc>();

                return docs.Select(MapBook)
                    .Where(x => x != null)
                    .ToList();
            }
            catch (Exception e)
            {
                _logger.Warn(e, "OpenLibrary search failed for query '{0}'", query);
                return new List<Book>();
            }
        }

        public Book LookupByIsbn(string isbn)
        {
            var normalized = NormalizeIsbn(isbn);
            if (normalized.IsNullOrWhiteSpace())
            {
                _logger.Debug("OpenLibrary ISBN lookup skipped: '{0}' is not a valid ISBN", isbn);
                return null;
            }

            var editionBook = TryIsbnEndpoint(normalized);
            if (editionBook != null)
            {
                return editionBook;
            }

            return TryIsbnSearch(normalized);
        }

        public Book LookupByAsin(string asin)
        {
            if (asin.IsNullOrWhiteSpace() || !AsinRegex.IsMatch(asin.Trim()))
            {
                return null;
            }

            _logger.Debug("OpenLibrary ASIN lookup (best-effort) for '{0}'", asin);

            var request = _requestBuilder.Create()
                .AddQueryParam("q", asin.Trim().ToUpperInvariant())
                .AddQueryParam("limit", "5")
                .Build();

            try
            {
                var response = _httpClient.Get<OpenLibrarySearchResponse>(request);
                var docs = response.Resource?.Docs ?? new List<OpenLibrarySearchDoc>();

                return docs.Count == 1 ? MapBook(docs[0]) : null;
            }
            catch (Exception e)
            {
                _logger.Warn(e, "OpenLibrary ASIN search failed for '{0}'", asin);
                return null;
            }
        }

        private Book TryIsbnEndpoint(string isbn)
        {
            var request = _isbnRequestBuilder.Create()
                .Resource($"/isbn/{isbn}.json")
                .Build();

            try
            {
                var response = _httpClient.Get<OpenLibraryEditionResource>(request);
                var edition = response.Resource;
                return edition != null ? MapEditionToBook(edition, isbn) : null;
            }
            catch (HttpException e) when (e.Response?.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }
            catch (Exception e)
            {
                _logger.Debug(e, "OpenLibrary ISBN endpoint unavailable for '{0}', trying search fallback", isbn);
                return null;
            }
        }

        private Book TryIsbnSearch(string isbn)
        {
            var request = _requestBuilder.Create()
                .AddQueryParam("isbn", isbn)
                .AddQueryParam("limit", "1")
                .Build();

            try
            {
                var response = _httpClient.Get<OpenLibrarySearchResponse>(request);
                var docs = response.Resource?.Docs ?? new List<OpenLibrarySearchDoc>();
                var doc = docs.FirstOrDefault();
                return doc != null ? MapBook(doc) : null;
            }
            catch (Exception e)
            {
                _logger.Warn(e, "OpenLibrary ISBN search fallback failed for '{0}'", isbn);
                return null;
            }
        }

        private static Book MapEditionToBook(OpenLibraryEditionResource edition, string lookupIsbn)
        {
            if (edition?.Key.IsNullOrWhiteSpace() ?? true)
            {
                return null;
            }

            var workKeyRaw = edition.Works?.FirstOrDefault()?.Key;
            var bookForeignId = workKeyRaw.IsNotNullOrWhiteSpace()
                ? $"openlibrary:work:{ExtractWorkKey(workKeyRaw)}"
                : $"openlibrary:edition:{ExtractEditionKey(edition.Key)}";

            var editionKey = ExtractEditionKey(edition.Key);
            var title = edition.Title.IsNotNullOrWhiteSpace() ? edition.Title.Trim() : editionKey;
            var authorSlug = edition.Authors?.Select(a => a.Key)
                .Where(k => k.IsNotNullOrWhiteSpace())
                .Select(k => k.Replace("/authors/", string.Empty).Trim())
                .FirstOrDefault();
            var authorName = authorSlug.IsNotNullOrWhiteSpace() ? authorSlug : "Unknown Author";
            var isbn13 = edition.Isbn13?.FirstOrDefault(IsThirteenDigitIsbn)
                ?? (Isbn13Regex.IsMatch(lookupIsbn) ? lookupIsbn : null);
            var publishYear = ParseEditionPublishYear(edition.PublishDate);
            var releaseDate = publishYear.HasValue
                ? new DateTime(publishYear.Value, 1, 1, 0, 0, 0, DateTimeKind.Utc)
                : (DateTime?)null;

            var authorMetadata = new AuthorMetadata
            {
                ForeignAuthorId = $"openlibrary:author:{NormalizeId(authorName)}",
                Name = authorName,
                SortName = authorName,
                NameLastFirst = authorName,
                SortNameLastFirst = authorName
            };

            var book = new Book
            {
                ForeignBookId = bookForeignId,
                Title = title,
                CleanTitle = title,
                AuthorMetadata = authorMetadata,
                AuthorMetadataId = authorMetadata.Id,
                Author = new Author
                {
                    Metadata = authorMetadata,
                    AuthorMetadataId = authorMetadata.Id
                },
                ReleaseDate = releaseDate,
                Ratings = new Ratings()
            };

            book.Editions = new List<Edition>
            {
                new Edition
                {
                    ForeignEditionId = $"openlibrary:edition:{editionKey}",
                    Title = title,
                    Isbn13 = isbn13,
                    Publisher = edition.Publishers?.FirstOrDefault()?.Trim(),
                    PageCount = edition.NumberOfPages ?? 0,
                    ReleaseDate = releaseDate,
                    IsEbook = true,
                    Format = "Ebook",
                    Book = book,
                    Ratings = new Ratings()
                }
            };

            return book;
        }

        private static Book MapBook(OpenLibrarySearchDoc doc)
        {
            var workKey = ExtractWorkKey(doc?.Key);
            if (workKey.IsNullOrWhiteSpace())
            {
                return null;
            }

            var title = doc.Title.IsNotNullOrWhiteSpace() ? doc.Title.Trim() : workKey;
            var authorName = doc.AuthorNames?.FirstOrDefault(x => x.IsNotNullOrWhiteSpace())?.Trim() ?? "Unknown Author";
            var authorKey = doc.AuthorKeys?.FirstOrDefault(x => x.IsNotNullOrWhiteSpace());

            var authorMetadata = new AuthorMetadata
            {
                ForeignAuthorId = authorKey.IsNotNullOrWhiteSpace()
                    ? $"openlibrary:author:{authorKey.Trim()}"
                    : $"openlibrary:author:{NormalizeId(authorName)}",
                Name = authorName,
                SortName = authorName,
                NameLastFirst = authorName,
                SortNameLastFirst = authorName
            };

            var book = new Book
            {
                ForeignBookId = $"openlibrary:work:{workKey}",
                Title = title,
                CleanTitle = title,
                AuthorMetadata = authorMetadata,
                AuthorMetadataId = authorMetadata.Id,
                Author = new Author
                {
                    Metadata = authorMetadata,
                    AuthorMetadataId = authorMetadata.Id
                },
                ReleaseDate = ParseReleaseDate(doc.FirstPublishYear),
                Ratings = new Ratings()
            };

            var isbn13 = doc.Isbn?.FirstOrDefault(x => IsThirteenDigitIsbn(x));

            book.Editions = new List<Edition>
            {
                new Edition
                {
                    ForeignEditionId = $"openlibrary:edition:{workKey}",
                    Title = title,
                    Isbn13 = isbn13,
                    ReleaseDate = book.ReleaseDate,
                    IsEbook = true,
                    Format = "Ebook",
                    Book = book,
                    Ratings = new Ratings()
                }
            };

            return book;
        }

        private static string NormalizeIsbn(string raw)
        {
            if (raw.IsNullOrWhiteSpace())
            {
                return null;
            }

            var digits = Regex.Replace(raw.Trim(), @"[^0-9Xx]", string.Empty).ToUpperInvariant();

            if (Isbn13Regex.IsMatch(digits) || Isbn10Regex.IsMatch(digits))
            {
                return digits;
            }

            return null;
        }

        private static DateTime? ParseReleaseDate(int? year)
        {
            if (!year.HasValue || year.Value <= 0 || year.Value > DateTime.MaxValue.Year)
            {
                return null;
            }

            return new DateTime(year.Value, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        }

        private static string ExtractWorkKey(string key)
        {
            if (key.IsNullOrWhiteSpace())
            {
                return null;
            }

            var normalized = key.Trim();
            if (normalized.StartsWith("/works/", StringComparison.InvariantCultureIgnoreCase))
            {
                normalized = normalized.Substring("/works/".Length);
            }

            return normalized.Trim();
        }

        private static string ExtractEditionKey(string key)
        {
            if (key.IsNullOrWhiteSpace())
            {
                return null;
            }

            var normalized = key.Trim();
            if (normalized.StartsWith("/books/", StringComparison.InvariantCultureIgnoreCase))
            {
                normalized = normalized.Substring("/books/".Length);
            }

            return normalized.Trim();
        }

        private static int? ParseEditionPublishYear(string publishDate)
        {
            if (publishDate.IsNullOrWhiteSpace())
            {
                return null;
            }

            var match = YearInDateRegex.Match(publishDate);
            if (match.Success && int.TryParse(match.Value, out var year))
            {
                return year;
            }

            return null;
        }

        private static bool IsThirteenDigitIsbn(string value)
        {
            if (value.IsNullOrWhiteSpace())
            {
                return false;
            }

            var cleaned = value.Trim();
            return cleaned.Length == 13 && cleaned.All(char.IsDigit);
        }

        private static string NormalizeId(string value)
        {
            var normalized = value.ToLowerInvariant().Trim();
            normalized = string.Join("-", normalized.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries));

            return new string(normalized.Where(c => char.IsLetterOrDigit(c) || c == '-').ToArray());
        }
    }

    public class OpenLibrarySearchResponse
    {
        [JsonProperty("docs")]
        public List<OpenLibrarySearchDoc> Docs { get; set; }
    }

    public class OpenLibraryEditionResource
    {
        [JsonProperty("key")]
        public string Key { get; set; }

        [JsonProperty("title")]
        public string Title { get; set; }

        [JsonProperty("authors")]
        public List<OpenLibraryKeyRef> Authors { get; set; }

        [JsonProperty("works")]
        public List<OpenLibraryKeyRef> Works { get; set; }

        [JsonProperty("isbn_13")]
        public List<string> Isbn13 { get; set; }

        [JsonProperty("isbn_10")]
        public List<string> Isbn10 { get; set; }

        [JsonProperty("publishers")]
        public List<string> Publishers { get; set; }

        [JsonProperty("publish_date")]
        public string PublishDate { get; set; }

        [JsonProperty("number_of_pages")]
        public int? NumberOfPages { get; set; }
    }

    public class OpenLibraryKeyRef
    {
        [JsonProperty("key")]
        public string Key { get; set; }
    }

    public class OpenLibrarySearchDoc
    {
        [JsonProperty("key")]
        public string Key { get; set; }

        [JsonProperty("title")]
        public string Title { get; set; }

        [JsonProperty("author_name")]
        public List<string> AuthorNames { get; set; }

        [JsonProperty("author_key")]
        public List<string> AuthorKeys { get; set; }

        [JsonProperty("first_publish_year")]
        public int? FirstPublishYear { get; set; }

        [JsonProperty("isbn")]
        public List<string> Isbn { get; set; }
    }
}
