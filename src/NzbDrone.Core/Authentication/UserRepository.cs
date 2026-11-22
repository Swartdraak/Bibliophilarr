using System;
using System.Linq;
using Dapper;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.Authentication
{
    public interface IUserRepository : IBasicRepository<User>
    {
        User FindUser(string username);
        User FindUser(Guid identifier);
        bool TryUpdatePasswordHash(User user, string newHash);
    }

    public class UserRepository : BasicRepository<User>, IUserRepository
    {
        public UserRepository(IMainDatabase database, IEventAggregator eventAggregator)
            : base(database, eventAggregator)
        {
        }

        public User FindUser(string username)
        {
            return Query(x => x.Username == username).SingleOrDefault();
        }

        public User FindUser(Guid identifier)
        {
            return Query(x => x.Identifier == identifier).SingleOrDefault();
        }

        public bool TryUpdatePasswordHash(User user, string newHash)
        {
            using var conn = _database.OpenConnection();
            return conn.Execute(
                $"""
                UPDATE "{_table}" SET "Password" = @NewHash
                WHERE "Id" = @Id AND "Username" = @Username AND "Password" = @OldHash
                """,
                new { user.Id, user.Username, OldHash = user.Password, NewHash = newHash }) == 1;
        }
    }
}
