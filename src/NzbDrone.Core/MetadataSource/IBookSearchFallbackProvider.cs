using System.Collections.Generic;
using NzbDrone.Core.Books;

namespace NzbDrone.Core.MetadataSource
{
    public interface IBookSearchFallbackProvider
    {
        string ProviderName { get; }
        ProviderRateLimitInfo RateLimitInfo { get; }
        List<Book> Search(string title, string author);
    }
}
