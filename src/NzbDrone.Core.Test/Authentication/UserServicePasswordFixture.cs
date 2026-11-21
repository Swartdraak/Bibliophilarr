using System;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Authentication;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Authentication
{
    [TestFixture]
    public class UserServicePasswordFixture : DbTest<UserRepository, User>
    {
        private const string Username = "fixture-account";
        private const string LegacyPassword = "Old disposable fixture passphrase.";
        private const string LegacyHash = "fe6700fa6e675534bf773c55e33bc23c8c8232d54d30021eb05e2b685b306add";
        private const string ReplacementPassword = "New disposable fixture passphrase.";

        [Test]
        public void should_salt_new_passwords_independently()
        {
            var service = new UserService(Subject);
            service.Add(Username, ReplacementPassword);
            service.Add("another-account", ReplacementPassword);

            var first = Subject.FindUser(Username);
            var second = Subject.FindUser("another-account");

            Assert.That(first.Password, Is.Not.EqualTo(second.Password));
            Assert.That(service.FindUser(Username, ReplacementPassword)?.Id, Is.EqualTo(first.Id));
            Assert.That(service.FindUser("another-account", ReplacementPassword)?.Id, Is.EqualTo(second.Id));
        }

        [Test]
        public void should_upgrade_legacy_hash_only_after_successful_login()
        {
            var service = new UserService(Subject);
            var legacy = InsertLegacyAccount();

            Assert.That(service.FindUser(Username, ReplacementPassword), Is.Null);
            Assert.That(Subject.FindUser(Username).Password, Is.EqualTo(LegacyHash));

            var loggedIn = service.FindUser(Username.ToUpperInvariant(), LegacyPassword);
            Assert.That(loggedIn?.Identifier, Is.EqualTo(legacy.Identifier));
            var upgraded = Subject.FindUser(Username).Password;
            Assert.That(upgraded, Is.Not.EqualTo(LegacyHash));
            Assert.That(UserPasswordHasher.Verify(upgraded, LegacyPassword, out var needsUpgrade), Is.True);
            Assert.That(needsUpgrade, Is.False);
            Assert.That(service.FindUser(Username, LegacyPassword)?.Password, Is.EqualTo(upgraded));
            Assert.That(Subject.FindUser(Username).Password, Is.EqualTo(upgraded));
        }

        [Test]
        public void should_reset_password_and_preserve_account_identity()
        {
            var service = new UserService(Subject);
            var legacy = InsertLegacyAccount();

            service.Upsert("NEW-FIXTURE-NAME", ReplacementPassword);

            var current = Subject.FindUser("new-fixture-name");
            Assert.That(current.Identifier, Is.EqualTo(legacy.Identifier));
            Assert.That(service.FindUser(Username, LegacyPassword), Is.Null);
            Assert.That(service.FindUser("new-fixture-name", LegacyPassword), Is.Null);
            Assert.That(service.FindUser("new-fixture-name", ReplacementPassword)?.Identifier, Is.EqualTo(legacy.Identifier));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void should_compare_both_old_hash_and_username_when_upgrading(bool rename)
        {
            var service = new UserService(Subject);
            var legacy = InsertLegacyAccount();
            var pendingUpgrade = Subject.FindUser(Username);
            var winningName = rename ? "replacement-name" : Username;
            var winningHash = rename ? LegacyHash : UserPasswordHasher.Hash(ReplacementPassword);
            Subject.Update(new User
            {
                Id = legacy.Id,
                Identifier = legacy.Identifier,
                Username = winningName,
                Password = winningHash
            });

            Assert.That(Subject.TryUpdatePasswordHash(pendingUpgrade, UserPasswordHasher.Hash(LegacyPassword)), Is.False);
            Assert.That(Subject.FindUser(winningName).Password, Is.EqualTo(winningHash));
            if (!rename)
            {
                Assert.That(service.FindUser(Username, ReplacementPassword)?.Id, Is.EqualTo(legacy.Id));
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void should_recheck_current_credentials_after_concurrent_update(bool samePassword)
        {
            var before = InsertLegacyAccount();
            var after = new User
            {
                Id = before.Id,
                Identifier = before.Identifier,
                Username = before.Username,
                Password = UserPasswordHasher.Hash(samePassword ? LegacyPassword : ReplacementPassword)
            };
            var repository = new Mock<IUserRepository>();
            repository.SetupSequence(repo => repo.FindUser(Username)).Returns(before).Returns(after);
            repository.Setup(repo => repo.TryUpdatePasswordHash(before, It.IsAny<string>())).Returns(false);
            var service = new UserService(repository.Object);

            var result = service.FindUser(Username, LegacyPassword);

            if (samePassword)
            {
                Assert.That(result, Is.SameAs(after));
            }
            else
            {
                Assert.That(result, Is.Null);
            }

            repository.Verify(repo => repo.FindUser(Username), Times.Exactly(2));
            repository.Verify(repo => repo.TryUpdatePasswordHash(before, It.IsAny<string>()), Times.Once);
        }

        [Test]
        public void should_reject_malformed_hashes()
        {
            var service = new UserService(Subject);
            var user = InsertLegacyAccount();
            user.Password = "pbkdf2-sha512-v1$" + new string('!', 64);
            Subject.Update(user);

            Assert.That(service.FindUser(Username, LegacyPassword), Is.Null);
            Assert.That(Subject.FindUser(Username).Password, Is.EqualTo(user.Password));
        }

        private User InsertLegacyAccount()
        {
            return Subject.Insert(new User
            {
                Identifier = Guid.NewGuid(),
                Username = Username,
                Password = LegacyHash
            });
        }
    }
}
