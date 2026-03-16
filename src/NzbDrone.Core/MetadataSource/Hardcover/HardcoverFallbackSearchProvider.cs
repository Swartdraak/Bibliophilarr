using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Http;
using NzbDrone.Common.Serializer;
using NzbDrone.Core.Books;
using NzbDrone.Core.Configuration;

namespace NzbDrone.Core.MetadataSource.Hardcover
{
    public class HardcoverFallbackSearchProvider : IBookSearchFallbackProvider
    {
        private const string Endpoint = "https://api.hardcover.app/v1/graphql";

        private readonly IConfigService _configService;
        private readonly IHttpClient _httpClient;

        public HardcoverFallbackSearchProvider(IConfigService configService, IHttpClient httpClient)
        {
            _configService = configService;
            _httpClient = httpClient;
        }

        public string ProviderName => "Hardcover";

        public ProviderRateLimitInfo RateLimitInfo => new ProviderRateLimitInfo
        {
            MaxRequests = 60,
            TimeWindow = TimeSpan.FromMinutes(1),
            SupportsAuthentication = true
        };

        public List<Book> Search(string title, string author)
        {
            if (!_configService.EnableHardcoverFallback)
            {
                return new List<Book>();
            }

            var token = _configService.HardcoverApiToken?.Trim();
            if (token.IsNullOrWhiteSpace())
            {
                return new List<Book>();
            }

            token = NormalizeBearerToken(token);

            var queryText = BuildSearchQuery(title, author);
            if (queryText.IsNullOrWhiteSpace())
            {
                return new List<Book>();
            }

            var request = new HttpRequest(Endpoint, HttpAccept.Json)
            {
                Method = HttpMethod.Post,
                RateLimit = TimeSpan.FromSeconds(1),
                RateLimitKey = ProviderName
            };

            if (_configService.HardcoverRequestTimeoutSeconds > 0)
            {
                request.RequestTimeout = TimeSpan.FromSeconds(_configService.HardcoverRequestTimeoutSeconds);
            }

            request.Headers.ContentType = "application/json; charset=utf-8";
            request.Headers["authorization"] = $"Bearer {token}";
            request.SetContent(new
            {
                query = "query SearchBooks($query: String!, $perPage: Int!, $page: Int!) { search(query: $query, query_type: \"Book\", per_page: $perPage, page: $page) { results { id title release_year slug contributors { author { name } } editions(limit: 5) { id title isbn_13 asin reading_format audio_seconds release_date publisher { name } language { code } } } } }",
                variables = new
                {
                    query = queryText,
                    perPage = 10,
                    page = 1
                }
            }.ToJson());

            var response = _httpClient.Post<HardcoverGraphQlResponse>(request);
            var results = response.Resource?.Data?.Search?.Results ?? new List<HardcoverSearchResult>();

            return results.Select(MapBook)
                .Where(x => x != null)
                .ToList();
        }

        private static string BuildSearchQuery(string title, string author)
        {
            var parts = new List<string>();

            if (title.IsNotNullOrWhiteSpace())
            {
                parts.Add(title.Trim());
            }

            if (author.IsNotNullOrWhiteSpace())
            {
                parts.Add(author.Trim());
            }

            return string.Join(" ", parts).Trim();
        }

        private static string NormalizeBearerToken(string token)
        {
            if (token.IsNullOrWhiteSpace())
            {
                return string.Empty;
            }

            if (token.StartsWith("Bearer ", StringComparison.InvariantCultureIgnoreCase))
            {
                return token.Substring("Bearer ".Length).Trim();
            }

            return token.Trim();
        }

