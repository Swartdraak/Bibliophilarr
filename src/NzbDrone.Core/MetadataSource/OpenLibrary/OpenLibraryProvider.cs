using System;
using System.Collections.Generic;
using System.Linq;
using NLog;
using NzbDrone.Core.Books;
using NzbDrone.Core.MetadataSource.OpenLibrary.Resources;
using NzbDrone.Common.Extensions;

namespace NzbDrone.Core.MetadataSource.OpenLibrary
{
    /// <summary>
    /// Secondary metadata provider backed by the Open Library REST API.
    /// Priority 2 — used by MetadataProviderRegistry when the primary (BookInfo) provider fails.
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
        public int Priority => 2;
        public bool IsEnabled => true;
        public bool SupportsAuthorSearch => true;
        public bool SupportsBookSearch => true;
        public bool SupportsIsbnLookup => true;
        public bool SupportsSeriesInfo => false;
        public bool SupportsCoverImages => true;

        private readonly IOpenLibraryClient _client;
        private readonly Logger _logger;

        public OpenLibraryProvider(IOpenLibraryClient client, Logger logger)
        {
            _client = client;
            _logger = logger;
        }

        // ── IProvideAuthorInfo ───────────────────────────────────────────────

        public Author GetAuthorInfo(string foreignAuthorId, bool useCache = true)
        {
            _logger.Debug("OpenLibraryProvider.GetAuthorInfo: {0}", foreignAuthorId);

            // foreignAuthorId is expected in form "OL{n}A"
            var authorResource = _client.GetAuthor(foreignAuthorId);
            if (authorResource == null)
            {
                throw new NzbDrone.Core.Exceptions.AuthorNotFoundException(foreignAuthorId);
            }

            var metadata = OpenLibraryMapper.MapAuthorToMetadata(authorResource);
            if (metadata == null)
            {
                throw new NzbDrone.Core.Exceptions.AuthorNotFoundException(foreignAuthorId);
            }

            // Fetch a sample of works via search to populate Books
            var searchResponse = _client.Search($"author_key:/authors/{foreignAuthorId}");
            var books = (searchResponse?.Docs ?? new List<OlSearchDoc>())
                .Select(d => OpenLibraryMapper.MapSearchDocToBook(d))
                .Where(b => b != null)
                .ToList();

            foreach (var book in books)
            {
                book.AuthorMetadata = metadata;
                if (book.Author?.Value != null)
                {
                    book.Author.Value.Metadata = metadata;
                }
            }

            return new Author
            {
                Metadata = metadata,
                CleanName = Parser.Parser.CleanAuthorName(metadata.Name),
                Books = books
            };
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

            // foreignBookId is expected in form "OL{n}W"
            var work = _client.GetWork(foreignBookId);
            if (work == null)
            {
                throw new NzbDrone.Core.Exceptions.BookNotFoundException(foreignBookId);
            }

            // Fetch primary author
            var authorKey = work.Authors?
                .Where(a => a.Author?.Key != null)
                .Select(a => a.Author.Key)
                .FirstOrDefault();

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

            var authorId = book.AuthorMetadata?.Value?.ForeignAuthorId ?? "OL-unknown";
            var authorMetaList = book.AuthorMetadata?.Value != null
                ? new List<AuthorMetadata> { book.AuthorMetadata.Value }
                : new List<AuthorMetadata>();

            return Tuple.Create(authorId, book, authorMetaList);
        }

        // ── ISearchForNewBook ────────────────────────────────────────────────

        public List<Book> SearchForNewBook(string title, string author, bool getAllEditions = true)
        {
            _logger.Debug("OpenLibraryProvider.SearchForNewBook: title={0} author={1}", title, author);

            var query = author.IsNullOrWhiteSpace() ? title : $"{title} {author}";

            try
            {
                var response = _client.Search(query, limit: 20);
                return MapSearchDocsToBooks(response?.Docs);
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
            // Open Library does not index ASIN
            _logger.Debug("OpenLibraryProvider.SearchByAsin: not supported by Open Library, skipping");
            return new List<Book>();
        }

        public List<Book> SearchByExternalId(string idType, string id)
        {
            _logger.Debug("OpenLibraryProvider.SearchByExternalId: type={0} id={1}", idType, id);

            if (idType == "isbn")
            {
                return SearchByIsbn(id);
            }

            if (idType == "olid")
            {
                try
                {
                    var tuple = GetBookInfo(id);
                    return new List<Book> { tuple.Item2 };
                }
                catch (NzbDrone.Core.Exceptions.BookNotFoundException)
                {
                    return new List<Book>();
                }
            }

            if (idType == "asin")
            {
                return SearchByAsin(id);
            }

            // "goodreads" IDs are not meaningful to Open Library
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

        private static Book BuildBookFromEditionOnly(Edition edition, OlEditionResource raw)
        {
            if (edition == null)
            {
                return null;
            }

            var authorKey = raw.Authors?.FirstOrDefault()?.Key;
            var foreignAuthorId = authorKey != null
                ? $"OL{authorKey.TrimStart('/').Split('/').LastOrDefault()}A"
                : "OL-unknown";

            var metadata = new AuthorMetadata
            {
                ForeignAuthorId = foreignAuthorId,
                TitleSlug = foreignAuthorId,
                Name = "Unknown Author",
                Status = AuthorStatusType.Continuing,
                Ratings = new Ratings { Votes = 0, Value = 0 }
            };

            metadata.SortName = metadata.Name.ToLower();
            metadata.NameLastFirst = metadata.Name.ToLastFirst();
            metadata.SortNameLastFirst = metadata.NameLastFirst.ToLower();

            var workKey = raw.Works?.FirstOrDefault()?.Key;
            var book = new Book
            {
                ForeignBookId = workKey != null ? $"OL{workKey.Split('/').Last()}W" : edition.ForeignEditionId,
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
    }
}
