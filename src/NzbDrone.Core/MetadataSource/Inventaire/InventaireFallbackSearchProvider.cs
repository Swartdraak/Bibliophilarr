using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Http;
using NzbDrone.Core.Books;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.MediaCover;

namespace NzbDrone.Core.MetadataSource.Inventaire
{
    public class InventaireFallbackSearchProvider : IBookSearchFallbackProvider
    {
        private readonly IConfigService _configService;
        private readonly IHttpClient _httpClient;
        private readonly IHttpRequestBuilderFactory _requestBuilder;
        private readonly Logger _logger;

        public InventaireFallbackSearchProvider(IConfigService configService, IHttpClient httpClient, Logger logger)
        {
            _configService = configService;
            _httpClient = httpClient;
            _logger = logger;

            _requestBuilder = new HttpRequestBuilder("https://inventaire.io/api/search")
                .Accept(HttpAccept.Json)
                .CreateFactory();
        }

        public string ProviderName => "Inventaire";

        public ProviderRateLimitInfo RateLimitInfo => new ProviderRateLimitInfo
        {
            MaxRequests = 40,
            TimeWindow = TimeSpan.FromMinutes(1),
            SupportsAuthentication = false
        };

        public List<Book> Search(string title, string author)
        {
            if (!_configService.EnableInventaireFallback)
            {
                return new List<Book>();
            }

            var query = BuildQuery(title, author);
            if (query.IsNullOrWhiteSpace())
            {
                return new List<Book>();
            }

            var request = _requestBuilder.Create()
                .AddQueryParam("types", "works")
                .AddQueryParam("search", query)
                .AddQueryParam("lang", "en")
                .AddQueryParam("limit", "10")
                .WithRateLimit(1)
                .Build();

            request.RateLimitKey = ProviderName;
            request.RequestTimeout = TimeSpan.FromSeconds(Math.Max(5, _configService.MetadataProviderTimeoutSeconds));

            var response = _httpClient.Get<InventaireSearchResponse>(request);
            var results = response.Resource?.Results ?? new List<InventaireSearchResult>();

            ValidateSearchResults(results, query);

            return results.Select(MapBook)
                .Where(x => x != null)
                .ToList();
        }

        private void ValidateSearchResults(List<InventaireSearchResult> results, string queryText)
        {
            if (!results.Any())
            {
                return;
            }

            var missingUris = results.Count(r => r.Uri.IsNullOrWhiteSpace());
            var missingLabels = results.Count(r =>
                r.Label.IsNullOrWhiteSpace() &&
                r.Name.IsNullOrWhiteSpace() &&
                r.Title.IsNullOrWhiteSpace());

            if (missingUris > 0)
            {
                _logger.Warn("Inventaire returned {0}/{1} result(s) with missing URIs for query '{2}' — these will be skipped", missingUris, results.Count, queryText);
            }

            if (missingLabels > 0)
            {
                _logger.Debug("Inventaire returned {0}/{1} result(s) with no title/label for query '{2}' — work ID will be used as fallback", missingLabels, results.Count, queryText);
            }
        }

        private static string BuildQuery(string title, string author)
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

        private static Book MapBook(InventaireSearchResult result)
        {
            var workId = NormalizeWorkId(result?.Uri);
            if (workId.IsNullOrWhiteSpace())
            {
                return null;
            }

            var title = FirstNonEmpty(result.Label, result.Name, result.Title, workId);
            var authorName = SelectAuthorName(result);
            var authorId = NormalizeId(authorName);

            var authorMetadata = new AuthorMetadata
            {
                ForeignAuthorId = $"inventaire:author:{authorId}",
                Name = authorName,
                SortName = authorName,
                NameLastFirst = authorName,
                SortNameLastFirst = authorName
            };

            AddAuthorImage(authorMetadata, result);

            var author = new Author
            {
                Metadata = authorMetadata,
                AuthorMetadataId = authorMetadata.Id
            };

            var book = new Book
            {
                ForeignBookId = $"inventaire:work:{workId}",
                TitleSlug = $"inventaire:work:{workId}",
                Title = title,
                CleanTitle = title,
                Author = author,
                AuthorMetadata = authorMetadata,
                AuthorMetadataId = authorMetadata.Id,
                ReleaseDate = null,
                Ratings = new Ratings()
            };

            var edition = new Edition
            {
                ForeignEditionId = $"inventaire:edition:{workId}",
                TitleSlug = $"inventaire:edition:{workId}",
                Title = title,
                Isbn13 = result.Isbn13,
                IsEbook = true,
                Format = "Ebook",
                Overview = result.Description ?? string.Empty,
                Book = book,
                Ratings = new Ratings()
            };

            var coverUrl = SelectCoverUrl(result);
            if (coverUrl.IsNotNullOrWhiteSpace())
            {
                edition.Images.Add(new MediaCover.MediaCover
                {
                    Url = coverUrl.Trim(),
                    CoverType = MediaCoverTypes.Cover
                });
            }

            book.Editions = new List<Edition> { edition };

            return book;
        }

