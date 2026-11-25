using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using FluentValidation;
using FluentValidation.Results;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Instrumentation;
using NzbDrone.Core.Exceptions;
using NzbDrone.Core.ImportLists.Exclusions;
using NzbDrone.Core.MediaFiles.BookImport;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.MetadataSource.Hardcover;
using NzbDrone.Core.MetadataSource.OpenLibrary;

namespace NzbDrone.Core.Books
{
    public interface IAddBookService
    {
        Book AddBook(Book book, bool doRefresh = true);
        Book AddBookByExactEdition(Book book, int claimedAuthorId);
        List<Book> AddBooks(List<Book> books, bool doRefresh = true);
    }

    public class AddBookService : IAddBookService
    {
        private readonly IAuthorService _authorService;
        private readonly IAddAuthorService _addAuthorService;
        private readonly IBookService _bookService;
        private readonly IMetadataProviderOrchestrator _orchestrator;
        private readonly IImportListExclusionService _importListExclusionService;
        private readonly Logger _logger;

        public AddBookService(IAuthorService authorService,
                               IAddAuthorService addAuthorService,
                               IBookService bookService,
                               IMetadataProviderOrchestrator orchestrator,
                               IImportListExclusionService importListExclusionService,
                               Logger logger)
        {
            _authorService = authorService;
            _addAuthorService = addAuthorService;
            _bookService = bookService;
            _orchestrator = orchestrator;
            _importListExclusionService = importListExclusionService;
            _logger = logger;
        }

        public Book AddBook(Book book, bool doRefresh = true)
        {
            return AddBook(book, doRefresh, requireSelectedEdition: true);
        }

        public Book AddBookByExactEdition(Book book, int claimedAuthorId)
        {
            var lookupId = OpenLibraryIdNormalizer.NormalizeWorkId(book.ForeignBookId) ?? book.ForeignBookId;
            var provider = GetBookMetadata(book.ForeignBookId, lookupId);
            var providerBook = provider?.Item2;
            if (providerBook == null || providerBook.ForeignBookId != book.ForeignBookId ||
                providerBook.TitleSlug != providerBook.ForeignBookId.ToUrlSlug())
            {
                throw new ValidationException(new[]
                {
                    new ValidationFailure("ForeignBookId", "The book provider did not return the selected work")
                });
            }

            var editions = providerBook.Editions?.Value
                ?.Where(edition => edition?.ForeignEditionId == book.ForeignEditionId)
                .Take(2)
                .ToList();
            if (editions?.Count != 1 || editions[0] is not { Id: 0, BookId: 0 } selectedEdition ||
                !IsSourceEdition(book.ForeignBookId, selectedEdition.ForeignEditionId) ||
                ImportFormat.ForEdition(selectedEdition, requireExplicitFormat: true) is not { } format ||
                selectedEdition.IsEbook != (format == FormatType.Ebook))
            {
                throw new ValidationException(new[]
                {
                    new ValidationFailure("ForeignEditionId", "The selected edition could not be verified for this work")
                });
            }

            var authorSourceId = provider.Item1;
            var authorClaim = book.Author?.Value;
            var providerOwners = provider.Item3?.Count(author => author?.ForeignAuthorId == authorSourceId);
            var owner = !string.IsNullOrWhiteSpace(authorSourceId) &&
                        providerOwners == 1 && providerBook.AuthorMetadata?.Value?.ForeignAuthorId == authorSourceId &&
                        authorClaim?.Metadata?.Value?.ForeignAuthorId == authorSourceId
                ? _authorService.FindById(authorSourceId)
                : null;
            if (owner is not { Id: > 0, AuthorMetadataId: > 0 } || owner.Metadata?.Value?.ForeignAuthorId != authorSourceId ||
                (authorClaim.Id != 0 && authorClaim.Id != owner.Id) ||
                (claimedAuthorId != 0 && claimedAuthorId != owner.Id))
            {
                throw new ValidationException(new[]
                {
                    new ValidationFailure("Author.ForeignAuthorId", "The selected work must belong to the specified existing author")
                });
            }

            if (book.Id != 0 || _bookService.FindById(book.ForeignBookId) != null)
            {
                throw new NzbDroneClientException(HttpStatusCode.Conflict, "The selected work already exists. Open its editions to change your selection.");
            }

            book.UseMetadataFrom(providerBook);
            book.ForeignEditionId = selectedEdition.ForeignEditionId;
            book.Added = DateTime.UtcNow;
            book.Editions = new List<Edition> { selectedEdition };
            book.AuthorMetadata = owner.Metadata.Value;
            book.AddOptions.AddType = BookAddType.Manual;
            selectedEdition.Monitored = true;
            selectedEdition.ManualAdd = true;

            return SaveBook(book, owner, doRefresh: true);
        }

