using System;
using System.Collections.Generic;
using System.Linq;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.Exceptions;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.MusicTests
{
    [TestFixture]
    public class RefreshBookDeletionGuardFixture : CoreTest<RefreshBookService>
    {
        private Book _book;

        [SetUp]
        public void SetUp()
        {
            _book = new Book
            {
                Id = 7,
                ForeignBookId = "openlibrary:work:OL7W",
                Title = "Guarded Delete",
                AddOptions = new AddBookOptions { AddType = BookAddType.Automatic },
                AuthorMetadata = new AuthorMetadata { ForeignAuthorId = "openlibrary:author:OL7A", Name = "Author" },
                Editions = new List<Edition>()
            };

            Mocker.GetMock<IMediaFileService>()
                .Setup(x => x.GetFilesByBook(_book.Id))
                .Returns(new List<BookFile>());

            Mocker.GetMock<ICheckIfBookShouldBeRefreshed>()
                .Setup(x => x.ShouldRefresh(It.IsAny<Book>()))
                .Returns(true);

            Mocker.GetMock<IMetadataProviderOrchestrator>()
                .Setup(x => x.GetBookInfo(It.IsAny<string>()))
                .Throws(new BookNotFoundException(_book.ForeignBookId));
        }

        [Test]
        public void should_keep_legacy_and_file_linked_source_editions_when_a_refresh_changes_representatives()
        {
            _book.ForeignBookId = "hardcover:work:7";
            _book.AuthorMetadata.Value.ForeignAuthorId = "hardcover:author:123";
            var author = new Author { Metadata = _book.AuthorMetadata };
            _book.Author = author;
            var legacy = new Edition { Id = 10, BookId = 7, ForeignEditionId = "hardcover:edition:7", Title = _book.Title, Monitored = true };
            var priorAudio = new Edition { Id = 12, BookId = 7, ForeignEditionId = "hardcover:source-edition:80", Format = "Audio CD" };
            var emptyPriorVariant = new Edition { Id = 14, BookId = 7, ForeignEditionId = "hardcover:source-edition:81", Format = "Audio CD" };
            var newAudio = new Edition { ForeignEditionId = "hardcover:source-edition:82", Format = "Audiobook" };
            var remote = new Book
            {
                ForeignBookId = _book.ForeignBookId,
                Title = _book.Title,
                AuthorMetadata = _book.AuthorMetadata,
                Editions = new List<Edition> { new Edition { ForeignEditionId = legacy.ForeignEditionId, Title = _book.Title }, newAudio }
            };

            Mocker.GetMock<IAuthorService>().Setup(service => service.FindById(author.ForeignAuthorId)).Returns(author);
            Mocker.GetMock<IEditionService>().Setup(service => service.GetEditionsForRefresh(_book.Id, It.IsAny<List<string>>()))
                .Returns(new List<Edition> { legacy, priorAudio, emptyPriorVariant });
            Mocker.GetMock<IMediaFileService>().Setup(service => service.GetFilesByEdition(12))
                .Returns(new List<BookFile> { new BookFile { Id = 99, EditionId = 12 } });
            Mocker.GetMock<IMediaFileService>().Setup(service => service.GetFilesByEdition(14))
                .Returns(new List<BookFile>());

            Subject.RefreshBookInfo(_book, new List<Book> { remote }, author, false);

            Mocker.GetMock<IEditionService>().Verify(service => service.InsertMany(
                It.Is<List<Edition>>(editions => editions.Single().ForeignEditionId == newAudio.ForeignEditionId)), Times.Once);
            Mocker.GetMock<IRefreshEditionService>().Verify(service => service.RefreshEditionInfo(
                It.IsAny<List<Edition>>(),
                It.Is<List<Edition>>(editions => editions.All(edition => edition.Id != priorAudio.Id)),
                It.IsAny<List<Tuple<Edition, Edition>>>(),
                It.Is<List<Edition>>(editions => editions.Single().Id == emptyPriorVariant.Id),
                It.Is<List<Edition>>(editions => editions.Any(edition => edition.Id == priorAudio.Id)),
                It.IsAny<List<Edition>>(),
                It.IsAny<bool>()), Times.Once);
        }

        [Test]
        public void should_require_repeat_miss_before_hard_delete()
        {
            var remoteData = new Author { Metadata = _book.AuthorMetadata };

            Subject.RefreshBookInfo(new List<Book> { _book }, new List<Book>(), remoteData, true, false, DateTime.UtcNow);
            Mocker.GetMock<IBookService>().Verify(x => x.DeleteBook(_book.Id, false, false), Times.Never());

            Subject.RefreshBookInfo(new List<Book> { _book }, new List<Book>(), remoteData, true, false, DateTime.UtcNow);
            Mocker.GetMock<IBookService>().Verify(x => x.DeleteBook(_book.Id, false, false), Times.Once());

            ExceptionVerification.IgnoreWarns();
            ExceptionVerification.IgnoreErrors();
        }

        [Test]
        public void should_suppress_hard_delete_on_degraded_provider_window()
        {
            Subject.RefreshBookInfo(new List<Book> { _book }, new List<Book>(), null, true, false, DateTime.UtcNow);
            Subject.RefreshBookInfo(new List<Book> { _book }, new List<Book>(), null, true, false, DateTime.UtcNow);

            Mocker.GetMock<IBookService>().Verify(x => x.DeleteBook(_book.Id, false, false), Times.Never());

            ExceptionVerification.IgnoreWarns();
            ExceptionVerification.IgnoreErrors();
        }
    }
}
