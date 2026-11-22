using System;
using System.Collections.Generic;
using System.Linq;
using FluentValidation;
using FluentValidation.Results;
using NLog;
using NzbDrone.Common.Instrumentation;
using NzbDrone.Core.Exceptions;
using NzbDrone.Core.ImportLists.Exclusions;
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
            var sourceWorkId = book.ForeignBookId;
            var sourceAuthorId = book.AuthorMetadata.Value.ForeignAuthorId;
            var dbAuthor = _authorService.FindById(sourceAuthorId);

            // we allow adding extra editions, so check if the book already exists
            var dbBook = _bookService.FindById(sourceWorkId);
            if (dbBook != null)
            {
                if (dbBook.AuthorMetadataId > 0 && dbAuthor?.AuthorMetadataId != dbBook.AuthorMetadataId)
                {
                    throw new ValidationException(new List<ValidationFailure>
                    {
                        new ("ForeignAuthorId", "The matched work belongs to a different catalogue author")
                    });
                }

                book.UseDbFieldsFrom(dbBook);
                book.ForeignBookId = dbBook.ForeignBookId;
                book.TitleSlug = dbBook.TitleSlug;
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

            // Remove any import list exclusions preventing addition
            _importListExclusionService.Delete(book.ForeignBookId);
            if (sourceWorkId != book.ForeignBookId)
            {
                _importListExclusionService.Delete(sourceWorkId);
            }

            _importListExclusionService.Delete(sourceAuthorId);

            book.Author = dbAuthor;
            book.AuthorMetadataId = dbAuthor.AuthorMetadataId;
            _bookService.AddBook(book, doRefresh);

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

            Tuple<string, Book, List<AuthorMetadata>> tuple = null;
            try
            {
                var normalizedBookId = OpenLibraryIdNormalizer.NormalizeWorkId(newBook.ForeignBookId) ?? newBook.ForeignBookId;
                tuple = _orchestrator.GetBookInfo(normalizedBookId);
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
            var editionToMonitor = providerEditions.Find(edition => edition.ForeignEditionId == editionId);

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
                        new ("Editions", "The selected edition is unavailable. Search for the book again and choose an available edition.")
                    });
                }
            }

            newBook.UseMetadataFrom(tuple.Item2);
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
    }
}
