using System.Threading;
using System.Threading.Tasks;
using NzbDrone.Core.Books;

namespace NzbDrone.Core.MetadataSource
{
    /// <summary>
    /// Async-capable author-info interface for v2 metadata providers.
    /// Supersedes <see cref="IProvideAuthorInfo"/> for new provider implementations.
    /// </summary>
    public interface IProvideAuthorInfoV2 : IMetadataProvider
    {
        Task<Author> GetAuthorInfoAsync(string providerId, CancellationToken cancellationToken = default);
        Task<Author> GetAuthorByNameAsync(string name, CancellationToken cancellationToken = default);
    }
}
