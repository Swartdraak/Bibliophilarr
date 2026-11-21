using System.Collections.Generic;

namespace NzbDrone.Core.MetadataSource
{
    public interface ISearchForNewSeries
    {
        // A null result indicates an unavailable or incomplete provider response.
        List<SeriesSearchSummary> SearchForNewSeries(string title);
    }
}
