using System;
using System.Collections.Generic;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.Exceptions;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.Test.Framework;

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
        public void should_require_repeat_miss_before_hard_delete()
        {
            var remoteData = new Author { Metadata = _book.AuthorMetadata };

            Subject.RefreshBookInfo(new List<Book> { _book }, new List<Book>(), remoteData, true, false, DateTime.UtcNow);
            Mocker.GetMock<IBookService>().Verify(x => x.DeleteBook(_book.Id, false, false), Times.Never());

            Subject.RefreshBookInfo(new List<Book> { _book }, new List<Book>(), remoteData, true, false, DateTime.UtcNow);
            Mocker.GetMock<IBookService>().Verify(x => x.DeleteBook(_book.Id, false, false), Times.Once());
        }

        [Test]
        public void should_suppress_hard_delete_on_degraded_provider_window()
        {
            Subject.RefreshBookInfo(new List<Book> { _book }, new List<Book>(), null, true, false, DateTime.UtcNow);
            Subject.RefreshBookInfo(new List<Book> { _book }, new List<Book>(), null, true, false, DateTime.UtcNow);

            Mocker.GetMock<IBookService>().Verify(x => x.DeleteBook(_book.Id, false, false), Times.Never());
        }
    }
}
