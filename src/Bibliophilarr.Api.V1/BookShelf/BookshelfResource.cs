using System.Collections.Generic;
using NzbDrone.Core.Books;

namespace Bibliophilarr.Api.V1.Bookshelf
{
    public class BookshelfResource
    {
        public List<BookshelfAuthorResource> Authors { get; set; }
        public BookshelfMonitoringOptionsResource MonitoringOptions { get; set; }
        public NewItemMonitorTypes? MonitorNewItems { get; set; }
    }

    public class BookshelfMonitoringOptionsResource
    {
        public MonitorTypes? Monitor { get; set; }
        public List<string> BooksToMonitor { get; set; }
        public bool Monitored { get; set; }

        public MonitoringOptions ToModel()
        {
            if (!Monitor.HasValue && BooksToMonitor?.Count is not > 0)
            {
                return null;
            }

            return new MonitoringOptions
            {
                Monitor = Monitor ?? MonitorTypes.All,
                BooksToMonitor = BooksToMonitor ?? new List<string>(),
                Monitored = Monitored
            };
        }
    }
}
