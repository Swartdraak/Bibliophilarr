using System;
using System.Collections.Generic;
using System.Linq;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Books;
using NzbDrone.Core.Books.Commands;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Lifecycle;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.MetadataSource.OpenLibrary
{
    public class OpenLibraryIdBackfillService :
        IHandle<ApplicationStartedEvent>,
        IExecute<BackfillOpenLibraryIdsCommand>
    {
        private const string ProviderName = "OpenLibrary";
        private const string IsbnEvidence = "exact-isbn-and-title-and-author";

        private readonly IConfigService _configService;
        private readonly IManageCommandQueue _commandQueueManager;
        private readonly IBookService _bookService;
        private readonly IEditionService _editionService;
        private readonly IAuthorService _authorService;
        private readonly IAuthorMetadataService _authorMetadataService;
        private readonly IMetadataProviderRegistry _metadataProviderRegistry;
        private readonly ICatalogueIdentityRepository _identityRepository;
        private readonly Logger _logger;

        public OpenLibraryIdBackfillService(
            IConfigService configService,
            IManageCommandQueue commandQueueManager,
            IBookService bookService,
            IEditionService editionService,
            IAuthorService authorService,
            IAuthorMetadataService authorMetadataService,
            IMetadataProviderRegistry metadataProviderRegistry,
            ICatalogueIdentityRepository identityRepository,
            Logger logger)
        {
            _configService = configService;
            _commandQueueManager = commandQueueManager;
            _bookService = bookService;
            _editionService = editionService;
            _authorService = authorService;
            _authorMetadataService = authorMetadataService;
            _metadataProviderRegistry = metadataProviderRegistry;
            _identityRepository = identityRepository;
            _logger = logger;
        }

        public void Handle(ApplicationStartedEvent message)
        {
            if (!_configService.EnableOpenLibraryProvider)
            {
                return;
            }

            _commandQueueManager.Push(new BackfillOpenLibraryIdsCommand());
        }

        public void Execute(BackfillOpenLibraryIdsCommand message)
        {
            var lookupBudget = Math.Max(0, message.MaxLookups);
            var batchSize = Math.Max(1, message.BatchSize);
            var books = _bookService.GetAllBooks().OrderBy(book => book.Id).ToList();
            var authorIds = _authorService.GetAllAuthors()
                .Select(author => author.AuthorMetadataId)
                .Concat(books.Select(book => book.AuthorMetadataId))
                .Distinct();
            var authors = _authorMetadataService.Get(authorIds).ToDictionary(author => author.Id);

            var bookChanges = new List<Book>();
            foreach (var book in books)
            {
                var workId = KnownWorkId(book);
                var evidence = OfficialId(CatalogueEntityKind.Work, book.ForeignBookId) == workId
                    ? CatalogueIdentityRepository.PrimaryProviderEvidence
                    : CatalogueIdentityRepository.LegacyLinkedEvidence;
                if (workId != null && book.OpenLibraryWorkId != workId &&
                    _identityRepository.TryAddSource(CatalogueEntityKind.Work, book.Id, ProviderName, workId, evidence))
                {
                    book.OpenLibraryWorkId = workId;
                    bookChanges.Add(book);
                }
            }

            var authorChanges = new List<AuthorMetadata>();
            foreach (var author in authors.Values)
            {
                var authorId = KnownAuthorId(author);
                var evidence = OfficialId(CatalogueEntityKind.Author, author.ForeignAuthorId) == authorId
                    ? CatalogueIdentityRepository.PrimaryProviderEvidence
                    : CatalogueIdentityRepository.LegacyLinkedEvidence;
                if (authorId != null && author.OpenLibraryAuthorId != authorId &&
                    _identityRepository.TryAddSource(CatalogueEntityKind.Author, author.Id, ProviderName, authorId, evidence))
                {
                    author.OpenLibraryAuthorId = authorId;
                    authorChanges.Add(author);
                }
            }

            var provider = lookupBudget > 0
                ? _metadataProviderRegistry.GetProviders()
                    .FirstOrDefault(candidate => candidate is ISearchForNewBook && candidate.SupportsIsbnLookup &&
                                                string.Equals(candidate.ProviderName, ProviderName, StringComparison.OrdinalIgnoreCase)) as ISearchForNewBook
                : null;
            if (provider != null)
            {
                var matches = FindCorroboratedMatches(books, authors, provider, batchSize, lookupBudget);
                ApplyCorroboratedMatches(matches, bookChanges, authorChanges);
            }

            foreach (var chunk in bookChanges.Chunk(batchSize))
            {
                _bookService.UpdateMany(chunk.ToList());
            }

            foreach (var chunk in authorChanges.Chunk(batchSize))
            {
                _authorMetadataService.UpsertMany(chunk.ToList());
            }

            _logger.Info("Open Library ID backfill complete. Updated books={0}, authors={1}", bookChanges.Count, authorChanges.Count);
        }

        private List<(Book Book, AuthorMetadata Author, string WorkId, string AuthorId)> FindCorroboratedMatches(
            List<Book> books,
            IReadOnlyDictionary<int, AuthorMetadata> authors,
            ISearchForNewBook provider,
            int batchSize,
            int lookupBudget)
        {
            var unresolved = books.Where(book =>
                authors.TryGetValue(book.AuthorMetadataId, out var author) &&
                (KnownWorkId(book) == null || KnownAuthorId(author) == null));
            var resultsByIsbn = new Dictionary<string, List<Book>>();
            var matches = new List<(Book Book, AuthorMetadata Author, string WorkId, string AuthorId)>();

            foreach (var chunk in unresolved.Chunk(batchSize))
            {
                var editionsByBook = _editionService.GetEditionsByBook(chunk.Select(book => book.Id).ToList())
                    .ToLookup(edition => edition.BookId);

                foreach (var book in chunk)
                {
                    var isbn = editionsByBook[book.Id]
                        .OrderByDescending(edition => edition.Monitored)
                        .ThenBy(edition => edition.Id)
                        .Select(edition => Isbn.AsIsbn13(edition.Isbn13))
                        .FirstOrDefault(candidate => candidate != null);

                    if (isbn == null)
                    {
                        continue;
                    }

                    if (!resultsByIsbn.TryGetValue(isbn, out var results))
                    {
                        if (resultsByIsbn.Count >= lookupBudget)
                        {
                            continue;
                        }

                        try
                        {
                            results = provider.SearchByIsbn(isbn);
                        }
                        catch (Exception ex)
                        {
                            _logger.Debug(ex, "Open Library ISBN lookup did not complete during identifier backfill");
                            if (MetadataProviderOrchestrator.IsTransportError(ex))
                            {
                                return matches;
                            }

                            results = null;
                        }

                        resultsByIsbn.Add(isbn, results);
                    }

                    var author = authors[book.AuthorMetadataId];
                    var (workId, authorId) = GetCorroboratedIdentity(book, author, isbn, results);
                    if (workId != null)
                    {
                        matches.Add((book, author, workId, authorId));
                    }
                }
            }

            return matches;
        }

        private void ApplyCorroboratedMatches(
            List<(Book Book, AuthorMetadata Author, string WorkId, string AuthorId)> matches,
            List<Book> bookChanges,
            List<AuthorMetadata> authorChanges)
        {
            var uniqueAuthors = matches.GroupBy(match => match.Author.Id)
                .Where(group => group.Select(match => match.AuthorId).Distinct().Count() == 1)
                .Select(group => (Id: group.Key, ProviderId: group.First().AuthorId))
                .GroupBy(author => author.ProviderId)
                .Where(group => group.Count() == 1)
                .Select(group => group.Single())
                .Where(author =>
                {
                    var existingOwner = _identityRepository.FindByProviderId(CatalogueEntityKind.Author, ProviderName, author.ProviderId);
                    return !existingOwner.HasValue || existingOwner == author.Id;
                })
                .Select(author => author.Id)
                .ToHashSet();

            foreach (var match in matches.GroupBy(match => match.WorkId).Where(group => group.Count() == 1).Select(group => group.Single()))
            {
                if (!uniqueAuthors.Contains(match.Author.Id) ||
                    !_identityRepository.TryAddSource(CatalogueEntityKind.Work, match.Book.Id, ProviderName, match.WorkId, IsbnEvidence))
                {
                    continue;
                }

                if (match.Book.OpenLibraryWorkId != match.WorkId)
                {
                    match.Book.OpenLibraryWorkId = match.WorkId;
                    bookChanges.Add(match.Book);
                }

                if (match.Author.OpenLibraryAuthorId != match.AuthorId &&
                    _identityRepository.TryAddSource(CatalogueEntityKind.Author, match.Author.Id, ProviderName, match.AuthorId, IsbnEvidence))
                {
                    match.Author.OpenLibraryAuthorId = match.AuthorId;
                    authorChanges.Add(match.Author);
                }
            }
        }

        private static (string WorkId, string AuthorId) GetCorroboratedIdentity(
            Book book,
            AuthorMetadata author,
            string isbn,
            IEnumerable<Book> results)
        {
            var knownWorkId = KnownWorkId(book);
            var knownAuthorId = KnownAuthorId(author);
            var candidates = MetadataProviderOrchestrator.GetExactIsbnMatches(results, isbn)
                .Select(match => match.Book)
                .Select(result =>
                {
                    var resultAuthor = result.AuthorMetadata?.Value;
                    var workId = UnambiguousId(CatalogueEntityKind.Work, result.OpenLibraryWorkId, result.ForeignBookId);
                    var authorId = UnambiguousId(CatalogueEntityKind.Author, resultAuthor?.OpenLibraryAuthorId, resultAuthor?.ForeignAuthorId);
                    return (WorkId: workId, AuthorId: authorId, Title: result.Title, Name: resultAuthor?.Name);
                })
                .Where(result => result.WorkId != null && result.AuthorId != null &&
                                 (knownWorkId != null ? knownWorkId == result.WorkId : SameName(book.Title, result.Title)) &&
                                 (knownAuthorId != null ? knownAuthorId == result.AuthorId : SameName(author.Name, result.Name)))
                .Select(result => (result.WorkId, result.AuthorId))
                .Distinct()
                .Take(2)
                .ToList();

            return candidates.Count == 1 ? candidates[0] : default;
        }

        private static string KnownWorkId(Book book)
        {
            return OfficialId(CatalogueEntityKind.Work, book.OpenLibraryWorkId) ?? OfficialId(CatalogueEntityKind.Work, book.ForeignBookId);
        }

        private static string KnownAuthorId(AuthorMetadata author)
        {
            return OfficialId(CatalogueEntityKind.Author, author.OpenLibraryAuthorId) ?? OfficialId(CatalogueEntityKind.Author, author.ForeignAuthorId);
        }

        private static string UnambiguousId(CatalogueEntityKind kind, string alternateId, string nativeId)
        {
            var alternate = OfficialId(kind, alternateId);
            var native = OfficialId(kind, nativeId);
            return alternate != null && native != null && alternate != native ? null : alternate ?? native;
        }

        private static string OfficialId(CatalogueEntityKind kind, string value)
        {
            return CatalogueIdentityRepository.NormalizeSource(kind, ProviderName, value, null)?.ProviderId;
        }

        private static bool SameName(string first, string second)
        {
            return first.IsNotNullOrWhiteSpace() && string.Equals(first.Trim(), second?.Trim(), StringComparison.OrdinalIgnoreCase);
        }
    }
}
