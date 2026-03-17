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
            var lookupsUsed = 0;

            var books = _bookService.GetAllBooks();
            var bookIds = books.Select(x => x.Id).ToList();
            var editionsByBookId = _editionService.GetEditionsByBook(bookIds)
                .GroupBy(x => x.BookId)
                .ToDictionary(x => x.Key, y => y.ToList());

            var authors = _authorService.GetAllAuthors();
            var authorsByMetadataId = authors.ToDictionary(x => x.AuthorMetadataId);

            var changedBooks = new List<Book>();
            var changedAuthors = new List<AuthorMetadata>();

            foreach (var book in books)
            {
                var changed = false;

                if (book.OpenLibraryWorkId.IsNullOrWhiteSpace())
                {
                    if (LooksLikeOpenLibraryWorkId(book.ForeignBookId))
                    {
                        book.OpenLibraryWorkId = book.ForeignBookId;
                        changed = true;
                    }
                    else if (lookupsUsed < lookupBudget)
                    {
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
                                    var resolvedWorkId = resolved.OpenLibraryWorkId;

                                    if (resolvedWorkId.IsNullOrWhiteSpace() && LooksLikeOpenLibraryWorkId(resolved.ForeignBookId))
                                    {
                                        resolvedWorkId = resolved.ForeignBookId;
                                    }

                                    if (resolvedWorkId.IsNotNullOrWhiteSpace())
                                    {
                                        book.OpenLibraryWorkId = resolvedWorkId;
                                        changed = true;
                                    }
                                }
                            }
                        }
                    }
                }

                if (changed)
                {
                    changedBooks.Add(book);
                }

                if (!authorsByMetadataId.TryGetValue(book.AuthorMetadataId, out var author))
                {
                    continue;
                }

                var metadata = author.Metadata.Value;
                if (metadata == null || metadata.OpenLibraryAuthorId.IsNotNullOrWhiteSpace())
                {
                    continue;
                }

                var authorChanged = false;
                if (LooksLikeOpenLibraryAuthorId(metadata.ForeignAuthorId))
                {
                    metadata.OpenLibraryAuthorId = metadata.ForeignAuthorId;
                    authorChanged = true;
                }
                else if (lookupsUsed < lookupBudget)
                {
                    lookupsUsed++;
                    var candidates = _metadataProviderOrchestrator.SearchForNewAuthor(metadata.Name ?? author.Name);
                    var openLibraryAuthor = candidates.FirstOrDefault(x => LooksLikeOpenLibraryAuthorId(x.ForeignAuthorId));

                    if (openLibraryAuthor != null)
                    {
                        metadata.OpenLibraryAuthorId = openLibraryAuthor.ForeignAuthorId;
                        authorChanged = true;
                    }
                }

                if (authorChanged)
                {
                    changedAuthors.Add(metadata);
                }
            }

            if (changedBooks.Any())
            {
                _bookService.UpdateMany(changedBooks);
            }

            if (changedAuthors.Any())
            {
                _authorMetadataService.UpsertMany(changedAuthors.DistinctBy(x => x.Id).ToList());
            }

            _logger.Info("OpenLibrary ID backfill complete. Updated books={0}, authors={1}, lookups={2}/{3}", changedBooks.Count, changedAuthors.Count, lookupsUsed, lookupBudget);
        }

        private static bool LooksLikeOpenLibraryWorkId(string value)
        {
            return value.IsNotNullOrWhiteSpace() && value.StartsWith("OL", StringComparison.OrdinalIgnoreCase) && value.EndsWith("W", StringComparison.OrdinalIgnoreCase);
        }

        private static bool LooksLikeOpenLibraryAuthorId(string value)
        {
            return value.IsNotNullOrWhiteSpace() && value.StartsWith("OL", StringComparison.OrdinalIgnoreCase) && value.EndsWith("A", StringComparison.OrdinalIgnoreCase);
        }
    }
}
