using System;
using System.Collections.Generic;
using System.Linq;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Books;
using NzbDrone.Core.Books.Commands;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Exceptions;
using NzbDrone.Core.Lifecycle;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.MetadataSource.OpenLibrary
{
    public class OpenLibraryIdBackfillService :
        IHandle<ApplicationStartedEvent>,
        IExecute<BackfillOpenLibraryIdsCommand>
    {
        private readonly IConfigService _configService;
        private readonly IManageCommandQueue _commandQueueManager;
        private readonly IBookService _bookService;
        private readonly IEditionService _editionService;
        private readonly IAuthorService _authorService;
        private readonly IAuthorMetadataService _authorMetadataService;
        private readonly IMetadataProviderOrchestrator _metadataProviderOrchestrator;
        private readonly Logger _logger;

        public OpenLibraryIdBackfillService(
            IConfigService configService,
            IManageCommandQueue commandQueueManager,
            IBookService bookService,
            IEditionService editionService,
            IAuthorService authorService,
            IAuthorMetadataService authorMetadataService,
            IMetadataProviderOrchestrator metadataProviderOrchestrator,
            Logger logger)
        {
            _configService = configService;
            _commandQueueManager = commandQueueManager;
            _bookService = bookService;
            _editionService = editionService;
            _authorService = authorService;
            _authorMetadataService = authorMetadataService;
            _metadataProviderOrchestrator = metadataProviderOrchestrator;
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
            var lookupsUsed = 0;

            var books = _bookService.GetAllBooks();
            var bookIds = books.Select(x => x.Id).ToList();
            var editionsByBookId = _editionService.GetEditionsByBook(bookIds)
                .GroupBy(x => x.BookId)
                .ToDictionary(x => x.Key, y => y.ToList());

            var authors = _authorService.GetAllAuthors();
            var authorsByMetadataId = authors.ToDictionary(x => x.AuthorMetadataId);
            var authorMetadataById = _authorMetadataService.Get(authorsByMetadataId.Keys).ToDictionary(x => x.Id);

            var changedBooks = new List<Book>();
            var changedAuthors = new List<AuthorMetadata>();

            foreach (var book in books)
            {
                var changed = false;
                var normalizedWorkId = OpenLibraryIdNormalizer.NormalizeWorkId(book.OpenLibraryWorkId) ?? OpenLibraryIdNormalizer.NormalizeWorkId(book.ForeignBookId);
                var foreignBookToken = OpenLibraryIdNormalizer.NormalizeBookToken(book.ForeignBookId);

                if (normalizedWorkId.IsNotNullOrWhiteSpace())
                {
                    if (book.OpenLibraryWorkId != normalizedWorkId)
                    {
                        book.OpenLibraryWorkId = normalizedWorkId;
                        changed = true;
                    }
                }
                else if (lookupsUsed < lookupBudget)
                {
                    if (foreignBookToken.IsNotNullOrWhiteSpace() && foreignBookToken.EndsWith("M", StringComparison.OrdinalIgnoreCase))
                    {
                        lookupsUsed++;
                        var resolvedByExternalId = _metadataProviderOrchestrator
                            .SearchByExternalId("olid", foreignBookToken)
                            .FirstOrDefault();

                        var resolvedWorkId = OpenLibraryIdNormalizer.NormalizeWorkId(resolvedByExternalId?.OpenLibraryWorkId) ??
                                             OpenLibraryIdNormalizer.NormalizeWorkId(resolvedByExternalId?.ForeignBookId);

                        if (resolvedWorkId.IsNullOrWhiteSpace())
                        {
                            try
                            {
                                var resolvedByProvider = _metadataProviderOrchestrator.GetBookInfo(foreignBookToken);
                                resolvedWorkId = OpenLibraryIdNormalizer.NormalizeWorkId(resolvedByProvider?.Item2?.OpenLibraryWorkId) ??
                                                 OpenLibraryIdNormalizer.NormalizeWorkId(resolvedByProvider?.Item2?.ForeignBookId);
                            }
                            catch (BookNotFoundException)
                            {
                            }
                        }

                        if (resolvedWorkId.IsNotNullOrWhiteSpace())
                        {
                            book.OpenLibraryWorkId = resolvedWorkId;
                            changed = true;
                        }
                    }

                    if (changed)
                    {
                        changedBooks.Add(book);
                        continue;
                    }

                    if (editionsByBookId.TryGetValue(book.Id, out var editions))
                    {
                        var isbn = editions
                            .Select(x => x.Isbn13)
                            .FirstOrDefault(x => x.IsNotNullOrWhiteSpace());

                        if (isbn.IsNotNullOrWhiteSpace())
                        {
                            lookupsUsed++;
                            var resolved = _metadataProviderOrchestrator.SearchByIsbn(isbn).FirstOrDefault();

                            if (resolved != null)
                            {
                                var resolvedWorkId = OpenLibraryIdNormalizer.NormalizeWorkId(resolved.OpenLibraryWorkId) ?? OpenLibraryIdNormalizer.NormalizeWorkId(resolved.ForeignBookId);

                                if (resolvedWorkId.IsNotNullOrWhiteSpace())
                                {
                                    book.OpenLibraryWorkId = resolvedWorkId;
                                    changed = true;
                                }
                            }
                        }
                    }
                }

                if (changed)
                {
                    changedBooks.Add(book);
                }
            }

            foreach (var author in authors)
            {
                if (!authorMetadataById.TryGetValue(author.AuthorMetadataId, out var metadata))
                {
                    continue;
                }

                if (metadata == null || metadata.OpenLibraryAuthorId.IsNotNullOrWhiteSpace())
                {
                    continue;
                }

                var authorChanged = false;
                var normalizedAuthorId = OpenLibraryIdNormalizer.NormalizeAuthorId(metadata.ForeignAuthorId);

                if (normalizedAuthorId.IsNotNullOrWhiteSpace())
                {
                    metadata.OpenLibraryAuthorId = normalizedAuthorId;
                    authorChanged = true;
                }
                else if (lookupsUsed < lookupBudget)
                {
                    lookupsUsed++;
                    var candidates = _metadataProviderOrchestrator.SearchForNewAuthor(metadata.Name ?? author.Name);
                    var openLibraryAuthor = candidates.FirstOrDefault(x =>
                        OpenLibraryIdNormalizer.NormalizeAuthorId(x.Metadata?.Value?.OpenLibraryAuthorId ?? x.ForeignAuthorId).IsNotNullOrWhiteSpace());

                    if (openLibraryAuthor != null)
                    {
                        var resolvedAuthorId = OpenLibraryIdNormalizer.NormalizeAuthorId(openLibraryAuthor.Metadata?.Value?.OpenLibraryAuthorId ?? openLibraryAuthor.ForeignAuthorId);

                        if (resolvedAuthorId.IsNotNullOrWhiteSpace())
                        {
                            metadata.OpenLibraryAuthorId = resolvedAuthorId;
                            authorChanged = true;
                        }
                    }
                }

                if (authorChanged)
                {
                    changedAuthors.Add(metadata);
                }
            }

            if (changedBooks.Any())
            {
                foreach (var chunk in changedBooks.Chunk(batchSize))
                {
                    _bookService.UpdateMany(chunk.ToList());
                }
            }

            if (changedAuthors.Any())
            {
                var distinctAuthors = changedAuthors.DistinctBy(x => x.Id).ToList();
                foreach (var chunk in distinctAuthors.Chunk(batchSize))
                {
                    _authorMetadataService.UpsertMany(chunk.ToList());
                }
            }

            _logger.Info("OpenLibrary ID backfill complete. Updated books={0}, authors={1}, lookups={2}/{3}", changedBooks.Count, changedAuthors.Count, lookupsUsed, lookupBudget);
        }
    }
}
