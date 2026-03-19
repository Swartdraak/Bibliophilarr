using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Http;
using NzbDrone.Core.Books;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.MediaCover;

namespace NzbDrone.Core.MetadataSource.GoogleBooks
{
    public class GoogleBooksFallbackSearchProvider : IBookSearchFallbackProvider
    {
        private readonly IConfigService _configService;
        private readonly IHttpClient _httpClient;
        private readonly IHttpRequestBuilderFactory _requestBuilder;

        public GoogleBooksFallbackSearchProvider(IConfigService configService, IHttpClient httpClient)
        {
            _configService = configService;
            _httpClient = httpClient;

            _requestBuilder = new HttpRequestBuilder("https://www.googleapis.com/books/v1/volumes")
                .Accept(HttpAccept.Json)
                .CreateFactory();
        }

        public string ProviderName => "GoogleBooks";

        public ProviderRateLimitInfo RateLimitInfo => new ProviderRateLimitInfo
        {
            MaxRequests = _configService.GoogleBooksApiKey.IsNotNullOrWhiteSpace() ? 120 : 30,
            TimeWindow = TimeSpan.FromMinutes(1),
            SupportsAuthentication = true
        };

        public List<Book> Search(string title, string author)
        {
            if (!_configService.EnableGoogleBooksFallback)
            {
                return new List<Book>();
            }

            var query = BuildQuery(title, author);
            if (query.IsNullOrWhiteSpace())
            {
                return new List<Book>();
            }

            var request = _requestBuilder.Create()
                .AddQueryParam("q", query)
                .AddQueryParam("langRestrict", "en")
                .AddQueryParam("maxResults", "10")
                .WithRateLimit(1);

            if (_configService.GoogleBooksApiKey.IsNotNullOrWhiteSpace())
            {
                request.AddQueryParam("key", _configService.GoogleBooksApiKey.Trim());
            }

            var httpRequest = request.Build();
            httpRequest.RateLimitKey = ProviderName;

            var response = _httpClient.Get<GoogleBooksSearchResponse>(httpRequest);
            var items = response.Resource?.Items ?? new List<GoogleBooksVolumeResource>();

            return items.Select(MapBook)
                .Where(b => b != null)
                .ToList();
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
                ForeignAuthorId = $"googlebooks:author:{NormalizeId(authorName)}",
                Name = authorName,
                SortName = authorName,
                NameLastFirst = authorName,
                SortNameLastFirst = authorName
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
                Ratings = new Ratings()
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
                Format = string.Equals(volume.PrintType, "BOOK", StringComparison.InvariantCultureIgnoreCase) ? "Ebook" : volume.PrintType,
                IsEbook = true,
                Book = book,
                Ratings = new Ratings()
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
    }

    public class GoogleBooksSearchResponse
    {
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
        public List<string> Authors { get; set; }
        public string PublishedDate { get; set; }
        public string Publisher { get; set; }
        public string Language { get; set; }
        public string PrintType { get; set; }
        public List<GoogleBooksIndustryIdentifierResource> IndustryIdentifiers { get; set; }
        public GoogleBooksImageLinksResource ImageLinks { get; set; }
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
