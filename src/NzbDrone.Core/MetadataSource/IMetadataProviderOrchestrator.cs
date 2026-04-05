using System;
using System.Collections.Generic;
using NzbDrone.Core.Books;

namespace NzbDrone.Core.MetadataSource
{
    /// <summary>
    /// High-level metadata operations facade. Routes each operation through the
    /// provider registry, applying ID-scope compatibility filtering and priority-based
    /// fallback. Consumers should use this interface rather than interacting with
    /// individual providers directly.
    /// </summary>
    public interface IMetadataProviderOrchestrator
    {
        List<Book> SearchForNewBook(string title, string author, bool getAllEditions = true);
        List<Book> SearchByIsbn(string isbn);
        List<Book> SearchByAsin(string asin);
        List<Book> SearchByExternalId(string idType, string id);
        List<Author> SearchForNewAuthor(string title);
        List<object> SearchForNewEntity(string title);
        System.Tuple<string, Book, List<AuthorMetadata>> GetBookInfo(string id);
        Author GetAuthorInfo(string id, bool useCache = true);
        HashSet<string> GetChangedAuthors(DateTime startTime);
    }
}
