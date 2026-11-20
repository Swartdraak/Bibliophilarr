using System;
using System.Collections.Generic;
using FizzWare.NBuilder;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.Books.Commands;
using NzbDrone.Core.History;
using NzbDrone.Core.Messaging.Commands;
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

            var summary = Subject.CanonicalizeDuplicates(true, 0, 10);
            summary.MergesPerformed.Should().Be(0);
            summary.MergesSkipped.Should().Be(1);
            Mocker.GetMock<IAuthorService>().Verify(x => x.DeleteAuthor(It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<bool>()), Times.Never);
        }

        [Test]
        public void should_preview_a_candidate_without_changing_either_author()
        {
            _primary.Path = "/library/primary";
            _duplicate.Path = "/library/duplicate";
            var command = new CanonicalizeAuthorsCommand();

            command.DryRun.Should().BeTrue();
            Subject.Execute(command);
            var summary = Subject.CanonicalizeDuplicates(true, 0.95, 10);

            summary.DuplicateGroupsEvaluated.Should().Be(1);
            summary.CandidatesConsidered.Should().Be(1);
            summary.MergesPerformed.Should().Be(1);
            summary.MergesSkipped.Should().Be(0);
            _primary.Path.Should().Be("/library/primary");
            _duplicate.Path.Should().Be("/library/duplicate");
            Mocker.GetMock<IAuthorService>().Verify(service => service.GetAllAuthors(), Times.Exactly(2));
            Mocker.GetMock<IAuthorService>().VerifyNoOtherCalls();
            Mocker.GetMock<ICatalogueIdentityRepository>().VerifyNoOtherCalls();
        }

        [TestCase(false)]
        [TestCase(true)]
        public void should_reject_legacy_apply_before_reading_or_changing_authors(bool queued)
        {
            Mocker.GetMock<IBookService>().Setup(service => service.GetBooksByAuthor(_duplicate.Id)).Returns(new List<Book>());
            Mocker.GetMock<IHistoryService>().Setup(service => service.GetByAuthor(_duplicate.Id, null)).Returns(new List<EntityHistory>());
            var command = new CanonicalizeAuthorsCommand { DryRun = false };
            Action apply;
            if (queued)
            {
                apply = () => Subject.Execute(command);
            }
            else
            {
                apply = () => Subject.CanonicalizeDuplicates(false, 0.95, 10);
            }

            apply.Should().Throw<CommandFailedException>().WithMessage("*ReconcileCatalogueAuthors*");
            Mocker.GetMock<IAuthorService>().VerifyNoOtherCalls();
            Mocker.GetMock<IBookService>().VerifyNoOtherCalls();
            Mocker.GetMock<IHistoryService>().VerifyNoOtherCalls();
            Mocker.GetMock<ICatalogueIdentityRepository>().VerifyNoOtherCalls();
        }
    }
}
