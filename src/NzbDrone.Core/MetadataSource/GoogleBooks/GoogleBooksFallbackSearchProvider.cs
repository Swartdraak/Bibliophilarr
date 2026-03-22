using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Http;
using NzbDrone.Core.Books;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Exceptions;
using NzbDrone.Core.MediaCover;

namespace NzbDrone.Core.MetadataSource.GoogleBooks
{
    public class GoogleBooksFallbackSearchProvider :
        IBookSearchFallbackProvider,
        IMetadataProvider,
        IProvideAuthorInfo,
        IProvideBookInfo,
        ISearchForNewBook,
        ISearchForNewAuthor,
        ISearchForNewEntity
    {
        private const int MaxGooglePageSize = 40;
        private const int MaxAuthorVolumes = 400;

        private readonly IConfigService _configService;
        private readonly IHttpClient _httpClient;
        private readonly IHttpRequestBuilderFactory _requestBuilder;
        private readonly Logger _logger;

        public GoogleBooksFallbackSearchProvider(IConfigService configService, IHttpClient httpClient, Logger logger)
        {
            _configService = configService;
            _httpClient = httpClient;
            _logger = logger;

            _requestBuilder = new HttpRequestBuilder("https://www.googleapis.com/books/v1/volumes")
                .Accept(HttpAccept.Json)
                .CreateFactory();
        }

        // ── IMetadataProvider ────────────────────────────────────────────────
        public string ProviderName => "GoogleBooks";
        public int Priority => 3;
        public bool IsEnabled => _configService.EnableGoogleBooksProvider;
        public bool SupportsAuthorSearch => true;
        public bool SupportsBookSearch => true;
        public bool SupportsIsbnLookup => true;
        public bool SupportsSeriesInfo => false;
        public bool SupportsCoverImages => true;

        public ProviderRateLimitInfo RateLimitInfo => new ProviderRateLimitInfo
        {
            MaxRequests = _configService.GoogleBooksApiKey.IsNotNullOrWhiteSpace() ? 120 : 30,
            TimeWindow = TimeSpan.FromMinutes(1),
            SupportsAuthentication = true
        };

        // ── IBookSearchFallbackProvider + ISearchForNewBook ───────────────
        public List<Book> Search(string title, string author)
        {
            if (!_configService.EnableGoogleBooksFallback)
            {
                return new List<Book>();
            }

            var query = BuildQuery(title, author);
            return SearchVolumes(query, 10, 0)
                .Select(MapBook)
                .Where(b => b != null)
                .DistinctBy(x => x.ForeignBookId)
                .ToList();
        }

        public List<Book> SearchForNewBook(string title, string author, bool getAllEditions = true)
        {
            var query = BuildQuery(title, author);
            if (query.IsNullOrWhiteSpace())
            {
                return new List<Book>();
            }

            return SearchVolumes(query, MaxGooglePageSize, 0)
                .Select(MapBook)
                .Where(b => b != null)
                .DistinctBy(x => x.ForeignBookId)
                .ToList();
        }

        public List<Book> SearchByIsbn(string isbn)
        {
            if (isbn.IsNullOrWhiteSpace())
            {
                return new List<Book>();
            }

            return SearchVolumes($"isbn:{isbn.Trim()}", MaxGooglePageSize, 0)
                .Select(MapBook)
                .Where(b => b != null)
                .DistinctBy(x => x.ForeignBookId)
                .ToList();
        }

        public List<Book> SearchByAsin(string asin)
        {
            if (asin.IsNullOrWhiteSpace())
            {
                return new List<Book>();
            }

            return SearchVolumes(asin.Trim(), MaxGooglePageSize, 0)
                .Select(MapBook)
                .Where(b => b != null)
                .DistinctBy(x => x.ForeignBookId)
                .ToList();
        }

        public List<Book> SearchByExternalId(string idType, string id)
        {
            if (idType.IsNullOrWhiteSpace() || id.IsNullOrWhiteSpace())
            {
                return new List<Book>();
            }

            var normalizedIdType = idType.Trim().ToLowerInvariant();

            if (normalizedIdType == "isbn")
            {
                return SearchByIsbn(id);
            }

            if (normalizedIdType == "asin")
            {
                return SearchByAsin(id);
            }

            if (normalizedIdType == "googlebooks" || normalizedIdType == "volume")
            {
                var volume = GetVolume(NormalizeGoogleBookToken(id));
                var book = MapBook(volume);
                return book != null ? new List<Book> { book } : new List<Book>();
            }

            return new List<Book>();
        }

        // ── IProvideBookInfo ─────────────────────────────────────────────────
        public Tuple<string, Book, List<AuthorMetadata>> GetBookInfo(string foreignBookId)
        {
            var token = NormalizeGoogleBookToken(foreignBookId);

            var volume = GetVolume(token);

            if (volume == null)
            {
                throw new BookNotFoundException(foreignBookId);
            }

            var mapped = MapBook(volume);
            if (mapped == null)
            {
                throw new BookNotFoundException(foreignBookId);
            }

            var metadata = mapped.AuthorMetadata?.Value;
            var metadataList = metadata != null ? new List<AuthorMetadata> { metadata } : new List<AuthorMetadata>();

            return Tuple.Create(metadata?.ForeignAuthorId ?? "googlebooks:author:unknown", mapped, metadataList);
        }

        // ── IProvideAuthorInfo ───────────────────────────────────────────────
        public Author GetAuthorInfo(string foreignAuthorId, bool useCache = true)
        {
            var authorName = DecodeAuthorToken(foreignAuthorId);
            if (authorName.IsNullOrWhiteSpace())
            {
                throw new AuthorNotFoundException(foreignAuthorId);
            }

            var volumes = SearchVolumes($"inauthor:{authorName}", MaxGooglePageSize, 0, MaxAuthorVolumes);
            var books = volumes
                .Select(MapBook)
                .Where(b => b != null)
                .Where(b => IsMatchingAuthor(b.AuthorMetadata?.Value?.Name, authorName))
                .DistinctBy(x => x.ForeignBookId)
                .ToList();

            if (!books.Any())
            {
                throw new AuthorNotFoundException(foreignAuthorId);
            }

            var primaryMetadata = books.First().AuthorMetadata.Value;
            primaryMetadata.ForeignAuthorId = BuildGoogleAuthorId(authorName);
            primaryMetadata.Name = authorName;

            foreach (var book in books)
            {
                book.AuthorMetadata = primaryMetadata;
                if (book.Author?.Value != null)
                {
                    book.Author.Value.Metadata = primaryMetadata;
                }
            }

            return new Author
            {
                Metadata = primaryMetadata,
                CleanName = Parser.Parser.CleanAuthorName(authorName),
                Books = books,
                Series = new List<Series>()
            };
        }

        public HashSet<string> GetChangedAuthors(DateTime startTime)
        {
            _logger.Debug("GoogleBooks does not expose changed-author feed");
            return null;
        }

        // ── ISearchForNewAuthor / ISearchForNewEntity ──────────────────────
        public List<Author> SearchForNewAuthor(string title)
        {
            if (title.IsNullOrWhiteSpace())
            {
                return new List<Author>();
            }

            return SearchVolumes($"inauthor:{title.Trim()}", 20, 0)
                .Select(x => x?.VolumeInfo?.Authors?.FirstOrDefault(a => a.IsNotNullOrWhiteSpace())?.Trim())
                .Where(x => x.IsNotNullOrWhiteSpace())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(BuildAuthorFromName)
                .Where(x => x != null)
                .ToList();
        }

        public List<object> SearchForNewEntity(string title)
        {
            var books = SearchForNewBook(title, null, false);
            var output = new List<object>();

            foreach (var book in books)
            {
                if (book?.Author?.Value != null && !output.Contains(book.Author.Value))
                {
                    output.Add(book.Author.Value);
                }

                if (book != null)
                {
                    output.Add(book);
                }
            }

            return output;
        }

        private static string BuildQuery(string title, string author)
        {
            var queryParts = new List<string>();

            if (title.IsNotNullOrWhiteSpace())
            {
                queryParts.Add($"intitle:{title.Trim()}");
            }

            if (author.IsNotNullOrWhiteSpace())
            {
                queryParts.Add($"inauthor:{author.Trim()}");
            }

            return queryParts.ConcatToString(" ").Trim();
        }

        private List<GoogleBooksVolumeResource> SearchVolumes(string query, int maxResults, int startIndex, int maxItems = 200)
        {
            if (query.IsNullOrWhiteSpace())
            {
                return new List<GoogleBooksVolumeResource>();
            }

            var allItems = new List<GoogleBooksVolumeResource>();
            var pageStart = Math.Max(0, startIndex);
            var pageSize = Math.Max(1, Math.Min(MaxGooglePageSize, maxResults));
            var safeMaxItems = Math.Max(pageSize, maxItems);

            while (allItems.Count < safeMaxItems)
            {
                var request = _requestBuilder.Create()
                    .AddQueryParam("q", query)
                    .AddQueryParam("langRestrict", "en")
                    .AddQueryParam("maxResults", pageSize.ToString())
                    .AddQueryParam("startIndex", pageStart.ToString())
                    .WithRateLimit(1);

                if (_configService.GoogleBooksApiKey.IsNotNullOrWhiteSpace())
                {
                    request.AddQueryParam("key", _configService.GoogleBooksApiKey.Trim());
                }

                var httpRequest = request.Build();
                httpRequest.RateLimitKey = ProviderName;
                httpRequest.RequestTimeout = TimeSpan.FromSeconds(Math.Max(5, _configService.MetadataProviderTimeoutSeconds));
                httpRequest.SuppressHttpError = true;

                GoogleBooksSearchResponse response;

                try
                {
                    response = _httpClient.Get<GoogleBooksSearchResponse>(httpRequest).Resource;
                }
                catch (Exception ex)
                {
                    _logger.Warn(ex, "GoogleBooks search failed for query '{0}'", query);
                    break;
                }

                var page = response?.Items ?? new List<GoogleBooksVolumeResource>();

                if (!page.Any())
                {
                    break;
                }

                allItems.AddRange(page);

                if (page.Count < pageSize)
                {
                    break;
                }

                pageStart += page.Count;
            }

            return allItems.Take(safeMaxItems).ToList();
        }

        private GoogleBooksVolumeResource GetVolume(string token)
        {
            if (token.IsNullOrWhiteSpace())
            {
                return null;
            }

            try
            {
                var request = new HttpRequestBuilder($"https://www.googleapis.com/books/v1/volumes/{Uri.EscapeDataString(token)}")
                    .Accept(HttpAccept.Json);

                if (_configService.GoogleBooksApiKey.IsNotNullOrWhiteSpace())
                {
                    request.AddQueryParam("key", _configService.GoogleBooksApiKey.Trim());
                }

                var httpRequest = request.Build();
                httpRequest.RateLimitKey = ProviderName;
                httpRequest.RequestTimeout = TimeSpan.FromSeconds(Math.Max(5, _configService.MetadataProviderTimeoutSeconds));
                httpRequest.SuppressHttpError = true;

                return _httpClient.Get<GoogleBooksVolumeResource>(httpRequest).Resource;
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "GoogleBooks get volume failed for token '{0}'", token);
                return null;
            }
        }

