using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions;
using System.Linq;
using Bibliophilarr.Http;
using Bibliophilarr.Http.REST;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.Books;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Datastore.Events;
using NzbDrone.Core.DecisionEngine.Specifications;
using NzbDrone.Core.Exceptions;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.Events;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Qualities;
using NzbDrone.Http.REST.Attributes;
using NzbDrone.SignalR;
using BadRequestException = NzbDrone.Core.Exceptions.BadRequestException;
using HttpStatusCode = System.Net.HttpStatusCode;

namespace Bibliophilarr.Api.V1.BookFiles
{
    [V1ApiController]
    public class BookFileController : RestControllerWithSignalR<BookFileResource, BookFile>,
                                 IHandle<BookFileAddedEvent>,
                                 IHandle<BookFileDeletedEvent>
    {
        private readonly IMediaFileService _mediaFileService;
        private readonly IDeleteMediaFiles _mediaFileDeletionService;
        private readonly IMetadataTagService _metadataTagService;
        private readonly IAuthorService _authorService;
        private readonly IBookService _bookService;
        private readonly IUpgradableSpecification _upgradableSpecification;
        private readonly IConfigService _configService;

        public BookFileController(IBroadcastSignalRMessage signalRBroadcaster,
                               IMediaFileService mediaFileService,
                               IDeleteMediaFiles mediaFileDeletionService,
                               IMetadataTagService metadataTagService,
                               IAuthorService authorService,
                               IBookService bookService,
                               IUpgradableSpecification upgradableSpecification,
                               IConfigService configService)
            : base(signalRBroadcaster)
        {
            _mediaFileService = mediaFileService;
            _mediaFileDeletionService = mediaFileDeletionService;
            _metadataTagService = metadataTagService;
            _authorService = authorService;
            _bookService = bookService;
            _upgradableSpecification = upgradableSpecification;
            _configService = configService;
        }

        private BookFileResource MapToResource(BookFile bookFile)
        {
            if (bookFile.EditionId > 0 && bookFile.Author != null && bookFile.Author.Value != null)
            {
                return bookFile.ToResource(bookFile.Author.Value, _upgradableSpecification);
            }
            else
            {
                return bookFile.ToResource();
            }
        }

        protected override BookFileResource GetResourceById(int id)
        {
            var resource = MapToResource(_mediaFileService.Get(id));
            resource.AudioTags = _metadataTagService.ReadTags((FileInfoBase)new FileInfo(resource.Path));
            return resource;
        }

        [HttpGet]
        public List<BookFileResource> GetBookFiles(int? authorId, [FromQuery] List<int> bookFileIds, [FromQuery(Name = "bookId")] List<int> bookIds, bool? unmapped, [FromQuery] string formatType = null)
        {
            if (!authorId.HasValue && !bookFileIds.Any() && !bookIds.Any() && !unmapped.HasValue)
            {
                throw new BadRequestException("authorId, bookId, bookFileIds or unmapped must be provided");
            }

            if (unmapped.HasValue && unmapped.Value)
            {
                var files = _mediaFileService.GetUnmappedFiles();
                return files.ConvertAll(f => MapToResource(f));
            }

            if (authorId.HasValue && !bookIds.Any())
            {
                var author = _authorService.GetAuthor(authorId.Value);

                return _mediaFileService.GetFilesByAuthor(authorId.Value).ConvertAll(f => f.ToResource(author, _upgradableSpecification));
            }

            if (bookIds.Any())
            {
                var result = new List<BookFileResource>();
                foreach (var bookId in bookIds)
                {
                    var book = _bookService.GetBook(bookId);
                    var bookAuthor = _authorService.GetAuthor(book.AuthorId);
                    result.AddRange(_mediaFileService.GetFilesByBook(book.Id).ConvertAll(f => f.ToResource(bookAuthor, _upgradableSpecification)));
                }

                // When formatType is specified and dual-format tracking is enabled,
                // filter to only return files matching the requested format type.
                // This prevents the confirm-import dialog from showing cross-format
                // files (e.g. ebook files when importing audiobooks).
                if (!string.IsNullOrEmpty(formatType) && _configService.EnableDualFormatTracking)
                {
                    var requestedFormat = string.Equals(formatType, "audiobook", global::System.StringComparison.OrdinalIgnoreCase)
                        ? FormatType.Audiobook
                        : FormatType.Ebook;

                    result = result.Where(f => f.Quality?.Quality != null &&
                        Quality.GetFormatType(Quality.FindById(f.Quality.Quality.Id)) == requestedFormat).ToList();
                }

                return result;
            }
            else
            {
                // trackfiles will come back with the author already populated
                var bookFiles = _mediaFileService.Get(bookFileIds);
                return bookFiles.ConvertAll(e => MapToResource(e));
            }
        }

        [RestPutById]
        public ActionResult<BookFileResource> SetQuality([FromBody] BookFileResource bookFileResource)
        {
            var bookFile = _mediaFileService.Get(bookFileResource.Id);
            bookFile.Quality = bookFileResource.Quality;
            _mediaFileService.Update(bookFile);
            return Accepted(bookFile.Id);
        }

        [HttpPut("editor")]
        public IActionResult SetQuality([FromBody] BookFileListResource resource)
        {
            var bookFiles = _mediaFileService.Get(resource.BookFileIds);

            foreach (var bookFile in bookFiles)
            {
                if (resource.Quality != null)
                {
                    bookFile.Quality = resource.Quality;
                }
            }

            _mediaFileService.Update(bookFiles);

            return Accepted(bookFiles.ConvertAll(f => f.ToResource(bookFiles.First().Author.Value, _upgradableSpecification)));
        }

        [RestDeleteById]
        public void DeleteBookFile(int id)
        {
            var bookFile = _mediaFileService.Get(id);

            if (bookFile == null)
            {
                throw new NzbDroneClientException(HttpStatusCode.NotFound, "Book file not found");
            }

            if (bookFile.EditionId > 0 && bookFile.Author != null && bookFile.Author.Value != null)
            {
                _mediaFileDeletionService.DeleteTrackFile(bookFile.Author.Value, bookFile);
            }
            else
            {
                _mediaFileDeletionService.DeleteTrackFile(bookFile, "Unmapped_Files");
            }
        }

        [HttpDelete("bulk")]
        public object DeleteTrackFiles([FromBody] BookFileListResource resource)
        {
            var bookFiles = _mediaFileService.Get(resource.BookFileIds);

            foreach (var bookFile in bookFiles)
            {
                if (bookFile.EditionId > 0 && bookFile.Author != null && bookFile.Author.Value != null)
                {
                    _mediaFileDeletionService.DeleteTrackFile(bookFile.Author.Value, bookFile);
                }
                else
                {
                    _mediaFileDeletionService.DeleteTrackFile(bookFile, "Unmapped_Files");
                }
            }

            return new { };
        }

        [NonAction]
        public void Handle(BookFileAddedEvent message)
        {
            BroadcastResourceChange(ModelAction.Updated, MapToResource(message.BookFile));
        }

        [NonAction]
        public void Handle(BookFileDeletedEvent message)
        {
            BroadcastResourceChange(ModelAction.Deleted, MapToResource(message.BookFile));
        }
    }
}
