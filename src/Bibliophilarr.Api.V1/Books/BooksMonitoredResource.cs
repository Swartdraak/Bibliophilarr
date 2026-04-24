using System.Collections.Generic;
using NzbDrone.Core.Books;

namespace Bibliophilarr.Api.V1.Books
{
    public class BooksMonitoredResource
    {
        public List<int> BookIds { get; set; }
        public bool Monitored { get; set; }
        public FormatType? FormatType { get; set; }
    }
}
