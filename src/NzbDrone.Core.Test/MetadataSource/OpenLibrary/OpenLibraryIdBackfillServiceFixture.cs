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
            Mocker.GetMock<IAuthorMetadataService>().Setup(x => x.Get(It.IsAny<IEnumerable<int>>())).Returns(new List<AuthorMetadata> { authorMetadata });
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
            Mocker.GetMock<IAuthorMetadataService>().Setup(x => x.Get(It.IsAny<IEnumerable<int>>())).Returns(new List<AuthorMetadata> { metadataOne, metadataTwo });
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
                    new Author { Metadata = new AuthorMetadata { ForeignAuthorId = "openlibrary:author:OL222A", OpenLibraryAuthorId = "OL222A", Name = "Author B" } }
                });

            Subject.Execute(new BackfillOpenLibraryIdsCommand { MaxLookups = 10 });

            Mocker.GetMock<IBookService>()
                .Verify(x => x.UpdateMany(It.Is<List<Book>>(list => list.Count == 1 && list[0].OpenLibraryWorkId == "OL222W")), Times.Once);

            Mocker.GetMock<IAuthorMetadataService>()
                .Verify(x => x.UpsertMany(It.Is<List<AuthorMetadata>>(list => list.Count == 1 && list[0].OpenLibraryAuthorId == "OL222A")), Times.Once);

            metadataOne.OpenLibraryAuthorId.Should().BeNull();
            metadataTwo.OpenLibraryAuthorId.Should().Be("OL222A");
        }

        [Test]
        public void execute_should_backfill_bare_open_library_author_id_from_prefixed_foreign_author_id_without_lookup()
        {
            var authorMetadata = new AuthorMetadata
            {
                Id = 100,
                ForeignAuthorId = "openlibrary:author:OL10A",
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
            Mocker.GetMock<IAuthorMetadataService>().Setup(x => x.Get(It.IsAny<IEnumerable<int>>())).Returns(new List<AuthorMetadata> { authorMetadata });
            Mocker.GetMock<IEditionService>().Setup(x => x.GetEditionsByBook(It.IsAny<List<int>>())).Returns(new List<Edition>());

            Subject.Execute(new BackfillOpenLibraryIdsCommand { MaxLookups = 0 });

            Mocker.GetMock<IMetadataProviderOrchestrator>()
                .Verify(x => x.SearchForNewAuthor(It.IsAny<string>()), Times.Never);

            Mocker.GetMock<IAuthorMetadataService>()
                .Verify(x => x.UpsertMany(It.Is<List<AuthorMetadata>>(list =>
                    list.Count == 1 &&
                    list[0].Id == 100 &&
                    list[0].OpenLibraryAuthorId == "OL10A")), Times.Once);

            authorMetadata.OpenLibraryAuthorId.Should().Be("OL10A");
        }

        [Test]
        public void execute_should_backfill_open_library_work_id_from_prefixed_foreign_book_id_without_lookup()
        {
            var authorMetadata = new AuthorMetadata
            {
                Id = 100,
                ForeignAuthorId = "openlibrary:author:OL10A",
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
                ForeignBookId = "openlibrary:work:OL100W"
            };

            Mocker.GetMock<IBookService>().Setup(x => x.GetAllBooks()).Returns(new List<Book> { book });
            Mocker.GetMock<IAuthorService>().Setup(x => x.GetAllAuthors()).Returns(new List<Author> { author });
            Mocker.GetMock<IAuthorMetadataService>().Setup(x => x.Get(It.IsAny<IEnumerable<int>>())).Returns(new List<AuthorMetadata> { authorMetadata });
            Mocker.GetMock<IEditionService>().Setup(x => x.GetEditionsByBook(It.IsAny<List<int>>())).Returns(new List<Edition>());

            Subject.Execute(new BackfillOpenLibraryIdsCommand { MaxLookups = 0 });

            Mocker.GetMock<IMetadataProviderOrchestrator>()
                .Verify(x => x.SearchByIsbn(It.IsAny<string>()), Times.Never);

            Mocker.GetMock<IBookService>()
                .Verify(x => x.UpdateMany(It.Is<List<Book>>(list =>
                    list.Count == 1 &&
                    list[0].Id == 1 &&
                    list[0].OpenLibraryWorkId == "OL100W")), Times.Once);

            Mocker.GetMock<IAuthorMetadataService>()
                .Verify(x => x.UpsertMany(It.Is<List<AuthorMetadata>>(list =>
                    list.Count == 1 &&
                    list[0].Id == 100 &&
                    list[0].OpenLibraryAuthorId == "OL10A")), Times.Once);

            book.OpenLibraryWorkId.Should().Be("OL100W");
            authorMetadata.OpenLibraryAuthorId.Should().Be("OL10A");
        }

        [Test]
        public void execute_should_resolve_open_library_edition_foreign_book_id_via_external_id_lookup()
        {
            var authorMetadata = new AuthorMetadata
            {
                Id = 414,
                ForeignAuthorId = "openlibrary:author:OL29303A",
                OpenLibraryAuthorId = "OL29303A",
                Name = "Dante Alighieri"
            };

            var author = new Author
            {
                Id = 41,
                AuthorMetadataId = 414,
                Metadata = authorMetadata
            };

            var book = new Book
            {
                Id = 1311,
                AuthorMetadataId = 414,
                ForeignBookId = "openlibrary:work:OL8547083M"
            };

            Mocker.GetMock<IBookService>().Setup(x => x.GetAllBooks()).Returns(new List<Book> { book });
            Mocker.GetMock<IAuthorService>().Setup(x => x.GetAllAuthors()).Returns(new List<Author> { author });
            Mocker.GetMock<IAuthorMetadataService>().Setup(x => x.Get(It.IsAny<IEnumerable<int>>())).Returns(new List<AuthorMetadata> { authorMetadata });
            Mocker.GetMock<IEditionService>().Setup(x => x.GetEditionsByBook(It.IsAny<List<int>>())).Returns(new List<Edition>());

            Mocker.GetMock<IMetadataProviderOrchestrator>()
                .Setup(x => x.SearchByExternalId("olid", "OL8547083M"))
                .Returns(new List<Book>
                {
                    new Book { ForeignBookId = "openlibrary:work:OL8547083W", OpenLibraryWorkId = "OL8547083W" }
                });

            Subject.Execute(new BackfillOpenLibraryIdsCommand { MaxLookups = 10 });

            Mocker.GetMock<IMetadataProviderOrchestrator>()
                .Verify(x => x.SearchByExternalId(It.IsAny<string>(), It.IsAny<string>()), Times.Never);

            Mocker.GetMock<IBookService>()
                .Verify(x => x.UpdateMany(It.Is<List<Book>>(list =>
                    list.Count == 1 &&
                    list[0].Id == 1311 &&
                    list[0].OpenLibraryWorkId == "OL8547083M")), Times.Once);

            book.OpenLibraryWorkId.Should().Be("OL8547083M");
        }

        [Test]
        public void execute_should_fallback_to_provider_book_lookup_when_external_id_lookup_is_empty()
        {
            var authorMetadata = new AuthorMetadata
            {
                Id = 414,
                ForeignAuthorId = "openlibrary:author:OL29303A",
                OpenLibraryAuthorId = "OL29303A",
                Name = "Dante Alighieri"
            };

            var author = new Author
            {
                Id = 41,
                AuthorMetadataId = 414,
                Metadata = authorMetadata
            };

            var book = new Book
            {
                Id = 1464,
                AuthorMetadataId = 414,
                ForeignBookId = "openlibrary:work:OL9205704M"
            };

            Mocker.GetMock<IBookService>().Setup(x => x.GetAllBooks()).Returns(new List<Book> { book });
            Mocker.GetMock<IAuthorService>().Setup(x => x.GetAllAuthors()).Returns(new List<Author> { author });
            Mocker.GetMock<IAuthorMetadataService>().Setup(x => x.Get(It.IsAny<IEnumerable<int>>())).Returns(new List<AuthorMetadata> { authorMetadata });
            Mocker.GetMock<IEditionService>().Setup(x => x.GetEditionsByBook(It.IsAny<List<int>>())).Returns(new List<Edition>());

            Mocker.GetMock<IMetadataProviderOrchestrator>()
                .Setup(x => x.SearchByExternalId("olid", "OL9205704M"))
                .Returns(new List<Book>());

            Mocker.GetMock<IMetadataProviderOrchestrator>()
                .Setup(x => x.GetBookInfo("OL9205704M"))
                .Returns(System.Tuple.Create(
                    "openlibrary:author:OL27695A",
                    new Book { ForeignBookId = "openlibrary:work:OL9205704W", OpenLibraryWorkId = "OL9205704W" },
                    new List<AuthorMetadata>()));

            Subject.Execute(new BackfillOpenLibraryIdsCommand { MaxLookups = 10 });

            Mocker.GetMock<IMetadataProviderOrchestrator>()
                .Verify(x => x.GetBookInfo(It.IsAny<string>()), Times.Never);

            book.OpenLibraryWorkId.Should().Be("OL9205704M");
        }

        [Test]
        public void execute_should_accept_m_suffixed_open_library_work_ids_returned_by_external_lookup()
        {
            var authorMetadata = new AuthorMetadata
            {
                Id = 414,
                ForeignAuthorId = "openlibrary:author:OL29303A",
                OpenLibraryAuthorId = "OL29303A",
                Name = "Dante Alighieri"
            };

            var author = new Author
            {
                Id = 41,
                AuthorMetadataId = 414,
                Metadata = authorMetadata
            };

            var book = new Book
            {
                Id = 1497,
                AuthorMetadataId = 414,
                ForeignBookId = "openlibrary:work:OL13335313M"
            };

            Mocker.GetMock<IBookService>().Setup(x => x.GetAllBooks()).Returns(new List<Book> { book });
            Mocker.GetMock<IAuthorService>().Setup(x => x.GetAllAuthors()).Returns(new List<Author> { author });
            Mocker.GetMock<IAuthorMetadataService>().Setup(x => x.Get(It.IsAny<IEnumerable<int>>())).Returns(new List<AuthorMetadata> { authorMetadata });
            Mocker.GetMock<IEditionService>().Setup(x => x.GetEditionsByBook(It.IsAny<List<int>>())).Returns(new List<Edition>());

            Mocker.GetMock<IMetadataProviderOrchestrator>()
                .Setup(x => x.SearchByExternalId("olid", "OL13335313M"))
                .Returns(new List<Book>
                {
                    new Book { ForeignBookId = "openlibrary:work:OL13335313M", OpenLibraryWorkId = "OL13335313M" }
                });

            Subject.Execute(new BackfillOpenLibraryIdsCommand { MaxLookups = 10 });

            Mocker.GetMock<IMetadataProviderOrchestrator>()
                .Verify(x => x.GetBookInfo(It.IsAny<string>()), Times.Never);

            book.OpenLibraryWorkId.Should().Be("OL13335313M");
        }

        [Test]
        public void execute_should_write_books_in_batches_when_batch_size_is_limited()
        {
            var authorMetadata = new AuthorMetadata
            {
                Id = 100,
                ForeignAuthorId = "openlibrary:author:OL10A",
                OpenLibraryAuthorId = "OL10A",
                Name = "Author"
            };

            var authors = new List<Author>
            {
                new Author { Id = 1, AuthorMetadataId = 100, Metadata = authorMetadata }
            };

            var books = new List<Book>
            {
                new Book { Id = 1, AuthorMetadataId = 100, ForeignBookId = "openlibrary:work:OL1W" },
                new Book { Id = 2, AuthorMetadataId = 100, ForeignBookId = "openlibrary:work:OL2W" },
                new Book { Id = 3, AuthorMetadataId = 100, ForeignBookId = "openlibrary:work:OL3W" }
            };

            Mocker.GetMock<IBookService>().Setup(x => x.GetAllBooks()).Returns(books);
            Mocker.GetMock<IAuthorService>().Setup(x => x.GetAllAuthors()).Returns(authors);
            Mocker.GetMock<IAuthorMetadataService>().Setup(x => x.Get(It.IsAny<IEnumerable<int>>())).Returns(new List<AuthorMetadata> { authorMetadata });
            Mocker.GetMock<IEditionService>().Setup(x => x.GetEditionsByBook(It.IsAny<List<int>>())).Returns(new List<Edition>());

            Subject.Execute(new BackfillOpenLibraryIdsCommand { MaxLookups = 0, BatchSize = 2 });

            Mocker.GetMock<IBookService>()
                .Verify(x => x.UpdateMany(It.IsAny<List<Book>>()), Times.Exactly(2));
        }
    }
}
