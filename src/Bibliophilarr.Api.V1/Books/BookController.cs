using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Bibliophilarr.Http;
using Bibliophilarr.Http.ErrorManagement;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.AuthorStats;
using NzbDrone.Core.Books;
using NzbDrone.Core.Books.Events;
using NzbDrone.Core.Datastore.Events;
using NzbDrone.Core.DecisionEngine.Specifications;
using NzbDrone.Core.Download;
using NzbDrone.Core.Exceptions;
using NzbDrone.Core.MediaCover;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.Events;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Validation;
using NzbDrone.Core.Validation.Paths;
using NzbDrone.Http.REST.Attributes;
using NzbDrone.SignalR;

namespace Bibliophilarr.Api.V1.Books
{
    /// <summary>
    /// REST API controller for book resources. Supports listing, creating, updating,
    /// and deleting books with optional filtering by author, title, and monitored status.
    /// Broadcasts real-time updates via SignalR on book state changes.
    /// </summary>
    [V1ApiController]
    public class BookController : BookControllerWithSignalR,
        IHandle<BookGrabbedEvent>,
        IHandle<BookEditedEvent>,
        IHandle<BookUpdatedEvent>,
        IHandle<BookDeletedEvent>,
        IHandle<BookImportedEvent>,
        IHandle<BookFileDeletedEvent>
    {
        private const int DefaultPageSize = 100;
        private const int MaxPageSize = 1000;
        private const int LargeLibraryWarningThreshold = 5000;
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        protected readonly IAuthorService _authorService;
        protected readonly IEditionService _editionService;
        protected readonly IAddBookService _addBookService;

        public BookController(IAuthorService authorService,
                          IBookService bookService,
                          IAddBookService addBookService,
                          IEditionService editionService,
                          ISeriesBookLinkService seriesBookLinkService,
                          IAuthorStatisticsService authorStatisticsService,
                          IMapCoversToLocal coverMapper,
                          IUpgradableSpecification upgradableSpecification,
                          IBroadcastSignalRMessage signalRBroadcaster,
                          IAuthorFormatProfileService formatProfileService,
                          NzbDrone.Core.Profiles.Qualities.IQualityProfileService qualityProfileService,
                          QualityProfileExistsValidator qualityProfileExistsValidator,
                          MetadataProfileExistsValidator metadataProfileExistsValidator)

        : base(bookService, seriesBookLinkService, authorStatisticsService, coverMapper, upgradableSpecification, signalRBroadcaster, formatProfileService, qualityProfileService)
        {
            _authorService = authorService;
            _editionService = editionService;
            _addBookService = addBookService;

            PostValidator.RuleFor(s => s.ForeignBookId).NotEmpty();
            PostValidator.RuleForEach(s => s.Editions).NotNull();

            // Issue #276: the Author-scoped rules below dereference s.Author, which is
            // null for a payload that omits the nested Author object (the simple
            // title+author add-book flow). FluentValidation evaluates the member
            // expression for every instance, so an unguarded RuleFor threw
            // NullReferenceException inside validation — surfacing as a 500 instead of
            // a 400 with validation details. Guarding each rule on s.Author != null
            // degrades a null Author to validation failures (ForeignAuthorId/
            // RootFolderPath/ForeignBookId not-empty), which the pipeline maps to 400.
            // When Author is present the rules keep their exact previous behavior.
            PostValidator.RuleFor(s => s.Author.QualityProfileId).SetValidator(qualityProfileExistsValidator).When(s => s.Author != null);
            PostValidator.RuleFor(s => s.Author.MetadataProfileId).SetValidator(metadataProfileExistsValidator).When(s => s.Author != null);
            PostValidator.RuleFor(s => s.Author.RootFolderPath).IsValidPath().When(s => s.Author != null && s.Author.Path.IsNullOrWhiteSpace());
            PostValidator.RuleFor(s => s.Author.ForeignAuthorId).NotEmpty().When(s => s.Author != null);
        }

        [HttpGet]
        public async Task<List<BookResource>> GetBooks([FromQuery] int? authorId,
            [FromQuery] List<int> bookIds,
            [FromQuery] string titleSlug,
            [FromQuery] bool includeAllAuthorBooks = false,
            [FromQuery] int? page = null,
            [FromQuery] int? pageSize = null,
            [FromQuery] bool includeShared = false)
        {
            if (!authorId.HasValue && !bookIds.Any() && titleSlug.IsNullOrWhiteSpace())
            {
                var editionTask = Task.Run(() => _editionService.GetAllMonitoredEditions());
                var metadataTask = Task.Run(() => _authorService.GetAllAuthors());
                var books = _bookService.GetAllBooks();

                if (books.Count >= LargeLibraryWarningThreshold)
                {
                    Logger.Warn("GetBooks called without filters on large library ({0} books). Consider using authorId or pagination parameters for better performance.", books.Count);
                }

                var editions = (await editionTask).GroupBy(x => x.BookId).ToDictionary(x => x.Key, y => y.ToList());

                var authors = (await metadataTask).ToDictionary(x => x.AuthorMetadataId);

                foreach (var book in books)
                {
                    book.Author = authors[book.AuthorMetadataId];
                    if (editions.TryGetValue(book.Id, out var bookEditions))
                    {
                        book.Editions = bookEditions;
                    }
                    else
                    {
                        book.Editions = new List<Edition>();
                    }
                }

                // Apply optional pagination for large libraries
                if (page.HasValue && pageSize.HasValue)
                {
                    var effectivePageSize = global::System.Math.Min(pageSize.Value, MaxPageSize);
                    var skip = (page.Value - 1) * effectivePageSize;
                    books = books.Skip(skip).Take(effectivePageSize).ToList();
                }

                return MapToResource(books, false);
            }

            if (authorId.HasValue)
            {
                var books = _bookService.GetBooksByAuthor(authorId.Value);

                var author = _authorService.GetAuthor(authorId.Value);
                var editions = _editionService.GetEditionsByAuthor(authorId.Value)
                    .GroupBy(x => x.BookId)
                    .ToDictionary(x => x.Key, y => y.ToList());

                foreach (var book in books)
                {
                    book.Author = author;
                    if (editions.TryGetValue(book.Id, out var bookEditions))
                    {
                        book.Editions = bookEditions;
                    }
                    else
                    {
                        book.Editions = new List<Edition>();
                    }
                }

                if (includeShared)
                {
                    // Credited works retain their owning Author, Editions, and monitoring policy.
                    books = books.Concat(_bookService.GetSharedBooksByAuthorMetadataId(author.AuthorMetadataId)).ToList();
                }

                return MapToResource(books, false);
            }

            if (titleSlug.IsNotNullOrWhiteSpace())
            {
                var book = _bookService.FindBySlug(titleSlug);

                if (book == null)
                {
                    return MapToResource(new List<Book>(), false);
                }

                if (includeAllAuthorBooks)
                {
                    return MapToResource(_bookService.GetBooksByAuthor(book.AuthorId), false);
                }
                else
                {
                    return MapToResource(new List<Book> { book }, false);
                }
            }

            return MapToResource(_bookService.GetBooks(bookIds), false);
        }

        [HttpGet("{id:int}/overview")]
        public object Overview(int id)
        {
            var overview = _editionService.GetEditionsByBook(id).GetBookOverview();
            return new
            {
                id,
                overview
            };
        }

        [RestPostById]
        [ProducesResponseType(typeof(BookResource), (int)HttpStatusCode.Created)]
        [ProducesResponseType(typeof(ErrorModel), (int)HttpStatusCode.Conflict)]
        public ActionResult<BookResource> AddBook([FromBody] BookResource bookResource)
        {
            try
            {
                var model = bookResource.ToModel();
                var book = !bookResource.EditionsSpecified && !string.IsNullOrWhiteSpace(bookResource.ForeignEditionId)
                    ? _addBookService.AddBookByExactEdition(model, bookResource.AuthorId)
                    : _addBookService.AddBook(model);
                return Created(book.Id);
            }
            catch (NzbDroneClientException ex) when (ex.StatusCode == HttpStatusCode.Conflict)
            {
                return Conflict(new ErrorModel { Message = ex.Message });
            }
        }

        [RestPutById]
        public ActionResult<BookResource> UpdateBook([FromBody] BookResource bookResource)
        {
            var book = _bookService.GetBook(bookResource.Id);

            var model = bookResource.ToModel(book);

            _bookService.UpdateBook(model);
            _editionService.UpdateMany(model.Editions.Value);

            BroadcastResourceChange(ModelAction.Updated, model.Id);

            return Accepted(model.Id);
        }

        [RestDeleteById]
        public void DeleteBook(int id, bool deleteFiles = false, bool addImportListExclusion = false)
        {
            _bookService.DeleteBook(id, deleteFiles, addImportListExclusion);
        }

        [HttpPut("monitor")]
        public IActionResult SetBooksMonitored([FromBody] BooksMonitoredResource resource)
        {
            if (resource.FormatType.HasValue)
            {
                if (resource.FormatType.Value != FormatType.Ebook && resource.FormatType.Value != FormatType.Audiobook)
                {
                    return BadRequest("Unsupported book format.");
                }

                _bookService.SetFormatMonitored(resource.BookIds, resource.FormatType.Value, resource.Monitored);

                return Accepted(MapToResource(_bookService.GetBooks(resource.BookIds), false));
            }

            _bookService.SetMonitored(resource.BookIds, resource.Monitored);

            if (resource.BookIds.Count == 1)
            {
                _bookService.SetBookMonitored(resource.BookIds.First(), resource.Monitored);
            }
            else
            {
                _bookService.SetMonitored(resource.BookIds, resource.Monitored);
            }

            return Accepted(MapToResource(_bookService.GetBooks(resource.BookIds), false));
        }

        [NonAction]
        public void Handle(BookGrabbedEvent message)
        {
            foreach (var book in message.Book.Books)
            {
                var resource = book.ToResource();
                resource.Grabbed = true;

                BroadcastResourceChange(ModelAction.Updated, resource);
            }
        }

        [NonAction]
        public void Handle(BookEditedEvent message)
        {
            BroadcastResourceChange(ModelAction.Updated, MapToResource(message.Book, true));
        }

        [NonAction]
        public void Handle(BookUpdatedEvent message)
        {
            BroadcastResourceChange(ModelAction.Updated, MapToResource(message.Book, true));
        }

        [NonAction]
        public void Handle(BookDeletedEvent message)
        {
            BroadcastResourceChange(ModelAction.Deleted, message.Book.ToResource());
        }

        [NonAction]
        public void Handle(BookImportedEvent message)
        {
            if (message?.Book?.Id is > 0)
            {
                BroadcastResourceChange(ModelAction.Updated, message.Book.Id);
            }
        }

        [NonAction]
        public void Handle(BookFileDeletedEvent message)
        {
            if (message?.BookFile?.Edition?.Value?.Book?.Value == null)
            {
                return;
            }

            if (message.Reason == DeleteMediaFileReason.Upgrade)
            {
                return;
            }

            BroadcastResourceChange(ModelAction.Updated, MapToResource(message.BookFile.Edition.Value.Book.Value, true));
        }
    }
}
