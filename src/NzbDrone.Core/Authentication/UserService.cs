using System;
using NzbDrone.Common.Extensions;

namespace NzbDrone.Core.Authentication
{
    public interface IUserService
    {
        User Add(string username, string password);
        User Update(User user);
        User Upsert(string username, string password);
        User FindUser();
        User FindUser(string username, string password);
        User FindUser(Guid identifier);
    }

    public class UserService : IUserService
    {
        private readonly IUserRepository _repo;

        public UserService(IUserRepository repo)
        {
            _repo = repo;
        }

        public User Add(string username, string password)
        {
            return _repo.Insert(new User
            {
                Identifier = Guid.NewGuid(),
                Username = username.ToLowerInvariant(),
                Password = UserPasswordHasher.Hash(password)
            });
        }

        public User Update(User user)
        {
            return _repo.Update(user);
        }

        public User Upsert(string username, string password)
        {
            var user = FindUser();

            if (user == null)
            {
                return Add(username, password);
            }

            user.Password = UserPasswordHasher.Hash(password);
            user.Username = username.ToLowerInvariant();

            return Update(user);
        }

        public User FindUser()
        {
            return _repo.SingleOrDefault();
        }

        public User FindUser(string username, string password)
        {
            if (username.IsNullOrWhiteSpace() || password.IsNullOrWhiteSpace())
            {
                return null;
            }

            var normalizedUsername = username.ToLowerInvariant();
            var user = _repo.FindUser(normalizedUsername);

            if (user == null || !UserPasswordHasher.Verify(user.Password, password, out var needsUpgrade))
            {
                return null;
            }

            if (!needsUpgrade)
            {
                return user;
            }

            var upgradedHash = UserPasswordHasher.Hash(password);
            if (_repo.TryUpdatePasswordHash(user, upgradedHash))
            {
                user.Password = upgradedHash;
                return user;
            }

            // Another request changed these credentials before the upgrade could be saved.
            var current = _repo.FindUser(normalizedUsername);
            return current != null && UserPasswordHasher.Verify(current.Password, password, out _) ? current : null;
        }

        public User FindUser(Guid identifier)
        {
            return _repo.FindUser(identifier);
        }
    }
}
