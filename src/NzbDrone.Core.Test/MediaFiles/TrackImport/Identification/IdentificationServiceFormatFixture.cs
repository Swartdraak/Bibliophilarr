using System.Collections.Generic;
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
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MediaFiles.TrackImport.Identification
{
    [TestFixture]
    public class IdentificationServiceFormatFixture : CoreTest<IdentificationService>
    {
        private Author _author;
        private Book _book;
        private Edition _ebookEdition;
        private Edition _audiobookEdition;
        private IdentificationOverrides _overrides;
        private ImportDecisionMakerConfig _config;

        [SetUp]
        public void Setup()
        {
            _author = new Author
            {
                Id = 1,
                Name = "Test Author",
                ForeignAuthorId = "test-author-1"
            };

            _book = new Book
            {
                Id = 1,
                Title = "Test Book",
                ForeignBookId = "test-book-1",
                AuthorMetadata = new AuthorMetadata { Name = "Test Author", ForeignAuthorId = "test-author-1" }
            };
            _book.Author = _author;

            _ebookEdition = new Edition
            {
                Id = 10,
                BookId = 1,
                Title = "Test Book (EPUB)",
                ForeignEditionId = "edition-ebook",
                IsEbook = true,
                Monitored = true,
                Book = _book,
                BookFiles = new List<BookFile>()
            };

            _audiobookEdition = new Edition
            {
                Id = 20,
                BookId = 1,
                Title = "Test Book (Audiobook)",
                ForeignEditionId = "edition-audiobook",
                IsEbook = false,
                Monitored = true,
                Book = _book,
                BookFiles = new List<BookFile>()
            };

            _book.Editions = new List<Edition> { _ebookEdition, _audiobookEdition };

            _overrides = new IdentificationOverrides { Author = _author, Book = _book };
            _config = new ImportDecisionMakerConfig
            {
                Filter = FilterFilesType.None,
                NewDownload = true,
                SingleRelease = true,
                IncludeExisting = false,
                AddNewAuthors = false,
                KeepAllEditions = false
            };

            // Track grouping returns input as single release
            Mocker.GetMock<ITrackGroupingService>()
                .Setup(s => s.GroupTracks(It.IsAny<List<LocalBook>>()))
                .Returns<List<LocalBook>>(l => new List<LocalEdition> { new LocalEdition(l) });

            // Augmenting is a no-op
            Mocker.GetMock<IAugmentingService>()
                .Setup(s => s.Augment(It.IsAny<LocalEdition>()));

            // Author exists locally
            Mocker.GetMock<IAuthorService>()
                .Setup(s => s.FindById(It.IsAny<string>()))
                .Returns(_author);

            Mocker.GetMock<IAuthorService>()
                .Setup(s => s.FindByName(It.IsAny<string>()))
                .Returns(_author);
        }

        private List<LocalBook> CreateAudiobookLocalBooks()
        {
            return new List<LocalBook>
            {
                new LocalBook
                {
                    Path = "/media/audiobooks/Test Book - Part 01.m4b",
                    Quality = new QualityModel(Quality.M4B),
                    FileTrackInfo = new ParsedTrackInfo(),
                    Part = 1,
                    Size = 100000,
                    ExistingFile = false
                }
            };
        }

        private List<LocalBook> CreateEbookLocalBooks()
        {
            return new List<LocalBook>
            {
                new LocalBook
                {
                    Path = "/media/ebooks/Test Book.epub",
                    Quality = new QualityModel(Quality.EPUB),
                    FileTrackInfo = new ParsedTrackInfo(),
                    Part = 1,
                    Size = 5000,
                    ExistingFile = false
                }
            };
        }

        private void GivenBothEditionsAsCandidates()
        {
            var ebookCandidate = new CandidateEdition
            {
                Edition = _ebookEdition,
                ExistingFiles = new List<BookFile>()
            };

            var audiobookCandidate = new CandidateEdition
            {
                Edition = _audiobookEdition,
                ExistingFiles = new List<BookFile>()
            };

            Mocker.GetMock<ICandidateService>()
                .Setup(s => s.GetDbCandidatesFromTags(It.IsAny<LocalEdition>(), It.IsAny<IdentificationOverrides>(), It.IsAny<bool>()))
                .Returns(new List<CandidateEdition> { ebookCandidate, audiobookCandidate });
        }

        [Test]
        public void should_prefer_audiobook_edition_when_importing_m4b_with_flag_on()
        {
            Mocker.GetMock<IConfigService>()
                .Setup(s => s.EnableDualFormatTracking).Returns(true);
            Mocker.GetMock<IConfigService>()
                .Setup(s => s.IdentificationWorkerCount).Returns(1);

            GivenBothEditionsAsCandidates();

            var localBooks = CreateAudiobookLocalBooks();
            var results = Subject.Identify(localBooks, _overrides, _config);

            results.Should().HaveCount(1);

            var result = results.First();
            result.Edition.Should().NotBeNull();

            // With flag on, should prefer the audiobook edition for m4b files
            result.Edition.IsEbook.Should().BeFalse();
        }

        [Test]
        public void should_prefer_ebook_edition_when_importing_epub_with_flag_on()
        {
            Mocker.GetMock<IConfigService>()
                .Setup(s => s.EnableDualFormatTracking).Returns(true);
            Mocker.GetMock<IConfigService>()
                .Setup(s => s.IdentificationWorkerCount).Returns(1);

            GivenBothEditionsAsCandidates();

            var localBooks = CreateEbookLocalBooks();
            var results = Subject.Identify(localBooks, _overrides, _config);

            results.Should().HaveCount(1);

            var result = results.First();
            result.Edition.Should().NotBeNull();

            // With flag on, should prefer the ebook edition for epub files
            result.Edition.IsEbook.Should().BeTrue();
        }

        [Test]
        public void should_still_select_best_match_when_flag_off()
        {
            Mocker.GetMock<IConfigService>()
                .Setup(s => s.EnableDualFormatTracking).Returns(false);
            Mocker.GetMock<IConfigService>()
                .Setup(s => s.IdentificationWorkerCount).Returns(1);

            GivenBothEditionsAsCandidates();

            var localBooks = CreateAudiobookLocalBooks();
            var results = Subject.Identify(localBooks, _overrides, _config);

            results.Should().HaveCount(1);

            // With flag off, no format preference applied — result may be either edition
            // depending on distance calculation. The key assertion is that identification
            // completes without error.
        }
    }
}