        private static bool IsSourceEdition(string workId, string editionId)
        {
            if (workId.StartsWith("hardcover:work:", StringComparison.Ordinal))
            {
                const string prefix = HardcoverFallbackSearchProvider.ProviderEditionIdPrefix;
                return editionId.StartsWith(prefix, StringComparison.Ordinal) &&
                       int.TryParse(editionId.AsSpan(prefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out var id) && id > 0;
            }

            return workId.StartsWith("openlibrary:work:", StringComparison.Ordinal) &&
                   IsOpenLibraryToken(OpenLibraryIdNormalizer.NormalizeWorkId(workId), 'W') &&
                   OpenLibraryIdNormalizer.NormalizeEditionId(editionId) != null;
        }

        private Book AddBook(Book book, bool doRefresh, bool requireSelectedEdition)
        {
            _logger.Debug("Adding book {0} - {1}", LogSanitizer.Sanitize(book?.ForeignBookId), LogSanitizer.Sanitize(book?.Title));

            book = AddSkyhookData(book, requireSelectedEdition, out var confirmedNewAuthorSelection);

            // we allow adding extra editions, so check if the book already exists
            var dbBook = _bookService.FindById(book.ForeignBookId);
            if (dbBook != null)
            {
                book.UseDbFieldsFrom(dbBook);
            }

            // Note it's a manual addition so it's not deleted on next refresh
            book.AddOptions.AddType = BookAddType.Manual;
            var selectedEdition = book.GetPreferredEdition();

            if (selectedEdition == null)
            {
                throw new ValidationException(new List<ValidationFailure>
                                              {
                                                  new ValidationFailure("Editions", "Book must include at least one edition")
                                              });
            }

            selectedEdition.ManualAdd = true;

            // Add the author if necessary
            var dbAuthor = _authorService.FindById(book.AuthorMetadata.Value.ForeignAuthorId);
            if (dbAuthor == null)
            {
                var author = book.Author?.Value;
                if (string.IsNullOrWhiteSpace(author?.Path) && string.IsNullOrWhiteSpace(author?.RootFolderPath))
                {
                    throw new ValidationException(new List<ValidationFailure>
                    {
                        new ("Author.RootFolderPath", "A root folder or author path is required to add a new author")
                    });
                }

                var profileErrors = new List<ValidationFailure>();
                if (author.QualityProfileId <= 0)
                {
                    profileErrors.Add(new ValidationFailure("Author.QualityProfileId", "A quality profile is required to add a new author"));
                }

                if (author.MetadataProfileId <= 0)
                {
                    profileErrors.Add(new ValidationFailure("Author.MetadataProfileId", "A metadata profile is required to add a new author"));
                }

                if (profileErrors.Count > 0)
                {
                    throw new ValidationException(profileErrors);
                }

                var metadata = book.AuthorMetadata.Value;
                if (confirmedNewAuthorSelection && book.Id == 0 && author.Id == 0)
                {
                    return _addAuthorService.AddNewAuthorWithBook(author, book, doRefresh);
                }

                // Preserve the compatibility path for bulk and legacy catalogue requests.
                dbAuthor = _addAuthorService.AddAuthorWithMetadata(author, metadata, false);
            }

            return SaveBook(book, dbAuthor, doRefresh);
        }

        private Book SaveBook(Book book, Author author, bool doRefresh)
        {
            book.Author = author;
            book.AuthorMetadataId = author.AuthorMetadataId;
            _bookService.AddBook(book, doRefresh);

            _importListExclusionService.Delete(book.ForeignBookId);
            _importListExclusionService.Delete(book.AuthorMetadata.Value.ForeignAuthorId);

            return book;
        }

        public List<Book> AddBooks(List<Book> books, bool doRefresh = true)
        {
            var added = DateTime.UtcNow;
            var addedBooks = new List<Book>();

            foreach (var a in books)
            {
                a.Added = added;
                try
                {
                    addedBooks.Add(AddBook(a, doRefresh, requireSelectedEdition: false));
                }
                catch (Exception ex)
                {
                    // Could be a bad id from an import list
                    _logger.Error(ex, "Failed to import id: {0} - {1}", LogSanitizer.Sanitize(a.ForeignBookId), LogSanitizer.Sanitize(a.Title));
                }
            }

            return addedBooks;
        }

        private Book AddSkyhookData(Book newBook, bool requireSelectedEdition, out bool confirmedNewAuthorSelection)
        {
            var selectedEdition = newBook.GetPreferredEdition();

            if (selectedEdition == null)
            {
                throw new ValidationException(new List<ValidationFailure>
                                              {
                                                  new ValidationFailure("Editions", "Book must include at least one edition")
                                              });
            }

            var editionId = selectedEdition.ForeignEditionId;
            var requestedWork = newBook.ForeignBookId;
            var requestedOwner = newBook.Author?.Value?.Metadata?.Value?.ForeignAuthorId;
            var normalizedBookId = OpenLibraryIdNormalizer.NormalizeWorkId(requestedWork);
            var resolvesWorkMarker = requireSelectedEdition &&
                                     IsOpenLibraryToken(normalizedBookId, 'W') &&
                                     string.Equals(newBook.ForeignBookId, $"openlibrary:work:{normalizedBookId}", StringComparison.OrdinalIgnoreCase) &&
                                     string.Equals(editionId, newBook.ForeignBookId, StringComparison.OrdinalIgnoreCase) &&
                                     string.Equals(newBook.ForeignEditionId, editionId, StringComparison.OrdinalIgnoreCase) &&
                                     newBook.Editions.Value.Count == 1;

            var tuple = GetBookMetadata(newBook.ForeignBookId, normalizedBookId ?? newBook.ForeignBookId);

            var providerEditions = tuple.Item2.Editions?.Value?.Where(edition => edition != null).ToList() ?? new List<Edition>();

            // A title result's singleton Open Library work marker requests the provider's default
            // tracked representative of that work. Explicit edition IDs still require an exact match.
            var editionToMonitor = resolvesWorkMarker
                ? ResolveOpenLibraryWorkEdition(newBook, tuple, providerEditions)
                : providerEditions.Find(edition => edition.ForeignEditionId == editionId);

            if (requireSelectedEdition && !string.IsNullOrWhiteSpace(editionId) && editionToMonitor == null)
            {
                var openLibraryId = OpenLibraryIdNormalizer.NormalizeEditionId(editionId);
                if (openLibraryId != null)
                {
                    editionToMonitor = providerEditions.Find(edition =>
                        OpenLibraryIdNormalizer.NormalizeEditionId(edition.ForeignEditionId) == openLibraryId);
                }

                if (editionToMonitor == null)
                {
                    throw new ValidationException(new List<ValidationFailure>
                    {
                        new ("Editions", resolvesWorkMarker
                            ? "Open Library did not verify an ebook or audiobook edition for this work. Try another result or retry later."
                            : "The selected edition is unavailable. Search for the book again and choose an available edition.")
                    });
                }
            }

            // The resolved marker and a normalized edition alias have already been matched above.
            // Do not include an edition chosen later by the unselected provider fallback.
            confirmedNewAuthorSelection = requireSelectedEdition &&
                                          !string.IsNullOrWhiteSpace(editionId) && editionToMonitor != null &&
                                          HasConfirmedNewAuthorSelection(requestedWork, requestedOwner, tuple);

            newBook.UseMetadataFrom(tuple.Item2);
            if (resolvesWorkMarker)
            {
                newBook.ForeignEditionId = editionToMonitor.ForeignEditionId;
            }

            newBook.Added = DateTime.UtcNow;
            newBook.Editions = providerEditions;
            providerEditions.ForEach(edition => edition.Monitored = false);
            editionToMonitor ??= newBook.GetPreferredEdition();

            if (editionToMonitor != null)
            {
                editionToMonitor.Monitored = true;
            }

            var metadata = tuple.Item3?.FirstOrDefault(author => author?.ForeignAuthorId == tuple.Item1);
            if (string.IsNullOrWhiteSpace(metadata?.ForeignAuthorId))
            {
                throw new ValidationException(new List<ValidationFailure>
                {
                    new ("ForeignAuthorId", "The book provider did not identify its author")
                });
            }

            newBook.AuthorMetadata = metadata;

            return newBook;
        }

        private static bool HasConfirmedNewAuthorSelection(string requestedWork,
                                                            string requestedOwner,
                                                            Tuple<string, Book, List<AuthorMetadata>> response)
        {
            if (string.IsNullOrWhiteSpace(requestedOwner))
            {
                return false;
            }

            var returnedOwner = response.Item1;
            var workOwner = response.Item2.AuthorMetadata?.Value?.ForeignAuthorId;
            var matchingMetadata = response.Item3?.Where(owner => owner?.ForeignAuthorId == returnedOwner).Take(2).ToList();
            if (matchingMetadata?.Count != 1 || string.IsNullOrWhiteSpace(matchingMetadata[0].Name))
            {
                return false;
            }

            if (requestedWork?.StartsWith("hardcover:work:", StringComparison.Ordinal) == true)
            {
                return requestedWork == response.Item2.ForeignBookId &&
                       requestedOwner == returnedOwner && returnedOwner == workOwner;
            }

            var work = OpenLibraryIdNormalizer.NormalizeWorkId(requestedWork);
            var ownerId = OpenLibraryIdNormalizer.NormalizeAuthorId(returnedOwner);
            return IsOpenLibraryToken(work, 'W') &&
                   string.Equals(requestedWork, $"openlibrary:work:{work}", StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(requestedWork, response.Item2.ForeignBookId, StringComparison.OrdinalIgnoreCase) &&
                   IsOpenLibraryToken(ownerId, 'A') &&
                   ownerId == OpenLibraryIdNormalizer.NormalizeAuthorId(requestedOwner) &&
                   ownerId == OpenLibraryIdNormalizer.NormalizeAuthorId(workOwner);
        }

        private Tuple<string, Book, List<AuthorMetadata>> GetBookMetadata(string foreignBookId, string lookupId)
        {
            try
            {
                return _orchestrator.GetBookInfo(lookupId);
            }
            catch (BookNotFoundException)
            {
                _logger.Error("Book with provider ID {0} was not found by the metadata provider.", LogSanitizer.Sanitize(foreignBookId));

                throw new ValidationException(new List<ValidationFailure>
                                              {
                                                  new ValidationFailure(nameof(Book.ForeignBookId), "A book with this provider ID was not found", foreignBookId)
                                              });
            }
        }

        private static Edition ResolveOpenLibraryWorkEdition(Book request, Tuple<string, Book, List<AuthorMetadata>> response, List<Edition> editions)
        {
            var returnedAuthor = OpenLibraryIdNormalizer.NormalizeAuthorId(response.Item1);
            var workAuthor = OpenLibraryIdNormalizer.NormalizeAuthorId(response.Item2.AuthorMetadata?.Value?.ForeignAuthorId);
            var suppliedAuthor = request.AuthorMetadata?.Value?.ForeignAuthorId;
            if (!string.Equals(response.Item2.ForeignBookId, request.ForeignBookId, StringComparison.OrdinalIgnoreCase) ||
                !IsOpenLibraryToken(returnedAuthor, 'A') ||
                returnedAuthor != workAuthor ||
                (!string.IsNullOrWhiteSpace(suppliedAuthor) && returnedAuthor != OpenLibraryIdNormalizer.NormalizeAuthorId(suppliedAuthor)))
            {
                return null;
            }

            return editions.Where(edition => OpenLibraryIdNormalizer.NormalizeEditionId(edition.ForeignEditionId) != null &&
                                             ImportFormat.ForEdition(edition, requireExplicitFormat: true) is { } format &&
                                             edition.IsEbook == (format == FormatType.Ebook))
                .GetPreferredEdition();
        }

        private static bool IsOpenLibraryToken(string token, char suffix) =>
            token is { Length: > 3 } && token.EndsWith(suffix) && token[2..^1].All(char.IsAsciiDigit);
    }
}
