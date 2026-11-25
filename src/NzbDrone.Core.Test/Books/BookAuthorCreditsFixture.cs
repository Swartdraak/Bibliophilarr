using System.Collections.Generic;
using Dapper;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.BooksTests
{
    [TestFixture]
    public class BookAuthorCreditsFixture : DbTest<BookRepository, Book>
    {
        [Test]
        public void should_show_the_same_work_under_a_credited_author_without_transferring_ownership()
        {
            var owner = AddAuthor(100);
            var credited = AddAuthor(200);
            var work = AddWork(owner.Id, 300);
            var ids = new List<string> { work.ForeignBookId, "hardcover:work:999" };

            Subject.RecordHardcoverAuthorCredits(credited.Id, 200, ids);
            Subject.RecordHardcoverAuthorCredits(credited.Id, 200, ids);

            using var connection = Mocker.Resolve<IMainDatabase>().OpenConnection();
            connection.ExecuteScalar<int>("SELECT COUNT(*) FROM \"BookAuthorCredits\"").Should().Be(1);
            var shared = Subject.GetSharedBooksByAuthorMetadataId(credited.Id).Should().ContainSingle().Which;
            shared.Id.Should().Be(work.Id);
            shared.AuthorMetadataId.Should().Be(owner.Id);
            Subject.GetBooksByAuthorMetadataId(credited.Id).Should().BeEmpty();
            Subject.GetBooksByAuthorMetadataId(owner.Id).Should().ContainSingle();
        }

        [Test]
        public void should_not_record_a_credit_for_a_different_authority_or_an_owned_work()
        {
            var owner = AddAuthor(100);
            var other = AddAuthor(200);
            var work = AddWork(owner.Id, 300);
            var ids = new List<string> { work.ForeignBookId };

            Subject.RecordHardcoverAuthorCredits(owner.Id, 100, ids);
            Subject.RecordHardcoverAuthorCredits(other.Id, 100, ids);

            using var connection = Mocker.Resolve<IMainDatabase>().OpenConnection();
            connection.ExecuteScalar<int>("SELECT COUNT(*) FROM \"BookAuthorCredits\"").Should().Be(0);
            Subject.GetSharedBooksByAuthorMetadataId(owner.Id).Should().BeEmpty();
            Subject.GetSharedBooksByAuthorMetadataId(other.Id).Should().BeEmpty();
        }

        private AuthorMetadata AddAuthor(int providerId)
        {
            return Db.Insert(new AuthorMetadata
            {
                ForeignAuthorId = $"hardcover:author:{providerId}",
                TitleSlug = $"author-{providerId}",
                Name = "Fixture Author",
                SortName = "fixture author",
                NameLastFirst = "Author, Fixture",
                SortNameLastFirst = "author, fixture"
            });
        }

        private Book AddWork(int owner, int providerId)
        {
            return Storage.Insert(new Book
            {
                AuthorMetadataId = owner,
                ForeignBookId = $"hardcover:work:{providerId}",
                TitleSlug = $"work-{providerId}",
                Title = "Fixture Work",
                CleanTitle = "fixturework"
            });
        }
    }
}
