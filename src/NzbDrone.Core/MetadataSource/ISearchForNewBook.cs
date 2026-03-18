using System.Collections.Generic;
using NzbDrone.Core.Books;

namespace NzbDrone.Core.MetadataSource
{
    public interface ISearchForNewBook
    {
        List<Book> SearchForNewBook(string title, string author, bool getAllEditions = true);
        List<Book> SearchByIsbn(string isbn);
        List<Book> SearchByAsin(string asin);

        /// <summary>
        /// Provider-agnostic identifier lookup.
        /// Supported idType values: "openlibrary" (int edition id), "olid" (Open Library work key), "isbn", "asin".
        /// Unknown or unsupported idTypes return an empty list.
        /// </summary>
        List<Book> SearchByExternalId(string idType, string id);
    }
}
