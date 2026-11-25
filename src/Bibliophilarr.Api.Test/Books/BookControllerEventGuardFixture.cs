using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Bibliophilarr.Api.V1.Books;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.AuthorStats;
using NzbDrone.Core.Books;
using NzbDrone.Core.DecisionEngine.Specifications;
using NzbDrone.Core.MediaCover;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.Events;
using NzbDrone.Core.Profiles.Metadata;
using NzbDrone.Core.Profiles.Qualities;
using NzbDrone.Core.Validation;
using NzbDrone.SignalR;

namespace NzbDrone.Api.Test.Books
{
    [TestFixture]
    public class BookControllerEventGuardFixture
    {
        [Test]
        public void handle_book_file_deleted_should_ignore_missing_payload_without_throwing()
        {
            var controller = CreateController(Mock.Of<IBookService>(), Mock.Of<IBroadcastSignalRMessage>());

            Assert.DoesNotThrow(() => controller.Handle(new BookFileDeletedEvent(null, DeleteMediaFileReason.Manual)));
        }

        [Test]
        public void handle_book_imported_should_fetch_the_persisted_projection_before_broadcasting()
        {
            var author = new Author { Id = 5, Metadata = new AuthorMetadata { Name = "Fixture Author" } };
            var eventBook = new Book { Id = 12, Author = author };
            var bookService = new Mock<IBookService>();
            bookService.Setup(service => service.GetBook(12)).Returns(new Book { Id = 12, Author = author });
            var broadcaster = new Mock<IBroadcastSignalRMessage>();
            broadcaster.SetupGet(service => service.IsConnected).Returns(true);
            var controller = CreateController(bookService.Object, broadcaster.Object);

            controller.Handle(new BookImportedEvent(author, eventBook, new List<BookFile>(), new List<BookFile>(), false, null));

            bookService.Verify(service => service.GetBook(12), Times.Once);
            broadcaster.Verify(service => service.BroadcastMessage(It.IsAny<SignalRMessage>()), Times.Once);
        }

        [Test]
        public async Task author_bibliography_includes_shared_works_only_when_requested()
        {
            var credited = new Author { Id = 5, AuthorMetadataId = 50, Name = "Credited" };
            var owner = new Author { Id = 6, AuthorMetadataId = 60, Name = "Owner" };
            var authored = new Book { Id = 10, AuthorMetadataId = credited.AuthorMetadataId, Author = credited };
            var shared = new Book
            {
                Id = 11,
                AuthorMetadataId = owner.AuthorMetadataId,
                Author = owner,
                Editions = new List<Edition> { new Edition { Id = 12, BookId = 11, Format = "Audiobook", Monitored = true } }
            };
            var authors = new Mock<IAuthorService>();
            authors.Setup(service => service.GetAuthor(credited.Id)).Returns(credited);
            var books = new Mock<IBookService>();
            books.Setup(service => service.GetBooksByAuthor(credited.Id)).Returns(new List<Book> { authored });
            books.Setup(service => service.GetSharedBooksByAuthorMetadataId(credited.AuthorMetadataId)).Returns(new List<Book> { shared });
            var editions = new Mock<IEditionService>();
            editions.Setup(service => service.GetEditionsByAuthor(credited.Id)).Returns(new List<Edition>());
            var controller = CreateController(books.Object, Mock.Of<IBroadcastSignalRMessage>(), authors.Object, editions.Object);

            var owned = await controller.GetBooks(credited.Id, new List<int>(), null);
            var bibliography = await controller.GetBooks(credited.Id, new List<int>(), null, includeShared: true);

            owned.Select(book => book.Id).Should().Equal(authored.Id);
            bibliography.Select(book => book.Id).Should().Equal(authored.Id, shared.Id);
            bibliography.Single(book => book.Id == shared.Id).AuthorId.Should().Be(owner.Id);
            bibliography.Single(book => book.Id == shared.Id).Editions.Should().ContainSingle().Which.Id.Should().Be(12);
            books.Verify(service => service.GetSharedBooksByAuthorMetadataId(credited.AuthorMetadataId), Times.Once());
        }

