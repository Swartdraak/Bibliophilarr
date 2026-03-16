using System.Collections.Generic;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.MetadataSource.BookInfo;
using NzbDrone.Core.MetadataSource.Goodreads;
using NzbDrone.Core.MetadataSource.OpenLibrary;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MetadataSource.BookInfo
{
    [TestFixture]
    public class BookInfoProxyOpenLibraryFixture : CoreTest<BookInfoProxy>
    {
        [Test]
        public void should_prefer_open_library_results_when_available()
        {
            var openLibraryBook = new Book
            {
                ForeignBookId = "openlibrary:work:OL123W",
                Title = "Dune",
                Editions = new List<Edition>()
            };

            Mocker.GetMock<IOpenLibrarySearchProxy>()
                .Setup(x => x.Search(It.IsAny<string>()))
                .Returns(new List<Book> { openLibraryBook });

            Mocker.GetMock<IGoodreadsSearchProxy>()
                .Setup(x => x.Search(It.IsAny<string>()))
                .Returns(new List<SearchJsonResource>());

            var result = Subject.SearchForNewBook("Dune", "Frank Herbert", false);

            result.Should().ContainSingle();
            result[0].ForeignBookId.Should().Be("openlibrary:work:OL123W");

            Mocker.GetMock<IGoodreadsSearchProxy>()
                .Verify(x => x.Search(It.IsAny<string>()), Times.Never());
        }

        [Test]
        public void should_fallback_to_goodreads_when_open_library_returns_no_results()
        {
            Mocker.GetMock<IOpenLibrarySearchProxy>()
                .Setup(x => x.Search(It.IsAny<string>()))
                .Returns(new List<Book>());

            Mocker.GetMock<IGoodreadsSearchProxy>()
                .Setup(x => x.Search(It.IsAny<string>()))
                .Returns(new List<SearchJsonResource>());

            var result = Subject.SearchForNewBook("Dune", "Frank Herbert", false);

            result.Should().BeEmpty();

            Mocker.GetMock<IGoodreadsSearchProxy>()
                .Verify(x => x.Search(It.IsAny<string>()), Times.Once());
        }
    }
}
