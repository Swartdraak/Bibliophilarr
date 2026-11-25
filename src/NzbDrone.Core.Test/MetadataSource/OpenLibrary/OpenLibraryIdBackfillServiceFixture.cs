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
        private Mock<ISearchForNewBook> _provider;

        [SetUp]
        public void SetUp()
        {
            var provider = new Mock<IMetadataProvider>();
            provider.SetupGet(x => x.ProviderName).Returns("OpenLibrary");
            provider.SetupGet(x => x.SupportsIsbnLookup).Returns(true);
            _provider = provider.As<ISearchForNewBook>();
            Mocker.GetMock<IMetadataProviderRegistry>().Setup(x => x.GetProviders()).Returns(new[] { provider.Object });
            Mocker.GetMock<ICatalogueIdentityRepository>()
                .Setup(x => x.TryAddSource(It.IsAny<CatalogueEntityKind>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
                .Returns(true);
        }

        private void SetCatalogue(AuthorMetadata owner, Book book, string isbn)
        {
            Mocker.GetMock<IBookService>().Setup(x => x.GetAllBooks()).Returns(new List<Book> { book });
            Mocker.GetMock<IAuthorService>().Setup(x => x.GetAllAuthors())
                .Returns(new List<Author> { new Author { Id = 1, AuthorMetadataId = owner.Id, Metadata = owner } });
            Mocker.GetMock<IAuthorMetadataService>().Setup(x => x.Get(It.IsAny<IEnumerable<int>>())).Returns(new List<AuthorMetadata> { owner });
            Mocker.GetMock<IEditionService>().Setup(x => x.GetEditionsByBook(It.IsAny<List<int>>()))
                .Returns(new List<Edition> { new Edition { BookId = book.Id, Isbn13 = isbn } });
        }

        private static Book SearchResult(string title, string author, string isbn, string workId, string authorId)
        {
            return new Book
            {
                Title = title,
                ForeignBookId = $"openlibrary:work:{workId}",
                AuthorMetadata = new AuthorMetadata { Name = author, ForeignAuthorId = $"openlibrary:author:{authorId}" },
                Editions = new List<Edition> { new Edition { Isbn13 = isbn } }
            };
        }

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

        [TestCase(false)]
        [TestCase(true)]
        public void execute_should_continue_after_null_or_empty_lookup_and_update_remaining_items(bool missingResult)
        {
            const string firstIsbn = "9780141439518";
            const string secondIsbn = "9780140328721";
            var firstOwner = new AuthorMetadata { Id = 10, Name = "First Author", ForeignAuthorId = "hardcover:author:9" };
            var secondOwner = new AuthorMetadata { Id = 11, Name = "Second Author", ForeignAuthorId = "hardcover:author:10" };
            var first = new Book { Id = 20, AuthorMetadataId = firstOwner.Id, Title = "First Work", ForeignBookId = "hardcover:work:19" };
            var second = new Book { Id = 21, AuthorMetadataId = secondOwner.Id, Title = "Second Work", ForeignBookId = "hardcover:work:20" };
            Mocker.GetMock<IBookService>().Setup(x => x.GetAllBooks()).Returns(new List<Book> { first, second });
            Mocker.GetMock<IAuthorService>().Setup(x => x.GetAllAuthors()).Returns(new List<Author>
            {
                new Author { Id = 1, AuthorMetadataId = firstOwner.Id, Metadata = firstOwner },
                new Author { Id = 2, AuthorMetadataId = secondOwner.Id, Metadata = secondOwner },
            });
            Mocker.GetMock<IAuthorMetadataService>().Setup(x => x.Get(It.IsAny<IEnumerable<int>>()))
                .Returns(new List<AuthorMetadata> { firstOwner, secondOwner });
            Mocker.GetMock<IEditionService>().Setup(x => x.GetEditionsByBook(It.IsAny<List<int>>()))
                .Returns(new List<Edition>
                {
                    new Edition { BookId = first.Id, Isbn13 = firstIsbn },
                    new Edition { BookId = second.Id, Isbn13 = secondIsbn },
                });
            _provider.Setup(x => x.SearchByIsbn(firstIsbn)).Returns(missingResult ? null : new List<Book>());
            _provider.Setup(x => x.SearchByIsbn(secondIsbn))
                .Returns(new List<Book> { SearchResult(second.Title, secondOwner.Name, secondIsbn, "OL200W", "OL400A") });

            Subject.Execute(new BackfillOpenLibraryIdsCommand());

            first.OpenLibraryWorkId.Should().BeNull();
            firstOwner.OpenLibraryAuthorId.Should().BeNull();
            second.OpenLibraryWorkId.Should().Be("OL200W");
            secondOwner.OpenLibraryAuthorId.Should().Be("OL400A");
            _provider.Verify(x => x.SearchByIsbn(firstIsbn), Times.Once);
            _provider.Verify(x => x.SearchByIsbn(secondIsbn), Times.Once);
            Mocker.GetMock<IBookService>().Verify(x => x.UpdateMany(It.Is<List<Book>>(books => books.Count == 1 && books[0] == second)), Times.Once);
            Mocker.GetMock<IAuthorMetadataService>().Verify(x => x.UpsertMany(It.Is<List<AuthorMetadata>>(owners => owners.Count == 1 && owners[0] == secondOwner)), Times.Once);
        }

        [TestCase("Correct Work", "Correct Author", "9780141439518", "OL200W", "OL400A", true)]
        [TestCase("Other Work", "Correct Author", "9780141439518", "OL200W", "OL400A", false)]
        [TestCase("Correct Work", "Other Author", "9780141439518", "OL200W", "OL400A", false)]
        [TestCase("Correct Work", "Correct Author", "9780141439525", "OL200W", "OL400A", false)]
        [TestCase("Correct Work", "Correct Author", "9780141439518", "OL200M", "OL400A", false)]
        [TestCase("Correct Work", "Correct Author", "9780141439518", "OL200W", "OLfakeA", false)]
        public void execute_should_only_backfill_corroborated_open_library_ids(string title, string author, string isbn, string workId, string authorId, bool matches)
        {
            var owner = new AuthorMetadata { Id = 10, Name = "Correct Author", ForeignAuthorId = "hardcover:author:9", Overview = "Keep this biography" };
            var book = new Book { Id = 20, AuthorMetadataId = owner.Id, Title = "Correct Work", ForeignBookId = "hardcover:work:19" };
            SetCatalogue(owner, book, "0-14-143951-3");

            _provider.Setup(x => x.SearchByIsbn("9780141439518"))
                .Returns(new List<Book> { SearchResult(title, author, isbn, workId, authorId) });

            Subject.Execute(new BackfillOpenLibraryIdsCommand());

            book.OpenLibraryWorkId.Should().Be(matches ? "OL200W" : null);
            owner.OpenLibraryAuthorId.Should().Be(matches ? "OL400A" : null);
            owner.Overview.Should().Be("Keep this biography");
            Mocker.GetMock<ICatalogueIdentityRepository>().Verify(x => x.TryAddSource(
                CatalogueEntityKind.Work,
                book.Id,
                "OpenLibrary",
                "OL200W",
                "exact-isbn-and-title-and-author"), matches ? Times.Once() : Times.Never());
        }

        [Test]
        public void execute_should_hold_multiple_corroborated_results_and_avoid_name_only_search()
        {
            const string isbn = "9780141439518";
            var owner = new AuthorMetadata { Id = 10, Name = "Correct Author", ForeignAuthorId = "hardcover:author:9" };
            var book = new Book { Id = 20, AuthorMetadataId = owner.Id, Title = "Correct Work", ForeignBookId = "hardcover:work:19" };
            SetCatalogue(owner, book, isbn);

            _provider.Setup(x => x.SearchByIsbn(isbn)).Returns(new List<Book>
            {
                SearchResult(book.Title, owner.Name, isbn, "OL200W", "OL400A"),
                SearchResult(book.Title, owner.Name, isbn, "OL201W", "OL401A")
            });

            Subject.Execute(new BackfillOpenLibraryIdsCommand());

            book.OpenLibraryWorkId.Should().BeNull();
            owner.OpenLibraryAuthorId.Should().BeNull();
            _provider.Verify(x => x.SearchForNewBook(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>()), Times.Never);
        }

        [Test]
        public void execute_should_preserve_verified_ids_when_open_library_proposes_a_conflicting_author()
        {
            const string isbn = "9780141439518";
            var owner = new AuthorMetadata { Id = 10, Name = "Correct Author", ForeignAuthorId = "hardcover:author:9", OpenLibraryAuthorId = "OL401A" };
            var book = new Book { Id = 20, AuthorMetadataId = owner.Id, Title = "Correct Work", ForeignBookId = "hardcover:work:19" };
            SetCatalogue(owner, book, isbn);

            _provider.Setup(x => x.SearchByIsbn(isbn)).Returns(new List<Book> { SearchResult(book.Title, owner.Name, isbn, "OL200W", "OL400A") });

            Subject.Execute(new BackfillOpenLibraryIdsCommand());

            book.OpenLibraryWorkId.Should().BeNull();
            owner.OpenLibraryAuthorId.Should().Be("OL401A");
            Mocker.GetMock<IBookService>().Verify(x => x.UpdateMany(It.IsAny<List<Book>>()), Times.Never);
        }

        [Test]
        public void execute_should_not_write_a_compatibility_id_when_the_source_has_another_owner()
        {
            const string isbn = "9780141439518";
            var owner = new AuthorMetadata { Id = 10, Name = "Correct Author", ForeignAuthorId = "hardcover:author:9" };
            var book = new Book { Id = 20, AuthorMetadataId = owner.Id, Title = "Correct Work", ForeignBookId = "hardcover:work:19" };
            SetCatalogue(owner, book, isbn);

            _provider.Setup(x => x.SearchByIsbn(isbn)).Returns(new List<Book> { SearchResult(book.Title, owner.Name, isbn, "OL200W", "OL400A") });
            Mocker.GetMock<ICatalogueIdentityRepository>()
                .Setup(x => x.TryAddSource(CatalogueEntityKind.Work, book.Id, "OpenLibrary", "OL200W", It.IsAny<string>()))
                .Returns(false);

            Subject.Execute(new BackfillOpenLibraryIdsCommand());

            book.OpenLibraryWorkId.Should().BeNull();
            Mocker.GetMock<IBookService>().Verify(x => x.UpdateMany(It.IsAny<List<Book>>()), Times.Never);
        }

        [Test]
        public void execute_should_hold_one_official_work_claimed_by_two_local_works_and_cache_the_lookup()
        {
            const string isbn = "9780141439518";
            var owner = new AuthorMetadata { Id = 10, Name = "Correct Author", ForeignAuthorId = "hardcover:author:9" };
            var book = new Book { Id = 20, AuthorMetadataId = owner.Id, Title = "Correct Work", ForeignBookId = "hardcover:work:19" };
            var duplicate = new Book { Id = 21, AuthorMetadataId = owner.Id, Title = book.Title, ForeignBookId = "hardcover:work:23" };
            SetCatalogue(owner, book, isbn);
            Mocker.GetMock<IBookService>().Setup(x => x.GetAllBooks()).Returns(new List<Book> { book, duplicate });
            Mocker.GetMock<IEditionService>().Setup(x => x.GetEditionsByBook(It.IsAny<List<int>>())).Returns(new List<Edition>
            {
                new Edition { BookId = book.Id, Isbn13 = isbn },
                new Edition { BookId = duplicate.Id, Isbn13 = "0-14-143951-3" }
            });
            _provider.Setup(x => x.SearchByIsbn(isbn)).Returns(new List<Book> { SearchResult(book.Title, owner.Name, isbn, "OL200W", "OL400A") });

            Subject.Execute(new BackfillOpenLibraryIdsCommand { MaxLookups = 1, BatchSize = 1 });

            book.OpenLibraryWorkId.Should().BeNull();
            duplicate.OpenLibraryWorkId.Should().BeNull();
            owner.OpenLibraryAuthorId.Should().BeNull();
            _provider.Verify(x => x.SearchByIsbn(isbn), Times.Once);
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

            _provider.Verify(x => x.SearchByIsbn(It.IsAny<string>()), Times.Never);

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

            _provider.Verify(x => x.SearchByIsbn(It.IsAny<string>()), Times.Never);

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

        [TestCase("openlibrary:work:OL123M")]
        [TestCase("openlibrary:edition:OL123M")]
        [TestCase("OL123M")]
        public void execute_should_not_promote_an_edition_id_to_work_identity(string foreignId)
        {
            var author = new AuthorMetadata { Id = 10, Name = "Correct Author", ForeignAuthorId = "hardcover:author:9" };
            var book = new Book { Id = 20, AuthorMetadataId = author.Id, Title = "Correct Work", ForeignBookId = foreignId };
            SetCatalogue(author, book, null);

            Subject.Execute(new BackfillOpenLibraryIdsCommand());

            book.OpenLibraryWorkId.Should().BeNull();
            author.OpenLibraryAuthorId.Should().BeNull();
            _provider.Verify(x => x.SearchByIsbn(It.IsAny<string>()), Times.Never);
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

            Subject.Execute(new BackfillOpenLibraryIdsCommand { MaxLookups = 100, BatchSize = 2 });

            Mocker.GetMock<IBookService>()
                .Verify(x => x.UpdateMany(It.IsAny<List<Book>>()), Times.Exactly(2));
        }
    }
}