        private static string SelectAuthorName(InventaireSearchResult result)
        {
            var contributor = result?.Contributors?.FirstOrDefault(c => c?.Name.IsNotNullOrWhiteSpace() == true)?.Name;
            return FirstNonEmpty(contributor, result?.Author, "Unknown Author");
        }

        private static void AddAuthorImage(AuthorMetadata authorMetadata, InventaireSearchResult result)
        {
            var url = FirstNonEmpty(result?.AuthorImage, result?.Picture, result?.ImageUrl);
            if (url.IsNullOrWhiteSpace())
            {
                return;
            }

            authorMetadata.Images.Add(new MediaCover.MediaCover
            {
                Url = url.Trim(),
                CoverType = MediaCoverTypes.Poster
            });
        }

        private static string SelectCoverUrl(InventaireSearchResult result)
        {
            return FirstNonEmpty(result?.CoverUrl, result?.Picture, result?.ImageUrl, result?.Thumbnail);
        }

        private static string NormalizeWorkId(string uri)
        {
            if (uri.IsNullOrWhiteSpace())
            {
                return null;
            }

            var normalized = uri.Trim();
            if (normalized.StartsWith("https://", StringComparison.InvariantCultureIgnoreCase) ||
                normalized.StartsWith("http://", StringComparison.InvariantCultureIgnoreCase))
            {
                normalized = new Uri(normalized).AbsolutePath;
            }

            var parts = normalized.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            return parts.LastOrDefault()?.Trim();
        }

        private static string NormalizeId(string value)
        {
            var normalized = value?.ToLowerInvariant().Trim() ?? "unknown-author";
            normalized = string.Join("-", normalized.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries));

            var filtered = new string(normalized.Where(c => char.IsLetterOrDigit(c) || c == '-').ToArray());
            return filtered.IsNotNullOrWhiteSpace() ? filtered : "unknown-author";
        }

        private static string FirstNonEmpty(params string[] values)
        {
            foreach (var value in values)
            {
                if (value.IsNotNullOrWhiteSpace())
                {
                    return value.Trim();
                }
            }

            return string.Empty;
        }
    }

    public class InventaireSearchResponse
    {
        [JsonProperty("results")]
        public List<InventaireSearchResult> Results { get; set; }
    }

    public class InventaireSearchResult
    {
        [JsonProperty("uri")]
        public string Uri { get; set; }

        [JsonProperty("label")]
        public string Label { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("title")]
        public string Title { get; set; }

        [JsonProperty("description")]
        public string Description { get; set; }

        [JsonProperty("author")]
        public string Author { get; set; }

        [JsonProperty("isbn13")]
        public string Isbn13 { get; set; }

        [JsonProperty("picture")]
        public string Picture { get; set; }

        [JsonProperty("image")]
        public string ImageUrl { get; set; }

        [JsonProperty("thumbnail")]
        public string Thumbnail { get; set; }

        [JsonProperty("cover")]
        public string CoverUrl { get; set; }

        [JsonProperty("authorPicture")]
        public string AuthorImage { get; set; }

        [JsonProperty("contributors")]
        public List<InventaireContributorResult> Contributors { get; set; }
    }

    public class InventaireContributorResult
    {
        [JsonProperty("name")]
        public string Name { get; set; }
    }
}
