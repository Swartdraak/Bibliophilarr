using System.Collections.Generic;
using NzbDrone.Core.Books;

namespace NzbDrone.Core.MetadataSource
{
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
    }
}
