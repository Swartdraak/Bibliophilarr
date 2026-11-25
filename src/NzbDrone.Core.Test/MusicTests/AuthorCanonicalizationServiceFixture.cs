using System;
using System.Collections.Generic;
using FizzWare.NBuilder;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Http;
using NzbDrone.Core.Books;
using NzbDrone.Core.Books.Commands;
using NzbDrone.Core.Exceptions;
using NzbDrone.Core.History;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

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

        [TestCase("two independent works", true, true, 2)]
        [TestCase("one witness", true, false, 1)]
        [TestCase("same Open Library work", true, false, 2)]
        [TestCase("same Hardcover work", true, false, 1)]
        [TestCase("same ISBN", true, false, 0)]
        [TestCase("conflicting credited title", true, false, 2)]
        [TestCase("unverified ISBN", true, false, 2)]
        [TestCase("transport failure", true, false, 2)]
        [TestCase("supplementary pagination", false, false, 0)]
        [TestCase("second same-name candidate", true, false, 0)]
        public void should_recover_only_corroborated_author_identity_after_authoritative_early_end(
            string scenario, bool authoritative, bool reuse, int isbnLookups)
        {
            const string firstIsbn = "9781442482890";
            var secondIsbn = scenario == "same ISBN" ? firstIsbn : "9781455546176";
            const string firstTitle = "A Fixture Work";
            const string secondTitle = "Another Fixture Work";
            const string firstWork = "hardcover:work:20";
            var secondWork = scenario == "same Hardcover work" ? firstWork : "hardcover:work:21";
            var primary = _primary.ForeignAuthorId;
            var incoming = _duplicate.ForeignAuthorId;
            var incomingWorks = new List<Book> { Work(firstWork, firstTitle, firstIsbn) };
            if (scenario != "one witness")
            {
                incomingWorks.Add(Work(secondWork, secondTitle, secondIsbn));
            }

            var candidates = new List<Author> { _primary };
            if (scenario == "second same-name candidate")
            {
                candidates.Add(new Author { ForeignAuthorId = "openlibrary:author:OL99A", Name = _primary.Name });
            }

            Mocker.GetMock<IAuthorService>().Setup(service => service.GetAuthorsByCleanName(It.IsAny<string>())).Returns(candidates);
            var orchestrator = Mocker.GetMock<IMetadataProviderOrchestrator>();
            var incomplete = new MetadataProviderUnavailableException(
                "get-author-info",
                primary,
                new InvalidOperationException("Fixture pagination stopped early."),
                authoritative);
            orchestrator.Setup(service => service.GetAuthorInfo(primary, true)).Throws(incomplete);
            orchestrator.Setup(service => service.GetVerifiedAuthorWorkTitle(incoming, firstWork, firstIsbn)).Returns(firstTitle);
            orchestrator.Setup(service => service.GetVerifiedAuthorWorkTitle(incoming, secondWork, secondIsbn)).Returns(secondTitle);
            orchestrator.Setup(service => service.GetVerifiedOpenLibraryAuthorWorkByIsbn(primary, firstIsbn))
                .Returns(("openlibrary:work:OL10W", firstTitle));
            var second = orchestrator.Setup(service => service.GetVerifiedOpenLibraryAuthorWorkByIsbn(primary, secondIsbn));
            if (scenario == "transport failure")
            {
                second.Throws(new TimeoutException());
            }
            else if (scenario != "unverified ISBN" && scenario != "same ISBN")
            {
                var work = scenario == "same Open Library work" ? "openlibrary:work:OL10W" : "openlibrary:work:OL11W";
                second.Returns((work, scenario == "conflicting credited title" ? "Unrelated Work" : " another fixture work "));
            }

            var identity = Mocker.GetMock<ICatalogueIdentityRepository>();
            identity.Setup(service => service.TryAddSource(CatalogueEntityKind.Author, _primary.AuthorMetadataId, "Hardcover", "11", "two-isbn-title-author-work-witnesses"))
                .Returns(true);
            var match = () => Subject.TryReuseAuthorOnAdd(_duplicate, incomingWorks, new List<(Author, IReadOnlyList<Book>)>());

            if (reuse)
            {
                match().Should().BeSameAs(_primary);
            }
            else
            {
                match.Should().Throw<MetadataProviderUnavailableException>().Which.Should().BeSameAs(incomplete);
            }

            if (scenario == "transport failure")
            {
                ExceptionVerification.ExpectedWarns(1);
            }

            identity.Verify(service => service.TryAddSource(CatalogueEntityKind.Author, _primary.AuthorMetadataId, "Hardcover", "11", "two-isbn-title-author-work-witnesses"),
                reuse ? Times.Once() : Times.Never());
            orchestrator.Verify(service => service.GetVerifiedOpenLibraryAuthorWorkByIsbn(primary, It.IsAny<string>()), Times.Exactly(isbnLookups));
        }

        [TestCase(2, false, true, null)]
        [TestCase(1, false, false, null)]
        [TestCase(2, true, false, null)]
        [TestCase(2, false, true, "timeout")]
        [TestCase(1, false, false, "timeout")]
        [TestCase(2, true, false, "timeout")]
        [TestCase(2, false, false, "cooldown")]
        public void should_recover_an_incoming_open_library_author_only_with_eligible_hardcover_witnesses(int confirmedWorks, bool competingAuthor, bool reuse, string hardcoverFailure)
        {
            var incoming = _primary;
            var incumbent = _duplicate;
            incumbent.Metadata.Value.OpenLibraryAuthorId = null;
            var hardcoverBooks = new List<Book>
            {
                Work("hardcover:work:20", "A Fixture Work", "9781442482890"),
                Work("hardcover:work:21", "Another Fixture Work", "9781455546176")
            };
            var candidates = new List<Author> { incumbent };
            if (competingAuthor)
            {
                candidates.Add(new Author { ForeignAuthorId = "hardcover:author:42", Name = incumbent.Name });
            }

            Mocker.GetMock<IAuthorService>().Setup(service => service.GetAuthorsByCleanName(It.IsAny<string>())).Returns(candidates);
            var orchestrator = Mocker.GetMock<IMetadataProviderOrchestrator>();
            var incomplete = new MetadataProviderUnavailableException(
                "get-author-info",
                incoming.ForeignAuthorId,
                new InvalidOperationException("Fixture pagination stopped early."),
                true);
            orchestrator.Setup(service => service.GetAuthorInfo(incoming.ForeignAuthorId, true)).Throws(incomplete);
            var hardcoverBibliography = orchestrator.Setup(service => service.GetAuthorInfo(incumbent.ForeignAuthorId, true));
            if (hardcoverFailure != null)
            {
                incumbent.Books = hardcoverBooks;
                hardcoverBibliography.Throws(new MetadataProviderUnavailableException(
                    MetadataProviderOrchestrator.AuthorInfoOperation,
                    incumbent.ForeignAuthorId,
                    hardcoverFailure == "cooldown" ? new TooManyRequestsException("3600") : new TimeoutException()));
            }
            else
            {
                hardcoverBibliography.Returns(new Author { ForeignAuthorId = incumbent.ForeignAuthorId, Books = hardcoverBooks });
            }

            for (var i = 0; i < confirmedWorks; i++)
            {
                var work = hardcoverBooks[i];
                var isbn = work.Editions.Value[0].Isbn13;
                orchestrator.Setup(service => service.GetVerifiedAuthorWorkTitle(incumbent.ForeignAuthorId, work.ForeignBookId, isbn))
                    .Returns(work.Title);
                orchestrator.Setup(service => service.GetVerifiedOpenLibraryAuthorWorkByIsbn(incoming.ForeignAuthorId, isbn))
                    .Returns(($"openlibrary:work:OL{i + 50}W", work.Title));
            }

            var identity = Mocker.GetMock<ICatalogueIdentityRepository>();
            identity.Setup(service => service.TryAddSource(CatalogueEntityKind.Author, incumbent.AuthorMetadataId, "OpenLibrary", "OL10A", "two-isbn-title-author-work-witnesses"))
                .Returns(true);
            var match = () => Subject.TryReuseAuthorOnAdd(incoming, null, new List<(Author, IReadOnlyList<Book>)>());

            if (reuse)
            {
                match().Should().BeSameAs(incumbent);
            }
            else
            {
                match.Should().Throw<MetadataProviderUnavailableException>().Which.Should().BeSameAs(incomplete);
            }

            identity.Verify(service => service.TryAddSource(CatalogueEntityKind.Author, incumbent.AuthorMetadataId, "OpenLibrary", "OL10A", "two-isbn-title-author-work-witnesses"),
                reuse ? Times.Once() : Times.Never());
            if (hardcoverFailure == "cooldown")
            {
                ExceptionVerification.ExpectedWarns(1);
                orchestrator.Verify(service => service.GetVerifiedAuthorWorkTitle(incumbent.ForeignAuthorId, It.IsAny<string>(), It.IsAny<string>()), Times.Never());
            }

            orchestrator.Verify(service => service.GetVerifiedOpenLibraryAuthorWorkByIsbn(incoming.ForeignAuthorId, It.IsAny<string>()),
                Times.Exactly(competingAuthor || hardcoverFailure == "cooldown" ? 0 : confirmedWorks));
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
        public void should_reuse_the_exact_foreign_author_id_without_writing_a_second_author()
        {
            var incoming = new Author { ForeignAuthorId = _primary.ForeignAuthorId, Name = "Unrelated placeholder" };
            var authors = Mocker.GetMock<IAuthorService>();
            authors.Setup(service => service.FindById(incoming.ForeignAuthorId)).Returns(_primary);

            var match = Subject.TryFindCanonicalMatch(incoming, out var confidence, out var reason);

            match.Should().BeSameAs(_primary);
            confidence.Should().Be(1.0);
            reason.Should().Be("foreign-author-id match");
            authors.Verify(service => service.FindById(incoming.ForeignAuthorId), Times.Once());
            authors.Verify(service => service.AddAuthor(It.IsAny<Author>(), It.IsAny<bool>()), Times.Never());
            authors.Verify(service => service.UpdateAuthor(It.IsAny<Author>()), Times.Never());
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