        private static Book MapBook(GoogleBooksVolumeResource item)
        {
            if (item?.Id.IsNullOrWhiteSpace() ?? true)
            {
                return null;
            }

            var volume = item.VolumeInfo ?? new GoogleBooksVolumeInfoResource();
            var title = volume.Title.IsNotNullOrWhiteSpace() ? volume.Title.Trim() : item.Id;
            var authorName = volume.Authors?.FirstOrDefault(a => a.IsNotNullOrWhiteSpace())?.Trim() ?? "Unknown Author";

            var publishedDate = ParsePublishedDate(volume.PublishedDate);
            var isbn13 = volume.IndustryIdentifiers?
                .FirstOrDefault(x => string.Equals(x.Type, "ISBN_13", StringComparison.InvariantCultureIgnoreCase))?.Identifier;
            var asin = volume.IndustryIdentifiers?
                .FirstOrDefault(x => string.Equals(x.Type, "OTHER", StringComparison.InvariantCultureIgnoreCase) && x.Identifier.IsNotNullOrWhiteSpace() && x.Identifier.StartsWith("B0", StringComparison.InvariantCultureIgnoreCase))?.Identifier;

            var authorMetadata = new AuthorMetadata
            {
                ForeignAuthorId = BuildGoogleAuthorId(authorName),
                Name = authorName,
                SortName = authorName.ToLowerInvariant(),
                NameLastFirst = authorName.ToLastFirst(),
                SortNameLastFirst = authorName.ToLastFirst().ToLowerInvariant(),
                Overview = volume.Description,
                Ratings = new Ratings { Votes = 0, Value = 0 }
            };

            var author = new Author
            {
                Metadata = authorMetadata,
                AuthorMetadataId = authorMetadata.Id
            };

            var book = new Book
            {
                ForeignBookId = $"googlebooks:work:{item.Id}",
                TitleSlug = $"googlebooks:work:{item.Id}",
                Title = title,
                CleanTitle = title,
                Author = author,
                AuthorMetadata = authorMetadata,
                AuthorMetadataId = authorMetadata.Id,
                ReleaseDate = publishedDate,
                Ratings = new Ratings
                {
                    Votes = volume.RatingsCount ?? 0,
                    Value = (decimal)(volume.AverageRating ?? 0)
                },
                Genres = volume.Categories ?? new List<string>()
            };

            var edition = new Edition
            {
                ForeignEditionId = $"googlebooks:edition:{item.Id}",
                TitleSlug = $"googlebooks:edition:{item.Id}",
                Title = title,
                Publisher = volume.Publisher,
                ReleaseDate = publishedDate,
                Language = volume.Language,
                Isbn13 = isbn13,
                Asin = asin,
                Format = volume.PrintType,
                IsEbook = string.Equals(volume.PrintType, "EBOOK", StringComparison.InvariantCultureIgnoreCase),
                Book = book,
                PageCount = volume.PageCount ?? 0,
                Overview = volume.Description,
                Ratings = new Ratings
                {
                    Votes = volume.RatingsCount ?? 0,
                    Value = (decimal)(volume.AverageRating ?? 0)
                }
            };

            var coverUrl = volume.ImageLinks?.Large
                           ?? volume.ImageLinks?.Medium
                           ?? volume.ImageLinks?.Thumbnail
                           ?? volume.ImageLinks?.SmallThumbnail;

            if (coverUrl.IsNotNullOrWhiteSpace())
            {
                edition.Images.Add(new MediaCover.MediaCover
                {
                    Url = coverUrl,
                    CoverType = MediaCoverTypes.Cover
                });
            }

            if (volume.InfoLink.IsNotNullOrWhiteSpace())
            {
                book.Links.Add(new Links { Name = "Google Books", Url = volume.InfoLink });
                edition.Links.Add(new Links { Name = "Google Books", Url = volume.InfoLink });
            }

            book.Editions = new List<Edition> { edition };

            return book;
        }

