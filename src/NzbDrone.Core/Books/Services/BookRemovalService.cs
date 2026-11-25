using System.Net;
using NzbDrone.Core.Books.Events;
using NzbDrone.Core.Exceptions;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.Books
{
    public interface IRemoveBookFromLibrary
    {
        void Remove(int bookId, bool deleteFiles, bool addImportListExclusion);
    }

    public class BookRemovalService : IRemoveBookFromLibrary
    {
        private readonly IBookService _bookService;
        private readonly IBookRepository _bookRepository;
        private readonly IDeleteMediaFiles _deleteMediaFiles;
        private readonly IEventAggregator _eventAggregator;

        public BookRemovalService(IBookService bookService,
                                  IBookRepository bookRepository,
                                  IDeleteMediaFiles deleteMediaFiles,
                                  IEventAggregator eventAggregator)
        {
            _bookService = bookService;
            _bookRepository = bookRepository;
            _deleteMediaFiles = deleteMediaFiles;
            _eventAggregator = eventAggregator;
        }

        public void Remove(int bookId, bool deleteFiles, bool addImportListExclusion)
        {
            if (!deleteFiles)
            {
                _bookService.DeleteBook(bookId, false, addImportListExclusion);
                return;
            }

            var book = _bookRepository.Get(bookId);
            book.Author.LazyLoad();
            _deleteMediaFiles.DeleteFilesForExplicitBookRemoval(bookId);
            if (!_bookRepository.DeleteIfNoLinkedFiles(bookId))
            {
                throw new NzbDroneClientException(HttpStatusCode.Conflict, "The book or its files changed during deletion. Refresh before trying again.");
            }

            _eventAggregator.PublishEvent(new BookDeletedEvent(book, true, addImportListExclusion, mediaAlreadyRemoved: true));
        }
    }
}
