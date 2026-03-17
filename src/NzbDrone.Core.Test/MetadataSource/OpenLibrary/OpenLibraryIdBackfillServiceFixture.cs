using System.Collections.Generic;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.Books.Commands;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Lifecycle;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.MetadataSource.OpenLibrary;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MetadataSource.OpenLibrary
{
    [TestFixture]
    public class OpenLibraryIdBackfillServiceFixture : CoreTest<OpenLibraryIdBackfillService>
    {
        [Test]
        public void handle_should_queue_backfill_when_open_library_is_enabled()
        {
            Mocker.GetMock<IConfigService>().SetupGet(x => x.EnableOpenLibraryProvider).Returns(true);

            Subject.Handle(new ApplicationStartedEvent());

            Mocker.GetMock<IManageCommandQueue>()
                .Verify(x => x.Push(It.IsAny<BackfillOpenLibraryIdsCommand>(), It.IsAny<CommandPriority>(), It.IsAny<CommandTrigger>()), Times.Once);
        }

        [Test]
        public void handle_should_not_queue_backfill_when_open_library_is_disabled()
        {
            Mocker.GetMock<IConfigService>().SetupGet(x => x.EnableOpenLibraryProvider).Returns(false);

            Subject.Handle(new ApplicationStartedEvent());

            Mocker.GetMock<IManageCommandQueue>()
                .Verify(x => x.Push(It.IsAny<BackfillOpenLibraryIdsCommand>(), It.IsAny<CommandPriority>(), It.IsAny<CommandTrigger>()), Times.Never);
        }

        [Test]
        public void execute_should_be_idempotent_when_openlibrary_ids_already_present()
        {
            var authorMetadata = new AuthorMetadata
            {
                Id = 100,
                ForeignAuthorId = "OL10A",
                OpenLibraryAuthorId = "OL10A",
                Name = "Author"
            };

            var author = new Author
            {
                Id = 10,
                AuthorMetadataId = 100,
                Metadata = authorMetadata
            };

            var book = new Book
            {
                Id = 1,
                AuthorMetadataId = 100,
                ForeignBookId = "OL100W",
                OpenLibraryWorkId = "OL100W"
            };

            Mocker.GetMock<IBookService>().Setup(x => x.GetAllBooks()).Returns(new List<Book> { book });
            Mocker.GetMock<IAuthorService>().Setup(x => x.GetAllAuthors()).Returns(new List<Author> { author });
            Mocker.GetMock<IEditionService>().Setup(x => x.GetEditionsByBook(It.IsAny<List<int>>())).Returns(new List<Edition>());

            var command = new BackfillOpenLibraryIdsCommand { MaxLookups = 5 };

            Subject.Execute(command);
            Subject.Execute(command);

            Mocker.GetMock<IBookService>().Verify(x => x.UpdateMany(It.IsAny<List<Book>>()), Times.Never);
            Mocker.GetMock<IAuthorMetadataService>().Verify(x => x.UpsertMany(It.IsAny<List<AuthorMetadata>>()), Times.Never);
        }

        [Test]
        public void execute_should_continue_after_partial_lookup_failure_and_update_remaining_items()
        {
            var metadataOne = new AuthorMetadata { Id = 100, ForeignAuthorId = "legacy-a", Name = "Author A" };
            var metadataTwo = new AuthorMetadata { Id = 200, ForeignAuthorId = "legacy-b", Name = "Author B" };

            var authorOne = new Author { Id = 1, AuthorMetadataId = 100, Metadata = metadataOne };
            var authorTwo = new Author { Id = 2, AuthorMetadataId = 200, Metadata = metadataTwo };

            var bookOne = new Book { Id = 11, AuthorMetadataId = 100, ForeignBookId = "legacy-1" };
            var bookTwo = new Book { Id = 22, AuthorMetadataId = 200, ForeignBookId = "legacy-2" };

            var editions = new List<Edition>
            {
                new Edition { BookId = 11, Isbn13 = "1111111111111" },
                new Edition { BookId = 22, Isbn13 = "2222222222222" }
            };

            Mocker.GetMock<IBookService>().Setup(x => x.GetAllBooks()).Returns(new List<Book> { bookOne, bookTwo });
            Mocker.GetMock<IAuthorService>().Setup(x => x.GetAllAuthors()).Returns(new List<Author> { authorOne, authorTwo });
            Mocker.GetMock<IEditionService>().Setup(x => x.GetEditionsByBook(It.IsAny<List<int>>())).Returns(editions);

            Mocker.GetMock<IMetadataProviderOrchestrator>()
                .Setup(x => x.SearchByIsbn("1111111111111"))
                .Returns(new List<Book>());

            Mocker.GetMock<IMetadataProviderOrchestrator>()
                .Setup(x => x.SearchByIsbn("2222222222222"))
                .Returns(new List<Book>
                {
                    new Book { OpenLibraryWorkId = "OL222W", ForeignBookId = "legacy-2" }
                });

            Mocker.GetMock<IMetadataProviderOrchestrator>()
                .Setup(x => x.SearchForNewAuthor("Author A"))
                .Returns(new List<Author>());

            Mocker.GetMock<IMetadataProviderOrchestrator>()
                .Setup(x => x.SearchForNewAuthor("Author B"))
                .Returns(new List<Author>
                {
                    new Author { Metadata = new AuthorMetadata { ForeignAuthorId = "OL222A", Name = "Author B" } }
                });

            Subject.Execute(new BackfillOpenLibraryIdsCommand { MaxLookups = 10 });

            Mocker.GetMock<IBookService>()
                .Verify(x => x.UpdateMany(It.Is<List<Book>>(list => list.Count == 1 && list[0].OpenLibraryWorkId == "OL222W")), Times.Once);

            Mocker.GetMock<IAuthorMetadataService>()
                .Verify(x => x.UpsertMany(It.Is<List<AuthorMetadata>>(list => list.Count == 1 && list[0].OpenLibraryAuthorId == "OL222A")), Times.Once);

            metadataOne.OpenLibraryAuthorId.Should().BeNull();
            metadataTwo.OpenLibraryAuthorId.Should().Be("OL222A");
        }
    }
}
