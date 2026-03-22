using System;
using System.Collections.Generic;
using FizzWare.NBuilder;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.History;
using NzbDrone.Core.ImportLists.Exclusions;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MusicTests
{
    [TestFixture]
    public class AuthorCanonicalizationServiceFixture : CoreTest<AuthorCanonicalizationService>
    {
        private Author _primary;
        private Author _duplicate;

        [SetUp]
        public void Setup()
        {
            _primary = Builder<Author>.CreateNew()
                .With(x => x.Id = 10)
                .With(x => x.AuthorMetadataId = 100)
                .With(x => x.Added = DateTime.UtcNow.AddDays(-2))
                .With(x => x.Metadata = new AuthorMetadata
                {
                    Id = 100,
                    ForeignAuthorId = "openlibrary:author:OL10A",
                    Name = "Ursula Le Guin"
                })
                .With(x => x.Books = new List<Book>
                {
                    Builder<Book>.CreateNew().With(b => b.Id = 1).Build()
                })
                .Build();

            _duplicate = Builder<Author>.CreateNew()
                .With(x => x.Id = 11)
                .With(x => x.AuthorMetadataId = 101)
                .With(x => x.Added = DateTime.UtcNow)
                .With(x => x.Metadata = new AuthorMetadata
                {
                    Id = 101,
                    ForeignAuthorId = "openlibrary:author:OL11A",
                    Name = "Ursula Le Guin"
                })
                .With(x => x.Books = new List<Book>())
                .Build();

            Mocker.GetMock<IAuthorService>()
                .Setup(x => x.GetAllAuthors())
                .Returns(new List<Author> { _primary, _duplicate });
        }

        [Test]
        public void should_rewire_books_history_and_exclusions_when_merging_duplicate_author()
        {
            var books = new List<Book>
            {
                Builder<Book>.CreateNew()
                    .With(x => x.AuthorMetadataId = _duplicate.AuthorMetadataId)
                    .Build()
            };

            var history = new List<EntityHistory>
            {
                Builder<EntityHistory>.CreateNew()
                    .With(x => x.AuthorId = _duplicate.Id)
                    .Build()
            };

            var duplicateExclusion = new ImportListExclusion
            {
                Id = 2,
                ForeignId = _duplicate.Metadata.Value.ForeignAuthorId,
                Name = _duplicate.Metadata.Value.Name
            };

            Mocker.GetMock<IBookService>()
                .Setup(x => x.GetBooksByAuthor(_duplicate.Id))
                .Returns(books);

            Mocker.GetMock<IHistoryService>()
                .Setup(x => x.GetByAuthor(_duplicate.Id, null))
                .Returns(history);

            Mocker.GetMock<IImportListExclusionService>()
                .Setup(x => x.FindByForeignId(_duplicate.Metadata.Value.ForeignAuthorId))
                .Returns(duplicateExclusion);

            Mocker.GetMock<IImportListExclusionService>()
                .Setup(x => x.FindByForeignId(_primary.Metadata.Value.ForeignAuthorId))
                .Returns((ImportListExclusion)null);

            var summary = Subject.CanonicalizeDuplicates(false, 0.5, 10);

            summary.MergesPerformed.Should().Be(1);

            Mocker.GetMock<IBookService>()
                .Verify(x => x.UpdateMany(It.Is<List<Book>>(list =>
                    list.Count == 1 &&
                    list[0].AuthorMetadataId == _primary.AuthorMetadataId)), Times.Once);

            Mocker.GetMock<IHistoryService>()
                .Verify(x => x.UpdateMany(It.Is<List<EntityHistory>>(list =>
                    list.Count == 1 &&
                    list[0].AuthorId == _primary.Id)), Times.Once);

            Mocker.GetMock<IImportListExclusionService>()
                .Verify(x => x.Update(It.Is<ImportListExclusion>(e =>
                    e.Id == duplicateExclusion.Id &&
                    e.ForeignId == _primary.Metadata.Value.ForeignAuthorId &&
                    e.Name == _primary.Metadata.Value.Name)), Times.Once);

            Mocker.GetMock<IAuthorService>()
                .Verify(x => x.DeleteAuthor(_duplicate.Id, false, false), Times.Once);
        }

        [Test]
        public void should_delete_duplicate_exclusion_when_primary_exclusion_already_exists()
        {
            var duplicateExclusion = new ImportListExclusion
            {
                Id = 2,
                ForeignId = _duplicate.Metadata.Value.ForeignAuthorId,
                Name = _duplicate.Metadata.Value.Name
            };

            var primaryExclusion = new ImportListExclusion
            {
                Id = 3,
                ForeignId = _primary.Metadata.Value.ForeignAuthorId,
                Name = _primary.Metadata.Value.Name
            };

            Mocker.GetMock<IBookService>()
                .Setup(x => x.GetBooksByAuthor(_duplicate.Id))
                .Returns(new List<Book>());

            Mocker.GetMock<IHistoryService>()
                .Setup(x => x.GetByAuthor(_duplicate.Id, null))
                .Returns(new List<EntityHistory>());

            Mocker.GetMock<IImportListExclusionService>()
                .Setup(x => x.FindByForeignId(_duplicate.Metadata.Value.ForeignAuthorId))
                .Returns(duplicateExclusion);

            Mocker.GetMock<IImportListExclusionService>()
                .Setup(x => x.FindByForeignId(_primary.Metadata.Value.ForeignAuthorId))
                .Returns(primaryExclusion);

            var summary = Subject.CanonicalizeDuplicates(false, 0.5, 10);

            summary.MergesPerformed.Should().Be(1);

            Mocker.GetMock<IImportListExclusionService>()
                .Verify(x => x.Delete(duplicateExclusion.Id), Times.Once);
        }
    }
}