        [Test]
        public async Task book_list_should_show_saved_same_work_art_without_changing_edition_or_format_tracking()
        {
            const string shelfCover = "https://covers.example/shelf.jpg";
            const string workCover = "https://covers.example/work.jpg";
            var author = new Author { Id = 5, AuthorMetadataId = 50, Name = "Fixture Author" };
            var imported = new Book { Id = 10, AuthorMetadataId = author.AuthorMetadataId };
            var withCover = new Book { Id = 11, AuthorMetadataId = author.AuthorMetadataId };
            var outsidePage = new Book { Id = 12, AuthorMetadataId = author.AuthorMetadataId };
            var audiobook = new Edition { Id = 20, BookId = imported.Id, Format = "Audiobook", Monitored = true };
            var alternate = new Edition
            {
                Id = 21,
                BookId = imported.Id,
                Format = "Ebook",
                IsEbook = true,
                Images = new List<MediaCover> { new MediaCover(MediaCoverTypes.Cover, workCover) }
            };
            var ebook = new Edition
            {
                Id = 22,
                BookId = withCover.Id,
                Format = "Ebook",
                IsEbook = true,
                Monitored = true,
                Images = new List<MediaCover> { new MediaCover(MediaCoverTypes.Cover, shelfCover) }
            };
            var authors = new Mock<IAuthorService>();
            authors.Setup(service => service.GetAllAuthors()).Returns(new List<Author> { author });
            var books = new Mock<IBookService>();
            books.Setup(service => service.GetAllBooks()).Returns(new List<Book> { imported, withCover, outsidePage });
            var editions = new Mock<IEditionService>();
            editions.Setup(service => service.GetAllMonitoredEditions()).Returns(new List<Edition> { audiobook, ebook });
            editions.Setup(service => service.GetEditionsByBook(It.Is<IEnumerable<int>>(ids => ids.SequenceEqual(new[] { imported.Id }))))
                .Returns(new List<Edition> { audiobook, alternate });
            const string cachedCover = "/MediaCover/Books/10/cover.jpg?lastWrite=123";
            var covers = new Mock<IMapCoversToLocal>();
            covers.Setup(mapper => mapper.HasBookCoverFolder(imported.Id)).Returns(true);
            covers.Setup(mapper => mapper.GetCachedBookCover(imported, It.Is<IList<Edition>>(items => items.SequenceEqual(new[] { audiobook, alternate }))))
                .Returns(new MediaCover(MediaCoverTypes.Cover, cachedCover) { RemoteUrl = workCover });
            var controller = CreateController(books.Object, Mock.Of<IBroadcastSignalRMessage>(), authors.Object, editions.Object, covers.Object);

            var result = await controller.GetBooks(null, new List<int>(), null, page: 1, pageSize: 2);

            result.Select(book => book.Id).Should().Equal(imported.Id, withCover.Id);
            result[0].Images.Should().ContainSingle().Which.Url.Should().Be(cachedCover);
            result[0].Editions.Should().ContainSingle().Which.Id.Should().Be(audiobook.Id);
            result[0].FormatStatuses.Should().ContainSingle().Which.FormatType.Should().Be(FormatType.Audiobook);
            result[1].Images.Should().ContainSingle().Which.Url.Should().Be(shelfCover);
            imported.Editions.Value.Should().ContainSingle().Which.Should().BeSameAs(audiobook);
            editions.Verify(service => service.GetEditionsByBook(It.IsAny<IEnumerable<int>>()), Times.Once);
        }

        private static BookController CreateController(
            IBookService bookService,
            IBroadcastSignalRMessage broadcaster,
            IAuthorService authorService = null,
            IEditionService editionService = null,
            IMapCoversToLocal coverMapper = null)
        {
            var qualityProfileValidator = new QualityProfileExistsValidator(Mock.Of<IQualityProfileService>());
            var metadataProfileValidator = new MetadataProfileExistsValidator(Mock.Of<IMetadataProfileService>());
            var links = new Mock<ISeriesBookLinkService>();
            links.Setup(service => service.GetLinksByBook(It.IsAny<List<int>>())).Returns(new List<SeriesBookLink>());
            var stats = new Mock<IAuthorStatisticsService>();
            stats.Setup(service => service.AuthorStatistics()).Returns(new List<AuthorStatistics>());

            return new BookController(
                authorService ?? Mock.Of<IAuthorService>(),
                bookService,
                Mock.Of<IAddBookService>(),
                editionService ?? Mock.Of<IEditionService>(),
                links.Object,
                stats.Object,
                coverMapper ?? Mock.Of<IMapCoversToLocal>(),
                Mock.Of<IUpgradableSpecification>(),
                broadcaster,
                Mock.Of<IAuthorFormatProfileService>(),
                Mock.Of<IQualityProfileService>(),
                qualityProfileValidator,
                metadataProfileValidator);
        }
    }
}
