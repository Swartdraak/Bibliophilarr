using System.Collections.Generic;
using System.Linq;
using System.Net;
using FluentAssertions;
using FluentAssertions.Execution;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.Books.Events;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Exceptions;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.BooksTests.Repositories
{
    [TestFixture]
    public class BookEditionAtomicPersistenceFixture : DbTest<BookService, Book>
    {
        private const string ClaimedEdition = "openlibrary:edition:OL4011M";
        private const string FirstFreshEdition = "openlibrary:edition:OL4012M";
        private const string DistinctSelectedEdition = "openlibrary:edition:OL4013M";
        private BookRepository _books;
        private EditionRepository _editions;

        [SetUp]
        public void Setup()
        {
            _editions = Mocker.Resolve<EditionRepository>();
            Mocker.SetConstant<IEditionRepository>(_editions);
            _books = Mocker.Resolve<BookRepository>();
            Mocker.SetConstant<IBookRepository>(_books);
            Mocker.SetConstant<IEditionService>(Mocker.Resolve<EditionService>());
        }

        [Test]
        public void should_rollback_the_new_work_and_first_edition_when_the_selected_foreign_edition_is_already_owned()
        {
            var incumbentOwner = InsertAuthorMetadata("OL401A");
            var incumbentWork = _books.Insert(NewWork(incumbentOwner, "OL401W"));
            var incumbent = _editions.Insert(NewEdition(ClaimedEdition, incumbentWork.Id, monitored: true));
            var incumbentFile = Db.Insert(new BookFile
            {
                EditionId = incumbent.Id,
                Path = "/fixture/existing.epub",
                Part = 1,
                Quality = new QualityModel(Quality.EPUB)
            });
            var freshOwner = InsertAuthorMetadata("OL402A");
            var first = NewEdition(FirstFreshEdition, 0, monitored: false);
            var selected = NewEdition(ClaimedEdition, 0, monitored: true);
            selected.TitleSlug = "fixture-conflicting-claim";
            var freshWork = NewWork(freshOwner, "OL402W");
            freshWork.Editions = new List<Edition> { first, selected };

            // Give the first valid edition a chance to insert before the conflicting selected row.
            var failure = Assert.Throws<NzbDroneClientException>(() => Subject.AddBook(freshWork, doRefresh: false));
            failure.StatusCode.Should().Be(HttpStatusCode.Conflict);
            failure.Message.Should().Be("A provider edition is already present in the catalogue.");

            using (new AssertionScope())
            {
                _books.All().Select(book => book.Id).Should().Equal(incumbentWork.Id);
                _books.FindById(freshWork.ForeignBookId).Should().BeNull();
                _editions.All().Select(edition => edition.Id).Should().Equal(incumbent.Id);
                _editions.FindByForeignEditionId(FirstFreshEdition).Should().BeNull();
                var retained = _editions.FindByForeignEditionId(ClaimedEdition);
                retained.Id.Should().Be(incumbent.Id);
                retained.BookId.Should().Be(incumbentWork.Id);
                retained.Monitored.Should().BeTrue();
                retained.IsEbook.Should().BeTrue();
                _books.Get(incumbentWork.Id).AuthorMetadataId.Should().Be(incumbentOwner.Id);
                Db.All<BookFile>().Should().HaveCount(1).And.ContainSingle(file =>
                    file.Id == incumbentFile.Id && file.EditionId == incumbent.Id && file.Path == incumbentFile.Path);
                freshWork.Id.Should().Be(0);
                first.Id.Should().Be(0);
                first.BookId.Should().Be(0);
                first.Monitored.Should().BeFalse();
                selected.Id.Should().Be(0);
                selected.BookId.Should().Be(0);
                selected.Monitored.Should().BeTrue();
            }

            Mocker.GetMock<IEventAggregator>().Verify(events => events.PublishEvent(It.IsAny<BookAddedEvent>()), Times.Never());
        }

        [Test]
        public void should_persist_a_fresh_work_with_both_unique_editions_and_the_selected_monitoring()
        {
            var author = InsertAuthorMetadata("OL402A");
            var work = NewWork(author, "OL402W");
            var first = NewEdition(FirstFreshEdition, 0, monitored: false);
            var selected = NewEdition(DistinctSelectedEdition, 0, monitored: true);
            selected.ManualAdd = true;
            work.Editions = new List<Edition> { first, selected };

            Subject.AddBook(work, doRefresh: false);

            work.Id.Should().BeGreaterThan(0);
            _books.All().Should().ContainSingle(book => book.Id == work.Id && book.AuthorMetadataId == author.Id);
            var saved = _editions.FindByBook(new[] { work.Id });
            saved.Should().HaveCount(2);
            saved.Should().ContainSingle(edition => edition.Id == first.Id && edition.BookId == work.Id && !edition.Monitored);
            saved.Should().ContainSingle(edition => edition.Id == selected.Id && edition.BookId == work.Id && edition.Monitored && edition.ManualAdd);
            Mocker.GetMock<IEventAggregator>().Verify(events => events.PublishEvent(
                It.Is<BookAddedEvent>(message => message.Book.Id == work.Id && !message.DoRefresh)), Times.Once());
        }

        [Test]
        public void should_keep_existing_work_editions_and_selection_on_the_existing_add_path()
        {
            var owner = InsertAuthorMetadata("OL403A");
            var work = _books.Insert(NewWork(owner, "OL403W"));
            var incumbent = _editions.Insert(NewEdition(ClaimedEdition, work.Id, monitored: true));
            var selected = NewEdition(FirstFreshEdition, 0, monitored: true);
            selected.ManualAdd = true;
            incumbent.Monitored = false;
            work.Editions = new List<Edition> { incumbent, selected };

            Subject.AddBook(work, doRefresh: false);

            var saved = _editions.FindByBook(new[] { work.Id });
            saved.Should().HaveCount(2);
            saved.Should().ContainSingle(edition => edition.Id == incumbent.Id && !edition.Monitored);
            saved.Should().ContainSingle(edition => edition.Id == selected.Id && edition.Monitored && edition.ManualAdd);
            _books.Count().Should().Be(1);
        }

        [Test]
        public void should_leave_other_persistence_errors_exceptional_and_rollback_the_new_work()
        {
            var owner = InsertAuthorMetadata("OL404A");
            var work = NewWork(owner, "OL404W");
            var first = NewEdition(FirstFreshEdition, 0, monitored: false);
            var invalid = NewEdition(DistinctSelectedEdition, 0, monitored: true);
            invalid.Title = null;
            work.Editions = new List<Edition> { first, invalid };

            var error = Assert.Catch(() => Subject.AddBook(work, doRefresh: false));

            error.Should().NotBeOfType<NzbDroneClientException>();
            _books.Count().Should().Be(0);
            _editions.Count().Should().Be(0);
            work.Id.Should().Be(0);
            first.Id.Should().Be(0);
            Mocker.GetMock<IEventAggregator>().Verify(events => events.PublishEvent(It.IsAny<BookAddedEvent>()), Times.Never());
        }

        private AuthorMetadata InsertAuthorMetadata(string id)
        {
            return Db.Insert(new AuthorMetadata
            {
                ForeignAuthorId = $"openlibrary:author:{id}",
                Name = $"Fixture {id}",
                OpenLibraryAuthorId = id,
                SortName = $"Fixture {id}",
                NameLastFirst = $"Fixture {id}",
                SortNameLastFirst = $"Fixture {id}",
                TitleSlug = id.ToLowerInvariant()
            });
        }

        private static Book NewWork(AuthorMetadata owner, string id)
        {
            return new Book
            {
                AuthorMetadataId = owner.Id,
                ForeignBookId = $"openlibrary:work:{id}",
                Title = $"Fixture {id}",
                CleanTitle = $"fixture-{id.ToLowerInvariant()}",
                TitleSlug = id.ToLowerInvariant()
            };
        }

        private static Edition NewEdition(string source, int bookId, bool monitored)
        {
            return new Edition
            {
                BookId = bookId,
                ForeignEditionId = source,
                Title = "Fixture edition",
                TitleSlug = source.Replace(':', '-'),
                Format = "Ebook",
                IsEbook = true,
                Monitored = monitored
            };
        }
    }
}