        private static Book MapBook(HardcoverSearchResult result)
        {
            if (result == null || result.Id.IsNullOrWhiteSpace())
            {
                return null;
            }

            var title = result.Title.IsNotNullOrWhiteSpace() ? result.Title.Trim() : result.Id;
            var authorName = result.Contributors?
                                 .Select(x => x?.Author?.Name)
                                 .FirstOrDefault(x => x.IsNotNullOrWhiteSpace())?.Trim() ?? "Unknown Author";

            var publishedDate = ParseDate(result.ReleaseDate, result.ReleaseYear);
            var authorMetadata = new AuthorMetadata
            {
                ForeignAuthorId = $"hardcover:author:{NormalizeId(authorName)}",
                Name = authorName,
                SortName = authorName,
                NameLastFirst = authorName,
                SortNameLastFirst = authorName
            };

            var book = new Book
            {
                ForeignBookId = $"hardcover:work:{result.Id}",
                Title = title,
                CleanTitle = title,
                AuthorMetadata = authorMetadata,
                AuthorMetadataId = authorMetadata.Id,
                Author = new Author
                {
                    Metadata = authorMetadata,
                    AuthorMetadataId = authorMetadata.Id
                },
                ReleaseDate = publishedDate,
                Ratings = new Ratings()
            };

            var editions = result.Editions ?? new List<HardcoverEditionResult>();
            if (editions.Any())
            {
                book.Editions = editions.Select(x => MapEdition(x, book, title, publishedDate)).ToList();
            }
            else
            {
                book.Editions = new List<Edition>
                {
                    new Edition
                    {
                        ForeignEditionId = $"hardcover:edition:{result.Id}",
                        Title = title,
                        ReleaseDate = publishedDate,
                        IsEbook = true,
                        Format = "Ebook",
                        Book = book,
                        Ratings = new Ratings()
                    }
                };
            }

            return book;
        }

        private static Edition MapEdition(HardcoverEditionResult edition, Book book, string fallbackTitle, DateTime? fallbackReleaseDate)
        {
            var title = edition?.Title.IsNotNullOrWhiteSpace() == true ? edition.Title.Trim() : fallbackTitle;
            var releaseDate = ParseDate(edition?.ReleaseDate, null) ?? fallbackReleaseDate;
            var format = edition?.ReadingFormat.IsNotNullOrWhiteSpace() == true ? edition.ReadingFormat : "Ebook";

            return new Edition
            {
                ForeignEditionId = $"hardcover:edition:{edition?.Id ?? book.ForeignBookId}",
                Title = title,
                Publisher = edition?.Publisher?.Name,
                Language = edition?.Language?.Code,
                Isbn13 = edition?.Isbn13,
                Asin = edition?.Asin,
                Format = format,
                IsEbook = !string.Equals(format, "Audiobook", StringComparison.InvariantCultureIgnoreCase) || (edition?.AudioSeconds ?? 0) <= 0,
                ReleaseDate = releaseDate,
                Book = book,
                Ratings = new Ratings()
            };
        }

        private static DateTime? ParseDate(string rawDate, int? releaseYear)
        {
            if (rawDate.IsNotNullOrWhiteSpace())
            {
                if (DateTime.TryParse(rawDate.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed))
                {
                    return parsed;
                }
            }

            if (releaseYear.HasValue && releaseYear.Value > 0)
            {
                return new DateTime(releaseYear.Value, 1, 1);
            }

            return null;
        }

        private static string NormalizeId(string value)
        {
            var normalized = value.ToLowerInvariant().Trim();
            normalized = string.Join("-", normalized.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries));

            return new string(normalized.Where(c => char.IsLetterOrDigit(c) || c == '-').ToArray());
        }
    }

    public class HardcoverGraphQlResponse
    {
        public HardcoverGraphQlData Data { get; set; }
    }

    public class HardcoverGraphQlData
    {
        public HardcoverSearchPayload Search { get; set; }
    }

    public class HardcoverSearchPayload
    {
        public List<HardcoverSearchResult> Results { get; set; }
    }

    public class HardcoverSearchResult
    {
        public string Id { get; set; }
        public string Title { get; set; }
        public int? ReleaseYear { get; set; }
        public string ReleaseDate { get; set; }
        public string Slug { get; set; }
        public List<HardcoverContributorResult> Contributors { get; set; }
        public List<HardcoverEditionResult> Editions { get; set; }
    }

    public class HardcoverContributorResult
    {
        public HardcoverAuthorResult Author { get; set; }
    }

    public class HardcoverAuthorResult
    {
        public string Name { get; set; }
    }

    public class HardcoverEditionResult
    {
        public string Id { get; set; }
        public string Title { get; set; }
        public string Isbn13 { get; set; }
        public string Asin { get; set; }
        public string ReadingFormat { get; set; }
        public int? AudioSeconds { get; set; }
        public string ReleaseDate { get; set; }
        public HardcoverPublisherResult Publisher { get; set; }
        public HardcoverLanguageResult Language { get; set; }
    }

    public class HardcoverPublisherResult
    {
        public string Name { get; set; }
    }

    public class HardcoverLanguageResult
    {
        public string Code { get; set; }
    }
}
