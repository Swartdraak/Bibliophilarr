using System;
using System.Collections.Generic;
using FizzWare.NBuilder;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.History;
using NzbDrone.Core.ImportLists.Exclusions;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MusicTests
{
    [TestFixture]
    public class AuthorCanonicalizationServiceFixture : CoreTest<AuthorCanonicalizationService>
    {
        private Author _primary;
        private Author _duplicate;

        [TestCase(0, false, false, false)]
        [TestCase(1, false, false, false)]
        [TestCase(2, false, false, true)]
        [TestCase(2, true, false, false)]
        [TestCase(2, false, true, true)]
        public void should_reuse_incumbent_with_two_independent_verified_works(int confirmedWorks, bool repeatedTitle, bool staged, bool reuse)
        {
            var primary = _primary.Metadata.Value.ForeignAuthorId;
            var incoming = _duplicate.Metadata.Value.ForeignAuthorId;
            var secondTitle = repeatedTitle ? "A Fixture Work" : "Another Fixture Work";
            var primaryWorks = new List<Book>
            {
                Work("openlibrary:work:OL10W", "A Fixture Work", "9781442482890"),
                Work("openlibrary:work:OL11W", secondTitle, "9781455546176")
            };
            var incomingWorks = new List<Book>
            {
                Work("hardcover:work:20", "A Fixture Work", "1-4424-8289-3"),
                Work("hardcover:work:21", secondTitle, "9781455546176")
            };
            var pending = new List<(Author, IReadOnlyList<Book>)>();
            if (staged)
            {
                _primary.Id = 0;
                pending.Add((_primary, primaryWorks));
            }

            Mocker.GetMock<IAuthorService>().Setup(service => service.GetAuthorsByCleanName(It.IsAny<string>()))
                .Returns(staged ? new List<Author>() : new List<Author> { _primary });
            var orchestrator = Mocker.GetMock<IMetadataProviderOrchestrator>();
            orchestrator.Setup(service => service.GetAuthorInfo(primary, true))
                .Returns(new Author { ForeignAuthorId = primary, Books = primaryWorks });
            for (var index = 0; index < confirmedWorks; index++)
            {
                var counterpart = primaryWorks[index];
                var edition = counterpart.Editions.Value[0];
                var candidate = incomingWorks[index];
                orchestrator.Setup(service => service.GetVerifiedAuthorWorkTitle(primary, counterpart.ForeignBookId, edition.Isbn13))
                    .Returns(counterpart.Title);
                orchestrator.Setup(service => service.GetVerifiedAuthorWorkTitle(incoming, candidate.ForeignBookId, edition.Isbn13))
                    .Returns(candidate.Title);
            }

            var identity = Mocker.GetMock<ICatalogueIdentityRepository>();
            identity.Setup(service => service.TryAddSource(CatalogueEntityKind.Author, _primary.AuthorMetadataId, "Hardcover", "11", "two-isbn-title-author-work-witnesses"))
                .Returns(true);

            var result = Subject.TryReuseAuthorOnAdd(_duplicate, incomingWorks, pending);

            result.Should().BeSameAs(reuse ? _primary : null);
            identity.Verify(service => service.TryAddSource(CatalogueEntityKind.Author, _primary.AuthorMetadataId, "Hardcover", "11", "two-isbn-title-author-work-witnesses"),
                reuse && !staged ? Times.Once() : Times.Never());
            if (staged)
            {
                _primary.Metadata.Value.PendingSourceAssociations.Should().ContainSingle()
                    .Which.ProviderId.Should().Be("11");
            }
        }

        private static Book Work(string id, string title, string isbn)
        {
            return new Book
            {
                ForeignBookId = id,
                Title = title,
                Editions = new List<Edition> { new Edition { Isbn13 = isbn } }
            };
        }

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
                    ForeignAuthorId = "hardcover:author:11",
                    OpenLibraryAuthorId = "OL10A",
                    Name = "Ursula Le Guin"
                })
                .With(x => x.Books = new List<Book>())
                .Build();

            Mocker.GetMock<IAuthorService>()
                .Setup(x => x.GetAllAuthors())
                .Returns(new List<Author> { _primary, _duplicate });
        }

        [Test]
        public void should_preserve_distinct_authors_with_the_same_name_even_with_low_threshold()
        {
            _duplicate.Metadata.Value.OpenLibraryAuthorId = "OL11A";
            var incoming = new Author
            {
                Metadata = new AuthorMetadata
                {
                    ForeignAuthorId = "hardcover:author:12",
                    OpenLibraryAuthorId = "OL12A",
                    Name = "Ursula Le Guin"
                }
            };

            var match = Subject.TryFindCanonicalMatch(incoming, out _, out _, 0.1);
            match.Should().BeNull();

            var summary = Subject.CanonicalizeDuplicates(false, 0, 10);
            summary.MergesPerformed.Should().Be(0);
            summary.MergesSkipped.Should().Be(1);
            Mocker.GetMock<IAuthorService>().Verify(x => x.DeleteAuthor(It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<bool>()), Times.Never);
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
