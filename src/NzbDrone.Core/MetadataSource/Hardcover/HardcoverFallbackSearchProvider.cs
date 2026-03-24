using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Http;
using NzbDrone.Common.Serializer;
using NzbDrone.Core.Books;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Exceptions;
using NzbDrone.Core.MediaCover;

namespace NzbDrone.Core.MetadataSource.Hardcover
{
    public class HardcoverFallbackSearchProvider :
        IBookSearchFallbackProvider,
        IMetadataProvider,
        IProvideBookInfo,
        IProvideAuthorInfo,
        ISearchForNewBook,
        ISearchForNewAuthor,
        ISearchForNewEntity
    {
        private const string Endpoint = "https://api.hardcover.app/v1/graphql";
        private const string HardcoverApiTokenEnvironmentVariable = "BIBLIOPHILARR_HARDCOVER_API_TOKEN";
        private const int SearchPageSize = 20;
        private const int AuthorPageSize = 40;
        private const int DeterministicErrorThreshold = 3;
        private static readonly TimeSpan DeterministicErrorCooldown = TimeSpan.FromMinutes(15);

        private readonly IConfigService _configService;
        private readonly IHttpClient _httpClient;
        private readonly Logger _logger;

        private int _consecutiveDeterministicSearchErrors;
        private DateTime? _deterministicSearchCooldownUntilUtc;

        public HardcoverFallbackSearchProvider(IConfigService configService, IHttpClient httpClient, Logger logger)
        {
            _configService = configService;
            _httpClient = httpClient;
            _logger = logger;
        }

        public string ProviderName => "Hardcover";

        public int Priority => 4;

        public bool IsEnabled => _configService.EnableHardcoverFallback && HasConfiguredToken();

        public bool SupportsAuthorSearch => true;

        public bool SupportsBookSearch => true;

        public bool SupportsIsbnLookup => true;

        public bool SupportsSeriesInfo => true;

        public bool SupportsCoverImages => true;

        public ProviderRateLimitInfo RateLimitInfo => new ProviderRateLimitInfo
        {
            MaxRequests = 60,
            TimeWindow = TimeSpan.FromMinutes(1),
            SupportsAuthentication = true
        };

        public List<Book> Search(string title, string author)
        {
            var queryText = BuildSearchQuery(title, author);
            if (queryText.IsNullOrWhiteSpace())
            {
                _logger.Trace("HardcoverProvider.Search skipped because query was empty.");
                return new List<Book>();
            }

            _logger.Trace("HardcoverProvider.Search: title='{0}', author='{1}'", title, author);
            return SearchBooks(queryText, 10);
        }

        public List<Book> SearchForNewBook(string title, string author, bool getAllEditions = true)
        {
            var queryText = BuildSearchQuery(title, author);
            if (queryText.IsNullOrWhiteSpace())
            {
                _logger.Trace("HardcoverProvider.SearchForNewBook skipped because query was empty.");
                return new List<Book>();
            }

            _logger.Trace("HardcoverProvider.SearchForNewBook: title='{0}', author='{1}', getAllEditions={2}", title, author, getAllEditions);
            return SearchBooks(queryText, SearchPageSize);
        }

        public List<Book> SearchByIsbn(string isbn)
        {
            if (isbn.IsNullOrWhiteSpace())
            {
                _logger.Trace("HardcoverProvider.SearchByIsbn skipped because ISBN was empty.");
                return new List<Book>();
            }

            _logger.Trace("HardcoverProvider.SearchByIsbn: {0}", isbn);
            return SearchBooks(isbn.Trim(), SearchPageSize);
        }

        public List<Book> SearchByAsin(string asin)
        {
            if (asin.IsNullOrWhiteSpace())
            {
                _logger.Trace("HardcoverProvider.SearchByAsin skipped because ASIN was empty.");
                return new List<Book>();
            }

            _logger.Trace("HardcoverProvider.SearchByAsin: {0}", asin);
            return SearchBooks(asin.Trim(), SearchPageSize);
        }

        public List<Book> SearchByExternalId(string idType, string id)
        {
            if (idType.IsNullOrWhiteSpace() || id.IsNullOrWhiteSpace())
            {
                _logger.Trace("HardcoverProvider.SearchByExternalId skipped because idType or id was empty.");
                return new List<Book>();
            }

            var normalized = idType.Trim().ToLowerInvariant();
            _logger.Trace("HardcoverProvider.SearchByExternalId: idType='{0}', id='{1}'", normalized, id);
            if (normalized == "isbn")
            {
                return SearchByIsbn(id);
            }

            if (normalized == "asin")
            {
                return SearchByAsin(id);
            }

            if (normalized == "hardcover" || normalized == "work")
            {
                var lookup = SearchBooks(BuildScopedQueryFromBookId(id), SearchPageSize);
                var requestedId = NormalizeHardcoverBookToken(id);

                return lookup
                    .Where(x => string.Equals(ExtractBookToken(x.ForeignBookId), requestedId, StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }

            return new List<Book>();
        }

        public Tuple<string, Book, List<AuthorMetadata>> GetBookInfo(string foreignBookId)
        {
            _logger.Debug("HardcoverProvider.GetBookInfo: {0}", foreignBookId);

            var requestedId = NormalizeHardcoverBookToken(foreignBookId);
            if (requestedId.IsNullOrWhiteSpace())
            {
                throw new BookNotFoundException(foreignBookId);
            }

            var books = SearchBooks(BuildScopedQueryFromBookId(requestedId), SearchPageSize);
            var book = books.FirstOrDefault(x => string.Equals(ExtractBookToken(x.ForeignBookId), requestedId, StringComparison.OrdinalIgnoreCase));

            if (book == null)
            {
                throw new BookNotFoundException(foreignBookId);
            }

            var metadata = book.AuthorMetadata?.Value;
            var metadataList = metadata != null ? new List<AuthorMetadata> { metadata } : new List<AuthorMetadata>();

            return Tuple.Create(metadata?.ForeignAuthorId ?? "hardcover:author:unknown", book, metadataList);
        }

        public Author GetAuthorInfo(string foreignAuthorId, bool useCache = true)
        {
            _logger.Debug("HardcoverProvider.GetAuthorInfo: {0}", foreignAuthorId);

            var authorName = DecodeAuthorToken(foreignAuthorId);
            if (authorName.IsNullOrWhiteSpace())
            {
                throw new AuthorNotFoundException(foreignAuthorId);
            }

            var books = SearchBooks(authorName.Trim(), AuthorPageSize)
                .Where(x => IsMatchingAuthor(x.AuthorMetadata?.Value?.Name, authorName))
                .DistinctBy(x => x.ForeignBookId)
                .ToList();

            if (!books.Any())
            {
                throw new AuthorNotFoundException(foreignAuthorId);
            }

            var metadata = books.First().AuthorMetadata.Value;
            metadata.ForeignAuthorId = BuildHardcoverAuthorId(authorName);
            metadata.TitleSlug ??= metadata.ForeignAuthorId;
            metadata.Name = authorName;
            metadata.SortName = authorName.ToLowerInvariant();
            metadata.NameLastFirst = authorName.ToLastFirst();
            metadata.SortNameLastFirst = metadata.NameLastFirst.ToLowerInvariant();

            foreach (var book in books)
            {
                book.AuthorMetadata = metadata;
                if (book.Author?.Value != null)
                {
                    book.Author.Value.Metadata = metadata;
                    book.Author.Value.AuthorMetadataId = metadata.Id;
                }
            }

            return new Author
            {
                Metadata = metadata,
                CleanName = Parser.Parser.CleanAuthorName(authorName),
                Books = books,
                Series = BuildAuthorSeries(books)
            };
        }

        public HashSet<string> GetChangedAuthors(DateTime startTime)
        {
            return null;
        }

        public List<Author> SearchForNewAuthor(string title)
        {
            if (title.IsNullOrWhiteSpace())
            {
                _logger.Trace("HardcoverProvider.SearchForNewAuthor skipped because title was empty.");
                return new List<Author>();
            }

            _logger.Trace("HardcoverProvider.SearchForNewAuthor: {0}", title);
            return SearchBooks(title.Trim(), SearchPageSize)
                .Select(x => x?.AuthorMetadata?.Value?.Name)
                .Where(x => x.IsNotNullOrWhiteSpace())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(BuildAuthorFromName)
                .Where(x => x != null)
                .ToList();
        }

        public List<object> SearchForNewEntity(string title)
        {
            _logger.Trace("HardcoverProvider.SearchForNewEntity: {0}", title);

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

        private List<Book> SearchBooks(string queryText, int perPage)
        {
            if (!_configService.EnableHardcoverFallback)
            {
                _logger.Debug("Hardcover search skipped for query '{0}' because the provider is disabled.", queryText);
                return new List<Book>();
            }

            if (!TryResolveToken(out var token, out var tokenSource))
            {
                _logger.Debug("Hardcover search skipped for query '{0}' because no API token is configured.", queryText);
                return new List<Book>();
            }

            if (IsDeterministicErrorCooldownActive(out var cooldownUntilUtc))
            {
                _logger.Debug("Hardcover search skipped for query '{0}' because the provider is cooling down until {1:u} after repeated deterministic errors.", queryText, cooldownUntilUtc.Value);
                return new List<Book>();
            }

            _logger.Trace("Hardcover search request: query='{0}', perPage={1}, tokenSource={2}", queryText, perPage, tokenSource);

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
                query = "query SearchBooks($query: String!, $perPage: Int!, $page: Int!) { search(query: $query, query_type: \"Book\", per_page: $perPage, page: $page) { error ids results } }",
                variables = new
                {
                    query = queryText,
                    perPage = perPage,
                    page = 1
                }
            }.ToJson());

            var response = _httpClient.Post<HardcoverGraphQlResponse>(request);
            var payload = response.Resource?.Data?.Search;

            if (payload == null)
            {
                _logger.Warn("Hardcover returned no search payload for query '{0}'.", queryText);
                return new List<Book>();
            }

            if (payload.Error.IsNotNullOrWhiteSpace())
            {
                _logger.Debug("Hardcover search returned provider error for query '{0}': {1}", queryText, payload.Error);

                if (IsDeterministicSearchError(payload.Error))
                {
                    RegisterDeterministicSearchError(payload.Error);
                    return new List<Book>();
                }
            }
            else
            {
                ResetDeterministicSearchErrorState();
            }

            var results = ParseSearchResults(payload);

            if (!results.Any())
            {
                _logger.Debug("Hardcover returned no matches for query '{0}'.", queryText);
                return new List<Book>();
            }

            var mapped = results.Select(MapBook)
                .Where(x => x != null)
                .DistinctBy(x => x.ForeignBookId)
                .ToList();

            ResetDeterministicSearchErrorState();

            _logger.Debug("Hardcover returned {0} unique mapped result(s) for query '{1}'.", mapped.Count, queryText);

            return mapped;
        }

        private bool IsDeterministicErrorCooldownActive(out DateTime? cooldownUntilUtc)
        {
            cooldownUntilUtc = _deterministicSearchCooldownUntilUtc;
            return cooldownUntilUtc.HasValue && cooldownUntilUtc.Value > DateTime.UtcNow;
        }

        private static bool IsDeterministicSearchError(string error)
        {
            if (error.IsNullOrWhiteSpace())
            {
                return false;
            }

            return error.IndexOf("query_by_weights", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   error.IndexOf("query_by fields", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   error.IndexOf("number of weights", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void RegisterDeterministicSearchError(string error)
        {
            var failureCount = Interlocked.Increment(ref _consecutiveDeterministicSearchErrors);

            if (failureCount < DeterministicErrorThreshold)
            {
                return;
            }

            var cooldownUntilUtc = DateTime.UtcNow.Add(DeterministicErrorCooldown);
            _deterministicSearchCooldownUntilUtc = cooldownUntilUtc;
            _logger.Warn("Hardcover search entering cooldown until {0:u} after {1} consecutive deterministic provider errors: {2}", cooldownUntilUtc, failureCount, error);
        }

        private void ResetDeterministicSearchErrorState()
        {
            if (_consecutiveDeterministicSearchErrors == 0 && !_deterministicSearchCooldownUntilUtc.HasValue)
            {
                return;
            }

            Interlocked.Exchange(ref _consecutiveDeterministicSearchErrors, 0);
            _deterministicSearchCooldownUntilUtc = null;
        }

        private bool HasConfiguredToken()
        {
            return Environment.GetEnvironmentVariable(HardcoverApiTokenEnvironmentVariable).IsNotNullOrWhiteSpace() ||
                   _configService.HardcoverApiToken.IsNotNullOrWhiteSpace();
        }

        private bool TryResolveToken(out string token, out string tokenSource)
        {
            token = string.Empty;
            tokenSource = "missing";

            if (!_configService.EnableHardcoverFallback)
            {
                tokenSource = "disabled";
                return false;
            }

            var rawToken = Environment.GetEnvironmentVariable(HardcoverApiTokenEnvironmentVariable);

            if (rawToken.IsNotNullOrWhiteSpace())
            {
                token = NormalizeBearerToken(rawToken.Trim());
                tokenSource = "environment";
                return token.IsNotNullOrWhiteSpace();
            }

            rawToken = _configService.HardcoverApiToken;

            if (rawToken.IsNullOrWhiteSpace())
            {
                return false;
            }

            token = NormalizeBearerToken(rawToken.Trim());
            tokenSource = "config";
            return token.IsNotNullOrWhiteSpace();
        }

        private List<HardcoverSearchResult> ParseSearchResults(HardcoverSearchPayload payload)
        {
            if (payload?.Results == null)
            {
                return new List<HardcoverSearchResult>();
            }

            JToken resultsToken;

            if (payload.Results.Type == JTokenType.String)
            {
                var raw = payload.Results.ToObject<string>();
                if (raw.IsNullOrWhiteSpace())
                {
                    return new List<HardcoverSearchResult>();
                }

                try
                {
                    resultsToken = JToken.Parse(raw);
                }
                catch (Exception ex)
                {
                    _logger.Warn(ex, "Hardcover returned an unparsable search result payload.");
                    return new List<HardcoverSearchResult>();
                }
            }
            else
            {
                resultsToken = payload.Results;
            }

            var parsed = resultsToken.ToObject<HardcoverSearchResults>();
            var hits = parsed?.Hits ?? new List<HardcoverSearchHit>();

            return hits
                .Select(x => x?.Document)
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
            var authorName = result.Contributions?
                                 .Select(x => x?.Author?.Name)
                                 .FirstOrDefault(x => x.IsNotNullOrWhiteSpace())?.Trim()
                             ?? result.AuthorNames?.FirstOrDefault(x => x.IsNotNullOrWhiteSpace())?.Trim()
                             ?? "Unknown Author";

            var publishedDate = ParseDate(result.ReleaseDate, result.ReleaseYear);
            var isbn13 = result.Isbns?.FirstOrDefault(x => IsIsbn13(x));
            var coverUrl = result.Image?.Url;
            var authorMetadata = new AuthorMetadata
            {
                ForeignAuthorId = BuildHardcoverAuthorId(authorName),
                TitleSlug = BuildHardcoverAuthorId(authorName),
                Name = authorName,
                SortName = authorName.ToLowerInvariant(),
                NameLastFirst = authorName.ToLastFirst(),
                SortNameLastFirst = authorName.ToLastFirst().ToLowerInvariant(),
                Overview = result.Description,
                Ratings = new Ratings
                {
                    Votes = result.RatingsCount ?? 0,
                    Value = (decimal)(result.Rating ?? 0)
                }
            };

            var author = new Author
            {
                Metadata = authorMetadata,
                AuthorMetadataId = authorMetadata.Id
            };

            var book = new Book
            {
                ForeignBookId = $"hardcover:work:{result.Id}",
                TitleSlug = $"hardcover:work:{result.Id}",
                Title = title,
                CleanTitle = title,
                AuthorMetadata = authorMetadata,
                AuthorMetadataId = authorMetadata.Id,
                Author = author,
                ReleaseDate = publishedDate,
                Ratings = new Ratings
                {
                    Votes = result.RatingsCount ?? 0,
                    Value = (decimal)(result.Rating ?? 0)
                },
                Genres = new List<string>()
            };

            if (result.FeaturedSeries?.Series != null && result.FeaturedSeries.Series.Name.IsNotNullOrWhiteSpace())
            {
                var series = new Series
                {
                    ForeignSeriesId = $"hardcover:series:{result.FeaturedSeries.Series.Id}",
                    Title = result.FeaturedSeries.Series.Name,
                    WorkCount = result.FeaturedSeries.Series.BooksCount,
                    PrimaryWorkCount = result.FeaturedSeries.Series.PrimaryBooksCount,
                    Numbered = true
                };

                var seriesLink = new SeriesBookLink
                {
                    IsPrimary = true,
                    Position = result.FeaturedSeries.Position?.ToString(CultureInfo.InvariantCulture),
                    SeriesPosition = (int)(result.FeaturedSeries.Position ?? 0),
                    Book = book,
                    Series = series
                };

                series.LinkItems = new List<SeriesBookLink> { seriesLink };
                book.SeriesLinks = new List<SeriesBookLink> { seriesLink };
            }

            var edition = new Edition
            {
                ForeignEditionId = $"hardcover:edition:{result.Id}",
                TitleSlug = $"hardcover:edition:{result.Id}",
                Title = title,
                ReleaseDate = publishedDate,
                Language = result.Language,
                Isbn13 = isbn13,
                Format = DetermineFormat(result),
                IsEbook = result.HasEbook,
                Book = book,
                PageCount = result.Pages ?? 0,
                Overview = result.Description,
                Ratings = new Ratings
                {
                    Votes = result.RatingsCount ?? 0,
                    Value = (decimal)(result.Rating ?? 0)
                }
            };

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

        private static bool IsMatchingAuthor(string candidate, string expected)
        {
            if (candidate.IsNullOrWhiteSpace() || expected.IsNullOrWhiteSpace())
            {
                return false;
            }

            var normalizedCandidate = candidate.Trim();
            var normalizedExpected = expected.Trim();

            return string.Equals(normalizedCandidate, normalizedExpected, StringComparison.InvariantCultureIgnoreCase) ||
                   normalizedCandidate.IndexOf(normalizedExpected, StringComparison.InvariantCultureIgnoreCase) >= 0 ||
                   normalizedExpected.IndexOf(normalizedCandidate, StringComparison.InvariantCultureIgnoreCase) >= 0;
        }

        private static Author BuildAuthorFromName(string name)
        {
            if (name.IsNullOrWhiteSpace())
            {
                return null;
            }

            var trimmed = name.Trim();
            var metadata = new AuthorMetadata
            {
                ForeignAuthorId = BuildHardcoverAuthorId(trimmed),
                Name = trimmed,
                SortName = trimmed.ToLowerInvariant(),
                NameLastFirst = trimmed.ToLastFirst(),
                SortNameLastFirst = trimmed.ToLastFirst().ToLowerInvariant(),
                Ratings = new Ratings { Votes = 0, Value = 0 }
            };

            return new Author
            {
                Metadata = metadata,
                CleanName = Parser.Parser.CleanAuthorName(trimmed),
                Books = new List<Book>(),
                Series = new List<Series>()
            };
        }

        private static List<Series> BuildAuthorSeries(IEnumerable<Book> books)
        {
            var links = books?
                .Where(book => book?.SeriesLinks?.Value?.Any() == true)
                .SelectMany(book => book.SeriesLinks.Value)
                .Where(link => link?.Series?.Value != null &&
                               link.Series.Value.ForeignSeriesId.IsNotNullOrWhiteSpace() &&
                               link.Book?.Value != null &&
                               link.Book.Value.ForeignBookId.IsNotNullOrWhiteSpace())
                .ToList() ?? new List<SeriesBookLink>();

            if (!links.Any())
            {
                return new List<Series>();
            }

            var grouped = links
                .GroupBy(link => link.Series.Value.ForeignSeriesId, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var series = new List<Series>();

            foreach (var group in grouped)
            {
                var template = group.First().Series.Value;
                var dedupedLinks = group
                    .GroupBy(link => link.Book.Value.ForeignBookId, StringComparer.OrdinalIgnoreCase)
                    .Select(linkGroup => linkGroup
                        .OrderByDescending(link => link.IsPrimary)
                        .ThenBy(link => link.SeriesPosition)
                        .First())
                    .ToList();

                var hydrated = new Series
                {
                    ForeignSeriesId = template.ForeignSeriesId,
                    Title = template.Title,
                    Description = template.Description,
                    Numbered = dedupedLinks.Any(link => link.Position.IsNotNullOrWhiteSpace()),
                    WorkCount = dedupedLinks.Count,
                    PrimaryWorkCount = dedupedLinks.Count(link => link.IsPrimary),
                    LinkItems = dedupedLinks
                };

                foreach (var link in dedupedLinks)
                {
                    link.Series = hydrated;
                }

                series.Add(hydrated);
            }

            return series.OrderBy(x => x.Title).ToList();
        }

        private static string DetermineFormat(HardcoverSearchResult result)
        {
            if (result == null)
            {
                return "Book";
            }

            if (result.HasAudiobook && !result.HasEbook)
            {
                return "Audiobook";
            }

            if (result.HasEbook)
            {
                return "Ebook";
            }

            return "Book";
        }

        private static bool IsIsbn13(string value)
        {
            if (value.IsNullOrWhiteSpace())
            {
                return false;
            }

            var normalized = new string(value.Where(char.IsDigit).ToArray());
            return normalized.Length == 13;
        }

        private static string BuildHardcoverAuthorId(string authorName)
        {
            var value = authorName.IsNotNullOrWhiteSpace() ? authorName.Trim() : "unknown";
            return $"hardcover:author:{Uri.EscapeDataString(value)}";
        }

        private static string DecodeAuthorToken(string foreignAuthorId)
        {
            if (foreignAuthorId.IsNullOrWhiteSpace())
            {
                return null;
            }

            const string prefix = "hardcover:author:";
            var raw = foreignAuthorId.Trim();
            if (raw.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                raw = raw.Substring(prefix.Length);
            }

            return Uri.UnescapeDataString(raw).Replace('+', ' ').Trim();
        }

        private static string NormalizeHardcoverBookToken(string foreignBookId)
        {
            if (foreignBookId.IsNullOrWhiteSpace())
            {
                return null;
            }

            const string prefix = "hardcover:work:";
            var raw = foreignBookId.Trim();
            if (raw.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                raw = raw.Substring(prefix.Length);
            }

            return Uri.UnescapeDataString(raw).Trim();
        }

        private static string ExtractBookToken(string foreignBookId)
        {
            return NormalizeHardcoverBookToken(foreignBookId);
        }

        private static string BuildScopedQueryFromBookId(string id)
        {
            return id.IsNotNullOrWhiteSpace() ? id.Trim() : string.Empty;
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
        [JsonProperty("error")]
        public string Error { get; set; }

        [JsonProperty("ids")]
        public List<int> Ids { get; set; }

        [JsonProperty("results")]
        public JToken Results { get; set; }
    }

    public class HardcoverSearchResults
    {
        [JsonProperty("found")]
        public int Found { get; set; }

        [JsonProperty("hits")]
        public List<HardcoverSearchHit> Hits { get; set; }
    }

    public class HardcoverSearchHit
    {
        [JsonProperty("document")]
        public HardcoverSearchResult Document { get; set; }
    }

    public class HardcoverSearchResult
    {
        [JsonProperty("id")]
        public string Id { get; set; }

        [JsonProperty("title")]
        public string Title { get; set; }

        [JsonProperty("description")]
        public string Description { get; set; }

        [JsonProperty("author_names")]
        public List<string> AuthorNames { get; set; }

        [JsonProperty("contributions")]
        public List<HardcoverContributorResult> Contributions { get; set; }

        [JsonProperty("series_names")]
        public List<string> SeriesNames { get; set; }

        [JsonProperty("featured_series")]
        public HardcoverFeaturedSeriesResult FeaturedSeries { get; set; }

        [JsonProperty("isbns")]
        public List<string> Isbns { get; set; }

        [JsonProperty("image")]
        public HardcoverImageResult Image { get; set; }

        [JsonProperty("rating")]
        public double? Rating { get; set; }

        [JsonProperty("ratings_count")]
        public int? RatingsCount { get; set; }

        [JsonProperty("pages")]
        public int? Pages { get; set; }

        [JsonProperty("language")]
        public string Language { get; set; }

        [JsonProperty("has_audiobook")]
        public bool HasAudiobook { get; set; }

        [JsonProperty("has_ebook")]
        public bool HasEbook { get; set; }

        [JsonProperty("release_year")]
        public int? ReleaseYear { get; set; }

        [JsonProperty("release_date")]
        public string ReleaseDate { get; set; }

        [JsonProperty("slug")]
        public string Slug { get; set; }
    }

    public class HardcoverContributorResult
    {
        [JsonProperty("author")]
        public HardcoverAuthorResult Author { get; set; }
    }

    public class HardcoverAuthorResult
    {
        [JsonProperty("name")]
        public string Name { get; set; }
    }

    public class HardcoverImageResult
    {
        [JsonProperty("url")]
        public string Url { get; set; }
    }

    public class HardcoverFeaturedSeriesResult
    {
        [JsonProperty("position")]
        public decimal? Position { get; set; }

        [JsonProperty("series")]
        public HardcoverSeriesResult Series { get; set; }
    }

    public class HardcoverSeriesResult
    {
        [JsonProperty("id")]
        public int Id { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("books_count")]
        public int BooksCount { get; set; }

        [JsonProperty("primary_books_count")]
        public int PrimaryBooksCount { get; set; }
    }
}
