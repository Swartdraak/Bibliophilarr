using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NzbDrone.Core.Books;

namespace NzbDrone.Core.MetadataSource
{
    /// <summary>
    /// Async-capable book search interface for v2 metadata providers.
    /// Supersedes <see cref="ISearchForNewBook"/> for new provider implementations.
    /// The synchronous <see cref="ISearchForNewBook"/> interface is retained for the
    /// existing BookInfoProxy so that no existing functionality is broken during migration.
    /// </summary>
    public interface ISearchForNewBookV2 : IMetadataProvider
    {
        Task<List<Book>> SearchForNewBookAsync(string title, string author, CancellationToken cancellationToken = default);
        Task<List<Book>> SearchByIsbnAsync(string isbn, CancellationToken cancellationToken = default);
        Task<List<Book>> SearchByAsinAsync(string asin, CancellationToken cancellationToken = default);
        Task<List<Book>> SearchByIdentifierAsync(string identifierType, string identifier, CancellationToken cancellationToken = default);
    }
}
