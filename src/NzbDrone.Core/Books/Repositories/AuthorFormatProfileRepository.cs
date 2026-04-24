using System.Collections.Generic;
using System.Linq;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.Books
{
    public interface IAuthorFormatProfileRepository : IBasicRepository<AuthorFormatProfile>
    {
        List<AuthorFormatProfile> GetByAuthorId(int authorId);
        AuthorFormatProfile GetByAuthorIdAndFormat(int authorId, FormatType formatType);
        void DeleteByAuthorId(int authorId);
    }

    public class AuthorFormatProfileRepository : BasicRepository<AuthorFormatProfile>, IAuthorFormatProfileRepository
    {
        public AuthorFormatProfileRepository(IMainDatabase database, IEventAggregator eventAggregator)
            : base(database, eventAggregator)
        {
        }

        public List<AuthorFormatProfile> GetByAuthorId(int authorId)
        {
            return Query(x => x.AuthorId == authorId);
        }

        public AuthorFormatProfile GetByAuthorIdAndFormat(int authorId, FormatType formatType)
        {
            return Query(x => x.AuthorId == authorId && x.FormatType == formatType).SingleOrDefault();
        }

        public void DeleteByAuthorId(int authorId)
        {
            Delete(x => x.AuthorId == authorId);
        }
    }
}
