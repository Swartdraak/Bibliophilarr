using System;
using System.Collections.Generic;
using System.Linq;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Books;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.MetadataSource.OpenLibrary.Resources;

namespace NzbDrone.Core.MetadataSource.OpenLibrary
{
    /// <summary>
    /// Primary metadata provider backed by the Open Library REST API.
    /// </summary>
    public class OpenLibraryProvider :
        IMetadataProvider,
        IProvideAuthorInfo,
        IProvideBookInfo,
        ISearchForNewBook,
        ISearchForNewAuthor,
        ISearchForNewEntity
    {
        // ── IMetadataProvider ────────────────────────────────────────────────
        public string ProviderName => "OpenLibrary";
        public int Priority => 1;
        public bool IsEnabled => _configService.EnableOpenLibraryProvider;
        public bool SupportsAuthorSearch => true;
        public bool SupportsBookSearch => true;
        public bool SupportsIsbnLookup => true;
        public bool SupportsSeriesInfo => false;
        public bool SupportsCoverImages => true;

        private readonly IOpenLibraryClient _client;
        private readonly IConfigService _configService;
        private readonly Logger _logger;

        public OpenLibraryProvider(IOpenLibraryClient client, IConfigService configService, Logger logger)
        {
            _client = client;
            _configService = configService;
            _logger = logger;
        }

        // ── IProvideAuthorInfo ───────────────────────────────────────────────
        public Author GetAuthorInfo(string foreignAuthorId, bool useCache = true)
        {
            _logger.Debug("OpenLibraryProvider.GetAuthorInfo: {0}", foreignAuthorId);

            var normalizedAuthorId = NormalizeAuthorId(foreignAuthorId);

            // foreignAuthorId may arrive as either "OL{n}A" or "openlibrary:author:OL{n}A"
            var authorResource = _client.GetAuthor(normalizedAuthorId);
            if (authorResource == null)
            {
                throw new NzbDrone.Core.Exceptions.AuthorNotFoundException(foreignAuthorId);
            }

            var metadata = OpenLibraryMapper.MapAuthorToMetadata(authorResource);
            if (metadata == null)
            {
                throw new NzbDrone.Core.Exceptions.AuthorNotFoundException(foreignAuthorId);
            }

            var works = GetAuthorWorkEntries(normalizedAuthorId);
            var workDocsById = GetAuthorWorkSearchDocs(normalizedAuthorId)
                .Where(x => x?.Key.IsNotNullOrWhiteSpace() == true)
                .GroupBy(x => NormalizeWorkId(x.Key))
                .Where(x => x.Key.IsNotNullOrWhiteSpace())
                .ToDictionary(x => x.Key, x => x.First());

            var books = works
                .Select(work => MapAuthorWorkToBook(work, authorResource, metadata, workDocsById))
                .Where(book => book != null)
                .DistinctBy(b => b.ForeignBookId)
                .ToList();

            if (!books.Any() && workDocsById.Any())
            {
                books = BuildBooksFromSearchDocFallback(workDocsById.Values, metadata);
            }

            var series = BuildAuthorSeries(books);

            return new Author
            {
                Metadata = metadata,
                CleanName = Parser.Parser.CleanAuthorName(metadata.Name),
                Books = books,
                Series = series
            };
        }

        private static List<Book> BuildBooksFromSearchDocFallback(IEnumerable<OlSearchDoc> docs, AuthorMetadata metadata)
        {
            return (docs ?? Enumerable.Empty<OlSearchDoc>())
                .Select(OpenLibraryMapper.MapSearchDocToBook)
                .Where(book => book != null)
                .Select(book =>
                {
                    book.AuthorMetadata = metadata;
                    if (book.Author?.Value != null)
                    {
                        book.Author.Value.Metadata = metadata;
                    }

                    return book;
                })
                .DistinctBy(book => book.ForeignBookId)
                .ToList();
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

        private List<OlWorkResource> GetAuthorWorkEntries(string normalizedAuthorId)
        {
            const int pageSize = 100;
            const int maxWorks = 1000;

            var works = new List<OlWorkResource>();
            var offset = 0;

            while (offset < maxWorks)
            {
                var response = _client.GetAuthorWorks(normalizedAuthorId, pageSize, offset);
                var page = response?.Entries ?? new List<OlWorkResource>();

                if (!page.Any())
                {
                    break;
                }

                works.AddRange(page);
                offset += page.Count;

                if (page.Count < pageSize)
                {
                    break;
                }

                if (response != null && response.Size > 0 && offset >= response.Size)
                {
                    break;
                }
            }

            return works;
        }

        private List<OlSearchDoc> GetAuthorWorkSearchDocs(string normalizedAuthorId)
        {
            const int pageSize = 100;
            const int maxDocs = 1000;

            var query = $"author_key:{normalizedAuthorId}";
            var docs = new List<OlSearchDoc>();
            var offset = 0;

            while (offset < maxDocs)
            {
                var response = _client.Search(query, pageSize, offset);
                var page = response?.Docs ?? new List<OlSearchDoc>();

                if (!page.Any())
                {
                    break;
                }

                docs.AddRange(page);
                offset += page.Count;

                if (page.Count < pageSize)
                {
                    break;
                }

                if (response != null && response.NumFound > 0 && offset >= response.NumFound)
                {
                    break;
                }
            }

            return docs;
        }

        private Book MapAuthorWorkToBook(OlWorkResource work,
                                         OlAuthorResource authorResource,
                                         AuthorMetadata metadata,
                                         IReadOnlyDictionary<string, OlSearchDoc> searchDocsById)
        {
            var workBook = OpenLibraryMapper.MapWorkToBook(work, authorResource);
            if (workBook == null)
            {
                return null;
            }

            var book = workBook;
            var normalizedWorkId = NormalizeWorkId(work?.Key);

            if (normalizedWorkId.IsNotNullOrWhiteSpace() && searchDocsById.TryGetValue(normalizedWorkId, out var searchDoc))
            {
                book = OpenLibraryMapper.MapSearchDocToBook(searchDoc) ?? workBook;
            }

            // Contract: works feed is the identity source; search docs are enrichment.
            ApplyWorksIdentityContract(book, workBook);
            EnrichBookFromWork(book, workBook);

            book.AuthorMetadata = metadata;
            if (book.Author?.Value != null)
            {
                book.Author.Value.Metadata = metadata;
            }

            return book;
        }

        private static void ApplyWorksIdentityContract(Book book, Book workBook)
        {
            if (book == null || workBook == null)
            {
                return;
            }

            if (workBook.ForeignBookId.IsNotNullOrWhiteSpace())
            {
                book.ForeignBookId = workBook.ForeignBookId;
            }

            if (workBook.OpenLibraryWorkId.IsNotNullOrWhiteSpace())
            {
                book.OpenLibraryWorkId = workBook.OpenLibraryWorkId;
            }

            if (workBook.TitleSlug.IsNotNullOrWhiteSpace())
            {
                book.TitleSlug = workBook.TitleSlug;
            }
        }

        private static void EnrichBookFromWork(Book book, Book workBook)
        {
            if (book == null || workBook == null)
            {
                return;
            }

            if (!book.ReleaseDate.HasValue)
            {
                book.ReleaseDate = workBook.ReleaseDate;
            }

            if (!book.Genres.Any() && workBook.Genres.Any())
            {
                book.Genres = workBook.Genres;
            }

            if (!book.Links.Any() && workBook.Links.Any())
            {
                book.Links = workBook.Links;
            }

            var edition = book.Editions?.Value?.FirstOrDefault();
            var workEdition = workBook.Editions?.Value?.FirstOrDefault();

            if (edition == null || workEdition == null)
            {
                return;
            }

            if (edition.Overview.IsNullOrWhiteSpace())
            {
                edition.Overview = workEdition.Overview;
            }

            if (edition.ReleaseDate == null)
            {
                edition.ReleaseDate = workEdition.ReleaseDate;
            }

            if (edition.Format.IsNullOrWhiteSpace())
            {
                edition.Format = workEdition.Format;
            }

            if (edition.Publisher.IsNullOrWhiteSpace())
            {
                edition.Publisher = workEdition.Publisher;
            }

            if (edition.PageCount == 0)
            {
                edition.PageCount = workEdition.PageCount;
            }

            if (edition.Language.IsNullOrWhiteSpace())
            {
                edition.Language = workEdition.Language;
            }

            if (!edition.Images.Any() && workEdition.Images.Any())
            {
                edition.Images = workEdition.Images;
            }

            if (!edition.Links.Any() && workEdition.Links.Any())
            {
                edition.Links = workEdition.Links;
            }

            if ((edition.Ratings?.Votes ?? 0) == 0 && (workEdition.Ratings?.Votes ?? 0) > 0)
            {
                edition.Ratings = workEdition.Ratings;
            }
        }

        private static string NormalizeAuthorId(string foreignAuthorId)
        {
            if (foreignAuthorId.IsNullOrWhiteSpace())
            {
                return foreignAuthorId;
            }

            var normalizedAuthorId = OpenLibraryIdNormalizer.NormalizeAuthorId(foreignAuthorId);
            if (normalizedAuthorId.IsNotNullOrWhiteSpace())
            {
                return normalizedAuthorId;
            }

            const string prefix = "openlibrary:author:";

            var normalized = foreignAuthorId.Trim();

            if (normalized.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                normalized = normalized.Substring(prefix.Length);
            }

            return normalized;
        }

        public HashSet<string> GetChangedAuthors(DateTime startTime)
        {
            // Open Library does not expose a "changed since" feed in a queryable form.
            _logger.Debug("OpenLibraryProvider.GetChangedAuthors: not supported, returning null");
            return null;
        }

        // ── IProvideBookInfo ─────────────────────────────────────────────────
        public Tuple<string, Book, List<AuthorMetadata>> GetBookInfo(string foreignBookId)
        {
            _logger.Debug("OpenLibraryProvider.GetBookInfo: {0}", foreignBookId);

            var normalizedWorkId = NormalizeWorkId(foreignBookId);
            OlEditionResource edition = null;

            // foreignBookId may arrive as either "OL{n}W" or "openlibrary:work:OL{n}W"
            var work = _client.GetWork(normalizedWorkId);

            // Some legacy rows carry an OL edition id (OL...M) in ForeignBookId.
            // Resolve edition -> work key, then continue via the work endpoint.
            if (work == null && normalizedWorkId.IsNotNullOrWhiteSpace() && normalizedWorkId.EndsWith("M", StringComparison.OrdinalIgnoreCase))
            {
                edition = _client.GetEdition(normalizedWorkId);
                var workKey = edition?.Works?.FirstOrDefault()?.Key;
                var normalizedFromEdition = NormalizeWorkId(workKey);

                if (normalizedFromEdition.IsNullOrWhiteSpace())
                {
                    var resolvedFromEditionIsbn = ResolveBookInfoFromEditionIsbn(edition);

                    if (resolvedFromEditionIsbn != null)
                    {
                        return resolvedFromEditionIsbn;
                    }
                }

                if (normalizedFromEdition.IsNotNullOrWhiteSpace())
                {
                    normalizedWorkId = normalizedFromEdition;
                    work = _client.GetWork(normalizedWorkId);
                }
            }

            if (work == null)
            {
                throw new NzbDrone.Core.Exceptions.BookNotFoundException(foreignBookId);
            }

            // Fetch primary author
            var authorKey = work.Authors?
                .Where(a => a.Author?.Key != null)
                .Select(a => a.Author.Key)
                .FirstOrDefault();

            authorKey ??= edition?.Authors?.FirstOrDefault()?.Key;

            OlAuthorResource authorResource = null;
            if (authorKey != null)
            {
                authorResource = _client.GetAuthor(authorKey);
            }

            var book = OpenLibraryMapper.MapWorkToBook(work, authorResource);
            if (book == null)
            {
                throw new NzbDrone.Core.Exceptions.BookNotFoundException(foreignBookId);
            }

            var representativeEditions = GetRepresentativeEditions(normalizedWorkId);
            if (representativeEditions.Any())
            {
                representativeEditions[0].Monitored = true;

                foreach (var additionalEdition in representativeEditions.Skip(1))
                {
                    additionalEdition.Monitored = false;
                }

                book.Editions = representativeEditions;
                EnrichBookFromEdition(book, representativeEditions[0]);
            }

            var authorId = book.AuthorMetadata?.Value?.ForeignAuthorId ?? "OL-unknown";
            var authorMetaList = book.AuthorMetadata?.Value != null
                ? new List<AuthorMetadata> { book.AuthorMetadata.Value }
                : new List<AuthorMetadata>();

            return Tuple.Create(authorId, book, authorMetaList);
        }

        private List<Edition> GetRepresentativeEditions(string normalizedWorkId)
        {
            var response = _client.GetWorkEditions(normalizedWorkId, 20, 0);

            return (response?.Entries ?? new List<OlEditionResource>())
                .Select(OpenLibraryMapper.MapEdition)
                .Where(x => x != null)
                .OrderByDescending(IsPreferredEdition)
                .ThenByDescending(x => x.Images.Any())
                .ThenByDescending(x => x.PageCount)
                .ThenByDescending(x => x.ReleaseDate)
                .DistinctBy(x => x.ForeignEditionId)
                .Take(5)
                .ToList();
        }

        private static bool IsPreferredEdition(Edition edition)
        {
            var language = edition.Language?.Trim();
            var isEnglishOrUnknown = language.IsNullOrWhiteSpace() || language.Equals("eng", StringComparison.OrdinalIgnoreCase);
            var hasIdentifier = edition.Isbn13.IsNotNullOrWhiteSpace() || edition.Asin.IsNotNullOrWhiteSpace();

            return isEnglishOrUnknown && hasIdentifier;
        }

        private static void EnrichBookFromEdition(Book book, Edition edition)
        {
            if (book == null || edition == null)
            {
                return;
            }

            if (!book.ReleaseDate.HasValue)
            {
                book.ReleaseDate = edition.ReleaseDate;
            }
        }

        // ── ISearchForNewBook ────────────────────────────────────────────────
        public List<Book> SearchForNewBook(string title, string author, bool getAllEditions = true)
        {
            _logger.Debug("OpenLibraryProvider.SearchForNewBook: title={0} author={1}", title, author);

            try
            {
                var query = (title ?? string.Empty).ToLowerInvariant().Trim();

                if (author.IsNotNullOrWhiteSpace())
                {
                    query += " " + author;
                }

                var lowerTitle = title?.ToLowerInvariant() ?? string.Empty;
                var split = lowerTitle.Split(':');
                var prefix = split[0];

                if (split.Length == 2 && new[] { "author", "work", "edition", "isbn", "asin" }.Contains(prefix))
                {
                    var slug = split[1].Trim();

                    if (slug.IsNullOrWhiteSpace() || slug.Any(char.IsWhiteSpace))
                    {
                        return new List<Book>();
                    }

                    if (prefix == "author" || prefix == "work" || prefix == "edition")
                    {
                        var normalizedId = NormalizePrefixedOpenLibraryId(prefix, slug);
                        if (normalizedId.IsNullOrWhiteSpace())
                        {
                            return new List<Book>();
                        }

                        if (prefix == "author")
                        {
                            return SearchByOpenLibraryAuthorId(normalizedId);
                        }

                        if (prefix == "work")
                        {
                            return SearchByOpenLibraryWorkId(normalizedId, getAllEditions);
                        }

                        return SearchByOpenLibraryEditionId(normalizedId, getAllEditions);
                    }

                    query = slug;
                }

                return Search(query, getAllEditions);
            }
            catch (OpenLibraryException ex)
            {
                _logger.Warn(ex, "OpenLibrary search failed for '{0}'", title);
                return new List<Book>();
            }
        }

        public List<Book> SearchByIsbn(string isbn)
        {
            _logger.Debug("OpenLibraryProvider.SearchByIsbn: {0}", isbn);

            try
            {
                var edition = _client.GetEditionByIsbn(isbn);
                if (edition == null)
                {
                    return new List<Book>();
                }

                var mappedEdition = OpenLibraryMapper.MapEdition(edition);
                if (mappedEdition == null)
                {
                    return new List<Book>();
                }

                // Try to get full work for title / author
                var workKey = edition.Works?.FirstOrDefault()?.Key;
                if (workKey != null)
                {
                    var work = _client.GetWork(workKey);
                    var authorKey = work?.Authors?.FirstOrDefault()?.Author?.Key;
                    var authorResource = authorKey != null ? _client.GetAuthor(authorKey) : null;

                    var book = OpenLibraryMapper.MapWorkToBook(work, authorResource);
                    if (book != null)
                    {
                        // Prefer the edition we already fetched (has ISBN)
                        if (!book.Editions.Value.Any(e => e.Isbn13 == isbn))
                        {
                            book.Editions.Value.Insert(0, mappedEdition);
                        }

                        return new List<Book> { book };
                    }
                }

                var isbnSearchResults = SearchEditionByIsbn(isbn, mappedEdition);
                if (isbnSearchResults.Any())
                {
                    return isbnSearchResults;
                }

                // Fallback: build a minimal book from edition only
                var fallback = BuildBookFromEditionOnly(mappedEdition, edition);
                return fallback != null ? new List<Book> { fallback } : new List<Book>();
            }
            catch (OpenLibraryException ex)
            {
                _logger.Warn(ex, "OpenLibrary ISBN lookup failed for '{0}'", isbn);
                return new List<Book>();
            }
        }

        public List<Book> SearchByAsin(string asin)
        {
            _logger.Debug("OpenLibraryProvider.SearchByAsin: {0}", asin);
            return Search(asin, true);
        }

        public List<Book> SearchByExternalId(string idType, string id)
        {
            _logger.Debug("OpenLibraryProvider.SearchByExternalId: type={0} id={1}", idType, id);

            if (idType == "isbn")
            {
                return SearchByIsbn(id);
            }

            if (idType == "olid" || idType == "openlibrary")
            {
                var token = OpenLibraryIdNormalizer.NormalizeBookToken(id);

                if (token.IsNullOrWhiteSpace())
                {
                    return new List<Book>();
                }

                if (token.EndsWith("A", StringComparison.OrdinalIgnoreCase))
                {
                    return SearchByOpenLibraryAuthorId(token);
                }

                if (token.EndsWith("M", StringComparison.OrdinalIgnoreCase))
                {
                    return SearchByOpenLibraryEditionId(token, true);
                }

                if (token.EndsWith("W", StringComparison.OrdinalIgnoreCase))
                {
                    return SearchByOpenLibraryWorkId(token, true);
                }

                return new List<Book>();
            }

            if (idType == "asin")
            {
                return SearchByAsin(id);
            }

            // Unknown idType
            _logger.Debug("OpenLibraryProvider: id type '{0}' not supported; returning empty.", idType);
            return new List<Book>();
        }

        // ── ISearchForNewAuthor ──────────────────────────────────────────────
        public List<Author> SearchForNewAuthor(string title)
        {
            _logger.Debug("OpenLibraryProvider.SearchForNewAuthor: {0}", title);

            var response = _client.Search(title, limit: 10);
            return (response?.Docs ?? new List<OlSearchDoc>())
                .Select(d => OpenLibraryMapper.MapSearchDocToBook(d))
                .Where(b => b?.Author?.Value != null)
                .Select(b => b.Author.Value)
                .DistinctBy(a => a.ForeignAuthorId)
                .ToList();
        }

        // ── ISearchForNewEntity ──────────────────────────────────────────────
        public List<object> SearchForNewEntity(string title)
        {
            var books = SearchForNewBook(title, null, false);
            var result = new List<object>();

            foreach (var book in books)
            {
                var author = book.Author.Value;
                if (!result.Contains(author))
                {
                    result.Add(author);
                }

                result.Add(book);
            }

            return result;
        }

        // ── Private helpers ──────────────────────────────────────────────────
        private List<Book> MapSearchDocsToBooks(List<OlSearchDoc> docs)
        {
            if (docs == null || !docs.Any())
            {
                return new List<Book>();
            }

            return docs
                .Select(d => OpenLibraryMapper.MapSearchDocToBook(d))
                .Where(b => b != null)
                .ToList();
        }

        private Tuple<string, Book, List<AuthorMetadata>> ResolveBookInfoFromEditionIsbn(OlEditionResource edition)
        {
            if (edition == null)
            {
                return null;
            }

            var isbnCandidates = (edition.Isbn13 ?? new List<string>())
                .Concat(edition.Isbn10 ?? new List<string>())
                .Where(x => x.IsNotNullOrWhiteSpace())
                .Distinct()
                .ToList();

            foreach (var isbn in isbnCandidates)
            {
                var candidate = SearchByIsbn(isbn)
                    .FirstOrDefault(x => NormalizeWorkId(x.OpenLibraryWorkId).IsNotNullOrWhiteSpace() ||
                                         NormalizeWorkId(x.ForeignBookId).IsNotNullOrWhiteSpace());

                if (candidate?.AuthorMetadata?.Value == null)
                {
                    continue;
                }

                var authorId = candidate.AuthorMetadata.Value.ForeignAuthorId ?? "OL-unknown";
                var authorMetaList = new List<AuthorMetadata> { candidate.AuthorMetadata.Value };

                return Tuple.Create(authorId, candidate, authorMetaList);
            }

            return null;
        }

        private List<Book> SearchEditionByIsbn(string isbn, Edition mappedEdition)
        {
            var response = _client.Search(isbn, limit: 5);
            var books = MapSearchDocsToBooks(response?.Docs)
                .Where(x => NormalizeWorkId(x.OpenLibraryWorkId).IsNotNullOrWhiteSpace() ||
                            NormalizeWorkId(x.ForeignBookId).IsNotNullOrWhiteSpace())
                .ToList();

            if (!books.Any())
            {
                return books;
            }

            foreach (var book in books)
            {
                AddEditionIfMissing(book, mappedEdition, isbn);
            }

            return books;
        }

        private static void AddEditionIfMissing(Book book, Edition edition, string isbn)
        {
            if (book?.Editions?.Value == null || edition == null)
            {
                return;
            }

            if (book.Editions.Value.Any(x => string.Equals(x.Isbn13, isbn, StringComparison.OrdinalIgnoreCase)))
            {
                return;
            }

            var clone = new Edition
            {
                ForeignEditionId = edition.ForeignEditionId,
                TitleSlug = edition.TitleSlug,
                Title = edition.Title,
                Isbn13 = edition.Isbn13,
                Publisher = edition.Publisher,
                PageCount = edition.PageCount,
                ReleaseDate = edition.ReleaseDate,
                IsEbook = edition.IsEbook,
                Format = edition.Format,
                Ratings = edition.Ratings,
                Images = edition.Images,
                Book = book
            };

            book.Editions.Value.Insert(0, clone);
        }

        private static Book BuildBookFromEditionOnly(Edition edition, OlEditionResource raw)
        {
            if (edition == null)
            {
                return null;
            }

            var authorKey = raw.Authors?.FirstOrDefault()?.Key;
            var authorId = NormalizeAuthorId(authorKey?.TrimStart('/').Split('/').LastOrDefault());
            var foreignAuthorId = authorKey != null
                ? $"openlibrary:author:{authorId}"
                : "OL-unknown";

            var metadata = new AuthorMetadata
            {
                ForeignAuthorId = foreignAuthorId,
                OpenLibraryAuthorId = authorId,
                TitleSlug = foreignAuthorId,
                Name = "Unknown Author",
                Status = AuthorStatusType.Continuing,
                Ratings = new Ratings { Votes = 0, Value = 0 }
            };

            metadata.SortName = metadata.Name.ToLower();
            metadata.NameLastFirst = metadata.Name.ToLastFirst();
            metadata.SortNameLastFirst = metadata.NameLastFirst.ToLower();

            var workKey = raw.Works?.FirstOrDefault()?.Key;
            var workId = NormalizeWorkId(workKey?.Split('/').Last());
            var book = new Book
            {
                ForeignBookId = workId != null ? $"openlibrary:work:{workId}" : edition.ForeignEditionId,
                OpenLibraryWorkId = workId,
                TitleSlug = edition.TitleSlug,
                Title = edition.Title,
                CleanTitle = Parser.Parser.CleanAuthorName(edition.Title ?? string.Empty),
                ReleaseDate = edition.ReleaseDate,
                Ratings = new Ratings { Votes = 0, Value = 0 },
                AnyEditionOk = true
            };

            book.Editions = new List<Edition> { edition };
            book.AuthorMetadata = metadata;
            book.Author = new Author { Metadata = metadata };

            return book;
        }

        private List<Book> Search(string query, bool getAllEditions)
        {
            var response = _client.Search(query, limit: getAllEditions ? 20 : 10);
            return MapSearchDocsToBooks(response?.Docs);
        }

        private List<Book> SearchByOpenLibraryAuthorId(string authorId)
        {
            try
            {
                var author = GetAuthorInfo(authorId);
                return author.Books?.Value ?? new List<Book>();
            }
            catch (NzbDrone.Core.Exceptions.AuthorNotFoundException)
            {
                return new List<Book>();
            }
        }

        private List<Book> SearchByOpenLibraryWorkId(string workId, bool getAllEditions)
        {
            try
            {
                var tuple = GetBookInfo(workId);
                var book = tuple.Item2;

                if (!getAllEditions)
                {
                    TrimEditions(book, null);
                }

                return new List<Book> { book };
            }
            catch (NzbDrone.Core.Exceptions.BookNotFoundException)
            {
                return new List<Book>();
            }
        }

        private List<Book> SearchByOpenLibraryEditionId(string editionId, bool getAllEditions)
        {
            try
            {
                var tuple = GetBookInfo(editionId);
                var book = tuple.Item2;

                if (!book.Editions.Value.Any(e => string.Equals(e.ForeignEditionId, editionId, StringComparison.OrdinalIgnoreCase)))
                {
                    return new List<Book>();
                }

                if (!getAllEditions)
                {
                    TrimEditions(book, editionId);
                }

                return new List<Book> { book };
            }
            catch (NzbDrone.Core.Exceptions.BookNotFoundException)
            {
                return new List<Book>();
            }
        }

        private static void TrimEditions(Book book, string preferredEditionId)
        {
            if (book?.Editions?.Value == null || !book.Editions.Value.Any())
            {
                return;
            }

            var selected = preferredEditionId.IsNotNullOrWhiteSpace()
                ? book.Editions.Value.FirstOrDefault(e => string.Equals(e.ForeignEditionId, preferredEditionId, StringComparison.OrdinalIgnoreCase))
                : null;

            selected ??= book.Editions.Value.First();
            book.Editions = new List<Edition> { selected };
        }

        private static string NormalizePrefixedOpenLibraryId(string prefix, string slug)
        {
            if (prefix == "author")
            {
                return OpenLibraryIdNormalizer.NormalizeAuthorId(slug) ??
                       (int.TryParse(slug, out _) ? OpenLibraryIdNormalizer.EnsureToken(slug, "A") : null);
            }

            var token = OpenLibraryIdNormalizer.NormalizeBookToken(slug);

            if (token.IsNullOrWhiteSpace())
            {
                if (!int.TryParse(slug, out _))
                {
                    return null;
                }

                return prefix == "work"
                    ? OpenLibraryIdNormalizer.EnsureToken(slug, "W")
                    : OpenLibraryIdNormalizer.EnsureToken(slug, "M");
            }

            if (prefix == "work")
            {
                return token.EndsWith("W", StringComparison.OrdinalIgnoreCase)
                    ? OpenLibraryIdNormalizer.EnsureToken(token, "W")
                    : null;
            }

            return token.EndsWith("M", StringComparison.OrdinalIgnoreCase)
                ? OpenLibraryIdNormalizer.EnsureToken(token, "M")
                : null;
        }

        private static string NormalizeWorkId(string foreignBookId)
        {
            return OpenLibraryIdNormalizer.NormalizeBookToken(foreignBookId);
        }
    }
}
