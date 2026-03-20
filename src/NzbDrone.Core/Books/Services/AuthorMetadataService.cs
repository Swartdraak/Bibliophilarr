using System.Collections.Generic;
using System.Linq;

namespace NzbDrone.Core.Books
{
    public interface IAuthorMetadataService
    {
        List<AuthorMetadata> Get(IEnumerable<int> ids);
        bool Upsert(AuthorMetadata author);
        bool UpsertMany(List<AuthorMetadata> authors);
    }

    public class AuthorMetadataService : IAuthorMetadataService
    {
        private readonly IAuthorMetadataRepository _authorMetadataRepository;

        public AuthorMetadataService(IAuthorMetadataRepository authorMetadataRepository)
        {
            _authorMetadataRepository = authorMetadataRepository;
        }

        public List<AuthorMetadata> Get(IEnumerable<int> ids)
        {
            return _authorMetadataRepository.Get(ids).ToList();
        }

        public bool Upsert(AuthorMetadata author)
        {
            return _authorMetadataRepository.UpsertMany(new List<AuthorMetadata> { author });
        }

        public bool UpsertMany(List<AuthorMetadata> authors)
        {
            return _authorMetadataRepository.UpsertMany(authors);
        }
    }
}
