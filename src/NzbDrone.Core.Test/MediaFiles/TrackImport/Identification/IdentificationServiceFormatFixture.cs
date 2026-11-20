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

        [TestCase(false)]
        [TestCase(true)]
        public void should_try_remote_when_only_local_candidate_is_the_wrong_format(bool dualFormatTracking)
        {
            Mocker.GetMock<IConfigService>()
                .Setup(s => s.EnableDualFormatTracking).Returns(dualFormatTracking);
            Mocker.GetMock<IConfigService>()
                .Setup(s => s.IdentificationWorkerCount).Returns(1);

            _ebookEdition.Title = "Test Book";
            _ebookEdition.Format = "EPUB";
            _audiobookEdition.Format = "Audio Book";
            _config.KeepAllEditions = true;

            var localBooks = CreateAudiobookLocalBooks();
            localBooks.Single().FileTrackInfo.BookTitle = "Test Book";
            localBooks.Single().FileTrackInfo.Authors = new List<string> { "Test Author" };
            Mocker.GetMock<ICandidateService>()
                .Setup(s => s.GetDbCandidatesFromTags(It.IsAny<LocalEdition>(), It.IsAny<IdentificationOverrides>(), It.IsAny<bool>()))
                .Returns(new List<CandidateEdition> { new CandidateEdition { Edition = _ebookEdition, ExistingFiles = new List<BookFile>() } });
            Mocker.GetMock<ICandidateService>()
                .Setup(s => s.GetRemoteCandidates(It.IsAny<LocalEdition>(), It.IsAny<IdentificationOverrides>()))
                .Returns(new List<CandidateEdition> { new CandidateEdition { Edition = _audiobookEdition, ExistingFiles = new List<BookFile>() } });

            var results = Subject.Identify(localBooks, _overrides, _config);

            results.Single().Edition.Should().BeSameAs(_audiobookEdition);
            localBooks.Single().Edition.Should().BeSameAs(_audiobookEdition);
        }

        [TestCase(false, false)]
        [TestCase(true, true)]
        public void should_only_allow_name_based_existing_author_lookup_for_requested_imports(bool newDownload, bool expectedMatch)
        {
            _config.NewDownload = newDownload;
            _config.KeepAllEditions = true;
            _book.Id = 0;
            _author.Id = 0;
            _audiobookEdition.Format = "Audio Book";
            Mocker.GetMock<IAuthorService>().Setup(service => service.FindById(It.IsAny<string>())).Returns((Author)null);
            Mocker.GetMock<IAuthorService>().Setup(service => service.FindByName("Test Author")).Returns(new Author { Id = 42 });
            Mocker.GetMock<ICandidateService>()
                .Setup(service => service.GetDbCandidatesFromTags(It.IsAny<LocalEdition>(), It.IsAny<IdentificationOverrides>(), It.IsAny<bool>()))
                .Returns(new List<CandidateEdition>());
            Mocker.GetMock<ICandidateService>()
                .Setup(service => service.GetRemoteCandidates(It.IsAny<LocalEdition>(), It.IsAny<IdentificationOverrides>()))
                .Returns(new List<CandidateEdition> { new CandidateEdition { Edition = _audiobookEdition, ExistingFiles = new List<BookFile>() } });

            var tracks = CreateAudiobookLocalBooks();
            tracks.Single().FileTrackInfo.BookTitle = "Test Book";
            tracks.Single().FileTrackInfo.Authors = new List<string> { "Test Author" };
            var result = Subject.Identify(tracks, new IdentificationOverrides(), _config).Single();

            (result.Edition != null).Should().Be(expectedMatch);
            Mocker.GetMock<IAuthorService>().Verify(service => service.FindByName("Test Author"), newDownload ? Times.Once() : Times.Never());
        }

        [Test]
        public void should_leave_ebook_unmapped_when_an_incompatible_edition_is_explicitly_selected()
        {
            _audiobookEdition.Format = "M4B";
            _overrides.Edition = _audiobookEdition;
            var tracks = CreateEbookLocalBooks();

            Subject.Identify(tracks, _overrides, _config).Single().Edition.Should().BeNull();
            tracks.Single().Edition.Should().BeNull();
        }

        [Test]
        public void should_exclude_existing_tracks_from_the_other_format()
        {
            Mocker.GetMock<IConfigService>().Setup(s => s.EnableDualFormatTracking).Returns(true);
            _config.IncludeExisting = true;
            _config.KeepAllEditions = true;
            var tracks = CreateAudiobookLocalBooks();
            var existingEbook = new BookFile { Path = CreateEbookLocalBooks().Single().Path, EditionId = _ebookEdition.Id };
            Mocker.GetMock<ICandidateService>()
                .Setup(s => s.GetDbCandidatesFromTags(It.IsAny<LocalEdition>(), It.IsAny<IdentificationOverrides>(), true))
                .Returns(new List<CandidateEdition> { new CandidateEdition { Edition = _audiobookEdition, ExistingFiles = new List<BookFile> { existingEbook } } });

            var release = Subject.Identify(tracks, _overrides, _config).Single();

            release.Edition.Should().BeSameAs(_audiobookEdition);
            release.LocalBooks.Should().ContainSingle().Which.Should().BeSameAs(tracks.Single());
        }

        [Test]
        public void should_group_both_media_formats_separately_in_single_release_mode()
        {
            var localBooks = CreateAudiobookLocalBooks().Concat(CreateEbookLocalBooks()).ToList();

            Subject.GetLocalBookReleases(localBooks, true).Select(x => x.LocalBooks.Single())
                .Should().Equal(localBooks);
        }
    }
}
