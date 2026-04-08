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
    /// <summary>
    /// Metadata provider backed by the Hardcover GraphQL API (https://hardcover.app).
    /// Supports author/book search, author detail retrieval, book info lookup, and
    /// ISBN/ASIN resolution. Requires a Hardcover API token configured via the UI
    /// or the BIBLIOPHILARR_HARDCOVER_API_TOKEN environment variable.
    ///
    /// GraphQL responses are mapped to the internal Book/Author/AuthorMetadata models
    /// through MapAuthorSearchResult, MapBook, and MapEdition helper methods.
    /// </summary>
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

        // Hardcover API default page sizes — aligned with their typical result limits.
        private const int SearchPageSize = 20;
        private const int AuthorPageSize = 40;

        // After this many consecutive deterministic errors (e.g. 400 Bad Request from
        // malformed queries), suppress further searches for the cooldown period to avoid
        // hammering the API with known-bad requests.
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

        public int Priority => 1;

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
            if (!IsEnabled)
            {
                _logger.Trace("HardcoverProvider.Search skipped because provider is disabled.");
                return new List<Book>();
            }

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

            // Try direct work ID lookup first (numeric IDs only)
            if (int.TryParse(requestedId, out var numericId))
            {
                var directResult = FetchBookByWorkId(numericId);
                if (directResult != null)
                {
                    var metadata = directResult.AuthorMetadata?.Value;
                    var metadataList = metadata != null ? new List<AuthorMetadata> { metadata } : new List<AuthorMetadata>();
                    return Tuple.Create(metadata?.ForeignAuthorId ?? "hardcover:author:unknown", directResult, metadataList);
                }
            }

            // Fallback to text search
            var books = SearchBooks(BuildScopedQueryFromBookId(requestedId), SearchPageSize);
            var book = books.FirstOrDefault(x => string.Equals(ExtractBookToken(x.ForeignBookId), requestedId, StringComparison.OrdinalIgnoreCase));

            if (book == null)
            {
                throw new BookNotFoundException(foreignBookId);
            }

            var meta = book.AuthorMetadata?.Value;
            var metaList = meta != null ? new List<AuthorMetadata> { meta } : new List<AuthorMetadata>();

            return Tuple.Create(meta?.ForeignAuthorId ?? "hardcover:author:unknown", book, metaList);
        }

        public Author GetAuthorInfo(string foreignAuthorId, bool useCache = true)
        {
            _logger.Debug("HardcoverProvider.GetAuthorInfo: {0}", foreignAuthorId);

            List<Book> books;
            string authorImageUrl;
            int hardcoverAuthorId;
            string authorSlug;
            string authorName;

            // If the ForeignAuthorId already contains a numeric Hardcover ID,
            // fetch directly by ID (skip the name search step).
            if (TryParseNumericHardcoverAuthorId(foreignAuthorId, out var parsedNumericId))
            {
                books = FetchAuthorBooksById(parsedNumericId, out authorImageUrl, out authorSlug, out authorName);
                hardcoverAuthorId = parsedNumericId;

                if (authorName.IsNullOrWhiteSpace())
                {
                    authorName = foreignAuthorId;
                }
            }
            else
            {
                authorName = DecodeAuthorToken(foreignAuthorId);
                if (authorName.IsNullOrWhiteSpace())
                {
                    throw new AuthorNotFoundException(foreignAuthorId);
                }

                // Prefer author-specific search first (gets more books + author image)
                books = FetchAuthorBooks(authorName, out authorImageUrl, out hardcoverAuthorId, out authorSlug);
            }

            // Fallback to book search if author search returned nothing
            if (!books.Any())
            {
                if (authorName.IsNotNullOrWhiteSpace() && !int.TryParse(authorName, out _))
                {
                    _logger.Debug("Author-specific search found no results for '{0}', trying book search.", authorName);
                    books = SearchBooks(authorName.Trim(), AuthorPageSize)
                        .Where(x => IsMatchingAuthor(x.AuthorMetadata?.Value?.Name, authorName))
                        .DistinctBy(x => x.ForeignBookId)
                        .ToList();
                }
            }

            if (!books.Any())
            {
                throw new AuthorNotFoundException(foreignAuthorId);
            }

            var metadata = books.First().AuthorMetadata.Value;

            // Use numeric Hardcover author ID when available for stable identification
            metadata.ForeignAuthorId = hardcoverAuthorId > 0
                ? BuildHardcoverAuthorId(hardcoverAuthorId)
                : BuildHardcoverAuthorId(authorName);
            metadata.TitleSlug = metadata.ForeignAuthorId.ToUrlSlug();
            metadata.Name = authorName;
            metadata.SortName = authorName.ToLowerInvariant();
            metadata.NameLastFirst = authorName.ToLastFirst();
            metadata.SortNameLastFirst = metadata.NameLastFirst.ToLowerInvariant();

            // Set author links
            var authorLinks = new List<Links>();
            if (authorSlug.IsNotNullOrWhiteSpace())
            {
                authorLinks.Add(new Links { Name = "Hardcover", Url = $"https://hardcover.app/authors/{authorSlug}" });
            }

            metadata.Links = authorLinks;

            // Set author image if available
            if (authorImageUrl.IsNotNullOrWhiteSpace() && (metadata.Images == null || !metadata.Images.Any()))
            {
                metadata.Images = new List<MediaCover.MediaCover>
                {
                    new MediaCover.MediaCover(MediaCoverTypes.Poster, authorImageUrl)
                    {
                        RemoteUrl = authorImageUrl
                    }
                };
            }

            foreach (var book in books)
            {
                book.AuthorMetadata = metadata;
                if (book.Author?.Value != null)
                {
                    book.Author.Value.Metadata = metadata;
                    book.Author.Value.AuthorMetadataId = metadata.Id;
                }
            }

            // Fetch comprehensive series data via the book_series table
            // This captures ALL series for the author, not just those on fetched books
            var authorSeries = hardcoverAuthorId > 0
                ? FetchAuthorSeries(hardcoverAuthorId)
                : BuildAuthorSeries(books);

            if (!authorSeries.Any())
            {
                authorSeries = BuildAuthorSeries(books);
            }

            _logger.Debug("GetAuthorInfo: {0} books, {1} series for '{2}'", books.Count, authorSeries.Count, authorName);

            return new Author
            {
                Metadata = metadata,
                CleanName = Parser.Parser.CleanAuthorName(authorName),
                Books = books,
                Series = authorSeries
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

            // Search for authors directly using Hardcover's author search
            var results = SearchAuthors(title.Trim(), SearchPageSize);
            if (results.Any())
            {
                return results;
            }

            // Fallback: Search for books and extract authors
            _logger.Debug("Author search returned no results for '{0}', falling back to book search.", title);
            var bookResults = SearchBooks(title.Trim(), SearchPageSize);

            // Extract unique authors from book results - they have proper AuthorMetadata
            return bookResults
                .Where(x => x?.AuthorMetadata?.Value != null && x.AuthorMetadata.Value.ForeignAuthorId.IsNotNullOrWhiteSpace())
                .GroupBy(x => x.AuthorMetadata.Value.ForeignAuthorId, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .Select(book => new Author
                {
                    Metadata = book.AuthorMetadata.Value,
                    CleanName = Parser.Parser.CleanAuthorName(book.AuthorMetadata.Value.Name),
                    Books = new List<Book>(),
                    Series = new List<Series>()
                })
                .ToList();
        }

        private List<Author> SearchAuthors(string queryText, int perPage)
        {
            if (!_configService.EnableHardcoverFallback)
            {
                _logger.Debug("Hardcover author search skipped for query '{0}' because the provider is disabled.", queryText);
                return new List<Author>();
            }

            if (!TryResolveToken(out var token, out var tokenSource))
            {
                _logger.Debug("Hardcover author search skipped for query '{0}' because no API token is configured.", queryText);
                return new List<Author>();
            }

            if (IsDeterministicErrorCooldownActive(out var cooldownUntilUtc))
            {
                _logger.Debug("Hardcover author search skipped for query '{0}' because the provider is cooling down until {1:u}.", queryText, cooldownUntilUtc.Value);
                return new List<Author>();
            }

            _logger.Trace("Hardcover author search request: query='{0}', perPage={1}, tokenSource={2}", queryText, perPage, tokenSource);

            var request = new HttpRequest(Endpoint, HttpAccept.Json)
            {
                Method = HttpMethod.Post,
                RateLimit = TimeSpan.FromSeconds(1),
                RateLimitKey = ProviderName
            };

            var configuredTimeout = _configService.HardcoverRequestTimeoutSeconds;
            var fallbackTimeout = Math.Max(5, _configService.MetadataProviderTimeoutSeconds);
            request.RequestTimeout = TimeSpan.FromSeconds(configuredTimeout > 0 ? configuredTimeout : fallbackTimeout);

            request.Headers.ContentType = "application/json; charset=utf-8";
            request.Headers["authorization"] = $"Bearer {token}";
            request.SetContent(new
            {
                query = @"query SearchAuthors($query: String!, $perPage: Int!) {
                    search(query: $query, query_type: ""Author"", per_page: $perPage, page: 1) {
                        error
                        ids
                    }
                }",
                variables = new { query = queryText, perPage }
            }.ToJson());

            try
            {
                var response = _httpClient.Post<JObject>(request);
                var searchData = response.Resource?.SelectToken("data.search");
                var errorMsg = searchData?.Value<string>("error");

                if (errorMsg.IsNotNullOrWhiteSpace())
                {
                    _logger.Debug("Hardcover author search error for '{0}': {1}", queryText, errorMsg);
                    if (IsDeterministicSearchError(errorMsg))
                    {
                        RegisterDeterministicSearchError(errorMsg);
                    }

                    return new List<Author>();
                }

                ResetDeterministicSearchErrorState();

                var idsArray = searchData?.SelectToken("ids") as JArray;

                if (idsArray == null || !idsArray.Any())
                {
                    _logger.Debug("Hardcover author search returned no results for '{0}'.", queryText);
                    return new List<Author>();
                }

                var authorIds = idsArray.Select(x => x.Value<int>()).Take(perPage).ToList();
                _logger.Debug("Hardcover author search found {0} author(s) for '{1}'.", authorIds.Count, queryText);

                // Fetch details for each author
                return FetchAuthorDetailsBatch(authorIds, token, configuredTimeout, fallbackTimeout);
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Hardcover author search failed for '{0}'.", queryText);
                return new List<Author>();
            }
        }

        private List<Author> FetchAuthorDetailsBatch(List<int> authorIds, string token, int configuredTimeout, int fallbackTimeout)
        {
            if (!authorIds.Any())
            {
                return new List<Author>();
            }

            var request = new HttpRequest(Endpoint, HttpAccept.Json)
            {
                Method = HttpMethod.Post,
                RateLimit = TimeSpan.FromSeconds(1),
                RateLimitKey = ProviderName
            };

            request.RequestTimeout = TimeSpan.FromSeconds(configuredTimeout > 0 ? configuredTimeout : fallbackTimeout);
            request.Headers.ContentType = "application/json; charset=utf-8";
            request.Headers["authorization"] = $"Bearer {token}";
            request.SetContent(new
            {
                query = @"query GetAuthors($ids: [Int!]!) {
                    authors(where: {id: {_in: $ids}}) {
                        id
                        name
                        slug
                        bio
                        image { url }
                        books_count
                        users_count
                        contributions(limit: 10, order_by: {book: {ratings_count: desc_nulls_last}}) {
                            book { rating ratings_count }
                        }
                    }
                }",
                variables = new { ids = authorIds }
            }.ToJson());

            try
            {
                var response = _httpClient.Post<JObject>(request);
                var authorsArray = response.Resource?.SelectToken("data.authors") as JArray;

                if (authorsArray == null || !authorsArray.Any())
                {
                    // Log diagnostic info to help debug empty responses
                    var errors = response.Resource?.SelectToken("errors");
                    if (errors != null)
                    {
                        _logger.Debug("Hardcover author details fetch returned GraphQL errors: {0}", errors.ToString(Newtonsoft.Json.Formatting.None));
                    }
                    else
                    {
                        var topKeys = response.Resource?.Properties().Select(p => p.Name) ?? Enumerable.Empty<string>();
                        _logger.Debug("Hardcover author details fetch returned no authors for {0} ID(s) [{1}]. Response keys: [{2}]",
                            authorIds.Count,
                            string.Join(", ", authorIds),
                            string.Join(", ", topKeys));
                    }

                    // Batch query failed — try individual author queries as fallback
                    return FetchAuthorDetailsIndividual(authorIds.Take(5).ToList(), token, configuredTimeout, fallbackTimeout);
                }

                var authors = new List<Author>();

                foreach (var authorData in authorsArray)
                {
                    var author = MapAuthorSearchResult(authorData);
                    if (author != null)
                    {
                        authors.Add(author);
                    }
                }

                _logger.Debug("Hardcover returned {0} author detail(s).", authors.Count);
                return authors;
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Hardcover author details fetch failed.");
                return new List<Author>();
            }
        }

        private List<Author> FetchAuthorDetailsIndividual(List<int> authorIds, string token, int configuredTimeout, int fallbackTimeout)
        {
            _logger.Debug("Falling back to individual author queries for {0} author(s).", authorIds.Count);

            var authors = new List<Author>();

            foreach (var authorId in authorIds)
            {
                try
                {
                    var request = new HttpRequest(Endpoint, HttpAccept.Json)
                    {
                        Method = HttpMethod.Post,
                        RateLimit = TimeSpan.FromSeconds(1),
                        RateLimitKey = ProviderName
                    };

                    request.RequestTimeout = TimeSpan.FromSeconds(configuredTimeout > 0 ? configuredTimeout : fallbackTimeout);
                    request.Headers.ContentType = "application/json; charset=utf-8";
                    request.Headers["authorization"] = $"Bearer {token}";
                    request.SetContent(new
                    {
                        query = @"query GetAuthor($id: Int!) {
                            authors(where: {id: {_eq: $id}}) {
                                id
                                name
                                slug
                                bio
                                image { url }
                                books_count
                                users_count
                                contributions(limit: 10, order_by: {book: {ratings_count: desc_nulls_last}}) {
                                    book { rating ratings_count }
                                }
                            }
                        }",
                        variables = new { id = authorId }
                    }.ToJson());

                    var response = _httpClient.Post<JObject>(request);
                    var authorsArray = response.Resource?.SelectToken("data.authors") as JArray;

                    if (authorsArray?.Any() == true)
                    {
                        var author = MapAuthorSearchResult(authorsArray.First);
                        if (author != null)
                        {
                            authors.Add(author);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.Debug(ex, "Hardcover individual author fetch failed for ID {0}.", authorId);
                }
            }

            _logger.Debug("Hardcover individual fallback returned {0} author(s).", authors.Count);
            return authors;
        }

        private Author MapAuthorSearchResult(JToken authorData)
        {
            if (authorData == null)
            {
                return null;
            }

            var authorId = authorData.Value<int>("id");
            var name = authorData.Value<string>("name")?.Trim();
            var slug = authorData.Value<string>("slug");
            var bio = authorData.Value<string>("bio");
            var imageUrl = authorData.SelectToken("image.url")?.Value<string>();
            var booksCount = authorData.Value<int?>("books_count") ?? 0;

            // Calculate average rating from author's top books (via contributions)
            var contributionsArray = authorData.SelectToken("contributions") as JArray;
            var usersCount = authorData.Value<int?>("users_count") ?? 0;
            var avgRating = 0m;
            var totalVotes = 0;

            if (contributionsArray != null && contributionsArray.Any())
            {
                var validBooks = contributionsArray
                    .Select(c => c.SelectToken("book"))
                    .Where(b => b != null && b.Value<double?>("rating") > 0 && b.Value<int?>("ratings_count") > 0)
                    .ToList();

                if (validBooks.Any())
                {
                    // Weighted average by ratings_count
                    var totalWeightedRating = 0.0;
                    var totalCount = 0;

                    foreach (var book in validBooks)
                    {
                        var rating = book.Value<double>("rating");
                        var count = book.Value<int>("ratings_count");
                        totalWeightedRating += rating * count;
                        totalCount += count;
                    }

                    if (totalCount > 0)
                    {
                        avgRating = (decimal)(totalWeightedRating / totalCount);
                        totalVotes = totalCount;
                    }
                }
            }

            if (name.IsNullOrWhiteSpace())
            {
                return null;
            }

            var links = new List<Links>();
            if (slug.IsNotNullOrWhiteSpace())
            {
                links.Add(new Links { Name = "Hardcover", Url = $"https://hardcover.app/authors/{slug}" });
            }

            var images = new List<MediaCover.MediaCover>();
            if (imageUrl.IsNotNullOrWhiteSpace())
            {
                images.Add(new MediaCover.MediaCover(MediaCoverTypes.Poster, imageUrl)
                {
                    RemoteUrl = imageUrl
                });
            }

            var metadata = new AuthorMetadata
            {
                ForeignAuthorId = BuildHardcoverAuthorId(authorId),
                TitleSlug = BuildHardcoverAuthorId(authorId).ToUrlSlug(),
                Name = name,
                SortName = name.ToLowerInvariant(),
                NameLastFirst = name.ToLastFirst(),
                SortNameLastFirst = name.ToLastFirst().ToLowerInvariant(),
                Overview = bio,
                Links = links,
                Images = images,
                Ratings = new Ratings { Votes = totalVotes > 0 ? totalVotes : (usersCount > 0 ? usersCount : booksCount), Value = avgRating }
            };

            return new Author
            {
                Metadata = metadata,
                CleanName = Parser.Parser.CleanAuthorName(name),
                Books = new List<Book>(),
                Series = new List<Series>()
            };
        }

        public List<object> SearchForNewEntity(string title)
        {
            _logger.Trace("HardcoverProvider.SearchForNewEntity: {0}", title);

            var output = new List<object>();
            var seenAuthorIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var seenAuthorNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var seenBookIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // Search for authors first using direct author search
            var authors = SearchAuthors(title, 10);
            foreach (var author in authors)
            {
                if (author?.Metadata?.Value == null)
                {
                    continue;
                }

                var authorId = author.ForeignAuthorId;
                var authorName = author.Metadata.Value.Name?.Trim();

                // Deduplicate by both ID and normalized name
                if (authorId.IsNotNullOrWhiteSpace() && seenAuthorIds.Add(authorId))
                {
                    if (authorName.IsNotNullOrWhiteSpace())
                    {
                        seenAuthorNames.Add(authorName);
                    }

                    output.Add(author);
                }
            }

            // Then search for books
            var books = SearchForNewBook(title, null, false);
            foreach (var book in books)
            {
                // Add author from book results if not already present (by ID or name)
                if (book?.Author?.Value?.Metadata?.Value != null)
                {
                    var bookAuthor = book.Author.Value;
                    var authorId = bookAuthor.ForeignAuthorId;
                    var authorName = bookAuthor.Metadata.Value.Name?.Trim();

                    // Skip if we've already seen this author by ID or by name
                    var isNewById = authorId.IsNotNullOrWhiteSpace() && !seenAuthorIds.Contains(authorId);
                    var isNewByName = authorName.IsNotNullOrWhiteSpace() && !seenAuthorNames.Contains(authorName);

                    if (isNewById && isNewByName)
                    {
                        seenAuthorIds.Add(authorId);
                        seenAuthorNames.Add(authorName);
                        output.Add(bookAuthor);
                    }
                }

                // Add book if not already present
                if (book?.ForeignBookId.IsNotNullOrWhiteSpace() == true &&
                    seenBookIds.Add(book.ForeignBookId))
                {
                    output.Add(book);
                }
            }

            _logger.Debug("HardcoverProvider.SearchForNewEntity returned {0} result(s) for '{1}'.", output.Count, title);
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

            var configuredTimeout = _configService.HardcoverRequestTimeoutSeconds;
            var fallbackTimeout = Math.Max(5, _configService.MetadataProviderTimeoutSeconds);
            request.RequestTimeout = TimeSpan.FromSeconds(configuredTimeout > 0 ? configuredTimeout : fallbackTimeout);

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

            if (!ValidateGraphQlResponse(response.Resource, queryText))
            {
                return new List<Book>();
            }

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

            ValidateSearchResults(results, queryText);

            var mapped = results.Select(MapBook)
                .Where(x => x != null)
                .DistinctBy(x => x.ForeignBookId)
                .ToList();

            ResetDeterministicSearchErrorState();

            _logger.Debug("Hardcover returned {0} unique mapped result(s) for query '{1}'.", mapped.Count, queryText);

            return mapped;
        }

        private Book FetchBookByWorkId(int workId)
        {
            if (!_configService.EnableHardcoverFallback)
            {
                return null;
            }

            if (!TryResolveToken(out var token, out var tokenSource))
            {
                return null;
            }

            _logger.Trace("Hardcover direct work lookup: id={0}, tokenSource={1}", workId, tokenSource);

            var request = new HttpRequest(Endpoint, HttpAccept.Json)
            {
                Method = HttpMethod.Post,
                RateLimit = TimeSpan.FromSeconds(1),
                RateLimitKey = ProviderName
            };

            var configuredTimeout = _configService.HardcoverRequestTimeoutSeconds;
            var fallbackTimeout = Math.Max(5, _configService.MetadataProviderTimeoutSeconds);
            request.RequestTimeout = TimeSpan.FromSeconds(configuredTimeout > 0 ? configuredTimeout : fallbackTimeout);

            request.Headers.ContentType = "application/json; charset=utf-8";
            request.Headers["authorization"] = $"Bearer {token}";
            request.SetContent(new
            {
                query = @"query GetBook($id: Int!) {
                    books(where: {id: {_eq: $id}}, limit: 1) {
                        id
                        title
                        slug
                        description
                        release_date
                        release_year
                        pages
                        cached_image
                        cached_contributors
                        rating
                        ratings_count
                        book_series {
                            position
                            series {
                                id
                                name
                                books_count
                                primary_books_count
                            }
                        }
                        editions(limit: 1) {
                            isbn_13
                        }
                    }
                }",
                variables = new { id = workId }
            }.ToJson());

            try
            {
                var response = _httpClient.Post<JObject>(request);
                var booksArray = response.Resource?.SelectToken("data.books") as JArray;

                if (booksArray == null || !booksArray.Any())
                {
                    _logger.Debug("Hardcover direct lookup returned no result for work ID {0}.", workId);
                    return null;
                }

                var bookData = booksArray.First;
                var result = MapDirectBookResult(bookData);
                if (result != null)
                {
                    _logger.Debug("Hardcover direct lookup succeeded for work ID {0}: '{1}'.", workId, result.Title);
                }

                return result;
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "Hardcover direct work lookup failed for ID {0}.", workId);
                return null;
            }
        }

        private List<Book> FetchAuthorBooks(string authorName, out string authorImageUrl, out int authorIdResult, out string authorSlug)
        {
            authorImageUrl = null;
            authorIdResult = 0;
            authorSlug = null;

            if (!_configService.EnableHardcoverFallback)
            {
                return new List<Book>();
            }

            if (!TryResolveToken(out var token, out var tokenSource))
            {
                return new List<Book>();
            }

            // Step 1: Search for the author to get their Hardcover ID
            _logger.Trace("Hardcover author search: name='{0}', tokenSource={1}", authorName, tokenSource);

            var searchRequest = new HttpRequest(Endpoint, HttpAccept.Json)
            {
                Method = HttpMethod.Post,
                RateLimit = TimeSpan.FromSeconds(1),
                RateLimitKey = ProviderName
            };

            var configuredTimeout = _configService.HardcoverRequestTimeoutSeconds;
            var fallbackTimeout = Math.Max(5, _configService.MetadataProviderTimeoutSeconds);
            searchRequest.RequestTimeout = TimeSpan.FromSeconds(configuredTimeout > 0 ? configuredTimeout : fallbackTimeout);

            searchRequest.Headers.ContentType = "application/json; charset=utf-8";
            searchRequest.Headers["authorization"] = $"Bearer {token}";
            searchRequest.SetContent(new
            {
                query = @"query SearchAuthors($query: String!, $perPage: Int!) {
                    search(query: $query, query_type: ""Author"", per_page: $perPage, page: 1) {
                        error
                        ids
                    }
                }",
                variables = new { query = authorName, perPage = 5 }
            }.ToJson());

            try
            {
                var searchResponse = _httpClient.Post<JObject>(searchRequest);
                var searchData = searchResponse.Resource?.SelectToken("data.search");
                var idsArray = searchData?.SelectToken("ids") as JArray;

                if (idsArray == null || !idsArray.Any())
                {
                    _logger.Debug("Hardcover author search returned no results for '{0}'.", authorName);
                    return new List<Book>();
                }

                var authorId = idsArray.First.Value<int>();
                authorIdResult = authorId;
                _logger.Debug("Hardcover author search found ID {0} for '{1}'.", authorId, authorName);

                // Step 2: Fetch the author's contributions with book details
                var booksRequest = new HttpRequest(Endpoint, HttpAccept.Json)
                {
                    Method = HttpMethod.Post,
                    RateLimit = TimeSpan.FromSeconds(1),
                    RateLimitKey = ProviderName
                };

                booksRequest.RequestTimeout = TimeSpan.FromSeconds(configuredTimeout > 0 ? configuredTimeout : fallbackTimeout);
                booksRequest.Headers.ContentType = "application/json; charset=utf-8";
                booksRequest.Headers["authorization"] = $"Bearer {token}";
                booksRequest.SetContent(new
                {
                    query = @"query GetAuthorBooks($id: Int!) {
                        authors(where: {id: {_eq: $id}}) {
                            id
                            name
                            slug
                            image { url }
                            contributions(limit: 500, where: {contributable_type: {_eq: ""Book""}}) {
                                book {
                                    id
                                    title
                                    slug
                                    description
                                    release_date
                                    release_year
                                    pages
                                    cached_image
                                    cached_contributors
                                    rating
                                    ratings_count
                                    book_series {
                                        position
                                        series {
                                            id
                                            name
                                            books_count
                                            primary_books_count
                                        }
                                    }
                                    editions(limit: 1) {
                                        isbn_13
                                    }
                                }
                            }
                        }
                    }",
                    variables = new { id = authorId }
                }.ToJson());

                var booksResponse = _httpClient.Post<JObject>(booksRequest);
                var authorsArray = booksResponse.Resource?.SelectToken("data.authors") as JArray;

                if (authorsArray == null || !authorsArray.Any())
                {
                    _logger.Debug("Hardcover author lookup returned no results for ID {0}.", authorId);
                    return new List<Book>();
                }

                var authorData = authorsArray.First;

                // Extract author image URL and slug
                authorImageUrl = authorData.SelectToken("image.url")?.Value<string>();
                authorSlug = authorData.Value<string>("slug");

                var contributions = authorData.SelectToken("contributions") as JArray;

                if (contributions == null || !contributions.Any())
                {
                    _logger.Debug("Hardcover author {0} has no contributions.", authorId);
                    return new List<Book>();
                }

                var books = new List<Book>();
                var seenIds = new HashSet<string>();
                var seriesCount = 0;

                foreach (var contribution in contributions)
                {
                    var bookData = contribution.SelectToken("book");
                    if (bookData == null || bookData.Type == JTokenType.Null)
                    {
                        continue;
                    }

                    var book = MapDirectBookResult(bookData);
                    if (book != null && seenIds.Add(book.ForeignBookId))
                    {
                        books.Add(book);
                        if (book.SeriesLinks?.Value?.Any() == true)
                        {
                            seriesCount++;
                        }
                    }
                }

                _logger.Debug("Hardcover author search for '{0}' returned {1} books via contributions ({2} with series).", authorName, books.Count, seriesCount);
                return books;
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "Hardcover author search failed for '{0}'.", authorName);
                return new List<Book>();
            }
        }

        /// <summary>
        /// Fetches an author's books directly by numeric Hardcover author ID,
        /// skipping the name-search step. Used when the ForeignAuthorId already
        /// contains a resolved numeric ID (e.g. "hardcover:author:154441").
        /// </summary>
        private List<Book> FetchAuthorBooksById(int authorId, out string authorImageUrl, out string authorSlug, out string authorName)
        {
            authorImageUrl = null;
            authorSlug = null;
            authorName = null;

            if (!_configService.EnableHardcoverFallback)
            {
                return new List<Book>();
            }

            if (!TryResolveToken(out var token, out var tokenSource))
            {
                return new List<Book>();
            }

            _logger.Trace("Hardcover direct author fetch: id={0}, tokenSource={1}", authorId, tokenSource);

            var configuredTimeout = _configService.HardcoverRequestTimeoutSeconds;
            var fallbackTimeout = Math.Max(5, _configService.MetadataProviderTimeoutSeconds);

            var request = new HttpRequest(Endpoint, HttpAccept.Json)
            {
                Method = HttpMethod.Post,
                RateLimit = TimeSpan.FromSeconds(1),
                RateLimitKey = ProviderName
            };

            request.RequestTimeout = TimeSpan.FromSeconds(configuredTimeout > 0 ? configuredTimeout : fallbackTimeout);
            request.Headers.ContentType = "application/json; charset=utf-8";
            request.Headers["authorization"] = $"Bearer {token}";
            request.SetContent(new
            {
                query = @"query GetAuthorBooks($id: Int!) {
                    authors(where: {id: {_eq: $id}}) {
                        id
                        name
                        slug
                        image { url }
                        contributions(limit: 500, where: {contributable_type: {_eq: ""Book""}}) {
                            book {
                                id
                                title
                                slug
                                description
                                release_date
                                release_year
                                pages
                                cached_image
                                cached_contributors
                                rating
                                ratings_count
                                book_series {
                                    position
                                    series {
                                        id
                                        name
                                        books_count
                                        primary_books_count
                                    }
                                }
                                editions(limit: 1) {
                                    isbn_13
                                }
                            }
                        }
                    }
                }",
                variables = new { id = authorId }
            }.ToJson());

            try
            {
                var response = _httpClient.Post<JObject>(request);
                var authorsArray = response.Resource?.SelectToken("data.authors") as JArray;

                if (authorsArray == null || !authorsArray.Any())
                {
                    _logger.Debug("Hardcover direct author fetch returned no results for ID {0}.", authorId);
                    return new List<Book>();
                }

                var authorData = authorsArray.First;

                authorImageUrl = authorData.SelectToken("image.url")?.Value<string>();
                authorSlug = authorData.Value<string>("slug");
                authorName = authorData.Value<string>("name")?.Trim();

                var contributions = authorData.SelectToken("contributions") as JArray;

                if (contributions == null || !contributions.Any())
                {
                    _logger.Debug("Hardcover author {0} has no contributions.", authorId);
                    return new List<Book>();
                }

                var books = new List<Book>();
                var seenIds = new HashSet<string>();
                var seriesCount = 0;

                foreach (var contribution in contributions)
                {
                    var bookData = contribution.SelectToken("book");
                    if (bookData == null || bookData.Type == JTokenType.Null)
                    {
                        continue;
                    }

                    var book = MapDirectBookResult(bookData);
                    if (book != null && seenIds.Add(book.ForeignBookId))
                    {
                        books.Add(book);
                        if (book.SeriesLinks?.Value?.Any() == true)
                        {
                            seriesCount++;
                        }
                    }
                }

                _logger.Debug(
                    "Hardcover direct author fetch for ID {0} ('{1}') returned {2} books ({3} with series).",
                    authorId,
                    authorName ?? "?",
                    books.Count,
                    seriesCount);
                return books;
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "Hardcover direct author fetch failed for ID {0}.", authorId);
                return new List<Book>();
            }
        }

        private List<Series> FetchAuthorSeries(int authorId)
        {
            if (!TryResolveToken(out var token, out var tokenSource))
            {
                return new List<Series>();
            }

            _logger.Trace("Hardcover series fetch for author ID {0}, tokenSource={1}", authorId, tokenSource);

            var request = new HttpRequest(Endpoint, HttpAccept.Json)
            {
                Method = HttpMethod.Post,
                RateLimit = TimeSpan.FromSeconds(1),
                RateLimitKey = ProviderName
            };

            var configuredTimeout = _configService.HardcoverRequestTimeoutSeconds;
            var fallbackTimeout = Math.Max(5, _configService.MetadataProviderTimeoutSeconds);
            request.RequestTimeout = TimeSpan.FromSeconds(configuredTimeout > 0 ? configuredTimeout : fallbackTimeout);

            request.Headers.ContentType = "application/json; charset=utf-8";
            request.Headers["authorization"] = $"Bearer {token}";
            request.SetContent(new
            {
                query = @"query GetAuthorSeries($authorId: Int!) {
                    book_series(where: {book: {contributions: {author_id: {_eq: $authorId}, contributable_type: {_eq: ""Book""}}}}) {
                        book_id
                        series_id
                        position
                        series {
                            id
                            name
                            books_count
                            primary_books_count
                        }
                    }
                }",
                variables = new { authorId }
            }.ToJson());

            try
            {
                var response = _httpClient.Post<JObject>(request);
                var seriesArray = response.Resource?.SelectToken("data.book_series") as JArray;

                if (seriesArray == null || !seriesArray.Any())
                {
                    _logger.Debug("Hardcover series fetch returned no results for author ID {0}.", authorId);
                    return new List<Series>();
                }

                // Group by series ID and build Series objects with LinkItems
                var grouped = seriesArray
                    .GroupBy(entry => entry.SelectToken("series.id")?.Value<int>() ?? 0)
                    .Where(g => g.Key > 0)
                    .ToList();

                var result = new List<Series>();

                foreach (var group in grouped)
                {
                    var firstEntry = group.First();
                    var seriesName = firstEntry.SelectToken("series.name")?.Value<string>();
                    if (seriesName.IsNullOrWhiteSpace())
                    {
                        continue;
                    }

                    var series = new Series
                    {
                        ForeignSeriesId = $"hardcover:series:{group.Key}",
                        Title = seriesName,
                        WorkCount = firstEntry.SelectToken("series.books_count")?.Value<int>() ?? 0,
                        PrimaryWorkCount = firstEntry.SelectToken("series.primary_books_count")?.Value<int>() ?? 0,
                        Numbered = true
                    };

                    var links = new List<SeriesBookLink>();

                    foreach (var entry in group)
                    {
                        var bookId = entry.Value<int?>("book_id");
                        if (bookId == null || bookId <= 0)
                        {
                            continue;
                        }

                        var position = entry.Value<decimal?>("position");

                        var stubBook = new Book
                        {
                            ForeignBookId = $"hardcover:work:{bookId}"
                        };

                        var link = new SeriesBookLink
                        {
                            IsPrimary = true,
                            Position = position?.ToString(CultureInfo.InvariantCulture),
                            SeriesPosition = (int)(position ?? 0),
                            Book = stubBook,
                            Series = series
                        };

                        links.Add(link);
                    }

                    if (links.Any())
                    {
                        series.LinkItems = links;
                        result.Add(series);
                    }
                }

                _logger.Debug(
                    "Hardcover series fetch for author ID {0} returned {1} series with {2} total book links.",
                    authorId,
                    result.Count,
                    result.Sum(s => s.LinkItems.Value.Count));

                return result.OrderBy(x => x.Title).ToList();
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "Hardcover series fetch failed for author ID {0}.", authorId);
                return new List<Series>();
            }
        }

        private static Book MapDirectBookResult(JToken bookData)
        {
            if (bookData == null)
            {
                return null;
            }

            var id = bookData.Value<int?>("id")?.ToString();
            if (id.IsNullOrWhiteSpace())
            {
                return null;
            }

            var title = bookData.Value<string>("title") ?? id;
            var slug = bookData.Value<string>("slug");
            var description = bookData.Value<string>("description");
            var releaseDate = bookData.Value<string>("release_date");
            var releaseYear = bookData.Value<int?>("release_year");
            var pages = bookData.Value<int?>("pages");
            var rating = bookData.Value<double?>("rating");
            var ratingsCount = bookData.Value<int?>("ratings_count");

            var coverUrl = bookData.SelectToken("cached_image.url")?.Value<string>();
            var authorName = bookData.SelectToken("cached_contributors[0].author.name")?.Value<string>()
                             ?? "Unknown Author";
            var authorId = bookData.SelectToken("cached_contributors[0].author.id")?.Value<int?>();
            var authorSlug = bookData.SelectToken("cached_contributors[0].author.slug")?.Value<string>();
            var isbn13 = bookData.SelectToken("editions[0].isbn_13")?.Value<string>();

            var publishedDate = ParseDate(releaseDate, releaseYear);

            // Prefer numeric author ID if available, fall back to name-based ID
            var foreignAuthorId = authorId.HasValue
                ? BuildHardcoverAuthorId(authorId.Value)
                : BuildHardcoverAuthorId(authorName);

            var authorMetadata = new AuthorMetadata
            {
                ForeignAuthorId = foreignAuthorId,
                TitleSlug = foreignAuthorId.ToUrlSlug(),
                Name = authorName,
                SortName = authorName.ToLowerInvariant(),
                NameLastFirst = authorName.ToLastFirst(),
                SortNameLastFirst = authorName.ToLastFirst().ToLowerInvariant(),
                Overview = description,
                Ratings = new Ratings
                {
                    Votes = ratingsCount ?? 0,
                    Value = (decimal)(rating ?? 0)
                }
            };

            var author = new Author
            {
                Metadata = authorMetadata,
                AuthorMetadataId = authorMetadata.Id
            };

            var book = new Book
            {
                ForeignBookId = $"hardcover:work:{id}",
                TitleSlug = $"hardcover:work:{id}".ToUrlSlug(),
                Title = title,
                CleanTitle = title,
                AuthorMetadata = authorMetadata,
                AuthorMetadataId = authorMetadata.Id,
                Author = author,
                ReleaseDate = publishedDate,
                Ratings = new Ratings
                {
                    Votes = ratingsCount ?? 0,
                    Value = (decimal)(rating ?? 0)
                },
                Genres = new List<string>()
            };

            var bookSeriesArray = bookData.SelectToken("book_series") as JArray;
            var firstSeries = bookSeriesArray?.FirstOrDefault();
            if (firstSeries != null && firstSeries.Type != JTokenType.Null)
            {
                var seriesName = firstSeries.SelectToken("series.name")?.Value<string>();
                if (seriesName.IsNotNullOrWhiteSpace())
                {
                    var series = new Series
                    {
                        ForeignSeriesId = $"hardcover:series:{firstSeries.SelectToken("series.id")?.Value<int>()}",
                        Title = seriesName,
                        WorkCount = firstSeries.SelectToken("series.books_count")?.Value<int>() ?? 0,
                        PrimaryWorkCount = firstSeries.SelectToken("series.primary_books_count")?.Value<int>() ?? 0,
                        Numbered = true
                    };

                    var position = firstSeries.Value<decimal?>("position");
                    var seriesLink = new SeriesBookLink
                    {
                        IsPrimary = true,
                        Position = position?.ToString(CultureInfo.InvariantCulture),
                        SeriesPosition = (int)(position ?? 0),
                        Book = book,
                        Series = series
                    };

                    series.LinkItems = new List<SeriesBookLink> { seriesLink };
                    book.SeriesLinks = new List<SeriesBookLink> { seriesLink };
                }
            }

            var edition = new Edition
            {
                ForeignEditionId = $"hardcover:edition:{id}",
                TitleSlug = $"hardcover:edition:{id}".ToUrlSlug(),
                Title = title,
                ReleaseDate = publishedDate,
                Language = null,
                Isbn13 = isbn13,
                Book = book,
                PageCount = pages ?? 0,
                Overview = description,
                Ratings = new Ratings
                {
                    Votes = ratingsCount ?? 0,
                    Value = (decimal)(rating ?? 0)
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

            var bookSlug = slug.IsNotNullOrWhiteSpace() ? slug : id;
            var hardcoverBookUrl = $"https://hardcover.app/books/{bookSlug}";
            book.Links = new List<Links> { new Links { Name = "Hardcover", Url = hardcoverBookUrl } };
            edition.Links = new List<Links> { new Links { Name = "Hardcover", Url = hardcoverBookUrl } };

            return book;
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

        private bool ValidateGraphQlResponse(HardcoverGraphQlResponse response, string queryText)
        {
            if (response == null)
            {
                _logger.Warn("Hardcover returned null response for query '{0}'.", queryText);
                return false;
            }

            if (response.Errors != null && response.Errors.Any())
            {
                var errorMessages = string.Join("; ", response.Errors.Select(e => e.Message ?? "unknown error"));
                _logger.Warn("Hardcover GraphQL returned error(s) for query '{0}': {1}", queryText, errorMessages);

                if (response.Data == null)
                {
                    return false;
                }
            }

            if (response.Data == null)
            {
                _logger.Warn("Hardcover GraphQL response missing 'data' envelope for query '{0}'.", queryText);
                return false;
            }

            return true;
        }

        private void ValidateSearchResults(List<HardcoverSearchResult> results, string queryText)
        {
            var missingIds = results.Count(r => r.Id.IsNullOrWhiteSpace());
            var missingTitles = results.Count(r => r.Title.IsNullOrWhiteSpace());
            var missingAuthors = results.Count(r =>
                (r.Contributions == null || !r.Contributions.Any()) &&
                (r.AuthorNames == null || !r.AuthorNames.Any()));

            if (missingIds > 0)
            {
                _logger.Warn("Hardcover returned {0}/{1} result(s) with missing IDs for query '{2}' — these will be skipped", missingIds, results.Count, queryText);
            }

            if (missingTitles > 0)
            {
                _logger.Debug("Hardcover returned {0}/{1} result(s) with missing titles for query '{2}' — ID will be used as fallback title", missingTitles, results.Count, queryText);
            }

            if (missingAuthors > 0)
            {
                _logger.Debug("Hardcover returned {0}/{1} result(s) with no author information for query '{2}' — 'Unknown Author' will be used", missingAuthors, results.Count, queryText);
            }
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
            var contributor = result.Contributions?.FirstOrDefault(x => x?.Author?.Name.IsNotNullOrWhiteSpace() == true);
            var authorName = contributor?.Author?.Name?.Trim()
                             ?? result.AuthorNames?.FirstOrDefault(x => x.IsNotNullOrWhiteSpace())?.Trim()
                             ?? "Unknown Author";
            var authorId = contributor?.Author?.Id;

            var publishedDate = ParseDate(result.ReleaseDate, result.ReleaseYear);
            var isbn13 = result.Isbns?.FirstOrDefault(x => IsIsbn13(x));
            var coverUrl = result.Image?.Url;

            // Prefer numeric author ID if available, fall back to name-based ID
            var foreignAuthorId = authorId.HasValue && authorId.Value > 0
                ? BuildHardcoverAuthorId(authorId.Value)
                : BuildHardcoverAuthorId(authorName);

            var authorMetadata = new AuthorMetadata
            {
                ForeignAuthorId = foreignAuthorId,
                TitleSlug = foreignAuthorId.ToUrlSlug(),
                Name = authorName,
                SortName = authorName.ToLowerInvariant(),
                NameLastFirst = authorName.ToLastFirst(),
                SortNameLastFirst = authorName.ToLastFirst().ToLowerInvariant(),
                Overview = result.Description,
                Ratings = new Ratings { Votes = 0, Value = 0 }
            };

            var author = new Author
            {
                Metadata = authorMetadata,
                AuthorMetadataId = authorMetadata.Id
            };

            var book = new Book
            {
                ForeignBookId = $"hardcover:work:{result.Id}",
                TitleSlug = $"hardcover:work:{result.Id}".ToUrlSlug(),
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
                TitleSlug = $"hardcover:edition:{result.Id}".ToUrlSlug(),
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

            // Populate Links for author, book, and edition
            var bookSlug = result.Slug.IsNotNullOrWhiteSpace() ? result.Slug : result.Id;
            var hardcoverBookUrl = $"https://hardcover.app/books/{bookSlug}";
            book.Links = new List<Links> { new Links { Name = "Hardcover", Url = hardcoverBookUrl } };
            edition.Links = new List<Links> { new Links { Name = "Hardcover", Url = hardcoverBookUrl } };

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
                TitleSlug = BuildHardcoverAuthorId(trimmed).ToUrlSlug(),
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

        private static string BuildHardcoverAuthorId(int numericId)
        {
            return $"hardcover:author:{numericId}";
        }

        /// <summary>
        /// Attempts to parse a numeric Hardcover author ID from a ForeignAuthorId string.
        /// Returns true if the token after "hardcover:author:" is a positive integer.
        /// </summary>
        private static bool TryParseNumericHardcoverAuthorId(string foreignAuthorId, out int numericId)
        {
            numericId = 0;

            if (foreignAuthorId.IsNullOrWhiteSpace())
            {
                return false;
            }

            const string prefix = "hardcover:author:";
            var raw = foreignAuthorId.Trim();

            if (!raw.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var token = raw.Substring(prefix.Length);
            return int.TryParse(token, out numericId) && numericId > 0;
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

        [JsonProperty("errors")]
        public List<HardcoverGraphQlError> Errors { get; set; }
    }

    public class HardcoverGraphQlError
    {
        [JsonProperty("message")]
        public string Message { get; set; }

        [JsonProperty("extensions")]
        public JToken Extensions { get; set; }
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
        [JsonProperty("id")]
        public int? Id { get; set; }

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
