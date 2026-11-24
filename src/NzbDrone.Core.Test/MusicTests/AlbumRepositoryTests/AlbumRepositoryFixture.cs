using System;
using System.Collections.Generic;
using System.Linq;
using FizzWare.NBuilder;
using FluentAssertions;
using FluentAssertions.Equivalency;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MusicTests.BookRepositoryTests
{
    [TestFixture]
    public class BookRepositoryFixture : DbTest<BookService, Book>
    {
        private Author _author;
        private Book _book;
        private Book _bookSpecial;
        private List<Book> _books;
        private BookRepository _bookRepo;

        [SetUp]
        public void Setup()
        {
            AssertionConfiguration.Current.Equivalency.Modify(options =>
            {
                options.Using<DateTime>(ctx => ctx.Subject.Should().BeCloseTo(ctx.Expectation.ToUniversalTime(), TimeSpan.FromSeconds(1))).WhenTypeIs<DateTime>();
                options.Using<DateTime?>(ctx => ctx.Subject.Should().BeCloseTo(ctx.Expectation.Value.ToUniversalTime(), TimeSpan.FromSeconds(1))).WhenTypeIs<DateTime?>();
                return options;
            });

            _author = new Author
            {
                Name = "Alien Ant Farm",
                Monitored = true,
                ForeignAuthorId = "this is a fake id",
                Id = 1,
                AuthorMetadataId = 1
            };

            _bookRepo = Mocker.Resolve<BookRepository>();

            _book = new Book
            {
                Title = "ANThology",
                ForeignBookId = "1",
                TitleSlug = "1-ANThology",
                CleanTitle = "anthology",
                Author = _author,
                AuthorMetadataId = _author.AuthorMetadataId,
            };

            _bookRepo.Insert(_book);
            _bookRepo.Update(_book);

            _bookSpecial = new Book
            {
                Title = "+",
                ForeignBookId = "2",
                TitleSlug = "2-_",
                CleanTitle = "",
                Author = _author,
                AuthorMetadataId = _author.AuthorMetadataId
            };

            _bookRepo.Insert(_bookSpecial);
        }

        [Test]
        public void artwork_retry_should_page_by_primary_key()
        {
            _bookRepo.GetArtworkRetryPage(0, 1).Select(book => book.Id).Should().Equal(_book.Id);
            _bookRepo.GetArtworkRetryPage(_book.Id, 1).Select(book => book.Id).Should().Equal(_bookSpecial.Id);
            _bookRepo.GetArtworkRetryPage(_bookSpecial.Id, 1).Should().BeEmpty();
        }

        [Test]
        public void should_update_only_the_requested_book_and_format()
        {
            _bookRepo.SetFormatMonitored(new[] { _book.Id }, FormatType.Ebook, false);
            _bookRepo.SetFormatMonitored(new[] { _book.Id }, FormatType.Audiobook, true);

            var updated = _bookRepo.Get(_book.Id);
            updated.EbookMonitoringOverride.Should().BeFalse();
            updated.AudiobookMonitoringOverride.Should().BeTrue();
            updated.Monitored.Should().Be(_book.Monitored);

            var otherBook = _bookRepo.Get(_bookSpecial.Id);
            otherBook.EbookMonitoringOverride.Should().BeNull();
            otherBook.AudiobookMonitoringOverride.Should().BeNull();
        }

        [Test]
        public void missing_format_results_should_respect_the_book_preference()
        {
            var metadata = Mocker.Resolve<BasicRepository<AuthorMetadata>>().Insert(new AuthorMetadata
            {
                Name = "Sample author",
                ForeignAuthorId = "author-fixture",
                TitleSlug = "sample-author",
                SortName = "sample author",
                NameLastFirst = "Author, Sample",
                SortNameLastFirst = "author sample"
            });
            var author = Mocker.Resolve<BasicRepository<Author>>().Insert(new Author
            {
                AuthorMetadataId = metadata.Id,
                CleanName = "sampleauthor",
                Path = "/sample-author",
                Monitored = true
            });
            var profiles = Mocker.Resolve<BasicRepository<AuthorFormatProfile>>();
            foreach (var format in new[] { FormatType.Ebook, FormatType.Audiobook })
            {
                profiles.Insert(new AuthorFormatProfile { AuthorId = author.Id, FormatType = format, RootFolderPath = "/fixture" });
            }

            foreach (var book in new[] { _book, _bookSpecial })
            {
                book.AuthorMetadataId = metadata.Id;
                book.ReleaseDate = DateTime.UtcNow.AddDays(-1);
                book.Monitored = true;
                _bookRepo.Update(book);
            }

            _bookRepo.SetFormatMonitored(new[] { _book.Id }, FormatType.Ebook, false);
            var page = new PagingSpec<Book> { Page = 1, PageSize = 10, SortKey = "Id", SortDirection = SortDirection.Ascending };

            var ebooks = _bookRepo.BooksWithoutFiles(page, FormatType.Ebook, true);
            ebooks.Records.Select(book => book.Id).Should().Equal(_bookSpecial.Id);
            ebooks.TotalRecords.Should().Be(1);

            var audiobooks = _bookRepo.BooksWithoutFiles(page, FormatType.Audiobook, true);
            audiobooks.Records.Select(book => book.Id).Should().Equal(_book.Id, _bookSpecial.Id);
            audiobooks.TotalRecords.Should().Be(2);
        }

        [TestCase("ANThology")]
        [TestCase("anthology")]
        [TestCase("anthology!")]
        public void should_find_book_in_db_by_title(string title)
        {
            var book = _bookRepo.FindByTitle(_author.AuthorMetadataId, title);

            book.Should().NotBeNull();
            book.Title.Should().Be(_book.Title);
        }

        [Test]
        public void should_find_book_in_db_by_title_all_special_characters()
        {
            var book = _bookRepo.FindByTitle(_author.AuthorMetadataId, "+");

            book.Should().NotBeNull();
            book.Title.Should().Be(_bookSpecial.Title);
        }

        [TestCase("ANTholog")]
        [TestCase("nthology")]
        [TestCase("antholoyg")]
        [TestCase("÷")]
        public void should_not_find_book_in_db_by_incorrect_title(string title)
        {
            var book = _bookRepo.FindByTitle(_author.AuthorMetadataId, title);

            book.Should().BeNull();
        }

        [Test]
        public void should_not_find_book_when_two_books_have_same_name()
        {
            var books = Builder<Book>.CreateListOfSize(2)
                .All()
                .With(x => x.Id = 0)
                .With(x => x.Author = _author)
                .With(x => x.AuthorMetadataId = _author.AuthorMetadataId)
                .With(x => x.Title = "Weezer")
                .With(x => x.CleanTitle = "weezer")
                .Build();

            _bookRepo.InsertMany(books);

            var book = _bookRepo.FindByTitle(_author.AuthorMetadataId, "Weezer");

            _bookRepo.All().Should().HaveCount(4);
            book.Should().BeNull();
        }

        private void GivenMultipleBooks()
        {
            _books = Builder<Book>.CreateListOfSize(4)
                .All()
                .With(x => x.Id = 0)
                .With(x => x.Author = _author)
                .With(x => x.AuthorMetadataId = _author.AuthorMetadataId)
                .TheFirst(1)

                // next
                .With(x => x.ReleaseDate = DateTime.UtcNow.AddDays(1))
                .TheNext(1)

                // another future one
                .With(x => x.ReleaseDate = DateTime.UtcNow.AddDays(2))
                .TheNext(1)

                // most recent
                .With(x => x.ReleaseDate = DateTime.UtcNow.AddDays(-1))
                .TheNext(1)

                // an older one
                .With(x => x.ReleaseDate = DateTime.UtcNow.AddDays(-2))
                .BuildList();

            _bookRepo.InsertMany(_books);
        }

        [Test]
        public void get_next_books_should_return_next_book()
        {
            GivenMultipleBooks();

            var result = _bookRepo.GetNextBooks(new[] { _author.AuthorMetadataId });
            result.Should().BeEquivalentTo(_books.Take(1), BookComparerOptions);
        }

        [Test]
        public void get_last_books_should_return_next_book()
        {
            GivenMultipleBooks();

            var result = _bookRepo.GetLastBooks(new[] { _author.AuthorMetadataId });
            result.Should().BeEquivalentTo(_books.Skip(2).Take(1), BookComparerOptions);
        }

        private EquivalencyOptions<Book> BookComparerOptions(EquivalencyOptions<Book> opts) => opts.ComparingByMembers<Book>()
                .Excluding(x => x.AuthorId)
                .Excluding(x => x.ForeignEditionId)
                .Excluding(x => x.Author)
                .Excluding(x => x.AuthorMetadata)
                .Excluding(x => x.BookFiles)
                .Excluding(x => x.Editions)
                .Excluding(x => x.SeriesLinks);
    }
}
