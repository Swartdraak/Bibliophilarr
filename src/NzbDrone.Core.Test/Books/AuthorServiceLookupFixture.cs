using System;
using System.Collections.Generic;
using System.IO;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.BooksTests
{
    [TestFixture]
    public class AuthorServiceLookupFixture : DbTest<AuthorService, Author>
    {
        private AuthorRepository _authorRepository;

        [SetUp]
        public void Setup()
        {
            _authorRepository = Mocker.Resolve<AuthorRepository>();
            Mocker.SetConstant<IAuthorRepository>(_authorRepository);
        }

        [Test]
        public void should_not_throw_when_requested_author_row_is_missing()
        {
            // Issue #383: a post-create refresh can remove an author before the scan reads it.
            const int missingAuthorId = 987654;

            Subject.GetAuthors(new[] { missingAuthorId }).Should().BeEmpty();

            ExceptionVerification.ExpectedWarns(1, $"Author {missingAuthorId} no longer exists in the database");
        }

        [Test]
        public void should_return_author_when_requested_author_row_exists()
        {
            var metadata = Mocker.Resolve<AuthorMetadataRepository>().Insert(new AuthorMetadata
            {
                ForeignAuthorId = "hardcover:author:140004",
                TitleSlug = "test-author",
                Name = "Test Author",
                SortName = "Test Author",
                NameLastFirst = "Author, Test",
                SortNameLastFirst = "Author, Test",
                Status = AuthorStatusType.Continuing,
                Images = new List<NzbDrone.Core.MediaCover.MediaCover>(),
                Aliases = new List<string>()
            });

            var author = _authorRepository.Insert(new Author
            {
                AuthorMetadataId = metadata.Id,
                CleanName = "TestAuthor",
                Path = Path.Combine(TempFolder, "Author"),
                Monitored = true,
                Added = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            });

            Subject.GetAuthors(new[] { author.Id }).Should().ContainSingle(result => result.Id == author.Id);
        }
    }
}
