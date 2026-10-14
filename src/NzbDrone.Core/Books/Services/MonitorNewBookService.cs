using System;
using System.Collections.Generic;
using System.Linq;
using NLog;

namespace NzbDrone.Core.Books
{
    public interface IMonitorNewBookService
    {
        bool ShouldMonitorNewBook(Book addedBook, List<Book> existingBooks, NewItemMonitorTypes author);
    }

    public class MonitorNewBookService : IMonitorNewBookService
    {
        private readonly Logger _logger;

        public MonitorNewBookService(Logger logger)
        {
            _logger = logger;
        }

        public bool ShouldMonitorNewBook(Book addedBook, List<Book> existingBooks, NewItemMonitorTypes monitorNewItems)
        {
            if (monitorNewItems == NewItemMonitorTypes.None)
            {
                return false;
            }

            if (monitorNewItems == NewItemMonitorTypes.All)
            {
                return true;
            }

            if (monitorNewItems == NewItemMonitorTypes.New)
            {
                var newest = existingBooks.MaxBy(x => x.ReleaseDate ?? DateTime.MinValue)?.ReleaseDate ?? DateTime.MinValue;

                return (addedBook.ReleaseDate ?? DateTime.MinValue) >= newest;
            }

            // Unknown / out-of-range monitor value (e.g. a stale or externally-written
            // numeric value such as 3). Never throw: throwing here was caught upstream and
            // only logged, which silently dropped every new book on import/refresh (issue #256).
            // Log loudly and fall back to the safe "do not auto-monitor" default (None).
            _logger.Warn("Unknown NewItemMonitorTypes value {0} ({1}) for book '{2}'; defaulting to no-monitor. The value is out of range (expected All=0, None=1, New=2).",
                (int)monitorNewItems,
                monitorNewItems,
                addedBook?.Title);

            return false;
        }
    }
}
