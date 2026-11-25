using System;
using System.Collections.Generic;
using System.Linq;
using FluentValidation;
using FluentValidation.Results;
using NLog;
using NzbDrone.Common.Instrumentation;
using NzbDrone.Core.Exceptions;
using NzbDrone.Core.ImportLists.Exclusions;
using NzbDrone.Core.MediaFiles.BookImport;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.MetadataSource.OpenLibrary;

namespace NzbDrone.Core.Books
{
    public interface IAddBookService
    {
        Book AddBook(Book book, bool doRefresh = true);
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

        private Book AddBook(Book book, bool doRefresh, bool requireSelectedEdition)
        {
            _logger.Debug("Adding book {0} - {1}", LogSanitizer.Sanitize(book?.ForeignBookId), LogSanitizer.Sanitize(book?.Title));

            book = AddSkyhookData(book, requireSelectedEdition);

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

                // The work lookup already returned this author. The BookAdded
                // handler can hydrate their bibliography after the book is saved.
                dbAuthor = _addAuthorService.AddAuthorWithMetadata(author, book.AuthorMetadata.Value, false);
            }

            book.Author = dbAuthor;
            book.AuthorMetadataId = dbAuthor.AuthorMetadataId;
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

        private Book AddSkyhookData(Book newBook, bool requireSelectedEdition)
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
            var normalizedBookId = OpenLibraryIdNormalizer.NormalizeWorkId(newBook.ForeignBookId);
            var resolvesWorkMarker = requireSelectedEdition &&
                                     IsOpenLibraryToken(normalizedBookId, 'W') &&
                                     string.Equals(newBook.ForeignBookId, $"openlibrary:work:{normalizedBookId}", StringComparison.OrdinalIgnoreCase) &&
                                     string.Equals(editionId, newBook.ForeignBookId, StringComparison.OrdinalIgnoreCase) &&
                                     string.Equals(newBook.ForeignEditionId, editionId, StringComparison.OrdinalIgnoreCase) &&
                                     newBook.Editions.Value.Count == 1;

            Tuple<string, Book, List<AuthorMetadata>> tuple = null;
            try
            {
                tuple = _orchestrator.GetBookInfo(normalizedBookId ?? newBook.ForeignBookId);
            }
            catch (BookNotFoundException)
            {
                _logger.Error("Book with Foreign Id {0} was not found, it may have been removed from OpenLibrary.", LogSanitizer.Sanitize(newBook.ForeignBookId));

                throw new ValidationException(new List<ValidationFailure>
                                              {
                                                  new ValidationFailure("OpenLibraryId", "A book with this ID was not found", newBook.ForeignBookId)
                                              });
            }

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
