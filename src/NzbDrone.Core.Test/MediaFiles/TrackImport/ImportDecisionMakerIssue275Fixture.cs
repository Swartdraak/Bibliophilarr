using System;
using System.Collections.Generic;
using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;
using System.Linq;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.BookImport;
using NzbDrone.Core.MediaFiles.BookImport.Aggregation;
using NzbDrone.Core.MediaFiles.BookImport.Identification;
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
            _filePath = @"C:\Test\Unsorted\Dark Crime.epub".AsOsAgnostic();
            FileSystem.AddFile(_filePath, new MockFileData(string.Empty));
            _fileInfos = new List<IFileInfo> { DiskProvider.GetFileInfo(_filePath) };

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
            decision.Rejections.Should().ContainSingle()
                .Which.Reason.Should().Be("Couldn't find similar book for [" + _filePath + "]");
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

            decision.Rejections.Should().ContainSingle()
                .Which.Reason.Should().Be("Couldn't find similar book for [" + _filePath + "]");
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

            decision.Rejections.Should().ContainSingle()
                .Which.Reason.Should().Be("Couldn't find similar book for [" + _filePath + "]");
            decision.Approved.Should().BeFalse();
        }

        [Test]
        public void should_still_reject_when_identification_failed_and_no_metadata()
        {
            // Even with identification returning a null-edition release and the file
            // carrying no usable book/author metadata, the pipeline must not throw and
            // the item is rejected (not imported).
            Mocker.GetMock<IMetadataTagService>()
                .Setup(s => s.ReadTags(It.IsAny<IFileInfo>()))
                .Returns(null as ParsedTrackInfo);

            var decision = GetSingleDecision();

            decision.Rejections.Should().NotBeEmpty();
            decision.Approved.Should().BeFalse();
            decision.Item.Book.Should().BeNull();
        }
    }
}
