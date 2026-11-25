using System.Linq;
using NzbDrone.Core.Books;
using NzbDrone.Core.Books.Events;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.MediaCover
{
    public class RecoverImportedBookCoverService : IHandleAsync<EmbeddedBookCoversImportedEvent>, IExecute<RecoverImportedBookCoverCommand>
    {
        private readonly IManageCommandQueue _queue;
        private readonly IEmbeddedBookCoverService _covers;
        private readonly IBookService _books;
        private readonly IEventAggregator _events;

        public RecoverImportedBookCoverService(IManageCommandQueue queue,
                                               IEmbeddedBookCoverService covers,
                                               IBookService books,
                                               IEventAggregator events)
        {
            _queue = queue;
            _covers = covers;
            _books = books;
            _events = events;
        }

        public void HandleAsync(EmbeddedBookCoversImportedEvent message)
        {
            foreach (var command in message.Commands)
            {
                _queue.Push(command, CommandPriority.Low, CommandTrigger.Scheduled);
            }
        }

        public void Execute(RecoverImportedBookCoverCommand command)
        {
            if (command.Trigger != CommandTrigger.Scheduled || !_covers.RecoverImported(command))
            {
                return;
            }

            var book = _books.GetBooks(new[] { command.BookId }).SingleOrDefault();
            if (book != null && book.CanonicalId == command.CanonicalId)
            {
                _events.PublishEvent(new BookUpdatedEvent(book));
            }
        }
    }
}
