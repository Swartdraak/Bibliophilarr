using System;
using System.Collections.Generic;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.MetadataSource.BookInfo;
using NzbDrone.Core.MetadataSource.Goodreads;
using NzbDrone.Core.MetadataSource.OpenLibrary;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MetadataSource.OpenLibrary
{
    /// <summary>
    /// Unit tests for Phase 3 ISBN/ASIN lookup paths in BookInfoProxy and the
    /// OpenLibrarySearchProxy integration.
    /// </summary>
    [TestFixture]
    public class OpenLibraryIsbnAsinLookupFixture : CoreTest<BookInfoProxy>
    {
        // ------------------------------------------------------------------ //
        // SearchByIsbn – Open Library happy-path
        // ------------------------------------------------------------------ //
        [Test]
        public void search_by_isbn_returns_open_library_result_when_lookup_succeeds()
        {
            var expectedBook = BuildBook("openlibrary:work:OL123W", "Dune", "9780441013593");

            Mocker.GetMock<IOpenLibrarySearchProxy>()
                .Setup(x => x.LookupByIsbn("9780441013593"))
                .Returns(expectedBook);

            var result = Subject.SearchByIsbn("9780441013593");

            result.Should().ContainSingle();
            result[0].ForeignBookId.Should().Be("openlibrary:work:OL123W");
        }

        [Test]
        public void search_by_isbn_does_not_call_goodreads_when_open_library_succeeds()
        {
            var expectedBook = BuildBook("openlibrary:work:OL123W", "Dune", "9780441013593");

            Mocker.GetMock<IOpenLibrarySearchProxy>()
                .Setup(x => x.LookupByIsbn(It.IsAny<string>()))
                .Returns(expectedBook);

            Subject.SearchByIsbn("9780441013593");

            // Goodreads search should not be triggered when OL lookup succeeds
            Mocker.GetMock<IGoodreadsSearchProxy>()
                .Verify(x => x.Search(It.IsAny<string>()), Times.Never());
        }

        // ------------------------------------------------------------------ //
        // SearchByIsbn – Open Library miss → Goodreads fallback
        // ------------------------------------------------------------------ //
        [Test]
        public void search_by_isbn_falls_back_when_open_library_returns_null()
        {
            Mocker.GetMock<IOpenLibrarySearchProxy>()
                .Setup(x => x.LookupByIsbn(It.IsAny<string>()))
                .Returns((Book)null);

            Mocker.GetMock<IGoodreadsSearchProxy>()
                .Setup(x => x.Search(It.IsAny<string>()))
                .Returns(new List<SearchJsonResource>());

            var result = Subject.SearchByIsbn("9780441013593");

            result.Should().BeEmpty();

            // Goodreads fallback should be attempted
            Mocker.GetMock<IGoodreadsSearchProxy>()
                .Verify(x => x.Search(It.IsAny<string>()), Times.Once());
        }

        // ------------------------------------------------------------------ //
        // SearchByAsin – Open Library happy-path
        // ------------------------------------------------------------------ //
        [Test]
        public void search_by_asin_returns_open_library_result_when_lookup_succeeds()
        {
            var expectedBook = BuildBook("openlibrary:work:OL456W", "Foundation", null);

            Mocker.GetMock<IOpenLibrarySearchProxy>()
                .Setup(x => x.LookupByAsin("B000FC1PWW"))
                .Returns(expectedBook);

            var result = Subject.SearchByAsin("B000FC1PWW");

            result.Should().ContainSingle();
            result[0].ForeignBookId.Should().Be("openlibrary:work:OL456W");
        }

        [Test]
        public void search_by_asin_falls_back_when_open_library_returns_null()
        {
            Mocker.GetMock<IOpenLibrarySearchProxy>()
                .Setup(x => x.LookupByAsin(It.IsAny<string>()))
                .Returns((Book)null);

            Mocker.GetMock<IGoodreadsSearchProxy>()
                .Setup(x => x.Search(It.IsAny<string>()))
                .Returns(new List<SearchJsonResource>());

            var result = Subject.SearchByAsin("B000FC1PWW");

            result.Should().BeEmpty();

            Mocker.GetMock<IGoodreadsSearchProxy>()
                .Verify(x => x.Search(It.IsAny<string>()), Times.Once());
        }

        // ------------------------------------------------------------------ //
        // Helpers
        // ------------------------------------------------------------------ //
        private static Book BuildBook(string foreignId, string title, string isbn13)
        {
            var author = new AuthorMetadata
            {
                ForeignAuthorId = "openlibrary:author:test",
                Name = "Test Author",
                SortName = "Test Author",
                NameLastFirst = "Test Author",
                SortNameLastFirst = "Test Author"
            };

            var book = new Book
            {
                ForeignBookId = foreignId,
                Title = title,
                CleanTitle = title,
                AuthorMetadata = author,
                ReleaseDate = new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                Ratings = new Ratings()
            };

            book.Editions = new List<Edition>
            {
                new Edition
                {
                    ForeignEditionId = foreignId.Replace("work", "edition"),
                    Title = title,
                    Isbn13 = isbn13,
                    IsEbook = true,
                    Format = "Ebook",
                    Book = book,
                    Ratings = new Ratings()
                }
            };

            return book;
        }
    }
}
