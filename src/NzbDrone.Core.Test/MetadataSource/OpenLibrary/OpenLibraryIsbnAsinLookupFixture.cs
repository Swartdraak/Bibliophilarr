using System.Collections.Generic;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.MetadataSource.BookInfo;
using NzbDrone.Core.MetadataSource.OpenLibrary;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MetadataSource.OpenLibrary
{
    [TestFixture]
    public class OpenLibraryIsbnAsinLookupFixture : CoreTest<BookInfoProxy>
    {
        [Test]
        public void search_by_isbn_should_fallback_to_query_search_when_lookup_misses()
        {
            Mocker.GetMock<IOpenLibrarySearchProxy>()
                .Setup(x => x.LookupByIsbn("9780261103573"))
                .Returns((Book)null);

            var metadata = new AuthorMetadata
            {
                ForeignAuthorId = "OL3A",
                Name = "J.R.R. Tolkien"
            };

            var searchedBook = new Book
            {
                ForeignBookId = "OL3W",
                Title = "The Lord of the Rings",
                AuthorMetadata = new LazyLoaded<AuthorMetadata>(metadata)
            };

            Mocker.GetMock<IOpenLibrarySearchProxy>()
                .Setup(x => x.Search("9780261103573"))
                .Returns(new List<Book> { searchedBook });

            var results = Subject.SearchByIsbn("9780261103573");

            results.Should().HaveCount(1);
            results[0].ForeignBookId.Should().Be("OL3W");
            Mocker.GetMock<IOpenLibrarySearchProxy>().Verify(x => x.Search("9780261103573"), Times.Once);
        }

        [Test]
        public void search_by_asin_should_fallback_to_query_search_when_lookup_misses()
        {
            Mocker.GetMock<IOpenLibrarySearchProxy>()
                .Setup(x => x.LookupByAsin("B00JCDK5ME"))
                .Returns((Book)null);

            var metadata = new AuthorMetadata
            {
                ForeignAuthorId = "OL4A",
                Name = "George Orwell"
            };

            var searchedBook = new Book
            {
                ForeignBookId = "OL4W",
                Title = "Nineteen Eighty-Four",
                AuthorMetadata = new LazyLoaded<AuthorMetadata>(metadata)
            };

            Mocker.GetMock<IOpenLibrarySearchProxy>()
                .Setup(x => x.Search("B00JCDK5ME"))
                .Returns(new List<Book> { searchedBook });

            var results = Subject.SearchByAsin("B00JCDK5ME");

            results.Should().HaveCount(1);
            results[0].ForeignBookId.Should().Be("OL4W");
            Mocker.GetMock<IOpenLibrarySearchProxy>().Verify(x => x.Search("B00JCDK5ME"), Times.Once);
        }
    }
}
