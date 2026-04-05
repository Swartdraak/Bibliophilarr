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
using NzbDrone.Core.MediaCover;

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

        /// <summary>
        /// Fetch author metadata from Open Library author endpoint.
        /// Supports keys in either raw form (OL123A), /authors/OL123A, or
        /// bibliophilarr foreign id form (openlibrary:author:OL123A).
        /// </summary>
        Author LookupAuthorByKey(string authorKey);
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

        public Author LookupAuthorByKey(string authorKey)
        {
            var normalized = NormalizeAuthorKey(authorKey);
            if (normalized.IsNullOrWhiteSpace())
            {
                return null;
            }

            var request = _isbnRequestBuilder.Create()
                .Resource($"/authors/{normalized}.json")
                .Build();

            try
            {
                var response = _httpClient.Get<OpenLibraryAuthorResource>(request);
                return MapAuthor(response.Resource, normalized);
            }
            catch (HttpException e) when (e.Response?.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }
            catch (Exception e)
            {
                _logger.Warn(e, "OpenLibrary author lookup failed for '{0}'", authorKey);
                return null;
            }
        }

        private Book TryIsbnEndpoint(string isbn)
        {
            var request = _isbnRequestBuilder.Create()
                .Resource($"/isbn/{isbn}.json")
                .Build();

            // Open Library /isbn/{isbn}.json redirects to /books/OL{id}M.json;
            // allow the client to follow that redirect so we get the edition JSON
            // rather than an HTML redirect page that triggers UnexpectedHtmlContentException.
            request.AllowAutoRedirect = true;

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
            var authorKey = edition.Authors?
                .Select(a => NormalizeAuthorKey(a.Key))
                .FirstOrDefault(k => k.IsNotNullOrWhiteSpace());
            var authorName = authorKey.IsNotNullOrWhiteSpace() ? authorKey : "Unknown Author";
            var isbn13 = edition.Isbn13?.FirstOrDefault(IsThirteenDigitIsbn)
                ?? (Isbn13Regex.IsMatch(lookupIsbn) ? lookupIsbn : null);
            var publishYear = ParseEditionPublishYear(edition.PublishDate);
            var releaseDate = ParseReleaseDate(publishYear);

            var foreignAuthorId = authorKey.IsNotNullOrWhiteSpace()
                ? $"openlibrary:author:{authorKey}"
                : "openlibrary:author:unknown-author";

            var authorMetadata = new AuthorMetadata
            {
                ForeignAuthorId = foreignAuthorId,
                TitleSlug = foreignAuthorId.ToUrlSlug(),
                Name = authorName,
                SortName = authorName,
                NameLastFirst = authorName,
                SortNameLastFirst = authorName,
                Ratings = new Ratings { Votes = 0, Value = 0 }
            };

            var book = new Book
            {
                ForeignBookId = bookForeignId,
                TitleSlug = bookForeignId.ToUrlSlug(),
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

            var mappedEdition = new Edition
            {
                ForeignEditionId = $"openlibrary:edition:{editionKey}",
                TitleSlug = $"openlibrary:edition:{editionKey}".ToUrlSlug(),
                Title = title,
                Isbn13 = isbn13,
                Publisher = edition.Publishers?.FirstOrDefault()?.Trim(),
                PageCount = edition.NumberOfPages ?? 0,
                ReleaseDate = releaseDate,
                IsEbook = true,
                Format = "Ebook",
                Book = book,
                Ratings = new Ratings()
            };

            var editionCoverUrl = BuildBookCoverUrl(edition.Covers?.FirstOrDefault());
            if (editionCoverUrl.IsNotNullOrWhiteSpace())
            {
                mappedEdition.Images.Add(new MediaCover.MediaCover
                {
                    Url = editionCoverUrl,
                    CoverType = MediaCoverTypes.Cover
                });
            }

            book.Editions = new List<Edition> { mappedEdition };

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
            var authorKey = NormalizeAuthorKey(doc.AuthorKeys?.FirstOrDefault(x => x.IsNotNullOrWhiteSpace()));

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
                TitleSlug = $"openlibrary:work:{workKey}".ToUrlSlug(),
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

            var mappedEdition = new Edition
            {
                ForeignEditionId = $"openlibrary:edition:{workKey}",
                TitleSlug = $"openlibrary:edition:{workKey}".ToUrlSlug(),
                Title = title,
                Isbn13 = isbn13,
                ReleaseDate = book.ReleaseDate,
                IsEbook = true,
                Format = "Ebook",
                Book = book,
                Ratings = new Ratings()
            };

            var coverUrl = BuildBookCoverUrl(doc.CoverId);
            if (coverUrl.IsNotNullOrWhiteSpace())
            {
                mappedEdition.Images.Add(new MediaCover.MediaCover
                {
                    Url = coverUrl,
                    CoverType = MediaCoverTypes.Cover
                });
            }

            book.Editions = new List<Edition> { mappedEdition };

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

        private static string NormalizeAuthorKey(string authorKey)
        {
            if (authorKey.IsNullOrWhiteSpace())
            {
                return null;
            }

            var normalized = authorKey.Trim();
            if (normalized.StartsWith("openlibrary:author:", StringComparison.InvariantCultureIgnoreCase))
            {
                normalized = normalized.Substring("openlibrary:author:".Length);
            }

            if (normalized.StartsWith("/authors/", StringComparison.InvariantCultureIgnoreCase))
            {
                normalized = normalized.Substring("/authors/".Length);
            }

            normalized = normalized.Trim();
            var canonical = OpenLibraryIdNormalizer.NormalizeAuthorId(normalized);

            return canonical.IsNotNullOrWhiteSpace() ? canonical : normalized;
        }

        private static Author MapAuthor(OpenLibraryAuthorResource resource, string authorKey)
        {
            if (resource == null)
            {
                return null;
            }

            var normalizedKey = NormalizeAuthorKey(resource.Key).IsNotNullOrWhiteSpace()
                ? NormalizeAuthorKey(resource.Key)
                : NormalizeAuthorKey(authorKey);

            if (normalizedKey.IsNullOrWhiteSpace())
            {
                return null;
            }

            var authorName = resource.Name.IsNotNullOrWhiteSpace() ? resource.Name.Trim() : normalizedKey;
            var metadata = new AuthorMetadata
            {
                ForeignAuthorId = $"openlibrary:author:{normalizedKey}",
                OpenLibraryAuthorId = normalizedKey,
                TitleSlug = normalizedKey,
                Name = authorName,
                SortName = authorName,
                NameLastFirst = authorName,
                SortNameLastFirst = authorName,
                Overview = GetAuthorBio(resource.Bio),
                Status = AuthorStatusType.Continuing,
                Ratings = new Ratings()
            };

            var imageUrl = BuildAuthorCoverUrl(resource.Photos?.FirstOrDefault());
            if (imageUrl.IsNotNullOrWhiteSpace())
            {
                metadata.Images.Add(new MediaCover.MediaCover
                {
                    Url = imageUrl,
                    CoverType = MediaCoverTypes.Poster
                });
            }

            metadata.Links.Add(new Links
            {
                Url = $"https://openlibrary.org/authors/{normalizedKey}",
                Name = "Open Library"
            });

            return new Author
            {
                ForeignAuthorId = metadata.ForeignAuthorId,
                Metadata = metadata,
                AuthorMetadataId = metadata.Id,
                CleanName = Parser.Parser.CleanAuthorName(metadata.Name),
                Books = new List<Book>(),
                Series = new List<Series>()
            };
        }

        private static string GetAuthorBio(object bio)
        {
            if (bio is string raw)
            {
                return raw;
            }

            if (bio is OpenLibraryTextValueResource wrapped)
            {
                return wrapped.Value;
            }

            return null;
        }

        private static string BuildBookCoverUrl(int? coverId)
        {
            if (!coverId.HasValue || coverId.Value <= 0)
            {
                return null;
            }

            return $"https://covers.openlibrary.org/b/id/{coverId.Value}-L.jpg";
        }

        private static string BuildAuthorCoverUrl(int? coverId)
        {
            if (!coverId.HasValue || coverId.Value <= 0)
            {
                return null;
            }

            return $"https://covers.openlibrary.org/a/id/{coverId.Value}-L.jpg";
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

        [JsonProperty("covers")]
        public List<int> Covers { get; set; }
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

        [JsonProperty("cover_i")]
        public int? CoverId { get; set; }
    }

    public class OpenLibraryAuthorResource
    {
        [JsonProperty("key")]
        public string Key { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("bio")]
        public object Bio { get; set; }

        [JsonProperty("photos")]
        public List<int> Photos { get; set; }
    }

    public class OpenLibraryTextValueResource
    {
        [JsonProperty("value")]
        public string Value { get; set; }
    }
}
