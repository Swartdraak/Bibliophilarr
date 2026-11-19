using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.MediaFiles.BookImport.Identification;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MediaFiles.BookImport.Identification
{
    // Issue #340 regression: POST /api/v1/manualimport accepted (202) but no BookFile
    // created. Root cause: with NO metadata provider configured (a legal configuration
    // state), MetadataProviderOrchestrator.SearchForNewBook returns null (ExecuteFirst
    // returns null when no enabled provider yields a result), and
    // CandidateService.SearchPrimary immediately invokes results.Any() on it, throwing
    // an unhandled ArgumentNullException (Parameter: 'source'). The exception escapes
    // GetRemoteCandidates, aborts identification for the release, leaves
    // release.Edition == null, GetDecision rejects it and ImportApprovedBooks imports
    // 0 files while the endpoint still returns 202.
    //
    // The #275 fallback in ImportDecisionMaker has the same null hazard (books.Where on
    // the null result); it is swallowed by its catch, but leaves the release
    // unrecoverable.
    //
    // Pinned here with the orchestrator mock returning null — exactly the observable
    // behavior of the real orchestrator with no provider configured:
    //   1. GetRemoteCandidates must not throw and must return an empty candidate set
    //      (title+author, title-only, author-only, swapped-pair and ISBN-contextual
    //      paths all go through SearchPrimary / SearchForNewBook).
    //   2. The pre-existing exception path (OpenLibraryException) stays covered by
    //      should_not_throw_on_openlibrary_exception in CandidateServiceFixture.
    [TestFixture]
    public class CandidateServiceIssue340Fixture : CoreTest<CandidateService>
    {
        [SetUp]
        public void SetUp()
        {
            Mocker.GetMock<IConfigService>()
                .SetupGet(x => x.IsbnContextFallbackLimit)
                .Returns(3);

            Mocker.GetMock<IMetadataQueryNormalizationService>()
                .Setup(s => s.ExpandAuthorAliases(It.IsAny<IEnumerable<string>>()))
                .Returns((IEnumerable<string> authors) => (authors ?? new List<string>()).Where(a => !string.IsNullOrWhiteSpace(a)).ToList());

            Mocker.GetMock<IMetadataQueryNormalizationService>()
                .Setup(s => s.BuildTitleVariants(It.IsAny<string>()))
                .Returns((string title) => string.IsNullOrWhiteSpace(title) ? new List<string>() : new List<string> { title });

            Mocker.SetConstant<IEnumerable<IBookSearchFallbackProvider>>(new List<IBookSearchFallbackProvider>());
            Mocker.GetMock<IBookSearchFallbackExecutionService>()
                .Setup(x => x.Search(It.IsAny<IBookSearchFallbackProvider>(), It.IsAny<string>(), It.IsAny<string>()))
                .Returns((IBookSearchFallbackProvider provider, string title, string author) => provider.Search(title, author));
        }

        private static LocalEdition BuildEdition(string title, string author, string isbn = null)
        {
            var trackInfo = new ParsedTrackInfo
            {
                BookTitle = title,
                Authors = author == null ? new List<string>() : new List<string> { author }
            };

            if (isbn != null)
            {
                trackInfo.Isbn = isbn;
            }

            return new LocalEdition
            {
                LocalBooks = new List<LocalBook>
                {
                    new LocalBook
                    {
                        Path = @"/data/Author/Book/file.epub",
                        FileTrackInfo = trackInfo
                    }
                }
            };
        }

        [Test]
        public void should_not_throw_and_return_no_candidates_when_orchestrator_returns_null_for_no_configured_provider()
        {
            // No provider configured: the real orchestrator returns null here.
            Mocker.GetMock<IMetadataProviderOrchestrator>()
                .Setup(s => s.SearchForNewBook(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>()))
                .Returns((List<Book>)null);

            var edition = BuildEdition("Dark Crime", "Christine Feehan");

            var act = () => Subject.GetRemoteCandidates(edition, null).ToList();

            // Pre-fix this threw ArgumentNullException (Parameter 'source') at
            // CandidateService.SearchPrimary (results.Any() on null).
            var candidates = act.Should().NotThrow().Which;
            candidates.Should().BeEmpty();
        }

        [Test]
        public void should_not_throw_and_return_no_candidates_when_orchestrator_returns_null_for_title_only_search()
        {
            Mocker.GetMock<IMetadataProviderOrchestrator>()
                .Setup(s => s.SearchForNewBook(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>()))
                .Returns((List<Book>)null);

            var edition = BuildEdition("Book Without Author", null);

            var candidates = Subject.GetRemoteCandidates(edition, null).ToList();
            candidates.Should().BeEmpty();
        }

        [Test]
        public void should_not_throw_and_return_no_candidates_when_orchestrator_returns_null_for_author_only_search()
        {
            Mocker.GetMock<IMetadataProviderOrchestrator>()
                .Setup(s => s.SearchForNewBook(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>()))
                .Returns((List<Book>)null);

            var edition = BuildEdition(null, "Fallback Author");

            var candidates = Subject.GetRemoteCandidates(edition, null).ToList();
            candidates.Should().BeEmpty();
        }

        [Test]
        public void should_not_throw_when_orchestrator_returns_null_in_isbn_contextual_fallback()
        {
            // An ISBN triggers the primary SearchByIsbn path first (also null with no
            // provider), then the ISBN contextual fallback, which calls
            // SearchForNewBook(title, author, true) — also null pre-fix.
            Mocker.GetMock<IMetadataProviderOrchestrator>()
                .Setup(s => s.SearchByIsbn(It.IsAny<string>()))
                .Returns((List<Book>)null);

            Mocker.GetMock<IMetadataProviderOrchestrator>()
                .Setup(s => s.SearchForNewBook(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>()))
                .Returns((List<Book>)null);

            var edition = BuildEdition("Dark Crime", "Christine Feehan", "9780141180027");

            var candidates = Subject.GetRemoteCandidates(edition, null).ToList();
            candidates.Should().BeEmpty();
        }
    }
}