        private static DateTime? ParsePublishedDate(string rawDate)
        {
            if (rawDate.IsNullOrWhiteSpace())
            {
                return null;
            }

            var formats = new[] { "yyyy-MM-dd", "yyyy-MM", "yyyy" };
            foreach (var format in formats)
            {
                if (DateTime.TryParseExact(rawDate, format, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed))
                {
                    return parsed;
                }
            }

            return null;
        }

        private static string NormalizeId(string value)
        {
            var normalized = value.ToLowerInvariant().Trim();
            normalized = string.Join("-", normalized.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries));

            return new string(normalized.Where(c => char.IsLetterOrDigit(c) || c == '-').ToArray());
        }

        private static string BuildGoogleAuthorId(string authorName)
        {
            var value = authorName.IsNotNullOrWhiteSpace() ? authorName.Trim() : "unknown";
            return $"googlebooks:author:{Uri.EscapeDataString(value)}";
        }

        private static string DecodeAuthorToken(string foreignAuthorId)
        {
            if (foreignAuthorId.IsNullOrWhiteSpace())
            {
                return null;
            }

            const string prefix = "googlebooks:author:";
            var raw = foreignAuthorId.Trim();
            if (raw.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                raw = raw.Substring(prefix.Length);
            }

            return Uri.UnescapeDataString(raw).Replace('+', ' ').Trim();
        }

        private static string NormalizeGoogleBookToken(string foreignBookId)
        {
            if (foreignBookId.IsNullOrWhiteSpace())
            {
                return foreignBookId;
            }

            var normalized = foreignBookId.Trim();
            var prefixes = new[]
            {
                "googlebooks:work:",
                "googlebooks:edition:",
                "googlebooks:volume:"
            };

            foreach (var prefix in prefixes)
            {
                if (normalized.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    return normalized.Substring(prefix.Length);
                }
            }

            return normalized;
        }

        private static Author BuildAuthorFromName(string name)
        {
            if (name.IsNullOrWhiteSpace())
            {
                return null;
            }

            var metadata = new AuthorMetadata
            {
                ForeignAuthorId = BuildGoogleAuthorId(name),
                Name = name.Trim(),
                SortName = name.Trim().ToLowerInvariant(),
                NameLastFirst = name.Trim().ToLastFirst(),
                SortNameLastFirst = name.Trim().ToLastFirst().ToLowerInvariant(),
                Ratings = new Ratings { Votes = 0, Value = 0 }
            };

            return new Author
            {
                Metadata = metadata,
                CleanName = Parser.Parser.CleanAuthorName(metadata.Name)
            };
        }

        private static bool IsMatchingAuthor(string candidateName, string expectedName)
        {
            if (candidateName.IsNullOrWhiteSpace() || expectedName.IsNullOrWhiteSpace())
            {
                return false;
            }

            return candidateName.Trim().Equals(expectedName.Trim(), StringComparison.OrdinalIgnoreCase);
        }
    }

    public class GoogleBooksSearchResponse
    {
        public int TotalItems { get; set; }
        public List<GoogleBooksVolumeResource> Items { get; set; }
    }

    public class GoogleBooksVolumeResource
    {
        public string Id { get; set; }
        public GoogleBooksVolumeInfoResource VolumeInfo { get; set; }
    }

    public class GoogleBooksVolumeInfoResource
    {
        public string Title { get; set; }
        public string Subtitle { get; set; }
        public List<string> Authors { get; set; }
        public string PublishedDate { get; set; }
        public string Publisher { get; set; }
        public string Language { get; set; }
        public string PrintType { get; set; }
        public string Description { get; set; }
        public int? PageCount { get; set; }
        public double? AverageRating { get; set; }
        public int? RatingsCount { get; set; }
        public List<string> Categories { get; set; }
        public string InfoLink { get; set; }
        public List<GoogleBooksIndustryIdentifierResource> IndustryIdentifiers { get; set; }
        public GoogleBooksImageLinksResource ImageLinks { get; set; }

        [JsonExtensionData]
        public Dictionary<string, JsonElement> AdditionalData { get; set; }
    }

    public class GoogleBooksImageLinksResource
    {
        public string SmallThumbnail { get; set; }
        public string Thumbnail { get; set; }
        public string Medium { get; set; }
        public string Large { get; set; }
    }

    public class GoogleBooksIndustryIdentifierResource
    {
        public string Type { get; set; }
        public string Identifier { get; set; }
    }
}
