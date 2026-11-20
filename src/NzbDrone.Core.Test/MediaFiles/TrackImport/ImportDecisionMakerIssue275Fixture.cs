using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;
using System.Linq;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.DecisionEngine;
using NzbDrone.Core.Download;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.BookImport;
using NzbDrone.Core.MediaFiles.BookImport.Aggregation;
using NzbDrone.Core.MediaFiles.BookImport.Identification;
using NzbDrone.Core.MediaFiles.BookImport.Specifications;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.MediaFiles.BookImport
{
    // Issue #275 regression: GET /api/v1/manualimport leaves book/edition mapping NULL
    // -> permanent rejection "Couldn't find similar book" (ImportDecisionMaker.cs).
    //
    // When the identification step leaves a release without a catalogue Edition
    // (no usable identity in the file tags for the candidate service to match on),
    // ImportDecisionMaker must fall back to a bounded direct catalogue lookup from the
    // file's own book/author metadata so the item is not permanently rejected.
    [TestFixture]
    public class ImportDecisionMakerIssue275Fixture : FileSystemTest<ImportDecisionMaker>
    {
        private List<IFileInfo> _fileInfos;
        private ImportDecisionMakerConfig _idConfig;
        private string _filePath;

        [SetUp]
        public void Setup()
        {
            SetFiles(".epub");

            _idConfig = new ImportDecisionMakerConfig
            {
                Filter = FilterFilesType.None,
                NewDownload = true,
                SingleRelease = false,
                IncludeExisting = false,
                AddNewAuthors = false,
                KeepAllEditions = true
            };

            Mocker.GetMock<IImportRunTracker>()
                .Setup(s => s.CreateRun())
                .Returns(new ImportRunSummary());

            Mocker.GetMock<IMediaFileService>()
                .Setup(c => c.FilterUnchangedFiles(It.IsAny<List<IFileInfo>>(), It.IsAny<FilterFilesType>()))
                .Returns((List<IFileInfo> files, FilterFilesType filter) => files);

            Mocker.GetMock<IConfigService>()
                .SetupGet(x => x.ImportTagReadWorkerCount)
                .Returns(1);

            Mocker.GetMock<IAugmentingService>()
                .Setup(s => s.Augment(It.IsAny<LocalBook>(), It.IsAny<bool>()))
                .Returns((LocalBook t, bool b) => t);

            // The file DOES carry book/author metadata (the embedded audioTags from the
            // issue fixture), so the fallback has something to look up.
            Mocker.GetMock<IMetadataTagService>()
                .Setup(s => s.ReadTags(It.IsAny<IFileInfo>()))
                .Returns(new ParsedTrackInfo
                {
                    BookTitle = "Dark Crime",
                    Authors = new List<string> { "Christine Feehan" }
                });

            // Identification cannot resolve a catalogue edition from these tags: it
            // returns the release with Edition still null (the pre-fix condition).
            Mocker.GetMock<IIdentificationService>()
                .Setup(s => s.Identify(It.IsAny<List<LocalBook>>(), It.IsAny<IdentificationOverrides>(), It.IsAny<ImportDecisionMakerConfig>(), It.IsAny<ImportRunSummary>()))
                .Returns((List<LocalBook> tracks, IdentificationOverrides idOverrides, ImportDecisionMakerConfig config, ImportRunSummary summary) =>
                    new List<LocalEdition> { new LocalEdition(tracks) });

            // A resolvable release must clear the import-decision specs to be Approved.
            // The container's dynamic-registration fallback would otherwise hand the
            // ImportDecisionMaker AutoMoq proxy specs that return null from IsSatisfiedBy
            // (which NREs inside EvaluateSpec). Register explicit accepting mocks for the
            // concrete spec interfaces — the same GetMock-based approach the existing
            // ImportDecisionMakerFixture uses (it overrides the container's per-type mock,
            // so IEnumerable<spec> resolution picks up the accepting one).
            Mocker.GetMock<IImportDecisionEngineSpecification<LocalEdition>>()
                .Setup(c => c.IsSatisfiedBy(It.IsAny<LocalEdition>(), It.IsAny<DownloadClientItem>()))
                .Returns(Decision.Accept());

            Mocker.GetMock<IImportDecisionEngineSpecification<LocalBook>>()
                .Setup(c => c.IsSatisfiedBy(It.IsAny<LocalBook>(), It.IsAny<DownloadClientItem>()))
                .Returns(Decision.Accept());
        }

        private void SetFiles(params string[] extensions)
        {
            _fileInfos = new List<IFileInfo>();

            foreach (var extension in extensions)
            {
                _filePath = $@"C:\Test\Unsorted\Dark Crime{extension}".AsOsAgnostic();
                FileSystem.AddFile(_filePath, new MockFileData(string.Empty));
                _fileInfos.Add(DiskProvider.GetFileInfo(_filePath));
            }
        }

        private ImportDecision<LocalBook> GetSingleDecision()
        {
            var decisions = Subject.GetImportDecisions(_fileInfos, null, null, _idConfig);
            decisions.Should().HaveCount(1);
            return decisions.Single();
        }

        private (Book book, Edition edition) BuildMatch()
        {
            var author = new Author
            {
                Id = 42,
                ForeignAuthorId = "OL1A",
                Metadata = new AuthorMetadata { ForeignAuthorId = "OL1A", Name = "Christine Feehan" }
            };

            var book = new Book
            {
                Id = 7,
                ForeignBookId = "OL123W",
                Title = "Dark Crime",
                AuthorMetadataId = 42,
                Author = author,
                AuthorMetadata = new AuthorMetadata { ForeignAuthorId = "OL1A", Name = "Christine Feehan" },
                Editions = new List<Edition>()
            };

            var edition = new Edition
            {
                Id = 9,
                BookId = 7,
                ForeignEditionId = "OL321M",
                Title = "Dark Crime (eBook)",
                IsEbook = true,
                Book = book
            };

            book.Editions.Value.Add(edition);
            return (book, edition);
        }

        [Test]
        public void should_select_only_the_exact_work_for_a_background_library_fallback()
        {
            _idConfig.NewDownload = false;
            var (wrong, _) = BuildMatch();
            wrong.Title = "A Different Story";
            wrong.ForeignBookId = "OL456W";
            var (right, edition) = BuildMatch();
            GivenFallbackWorks(wrong, right);

            var decision = GetSingleDecision();

            decision.Approved.Should().BeTrue();
            decision.Item.Edition.Should().BeSameAs(edition);
        }

        [Test]
        public void should_reject_an_exact_title_with_a_different_author_on_background_fallback()
        {
            _idConfig.NewDownload = false;
            var (book, _) = BuildMatch();
            book.AuthorMetadata.Value.Name = "A Different Author";
            book.Author.Value.Metadata.Value.Name = "A Different Author";
            GivenFallbackWorks(book);

            GetSingleDecision().Approved.Should().BeFalse();
        }

        [Test]
        public void should_hold_distinct_corroborated_works_even_if_only_one_has_the_file_format()
        {
            _idConfig.NewDownload = false;
            var (first, _) = BuildMatch();
            var (second, otherEdition) = BuildMatch();
            second.Id = 8;
            second.ForeignBookId = "OL456W";
            otherEdition.Format = "Audiobook";
            otherEdition.IsEbook = false;
            GivenFallbackWorks(first, second);

            GetSingleDecision().Approved.Should().BeFalse();
        }

        [Test]
        public void should_deduplicate_one_authoritative_work_before_choosing_a_compatible_edition()
        {
            _idConfig.NewDownload = false;
            var (first, otherEdition) = BuildMatch();
            otherEdition.Format = "Audiobook";
            otherEdition.IsEbook = false;
            var (second, edition) = BuildMatch();
            second.ForeignBookId = "openlibrary:work:OL123W";
            GivenFallbackWorks(first, second);

            var decision = GetSingleDecision();

            decision.Approved.Should().BeTrue();
            decision.Item.Edition.Should().BeSameAs(edition);
        }

        [TestCase(false, false)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public void should_require_existing_author_identity_instead_of_a_matching_display_name_on_background_fallback(
            bool verifiedAuthorAlias, bool allowNewAuthors)
        {
            _idConfig.NewDownload = false;
            _idConfig.AddNewAuthors = allowNewAuthors;
            var (book, _) = BuildMatch();
            book.Id = 0;
            book.Author.Value.Id = 0;
            var localAuthor = new Author { Id = 42, Name = "Christine Feehan", ForeignAuthorId = "openlibrary:author:OL1A" };
            Mocker.GetMock<IAuthorService>().Setup(service => service.FindByName(localAuthor.Name)).Returns(localAuthor);
            Mocker.GetMock<IAuthorService>().Setup(service => service.FindById("OL1A"))
                .Returns(verifiedAuthorAlias ? localAuthor : null);
            GivenFallbackWorks(book);

            var decision = GetSingleDecision();

            decision.Approved.Should().Be(verifiedAuthorAlias);
            if (verifiedAuthorAlias)
            {
                decision.Item.Author.Should().BeSameAs(localAuthor);
            }
        }

        [Test]
        public void should_not_auto_add_a_provider_author_identified_only_by_name_on_background_fallback()
        {
            _idConfig.NewDownload = false;
            _idConfig.AddNewAuthors = true;
            var (book, _) = BuildMatch();
            book.Id = 0;
            book.Author.Value.Id = 0;
            book.Author.Value.ForeignAuthorId = "googlebooks:author:Christine%20Feehan";
            book.AuthorMetadata.Value.ForeignAuthorId = book.Author.Value.ForeignAuthorId;
            GivenFallbackWorks(book);

            GetSingleDecision().Approved.Should().BeFalse();
        }

        [TestCase(false)]
        [TestCase(true)]
        public void should_score_exact_background_fallback_on_the_real_match_policy(bool printOnly)
        {
            _idConfig.NewDownload = false;
            var (book, edition) = BuildMatch();
            edition.Title = book.Title;
            edition.Format = printOnly ? "Print" : "Ebook";
            edition.IsEbook = !printOnly;
            GivenFallbackWorks(book);
            GivenRealBookMatchPolicy();

            var decision = GetSingleDecision();

            decision.Approved.Should().BeTrue();
            decision.Item.Edition.Should().BeSameAs(edition);
        }

        [Test]
        public void should_reject_conflicting_edition_isbn_instead_of_using_a_cleared_distance()
        {
            _idConfig.NewDownload = false;
            GivenReliableFileIsbn();
            Mocker.GetMock<IIdentificationService>()
                .Setup(service => service.Identify(It.IsAny<List<LocalBook>>(), It.IsAny<IdentificationOverrides>(), It.IsAny<ImportDecisionMakerConfig>(), It.IsAny<ImportRunSummary>()))
                .Returns((List<LocalBook> tracks, IdentificationOverrides _, ImportDecisionMakerConfig _, ImportRunSummary _) =>
                    new List<LocalEdition> { new LocalEdition(tracks) { Distance = new Distance() } });
            var (book, edition) = BuildMatch();
            edition.Title = book.Title;
            edition.Isbn13 = "9781455546176";
            GivenFallbackWorks(book);
            GivenRealBookMatchPolicy();

            var decision = GetSingleDecision();

            decision.Approved.Should().BeFalse();
            decision.Rejections.Should().Contain(rejection => rejection.Reason.Contains("isbn", StringComparison.Ordinal));
        }

        [Test]
        public void should_prefer_a_compatible_edition_matching_the_files_verified_isbn()
        {
            _idConfig.NewDownload = false;
            GivenReliableFileIsbn();
            var (book, edition) = BuildMatch();
            edition.Title = book.Title;
            edition.Isbn13 = "9781455546176";
            var matching = new Edition
            {
                Book = book,
                BookId = book.Id,
                ForeignEditionId = "OL456M",
                Title = book.Title,
                IsEbook = true,
                Isbn13 = "9781442482890"
            };
            book.Editions.Value.Add(matching);
            GivenFallbackWorks(book);
            GivenRealBookMatchPolicy();

            var decision = GetSingleDecision();

            decision.Approved.Should().BeTrue();
            decision.Item.Edition.Should().BeSameAs(matching);
        }

        private void GivenReliableFileIsbn()
        {
            Mocker.GetMock<IMetadataTagService>().Setup(service => service.ReadTags(It.IsAny<IFileInfo>()))
                .Returns(new ParsedTrackInfo
                {
                    BookTitle = "Dark Crime",
                    BookTitleConfidence = 0.9,
                    Authors = new List<string> { "Christine Feehan" },
                    AuthorConfidence = 0.9,
                    Isbn = "9781442482890",
                    IsbnConfidence = 0.97
                });
        }

        private void GivenRealBookMatchPolicy()
        {
            var settings = Mocker.GetMock<IConfigService>();
            settings.SetupGet(config => config.BookImportMatchThresholdPercent).Returns(70);
            var specification = new CloseBookMatchSpecification(settings.Object, Mocker.GetMock<IBookService>().Object, TestLogger);
            Mocker.GetMock<IImportDecisionEngineSpecification<LocalEdition>>()
                .Setup(service => service.IsSatisfiedBy(It.IsAny<LocalEdition>(), It.IsAny<DownloadClientItem>()))
                .Returns((LocalEdition release, DownloadClientItem download) => specification.IsSatisfiedBy(release, download));
        }

        private void GivenFallbackWorks(params Book[] books)
        {
            Mocker.GetMock<IMetadataProviderOrchestrator>()
                .Setup(orchestrator => orchestrator.SearchForNewBook("Dark Crime", "Christine Feehan", true))
                .Returns(books.ToList());
        }

        [Test]
        public void should_match_catalogue_book_when_identification_left_edition_null()
        {
            var (book, edition) = BuildMatch();

            Mocker.GetMock<IMetadataProviderOrchestrator>()
                .Setup(o => o.SearchForNewBook(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>()))
                .Returns(new List<Book> { book });

            var decision = GetSingleDecision();

            // The permanent "Couldn't find similar book" rejection must be gone.
            decision.Rejections.Should().BeEmpty();
            decision.Approved.Should().BeTrue();
            decision.Item.Book.Should().NotBeNull();
            decision.Item.Book.Id.Should().Be(7);
            decision.Item.Edition.Should().NotBeNull();
            decision.Item.Edition.ForeignEditionId.Should().Be("OL321M");
            decision.Item.Author.Should().NotBeNull();
            decision.Item.Author.Id.Should().Be(42);
        }

        [Test]
        public void should_use_a_compatible_edition_of_the_same_work()
        {
            SetFiles(".m4b");
            var (book, _) = BuildMatch();
            var audiobook = new Edition
            {
                ForeignEditionId = "OL456M",
                Format = "Audiobook",
                Book = book
            };
            book.Editions.Value.Add(audiobook);

            Mocker.GetMock<IMetadataProviderOrchestrator>()
                .Setup(o => o.SearchForNewBook("Dark Crime", "Christine Feehan", true))
                .Returns(new List<Book> { book });

            var decision = GetSingleDecision();

            decision.Approved.Should().BeTrue();
            decision.Item.Book.Should().BeSameAs(book);
            decision.Item.Edition.Should().BeSameAs(audiobook);
        }

        [Test]
        public void should_leave_audiobook_unmapped_when_only_a_different_work_has_a_compatible_edition()
        {
            SetFiles(".m4b");
            var (book, _) = BuildMatch();
            var (unrelatedBook, unrelatedEdition) = BuildMatch();
            unrelatedBook.ForeignBookId = "OL789W";
            unrelatedBook.Title = "Dark Song";
            unrelatedEdition.ForeignEditionId = "OL789M";
            unrelatedEdition.IsEbook = false;
            unrelatedEdition.Format = "Audiobook";

            Mocker.GetMock<IMetadataProviderOrchestrator>()
                .Setup(o => o.SearchForNewBook("Dark Crime", "Christine Feehan", true))
                .Returns(new List<Book> { book, unrelatedBook });

            var decision = GetSingleDecision();

            decision.Approved.Should().BeFalse();
            decision.Item.Book.Should().BeNull();
            decision.Item.Edition.Should().BeNull();
        }

        [Test]
        public void should_reject_a_candidate_that_only_matches_some_release_paths()
        {
            SetFiles(".epub", ".m4b");
            var (book, _) = BuildMatch();

            Mocker.GetMock<IMetadataProviderOrchestrator>()
                .Setup(o => o.SearchForNewBook("Dark Crime", "Christine Feehan", true))
                .Returns(new List<Book> { book });

            var decisions = Subject.GetImportDecisions(_fileInfos, null, null, _idConfig);

            decisions.Should().HaveCount(2).And.OnlyContain(decision => !decision.Approved && decision.Item.Edition == null);
        }

        [Test]
        public void should_keep_permanent_rejection_when_no_title_or_author_metadata()
        {
            Mocker.GetMock<IMetadataTagService>()
                .Setup(s => s.ReadTags(It.IsAny<IFileInfo>()))
                .Returns(new ParsedTrackInfo());

            var orchestratorMock = Mocker.GetMock<IMetadataProviderOrchestrator>();
            orchestratorMock
                .Setup(o => o.SearchForNewBook(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>()))
                .Returns(new List<Book>());

            var decision = GetSingleDecision();

            // Genuinely unresolvable: original permanent rejection, unchanged.
            // The reason text is LocalEdition.ToString() => "[" + Path.GetDirectoryName(file) + "]".
            decision.Rejections.Should().ContainSingle()
                .Which.Reason.Should().Be($"Couldn't find similar book for [{Path.GetDirectoryName(_filePath)}]");
            decision.Approved.Should().BeFalse();
            decision.Item.Book.Should().BeNull();

            orchestratorMock.Verify(
                o => o.SearchForNewBook(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>()),
                Times.Never);
        }

        [Test]
        public void should_keep_permanent_rejection_when_catalogue_lookup_finds_nothing()
        {
            var orchestratorMock = Mocker.GetMock<IMetadataProviderOrchestrator>();
            orchestratorMock
                .Setup(o => o.SearchForNewBook(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>()))
                .Returns(new List<Book>());

            var decision = GetSingleDecision();

            // The reason text is LocalEdition.ToString() => "[" + Path.GetDirectoryName(file) + "]".
            decision.Rejections.Should().ContainSingle()
                .Which.Reason.Should().Be($"Couldn't find similar book for [{Path.GetDirectoryName(_filePath)}]");
            decision.Approved.Should().BeFalse();

            orchestratorMock.Verify(
                o => o.SearchForNewBook("Dark Crime", "Christine Feehan", true),
                Times.Once);
        }

        [Test]
        public void should_keep_permanent_rejection_when_catalogue_lookup_throws()
        {
            Mocker.GetMock<IMetadataProviderOrchestrator>()
                .Setup(o => o.SearchForNewBook(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>()))
                .Throws(() => new InvalidOperationException("provider unavailable"));

            var decision = GetSingleDecision();

            // The reason text is LocalEdition.ToString() => "[" + Path.GetDirectoryName(file) + "]".
            decision.Rejections.Should().ContainSingle()
                .Which.Reason.Should().Be($"Couldn't find similar book for [{Path.GetDirectoryName(_filePath)}]");
            decision.Approved.Should().BeFalse();
        }

        [Test]
        public void should_still_reject_when_identification_failed_and_no_metadata()
        {
            // Even with identification returning a null-edition release and the file
            // carrying no usable book/author metadata, the pipeline must not throw and
            // the item is rejected (not imported).
            //
            // NOTE (pre-existing production path, unchanged by issue #275): the real
            // IMetadataTagService.ReadTags never returns null — a file with no tags
            // yields a ParsedTrackInfo() with empty Authors/TrackNumbers. A literal null
            // would throw in ImportDecisionMaker.GetLocalTracks (fileTrackInfo.TrackNumbers
            // deref) before it could reach the rejection path, so this fixture models the
            // realistic untagged-file input (empty ParsedTrackInfo) instead.
            Mocker.GetMock<IMetadataTagService>()
                .Setup(s => s.ReadTags(It.IsAny<IFileInfo>()))
                .Returns(new ParsedTrackInfo());

            var decision = GetSingleDecision();

            decision.Rejections.Should().NotBeEmpty();
            decision.Approved.Should().BeFalse();
            decision.Item.Book.Should().BeNull();
        }

        // Issue #340 (complement of the #25 fix): with NO metadata provider configured
        // the orchestrator returns null from SearchForNewBook. The #275 fallback must
        // guard that null before books.Where(...) so the pipeline degrades to the
        // original permanent rejection instead of crashing (and leaving the release
        // with Edition=null so 0 files import while the endpoint still returns 202).
        [Test]
        public void should_keep_permanent_rejection_when_catalogue_lookup_returns_null_no_provider_configured()
        {
            Mocker.GetMock<IMetadataProviderOrchestrator>()
                .Setup(o => o.SearchForNewBook(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>()))
                .Returns((List<Book>)null);

            var decision = GetSingleDecision();

            // No provider configured -> no candidate -> original permanent rejection,
            // produced without any exception escaping the fallback.
            decision.Rejections.Should().ContainSingle()
                .Which.Reason.Should().Be($"Couldn't find similar book for [{Path.GetDirectoryName(_filePath)}]");
            decision.Approved.Should().BeFalse();
            decision.Item.Book.Should().BeNull();
        }
    }
}
