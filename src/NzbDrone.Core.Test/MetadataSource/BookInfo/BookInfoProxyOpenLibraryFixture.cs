using System.Collections.Generic;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.MetadataSource.BookInfo;
using NzbDrone.Core.MetadataSource.OpenLibrary;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MetadataSource.BookInfo
{
    [TestFixture]
    public class BookInfoProxyOpenLibraryFixture : CoreTest<BookInfoProxy>
    {
        [Test]
        public void search_for_new_book_should_use_openlibrary_search_proxy()
        {
            var metadata = new AuthorMetadata
            {
                ForeignAuthorId = "OL1A",
                Name = "Frank Herbert"
            };

            var resultBook = new Book
            {
                ForeignBookId = "OL1W",
                Title = "Dune",
                AuthorMetadata = new LazyLoaded<AuthorMetadata>(metadata)
            };

            Mocker.GetMock<IOpenLibrarySearchProxy>()
                .Setup(x => x.Search("dune"))
                .Returns(new List<Book> { resultBook });

            var results = Subject.SearchForNewBook("Dune", null, false);

            results.Should().HaveCount(1);
            results[0].Title.Should().Be("Dune");
            Mocker.GetMock<IOpenLibrarySearchProxy>().Verify(x => x.Search("dune"), Times.Once);
        }

        [Test]
        public void search_by_isbn_should_return_lookup_hit_without_fallback_search()
        {
            var metadata = new AuthorMetadata
            {
                ForeignAuthorId = "OL2A",
                Name = "Ursula Le Guin"
            };

            var lookupBook = new Book
            {
                ForeignBookId = "OL2W",
                Title = "A Wizard of Earthsea",
                AuthorMetadata = new LazyLoaded<AuthorMetadata>(metadata)
            };

            Mocker.GetMock<IOpenLibrarySearchProxy>()
                .Setup(x => x.LookupByIsbn("9780547773742"))
                .Returns(lookupBook);

            var results = Subject.SearchByIsbn("9780547773742");

            results.Should().HaveCount(1);
            results[0].ForeignBookId.Should().Be("OL2W");
            Mocker.GetMock<IOpenLibrarySearchProxy>().Verify(x => x.Search(It.IsAny<string>()), Times.Never);
        }
    }
}
