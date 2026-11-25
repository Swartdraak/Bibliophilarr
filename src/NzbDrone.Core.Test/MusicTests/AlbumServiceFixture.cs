using System.Collections.Generic;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.Books.Events;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MusicTests.BookRepositoryTests
{
    [TestFixture]
    public class BookServiceFixture : CoreTest<BookService>
    {
        private List<Book> _books;

        [SetUp]
        public void Setup()
        {
            _books = new List<Book>();
            _books.Add(new Book
            {
                Title = "ANThology",
                CleanTitle = "anthology",
                AuthorMetadata = new AuthorMetadata
                {
                    Name = "Author"
                }
            });

            _books.Add(new Book
            {
                Title = "+",
                CleanTitle = "",
                AuthorMetadata = new AuthorMetadata
                {
                    Name = "Author"
                }
            });

            Mocker.GetMock<IBookRepository>()
                .Setup(s => s.GetBooksByAuthorMetadataId(It.IsAny<int>()))
                .Returns(_books);
        }

        private void GivenSimilarBook()
        {
            _books.Add(new Book
            {
                Title = "ANThology2",
                CleanTitle = "anthology2",
                AuthorMetadata = new AuthorMetadata
                {
                    Name = "Author"
                }
            });
        }

        [TestCase("ANTholog", "ANThology")]
        [TestCase("antholoyg", "ANThology")]
        [TestCase("ANThology CD", "ANThology")]
        [TestCase("ANThology CD xxxx (Remastered) - [Oh please why do they do this?]", "ANThology")]
        [TestCase("+ (Plus) - I feel the need for redundant information in the title field", "+")]
        public void should_find_book_in_db_by_inexact_title(string title, string expected)
        {
            var book = Subject.FindByTitleInexact(0, title);

            book.Should().NotBeNull();
            book.Title.Should().Be(expected);
        }

        [TestCase("ANTholog")]
        [TestCase("antholoyg")]
        [TestCase("ANThology CD")]
        [TestCase("÷")]
        [TestCase("÷ (Divide)")]
        public void should_not_find_book_in_db_by_inexact_title_when_two_similar_matches(string title)
        {
            GivenSimilarBook();
            var book = Subject.FindByTitleInexact(0, title);

            book.Should().BeNull();
        }

        [TestCase(false)]
        [TestCase(true)]
        public void should_publish_deleted_books_with_the_original_author(bool deleteFiles)
        {
            var author = new Author { Id = 41, AuthorMetadataId = 7, Tags = new HashSet<int> { 5 } };
            var published = new List<BookDeletedEvent>();
            Mocker.GetMock<IEventAggregator>()
                .Setup(aggregator => aggregator.PublishEvent(It.IsAny<BookDeletedEvent>()))
                .Callback<BookDeletedEvent>(published.Add);

            Subject.Handle(new AuthorDeletedEvent(author, deleteFiles, false));

            published.Should().HaveSameCount(_books);
            published.Should().OnlyContain(message => ReferenceEquals(message.Book.Author.Value, author));
        }
    }
}
