using System.Collections.Generic;
using NzbDrone.Core.Books;

namespace NzbDrone.Core.MetadataSource
{
    public interface IProvideSeriesBooks
    {
        /// <summary>
        /// Returns a bounded first page of provider-ordered work summaries, or null when unavailable.
        /// </summary>
        List<Book> GetSeriesBooks(string foreignSeriesId);
    }
}
