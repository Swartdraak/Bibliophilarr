using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Moq;
using NLog;
using NLog.Config;
using NLog.Targets;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.DecisionEngine;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.BookImport;
using NzbDrone.Core.MediaFiles.BookImport.Aggregation;
using NzbDrone.Core.MediaFiles.BookImport.Identification;
using NzbDrone.Core.MetadataSource;
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
            GivenRemoteCandidates(new CandidateEdition { Edition = _audiobookEdition, ExistingFiles = new List<BookFile>() });

            var results = Subject.Identify(localBooks, _overrides, _config);

            results.Single().Edition.Should().BeSameAs(_audiobookEdition);
            localBooks.Single().Edition.Should().BeSameAs(_audiobookEdition);
        }

        [Test]
        public void should_retain_low_confidence_reason_when_holding_a_new_author_match()
        {
            var messages = new MemoryTarget { Layout = "${message}" };
            var logging = new LoggingConfiguration();
            logging.AddRule(LogLevel.Debug, LogLevel.Debug, messages);
            using var logFactory = new LogFactory { Configuration = logging };
            Mocker.SetConstant(logFactory.GetLogger("import-author-review"));
            _config.NewDownload = false;
            _config.AddNewAuthors = true;
            _book.Id = 0;
            _author.Id = 0;
            _audiobookEdition.Title = "Test Book";
            _audiobookEdition.Format = "Audio Book";
            _audiobookEdition.Language = "spa";
            Mocker.GetMock<IAuthorService>().Setup(service => service.FindById(It.IsAny<string>())).Returns((Author)null);
            Mocker.GetMock<ICandidateService>()
                .Setup(service => service.GetDbCandidatesFromTags(It.IsAny<LocalEdition>(), It.IsAny<IdentificationOverrides>(), It.IsAny<bool>()))
                .Returns(new List<CandidateEdition>());
            GivenRemoteCandidates(new CandidateEdition { Edition = _audiobookEdition, ExistingFiles = new List<BookFile>() });

            var tracks = CreateAudiobookLocalBooks();
            tracks.Single().FileTrackInfo.BookTitle = "Test Book";
            tracks.Single().FileTrackInfo.Authors = new List<string> { "Test Author" };
            tracks.Single().FileTrackInfo.Language = "eng";

            var result = Subject.Identify(tracks, new IdentificationOverrides(), _config).Single();

            result.Edition.Should().BeNull();
            result.IdentificationHoldReason.Should().Be(LibraryImportHoldReason.LowConfidence);
            messages.Logs.Should().ContainSingle(message => message.Contains("Import hold LowConfidence") &&
                message.Contains(tracks.Single().Path) && message.Contains(_book.ForeignBookId) &&
                message.Contains(_audiobookEdition.ForeignEditionId) &&
                message.Contains("candidateFormat=Audiobook") && message.Contains("releaseIdentifierAgreement=False"));
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
            GivenRemoteCandidates(new CandidateEdition { Edition = _audiobookEdition, ExistingFiles = new List<BookFile>() });

            var tracks = CreateAudiobookLocalBooks();
            tracks.Single().FileTrackInfo.BookTitle = "Test Book";
            tracks.Single().FileTrackInfo.Authors = new List<string> { "Test Author" };
            var result = Subject.Identify(tracks, new IdentificationOverrides(), _config).Single();

            (result.Edition != null).Should().Be(expectedMatch);
            Mocker.GetMock<IAuthorService>().Verify(service => service.FindByName("Test Author"), newDownload ? Times.Once() : Times.Never());
        }

        [TestCase(false, false, true, "Teacher's Guide, with Poster")]
        [TestCase(true, false, true, "Teacher’s Guide, with Poster")]
        [TestCase(true, false, false, "Teacher’s Guide, with Poster")]
        [TestCase(true, true, false, "Teaching Guide")]
        [TestCase(false, true, true, "Reading Guide")]
        public void should_reject_a_guide_when_the_embedded_title_identifies_its_base_work(bool findRemotely, bool guideOnWorkOnly, bool newDownload, string guideSuffix)
        {
            _config.NewDownload = newDownload;
            if (!newDownload)
            {
                _overrides = new IdentificationOverrides();
            }

            var guideTitle = $"Test Book: {guideSuffix}";
            _book.Title = guideTitle;
            _audiobookEdition.Title = guideOnWorkOnly ? "Test Book" : guideTitle;
            _audiobookEdition.Format = guideOnWorkOnly ? "Audio Book" : "Hardcover";
            GivenGuideCandidate(findRemotely);

            var result = IdentifyAudioTitle("Test Book");

            result.Edition.Should().BeNull();
            result.ProviderIdentityConflictDetail.Should().Be(findRemotely && !newDownload
                ? "Provider candidates identify a guide for the embedded title. Select the intended work manually."
                : null);
        }

        [Test]
        public void should_continue_to_a_correct_remote_candidate_when_the_local_candidate_is_a_guide()
        {
            _ebookEdition.Title = "Test Book (Study Guide)";
            _ebookEdition.IsEbook = false;
            _audiobookEdition.Title = "Test Book";
            Mocker.GetMock<ICandidateService>()
                .Setup(service => service.GetDbCandidatesFromTags(It.IsAny<LocalEdition>(), It.IsAny<IdentificationOverrides>(), It.IsAny<bool>()))
                .Returns(new List<CandidateEdition> { new CandidateEdition { Edition = _ebookEdition, ExistingFiles = new List<BookFile>() } });
            GivenRemoteCandidates(new CandidateEdition { Edition = _audiobookEdition, ExistingFiles = new List<BookFile>() });

            IdentifyAudioTitle("Test Book").Edition.Should().BeSameAs(_audiobookEdition);
        }

        [TestCase("Test Book: Teacher’s Guide, with Poster", "Test Book: Teacher’s Guide, with Poster", 0.98, false)]
        [TestCase("Test Book: Teacher’s Guide, with Poster", "Test Book", 0.98, true)]
        [TestCase("Test Book: Teaching Guide", "Test Book", 0.35, false)]
        [TestCase("Test Book: The Return", "Test Book", 0.98, false)]
        public void should_preserve_guide_editions_when_the_file_or_user_identifies_them_or_title_evidence_is_inconclusive(string candidateTitle, string fileTitle, double confidence, bool explicitEdition)
        {
            _book.Title = candidateTitle;
            _audiobookEdition.Title = candidateTitle;
            _overrides.Edition = explicitEdition ? _audiobookEdition : null;
            GivenGuideCandidate(false);

            IdentifyAudioTitle(fileTitle, confidence).Edition.Should().BeSameAs(_audiobookEdition);
        }

        [TestCase(false, "The Primal Hunter 7", "The Primal Hunter 3", null)]
        [TestCase(true, "The Primal Hunter Book 7", "The Primal Hunter Book 3", "Provider candidates name a different volume of the same series. Select the intended work manually.")]
        [TestCase(true, "Elven Star", "Serpent Mage", "Provider candidates disagree with the embedded title and filename. Select the intended work manually.")]
        [TestCase(false, "Winter", "Cinder", null)]
        public void should_skip_wrong_matched_retry_work_when_raw_title_and_filename_agree(bool findRemotely, string observedTitle, string candidateTitle, string conflictDetail)
        {
            GivenRetryCandidate(candidateTitle, findRemotely);
            var tracks = CreateRetryTracks(observedTitle, $"Test Author - {observedTitle}");

            var release = Subject.Identify(tracks, new IdentificationOverrides(), _config).Single();

            release.Edition.Should().BeNull();
            release.IdentificationHoldReason.Should().Be(findRemotely
                ? LibraryImportHoldReason.ProviderIdentityConflict
                : null);
            release.ProviderIdentityConflictDetail.Should().Be(conflictDetail);
            release.RejectedProviderCandidates.Should().Be(findRemotely);
        }

        [TestCase(FilterFilesType.None, false, 1.0, false)]
        [TestCase(FilterFilesType.Known, true, 0.92, false)]
        [TestCase(FilterFilesType.None, false, 0.55, true)]
        public void should_apply_corroborated_title_to_library_scans_with_initialled_authors(FilterFilesType filter, bool findRemotely, double confidence, bool shouldMatch)
        {
            GivenRetryCandidate("Harry Potter and the Philosopher's Stone", findRemotely);
            _config.Filter = filter;
            _book.AuthorMetadata.Value.Name = "J K Rowling";
            var fileTitle = "Harry Potter and the Chamber of Secrets";
            var tracks = CreateRetryTracks(fileTitle, $"J. K. Rowling - {fileTitle}", confidence);
            tracks[0].FileTrackInfo.Authors = new List<string> { "J.K. Rowling" };

            var identifiedEdition = Subject.Identify(tracks, new IdentificationOverrides(), _config).Single().Edition;

            (identifiedEdition == _audiobookEdition).Should().Be(shouldMatch);
        }

        [TestCase("Abiding in Mindfulness, Volume 2: On Feeling, the Mind & Dhamma", "Abiding in Mindfulness, Volume 2 - On Feeling, the Mind & Dhamma", "The Experience Of Insight: A Simple & Direct Guide To Buddhist Meditation")]
        [TestCase("City of Bones: The Little Book of Quotes", "City of Bones - The Little Book of Quotes", "City of Bones Movie postcards Mortal Instruments Paperback")]
        [TestCase("City of Bones: The Little Book of Quotes", "City of Bones - The Little Book of Quotes", "City of Bones")]
        [TestCase("Mistress of Dragons", "Mistress of Dragons (1)", "Master of Dragons")]
        [TestCase("Gryff the Griffin Rider: Omnibus Book 1 - 5", "Gryff the Griffin Rider - Omnibus Book 1 - 5", "Gryff The Griffin Rider 3")]
        [TestCase("Gryff the Griffin Rider: Omnibus Book 1 - 5", "Gryff the Griffin Rider - Omnibus Book 1 - 5", "Gryff The Griffin Rider")]
        [TestCase("Gryff the Griffin Rider: Omnibus Book 1 - 5", "Gryff the Griffin Rider - Omnibus Book 1 - 5", "Gryff the Griffin Rider: Omnibus Book 1-4")]
        [TestCase("Gryff the Griffin Rider: Omnibus Book 1 - 5", "Gryff the Griffin Rider - Omnibus Book 1 - 5", "Gryff the Griffin Rider: Book 3")]
        [TestCase("The World of Ice and Fire: The Untold History of Westeros and the Game of Thrones", "The World of Ice and Fire - The Untold History of Westeros and the Game of Thrones", "Selections from Dreamsongs 3")]
        public void should_hold_a_wrong_work_when_the_native_filename_differs_only_in_presentation(string tagTitle, string filenameTitle, string wrongTitle)
        {
            GivenRetryCandidate(wrongTitle);
            _config.Filter = FilterFilesType.None;
            var tracks = CreateRetryTracks(tagTitle, $"Test Author - {filenameTitle}");

            Subject.Identify(tracks, new IdentificationOverrides(), _config).Single().Edition.Should().BeNull();
        }

        [TestCase(7, "B0C6W5XQNW", "The Primal Hunter 3: A LitRPG Adventure", false)]
        [TestCase(8, "B0CHSJVWL7", "The Primal Hunter 3: A LitRPG Adventure", false)]
        [TestCase(7, "B0C6W5XQNW", "The Primal Hunter 7", true)]
        public void should_corroborate_a_numbered_work_when_the_filename_omits_its_generic_genre_line(int volume, string asin, string candidateTitle, bool shouldMatch)
        {
            GivenRetryCandidate(candidateTitle);
            _book.AuthorMetadata.Value.Name = "Zogarth";
            _audiobookEdition.Format = "Ebook";
            _audiobookEdition.IsEbook = true;
            var title = $"The Primal Hunter {volume}";
            var tracks = CreateRetryTracks($"{title}: A LitRPG Adventure", $"Zogarth - {title}", 0.92);
            tracks[0].Path = $"/media/ebooks/Zogarth - {title}.epub";
            tracks[0].Quality = new QualityModel(Quality.EPUB);
            tracks[0].FileTrackInfo.Authors = new List<string> { "Zogarth" };
            tracks[0].FileTrackInfo.Asin = asin;
            tracks[0].FileTrackInfo.AsinConfidence = 0.95;

            var identified = Subject.Identify(tracks, new IdentificationOverrides(), _config).Single().Edition;

            (identified == _audiobookEdition).Should().Be(shouldMatch);
        }

        [Test]
        public void should_not_select_other_chapters_just_because_the_chapter_range_agrees()
        {
            GivenRetryCandidate("Cress: Chapters 1-5");
            _book.AuthorMetadata.Value.Name = "Marissa Meyer";
            var tracks = CreateRetryTracks("Cinder: Chapters 1-5", "Marissa Meyer - Cinder - Chapters 1-5", 1.0);
            tracks[0].FileTrackInfo.Title = "Cinder: Chapters 1-5";
            tracks[0].FileTrackInfo.Authors = new List<string> { "Marissa Meyer" };

            Subject.Identify(tracks, new IdentificationOverrides(), _config).Single().Edition.Should().BeNull();
        }

        [TestCase("The Primal Hunter 15: A LitRPG Adventure", "The Primal Hunter 7 (1).mobi", 0.9, false, false, false)]
        [TestCase("The Primal Hunter 7: A LitRPG Adventure", "The Primal Hunter 7 (1).mobi", 0.9, false, false, true)]
        [TestCase("The Primal Hunter 15: A LitRPG Adventure", "Another Series 7 (1).mobi", 0.9, false, false, true)]
        [TestCase("The Primal Hunter Book 7", "The Primal Hunter Book 7 - Disc 15.mobi", 0.9, false, false, true)]
        [TestCase("The Primal Hunter Book 15", "The Primal Hunter Volume 7 (1).mobi", 0.9, false, false, false)]
        [TestCase("The Primal Hunter 15", "The Primal Hunter 7 (1).mobi", 0.6, false, false, true)]
        [TestCase("The Primal Hunter 15", "The Primal Hunter 7 (1).mobi", 0.9, true, false, true)]
        [TestCase("The Primal Hunter 15", "The Primal Hunter 7 (1).mobi", 0.9, false, true, true)]
        [TestCase("The Archive 2021", "The Archive 2019.mobi", 0.9, false, false, true)]
        public void should_hold_disagreeing_numbered_file_evidence_before_querying_automatic_candidates(
            string tagTitle, string filename, double confidence, bool requested, bool explicitEdition, bool shouldMatch)
        {
            GivenRetryCandidate(tagTitle);
            _config.Filter = FilterFilesType.None;
            _config.NewDownload = requested;
            _book.AuthorMetadata.Value.Name = "Zogarth";
            _audiobookEdition.Format = "Ebook";
            _audiobookEdition.IsEbook = true;
            var tracks = CreateRetryTracks(tagTitle, filename, confidence);
            tracks[0].Path = $"/media/ebooks/Zogarth - {filename}";
            tracks[0].Quality = new QualityModel(Quality.MOBI);
            tracks[0].FileTrackInfo.Authors = new List<string> { "Zogarth" };
            var overrides = new IdentificationOverrides { Edition = explicitEdition ? _audiobookEdition : null };

            var result = Subject.Identify(tracks, overrides, _config).Single();

            (result.Edition == _audiobookEdition).Should().Be(shouldMatch);
            if (!shouldMatch)
            {
                Mocker.GetMock<ICandidateService>().Verify(
                    service => service.GetDbCandidatesFromTags(It.IsAny<LocalEdition>(), It.IsAny<IdentificationOverrides>(), It.IsAny<bool>()), Times.Never());
                Mocker.GetMock<ICandidateService>().Verify(
                    service => service.GetRemoteCandidates(It.IsAny<LocalEdition>(), It.IsAny<IdentificationOverrides>(), It.IsAny<Func<Edition, bool>>(), It.IsAny<bool>()), Times.Never());
            }
        }

        [TestCase("Sheepfarmer's Daughter (The Deed of Paksenarrion)", false)]
        [TestCase("Surrender None", true)]
        public void should_read_an_exact_work_suffix_after_numbered_series_and_agreeing_inverted_author(string candidateTitle, bool shouldMatch)
        {
            GivenRetryCandidate(candidateTitle);
            _config.Filter = FilterFilesType.None;
            _book.AuthorMetadata.Value.Name = "Elizabeth Moon";
            var tracks = CreateRetryTracks("Paksenarrion 04 - Surrender None", "Elizabeth Moon - Surrender None", 0.9);
            tracks[0].FileTrackInfo.Authors = new List<string> { "Moon, Elizabeth" };

            var identified = Subject.Identify(tracks, new IdentificationOverrides(), _config).Single().Edition;

            (identified == _audiobookEdition).Should().Be(shouldMatch);
        }

        [TestCase("The Untold Story: A Novel", "The Untold Story - A Novel", "The Untold Story")]
        [TestCase("The Untold Story: A Novel", "The Untold Story - A Novel", "The Untold Story - A Novel")]
        [TestCase("Gryff the Griffin Rider: Omnibus Book 1 - 5", "Gryff the Griffin Rider - Omnibus Book 1 - 5", "Gryff the Griffin Rider: Omnibus Book 1 - 5")]
        [TestCase("Gryff the Griffin Rider: Omnibus Book 1 - 5", "Gryff the Griffin Rider - Omnibus Book 1 - 5", "Gryff the Griffin Rider: Omnibus Book 1-5")]
        [TestCase("Gryff the Griffin Rider: Omnibus Book 1–5", "Gryff the Griffin Rider - Omnibus Book 1 - 5", "Gryff the Griffin Rider: Omnibus Book 1-5")]
        [TestCase("A Story About Number 2", "A Different Novel", "A Story")]
        public void should_preserve_matching_work_scope_and_ordinary_title_variants(string tagTitle, string filenameTitle, string candidateTitle)
        {
            GivenRetryCandidate(candidateTitle);
            var tracks = CreateRetryTracks(tagTitle, $"Test Author - {filenameTitle}");

            Subject.Identify(tracks, new IdentificationOverrides(), _config).Single().Edition.Should().BeSameAs(_audiobookEdition);
        }

        [TestCase("The Amber Moon (Book 2)", "The Amber Moon", null, false)]
        [TestCase("The Amber Moon: Volume 2", "The Amber Moon", null, false)]
        [TestCase("The Amber Moon: Vol. #2", "The Amber Moon", null, false)]
        [TestCase("The Amber Moon: A Novel", "The Amber Moon", null, true)]
        [TestCase("The Amber Moon: Book 2: A Subtitle", "The Amber Moon", null, true)]
        [TestCase("The Amber Moon (Book 2)", "The Amber Moon", "B012345678", true)]
        [TestCase("The Amber Moon (Book 2)", "The Amber Moon (Book 2)", null, true)]
        public void should_require_corroboration_for_an_explicit_terminal_volume(string fileTitle, string candidateTitle, string candidateAsin, bool shouldMatch)
        {
            GivenRetryCandidate(candidateTitle, true);
            _audiobookEdition.Asin = candidateAsin;
            var tracks = CreateRetryTracks(fileTitle, $"Test Author - {fileTitle}");
            tracks[0].FileTrackInfo.Title = fileTitle;
            tracks[0].FileTrackInfo.Asin = "B012345678";
            tracks[0].FileTrackInfo.AsinConfidence = 0.9;

            var release = Subject.Identify(tracks, new IdentificationOverrides(), _config).Single();

            release.Edition.Should().BeSameAs(shouldMatch ? _audiobookEdition : null);
            release.IdentificationHoldReason.Should().Be(shouldMatch ? null : LibraryImportHoldReason.ProviderIdentityConflict);
        }

        [Test]
        public void should_retain_a_local_numbered_volume_conflict_without_disabling_fallback()
        {
            const string title = "The Amber Moon (Book 2)";
            GivenRetryCandidate("The Amber Moon");
            var tracks = CreateRetryTracks(title, $"Test Author - {title}");

            var release = Subject.Identify(tracks, new IdentificationOverrides(), _config).Single();

            release.Edition.Should().BeNull();
            release.IdentificationHoldReason.Should().BeNull();
            release.RejectedProviderCandidates.Should().BeFalse();
            release.ProviderIdentityConflictDetail.Should().Contain("numbered volume");
        }

        [TestCase(false)]
        [TestCase(true)]
        public void should_hold_a_corroborated_audio_part_when_both_candidate_titles_omit_it(bool findRemotely)
        {
            const string taggedTitle = "Starling Almanac: Part 2 Lanterns at Dawn";
            GivenRetryCandidate("Starling Almanac", findRemotely);
            _config.Filter = FilterFilesType.None;
            _config.AddNewAuthors = true;
            var tracks = CreateRetryTracks(taggedTitle, "Test Author - Starling Almanac - Part 2 Lanterns at Dawn");
            tracks[0].FileTrackInfo.Title = taggedTitle;
            tracks[0].FileTrackInfo.TrackNumbers = new[] { 1 };

            var release = Subject.Identify(tracks, new IdentificationOverrides(), _config).Single();

            release.Edition.Should().BeNull();
            release.ProviderIdentityConflictDetail.Should().Contain("numbered part");
            release.IdentificationHoldReason.Should().Be(findRemotely ? LibraryImportHoldReason.ProviderIdentityConflict : null);
        }

        [TestCase("Starling Almanac: Part 2 Lanterns at Dawn", 1, false)]
        [TestCase("Starling Almanac", 1, true)]
        [TestCase("Starling Almanac", 0, false)]
        [TestCase("Starling Almanac", 2, false)]
        public void should_preserve_exact_parts_trusted_identifiers_and_known_track_positions(
            string candidateTitle, int trackNumber, bool exactIdentifier)
        {
            const string taggedTitle = "Starling Almanac: Part 2 Lanterns at Dawn";
            GivenRetryCandidate(candidateTitle);
            _config.Filter = FilterFilesType.None;
            var tracks = CreateRetryTracks(taggedTitle, "Test Author - Starling Almanac - Part 2 Lanterns at Dawn");
            var tags = tracks[0].FileTrackInfo;
            tags.Title = taggedTitle;
            tags.TrackNumbers = new[] { trackNumber };
            if (exactIdentifier)
            {
                _audiobookEdition.Asin = "B012345678";
                tags.Asin = "B012345678";
                tags.AsinConfidence = 0.98;
            }

            var release = Subject.Identify(tracks, new IdentificationOverrides(), _config).Single();

            release.Edition.Should().BeSameAs(_audiobookEdition);
        }

        [Test]
        public void should_keep_two_track_parts_of_the_same_album_on_one_edition()
        {
            const string album = "Starling Almanac";
            GivenRetryCandidate(album);
            _config.Filter = FilterFilesType.None;
            var tracks = CreateRetryTracks(album, "Test Author - Starling Almanac - Part 1");
            var second = CreateRetryTracks(album, "Test Author - Starling Almanac - Part 2").Single();
            tracks[0].FileTrackInfo.Title = "Part 1";
            tracks[0].FileTrackInfo.TrackNumbers = new[] { 1 };
            second.FileTrackInfo.Title = "Part 2";
            second.FileTrackInfo.TrackNumbers = new[] { 2 };
            tracks.Add(second);

            Subject.Identify(tracks, new IdentificationOverrides(), _config).Single().Edition.Should().BeSameAs(_audiobookEdition);
        }

        [TestCase("Study Guide", false)]
        [TestCase("Teacher’s Guide, with Poster", false)]
        [TestCase("Reading Guide", false)]
        [TestCase("A Novel", true)]
        public void should_distinguish_an_embedded_guide_from_its_original_work_across_subtitle_separators(string subtitle, bool shouldMatch)
        {
            GivenRetryCandidate("Test Book");
            _config.Filter = FilterFilesType.None;
            var tracks = CreateRetryTracks($"Test Book - {subtitle}", $"Test Author - Test Book - {subtitle}");

            var result = Subject.Identify(tracks, new IdentificationOverrides(), _config).Single().Edition;

            (result == _audiobookEdition).Should().Be(shouldMatch);
        }

        [TestCase("The Primal Hunter: Book 7", "The Primal Hunter: Volume 3", false)]
        [TestCase("The Primal Hunter: Book 7", "The Primal Hunter 3: A LitRPG Adventure", false)]
        [TestCase("The Primal Hunter 7: A LitRPG Adventure", "The Primal Hunter: Volume 3", false)]
        [TestCase("The Primal Hunter: Book 7", "The Primal Hunter 7: A LitRPG Adventure", true)]
        [TestCase("Starling Almanac: Book 3", "Starling Almanac 7: An Adventure", false)]
        [TestCase("Starling Almanac 3 - An Adventure", "Starling Almanac: Book 7", false)]
        [TestCase("Starling Almanac: Book 3", "Starling Almanac 3: An Adventure", true)]
        [TestCase("Starling: Almanac: Book 3", "Starling: Almanac 7: An Adventure: Illustrated", false)]
        [TestCase("The Archive: Book 7", "The Archive 2021", true)]
        [TestCase("The Archive: Book 7", "The Archive 2021: A History", true)]
        [TestCase("The Archive 3", "The Archive 7: A History", true)]
        [TestCase("The Primal Hunter: Book 7", "Another Series 3", true)]
        public void should_skip_a_different_series_volume_without_a_full_filename_when_either_title_names_the_volume(
            string embeddedTitle, string candidateTitle, bool shouldMatch)
        {
            GivenRetryCandidate(candidateTitle);
            var tracks = CreateRetryTracks(embeddedTitle, "Chapter 01");

            var result = Subject.Identify(tracks, new IdentificationOverrides(), _config).Single().Edition;

            (result == _audiobookEdition).Should().Be(shouldMatch);
        }

        [Test]
        public void should_try_the_remote_work_after_excluding_a_contradicted_local_work_on_matched_retry()
        {
            GivenRetryCandidate("Serpent Mage");
            var remoteBook = new Book
            {
                Id = 2,
                Title = "Elven Star",
                Author = _author,
                AuthorMetadata = _book.AuthorMetadata
            };
            var remoteEdition = new Edition { Book = remoteBook, Title = remoteBook.Title, Format = "Audiobook" };
            GivenRemoteCandidates(new CandidateEdition { Edition = remoteEdition, ExistingFiles = new List<BookFile>() });
            var tracks = CreateRetryTracks("Elven Star", "Test Author - Elven Star");

            Subject.Identify(tracks, new IdentificationOverrides(), _config).Single().Edition.Should().BeSameAs(remoteEdition);
        }

        [TestCase("Elven Star", "Elven Star: A Novel", "Test Author - Elven Star", true, 0.98, true, false)]
        [TestCase("Elven Star", "Serpent Mage", "Test Author - Elven Star - Part 1", true, 0.98, true, false)]
        [TestCase("Elven Star", "Serpent Mage", "Test Author - Elven Star", true, 0.55, true, false)]
        [TestCase("Elven Star", "Serpent Mage", "Test Author - Elven Star", true, 0.98, false, false)]
        [TestCase("Elven Star", "Serpent Mage", "Test Author - Elven Star", false, 0.98, true, false)]
        [TestCase("Elven Star", "Serpent Mage", "Test Author - Elven Star", true, 0.98, true, true)]
        [TestCase("Saga Part 7", "Saga Part 3", "Part 1", true, 0.98, true, false)]
        public void should_preserve_candidates_without_a_confirmed_library_scan_conflict(string title, string candidate, string filename, bool existingLibrary, double confidence, bool hasTaggedAuthor, bool explicitEdition)
        {
            GivenRetryCandidate(candidate);
            _config.NewDownload = !existingLibrary;
            _config.Filter = FilterFilesType.None;
            var tracks = CreateRetryTracks(title, filename, confidence);
            if (!hasTaggedAuthor)
            {
                tracks[0].FileTrackInfo.Authors.Clear();
                Mocker.GetMock<IAugmentingService>().Setup(service => service.Augment(It.IsAny<LocalEdition>()))
                    .Returns((LocalEdition release) =>
                    {
                        release.LocalBooks[0].FileTrackInfo.Authors.Add("Test Author");
                        return release;
                    });
            }

            var selected = explicitEdition ? _audiobookEdition : null;
            Subject.Identify(tracks, new IdentificationOverrides { Edition = selected }, _config).Single().Edition.Should().BeSameAs(_audiobookEdition);
        }

        [TestCase(true, false, true)]
        [TestCase(false, false, true)]
        [TestCase(false, true, false)]
        public void should_preserve_a_candidate_corroborated_by_a_nonconflicting_trusted_edition_identifier(bool useIsbn, bool conflictingIsbn, bool shouldMatch)
        {
            GivenRetryCandidate("Serpent Mage");
            var tracks = CreateRetryTracks("Elven Star", "Test Author - Elven Star");
            var tags = tracks[0].FileTrackInfo;
            _audiobookEdition.Isbn13 = "9781442482890";
            _audiobookEdition.Asin = "B01ABCDEF1";

            if (useIsbn || conflictingIsbn)
            {
                tags.Isbn = conflictingIsbn ? "9781455546176" : "1-4424-8289-3";
                tags.IsbnConfidence = 0.98;
            }

            if (!useIsbn)
            {
                tags.Asin = "b01abcdef1";
                tags.AsinConfidence = 0.98;
            }

            var result = Subject.Identify(tracks, new IdentificationOverrides(), _config).Single();

            (result.Edition == _audiobookEdition).Should().Be(shouldMatch);
        }

        private void GivenRetryCandidate(string candidateTitle, bool findRemotely = false)
        {
            _config.NewDownload = false;
            _config.KeepAllEditions = true;
            _config.Filter = FilterFilesType.Matched;
            _book.Title = candidateTitle;
            _audiobookEdition.Title = candidateTitle;
            _audiobookEdition.Format = "Audiobook";
            GivenGuideCandidate(findRemotely);
        }

        [TestCase(true, true, false)]
        [TestCase(false, true, false)]
        [TestCase(false, false, false)]
        [TestCase(false, true, true)]
        [TestCase(false, false, true)]
        public void should_continue_remote_search_after_rejecting_an_ineligible_edition(bool rejectPrimary, bool laterMatch, bool requireLocalAuthor)
        {
            const string title = "Fixture Series Book 2";
            _config.NewDownload = false;
            _config.KeepAllEditions = true;
            _config.Filter = FilterFilesType.Matched;
            var tracks = CreateRetryTracks(title, "Test Author - " + title);
            tracks[0].FileTrackInfo.AuthorConfidence = 0.98;
            var wrong = RemoteWork(requireLocalAuthor ? title : "Fixture Series Book 1");
            var expected = RemoteWork(title);
            var authors = Mocker.GetMock<IAuthorService>();
            authors.Setup(service => service.GetCandidates(It.IsAny<string>())).Returns(new List<Author>());
            Mocker.GetMock<IMediaFileService>().Setup(service => service.GetFileWithPath(It.IsAny<List<string>>())).Returns(new List<BookFile>());
            if (requireLocalAuthor)
            {
                const string missingAuthor = "openlibrary:author:OL999A";
                wrong.Author = new Author { Name = _author.Name, ForeignAuthorId = missingAuthor };
                wrong.AuthorMetadata = new AuthorMetadata { Name = _author.Name, ForeignAuthorId = missingAuthor };
                authors.Setup(service => service.FindById(missingAuthor)).Returns((Author)null);
            }

            var orchestrator = Mocker.GetMock<IMetadataProviderOrchestrator>();
            orchestrator.Setup(service => service.SearchForNewBook(It.IsAny<string>(), It.IsAny<string>(), true))
                .Returns(rejectPrimary ? new List<Book> { wrong } : new List<Book>());

            var first = new Mock<IBookSearchFallbackProvider>();
            first.SetupGet(provider => provider.ProviderName).Returns("Inventaire");
            var second = new Mock<IBookSearchFallbackProvider>();
            second.SetupGet(provider => provider.ProviderName).Returns("GoogleBooks");
            var last = new Mock<IBookSearchFallbackProvider>();
            last.SetupGet(provider => provider.ProviderName).Returns("Hardcover");
            Mocker.SetConstant<IEnumerable<IBookSearchFallbackProvider>>(new[] { last.Object, second.Object, first.Object });

            var search = Mocker.GetMock<IBookSearchFallbackExecutionService>();
            search.Setup(service => service.Search(first.Object, It.IsAny<string>(), It.IsAny<string>()))
                .Returns(new List<Book> { wrong });
            search.Setup(service => service.Search(second.Object, It.IsAny<string>(), It.IsAny<string>()))
                .Returns(laterMatch ? new List<Book> { expected } : new List<Book> { wrong });
            search.Setup(service => service.Search(last.Object, It.IsAny<string>(), It.IsAny<string>()))
                .Returns(new List<Book>());
            Mocker.SetConstant<ICandidateService>(Mocker.Resolve<CandidateService>());

            var result = Subject.Identify(tracks, new IdentificationOverrides(), _config).Single();

            result.Edition.Should().BeSameAs(laterMatch ? expected.Editions.Value.Single() : null);
            result.IdentificationHoldReason.Should().Be(laterMatch || requireLocalAuthor
                ? null
                : LibraryImportHoldReason.ProviderIdentityConflict);
            search.Verify(service => service.Search(first.Object, It.IsAny<string>(), It.IsAny<string>()), Times.AtLeastOnce());
            search.Verify(service => service.Search(second.Object, It.IsAny<string>(), It.IsAny<string>()), Times.AtLeastOnce());
            search.Verify(service => service.Search(last.Object, It.IsAny<string>(), It.IsAny<string>()),
                laterMatch ? Times.Never() : Times.AtLeastOnce());
        }

        [TestCase(true)]
        [TestCase(false)]
        public void should_preserve_inventaire_ambiguity_after_rejecting_a_primary_candidate(bool precedingMismatch)
        {
            const string title = "Fixture Series Book 2";
            _config.NewDownload = false;
            _config.KeepAllEditions = true;
            _config.Filter = FilterFilesType.Matched;
            var tracks = CreateRetryTracks(title, "Test Author - " + title);
            tracks[0].FileTrackInfo.AuthorConfidence = 0.98;
            Mocker.GetMock<IAuthorService>()
                .Setup(service => service.GetCandidates(It.IsAny<string>())).Returns(new List<Author>());
            Mocker.GetMock<IMediaFileService>()
                .Setup(service => service.GetFileWithPath(It.IsAny<List<string>>())).Returns(new List<BookFile>());

            var orchestrator = Mocker.GetMock<IMetadataProviderOrchestrator>();
            orchestrator.Setup(service => service.SearchForNewBook(It.IsAny<string>(), It.IsAny<string>(), true))
                .Returns(precedingMismatch ? new List<Book> { RemoteWork("Fixture Series Book 1") } : new List<Book>());

            var inventaire = new Mock<IBookSearchFallbackProvider>();
            inventaire.SetupGet(provider => provider.ProviderName).Returns("Inventaire");
            inventaire.SetupGet(provider => provider.IsFallbackEnabled).Returns(true);
            Mocker.SetConstant<IEnumerable<IBookSearchFallbackProvider>>(new[] { inventaire.Object });

            var first = RemoteWork(title);
            first.ForeignBookId = "inventaire:work:wd:Q81001";
            var second = RemoteWork(title);
            second.ForeignBookId = "inventaire:work:wd:Q81002";
            var fallback = Mocker.GetMock<IBookSearchFallbackExecutionService>();
            fallback.Setup(service => service.Search(inventaire.Object, title, "Test Author"))
                .Returns(new List<Book> { first, second });
            Mocker.SetConstant<ICandidateService>(Mocker.Resolve<CandidateService>());

            var release = Subject.Identify(tracks, new IdentificationOverrides(), _config).Single();

            fallback.Verify(service => service.Search(inventaire.Object, title, "Test Author"), Times.Once());
            release.Edition.Should().BeNull();
            release.IdentificationHoldReason.Should().Be(LibraryImportHoldReason.ConflictingIdentity);
            release.ProviderIdentityConflictDetail.Should().Be("More than one Inventaire work matches the exact title and author.");
        }

        [Test]
        public void should_leave_author_policy_distinct_when_an_identity_compatible_provider_result_has_no_local_author()
        {
            const string title = "Fixture Series Book 2";
            GivenRetryCandidate("Fixture Series Book 1", findRemotely: true);
            var tracks = CreateRetryTracks(title, "Test Author - " + title);
            var compatible = RemoteWork(title);
            const string missingAuthor = "openlibrary:author:OL999A";
            compatible.Author = new Author { Name = _author.Name, ForeignAuthorId = missingAuthor };
            compatible.AuthorMetadata = new AuthorMetadata { Name = _author.Name, ForeignAuthorId = missingAuthor };
            Mocker.GetMock<IAuthorService>().Setup(service => service.FindById(missingAuthor)).Returns((Author)null);
            var compatibleEdition = compatible.Editions.Value.Single();
            compatibleEdition.Book = compatible;
            GivenRemoteCandidates(
                new CandidateEdition { Edition = _audiobookEdition },
                new CandidateEdition { Edition = compatibleEdition });

            var result = Subject.Identify(tracks, new IdentificationOverrides(), _config).Single();

            result.Edition.Should().BeNull();
            result.IdentificationHoldReason.Should().BeNull();
        }

        private Book RemoteWork(string title)
        {
            return new Book
            {
                Author = _author,
                AuthorMetadata = _book.AuthorMetadata,
                ForeignBookId = "openlibrary:work:OL531W",
                Title = title,
                Editions = new List<Edition>
                {
                    new Edition { ForeignEditionId = "openlibrary:edition:OL531M", Title = title, Format = "Audiobook" }
                }
            };
        }

        private List<LocalBook> CreateRetryTracks(string title, string filename, double confidence = 0.98)
        {
            var tracks = CreateAudiobookLocalBooks();
            tracks[0].Path = $"/media/audiobooks/{filename}.m4b";
            tracks[0].FileTrackInfo.BookTitle = title;
            tracks[0].FileTrackInfo.BookTitleConfidence = confidence;
            tracks[0].FileTrackInfo.Authors.Add("Test Author");
            tracks[0].ReliableTaggedBookTitle = IdentificationService.CaptureReliableTaggedBookTitle(tracks[0].FileTrackInfo);
            return tracks;
        }

        private void GivenGuideCandidate(bool findRemotely)
        {
            var candidates = new List<CandidateEdition> { new CandidateEdition { Edition = _audiobookEdition, ExistingFiles = new List<BookFile>() } };
            Mocker.GetMock<ICandidateService>()
                .Setup(service => service.GetDbCandidatesFromTags(It.IsAny<LocalEdition>(), It.IsAny<IdentificationOverrides>(), It.IsAny<bool>()))
                .Returns(findRemotely ? new List<CandidateEdition>() : candidates);
            GivenRemoteCandidates(findRemotely ? candidates.ToArray() : Array.Empty<CandidateEdition>());
        }

        private void GivenRemoteCandidates(params CandidateEdition[] candidates)
        {
            Mocker.GetMock<ICandidateService>()
                .Setup(service => service.GetRemoteCandidates(It.IsAny<LocalEdition>(), It.IsAny<IdentificationOverrides>(), It.IsAny<Func<Edition, bool>>(), It.IsAny<bool>()))
                .Returns((LocalEdition release, IdentificationOverrides overrides, Func<Edition, bool> allowsEdition, bool forLibraryScan) =>
                    candidates.Where(candidate => allowsEdition(candidate.Edition)));
        }

        private LocalEdition IdentifyAudioTitle(string title, double confidence = 0.98)
        {
            _config.KeepAllEditions = true;
            var tracks = CreateAudiobookLocalBooks();
            tracks.Single().FileTrackInfo.BookTitle = title;
            tracks.Single().FileTrackInfo.BookTitleConfidence = confidence;
            tracks.Single().FileTrackInfo.Authors = new List<string> { "Test Author" };
            tracks.Single().ReliableTaggedBookTitle = IdentificationService.CaptureReliableTaggedBookTitle(tracks.Single().FileTrackInfo);
            return Subject.Identify(tracks, _overrides, _config).Single();
        }

        [Test]
        public void should_leave_ebook_unmapped_when_an_incompatible_edition_is_explicitly_selected()
        {
            _audiobookEdition.Format = "M4B";
            _overrides.Edition = _audiobookEdition;
            GivenGuideCandidate(false);
            var tracks = CreateEbookLocalBooks();
            var summary = new ImportRunSummary();

            var release = Subject.Identify(tracks, _overrides, _config, summary).Single();

            release.Edition.Should().BeNull();
            release.IdentificationHoldReason.Should().BeNull();
            var track = release.LocalBooks.Should().ContainSingle().Which;
            track.Should().BeSameAs(tracks.Single());
            track.Edition.Should().BeNull();
            track.Author.Should().BeSameAs(_author);
            track.Book.Should().BeSameAs(_book);
            summary.Errors.Should().Be(0);
            Mocker.GetMock<ICandidateService>()
                .Verify(service => service.GetRemoteCandidates(release, _overrides, It.IsAny<Func<Edition, bool>>(), false), Times.Once);
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

        [TestCase(false)]
        [TestCase(true)]
        public void should_choose_the_typed_audiobook_in_a_perfect_same_work_tie(bool typedFirst)
        {
            var tracks = GivenAudioFormatScan();
            var typed = CreateTypedAudiobookEdition();
            GivenLocalEditionsAsCandidates(typedFirst ? new[] { typed, _audiobookEdition } : new[] { _audiobookEdition, typed });

            DistanceCalculator.BookDistance(tracks, _audiobookEdition).NormalizedDistance().Should().Be(0);
            DistanceCalculator.BookDistance(tracks, typed).NormalizedDistance().Should().Be(0);

            var release = Subject.Identify(tracks, new IdentificationOverrides(), _config).Single();
            release.Edition.Id.Should().Be(typed.Id);
            release.Edition.BookId.Should().Be(_book.Id);
            release.Edition.Format.Should().Be("M4B");
            release.Distance.NormalizedDistance().Should().Be(0);
        }

        [TestCase("different work")]
        [TestCase("another author projection")]
        [TestCase("already linked")]
        [TestCase("confirmed embedded ASIN")]
        [TestCase("different scores")]
        [TestCase("two typed siblings")]
        [TestCase("matched-only scan")]
        [TestCase("download")]
        [TestCase("explicit edition")]
        [TestCase("explicit work")]
        [TestCase("explicit author")]
        public void should_keep_the_original_audiobook_match_when_the_typed_sibling_is_not_a_safe_tie(string condition)
        {
            var tracks = GivenAudioFormatScan();
            var track = tracks.Single();
            var typed = CreateTypedAudiobookEdition();
            var candidates = new List<Edition> { _audiobookEdition, typed };
            var overrides = new IdentificationOverrides();
            switch (condition)
            {
                case "different work":
                    typed.Book = new Book { Id = 2, CanonicalId = "other-work", Title = _book.Title, AuthorMetadataId = _book.AuthorMetadataId, AuthorMetadata = _book.AuthorMetadata };
                    typed.BookId = typed.Book.Value.Id;
                    break;
                case "another author projection":
                    typed.Book = new Book { Id = _book.Id, CanonicalId = _book.CanonicalId, Title = _book.Title, AuthorMetadataId = 9, AuthorMetadata = _book.AuthorMetadata };
                    break;
                case "already linked":
                    Mocker.GetMock<IMediaFileService>().Setup(service => service.GetFileWithPath(It.IsAny<List<string>>()))
                        .Returns(new List<BookFile> { new BookFile { Path = track.Path, EditionId = _audiobookEdition.Id } });
                    break;
                case "confirmed embedded ASIN":
                    track.FileTrackInfo.Asin = _audiobookEdition.Asin = typed.Asin = "B000000001";
                    track.FileTrackInfo.AsinConfidence = 0.9;
                    break;
                case "different scores":
                    track.FileTrackInfo.Publisher = _audiobookEdition.Publisher = "Original Publisher";
                    typed.Publisher = "Different Publisher";
                    break;
                case "two typed siblings":
                    var second = CreateTypedAudiobookEdition();
                    second.Id++;
                    second.ForeignEditionId += "-other";
                    candidates.Add(second);
                    break;
                case "matched-only scan":
                    _config.Filter = FilterFilesType.Matched;
                    break;
                case "download":
                    _config.NewDownload = true;
                    break;
                case "explicit edition":
                    overrides.Edition = _audiobookEdition;
                    break;
                case "explicit work":
                    overrides.Book = _book;
                    break;
                case "explicit author":
                    overrides.Author = _author;
                    break;
            }

            GivenLocalEditionsAsCandidates(candidates.ToArray());

            var release = Subject.Identify(tracks, overrides, _config).Single();
            release.Edition.Id.Should().Be(_audiobookEdition.Id);
            release.Edition.BookId.Should().Be(_book.Id);
            release.Distance.NormalizedDistance().Should().Be(0);
            Mocker.GetMock<ICandidateService>().Verify(service => service.GetRemoteCandidates(
                release, overrides, It.IsAny<Func<Edition, bool>>(), It.IsAny<bool>()), Times.Never());
        }

        private List<LocalBook> GivenAudioFormatScan()
        {
            _config.NewDownload = false;
            _config.IncludeExisting = true;
            _config.SingleRelease = false;
            _book.CanonicalId = "test-canonical-work";
            _book.AuthorMetadataId = 1;
            _audiobookEdition.Title = _book.Title;
            Mocker.GetMock<IConfigService>().Setup(s => s.EnableDualFormatTracking).Returns(true);
            Mocker.GetMock<IMediaFileService>().Setup(service => service.GetFileWithPath(It.IsAny<List<string>>()))
                .Returns(new List<BookFile>());

            var tracks = CreateAudiobookLocalBooks();
            var track = tracks.Single();
            track.Path = "/media/audiobooks/Test Author - Test Book.m4b";
            track.FileTrackInfo.BookTitle = _book.Title;
            track.FileTrackInfo.Authors = new List<string> { _author.Name };
            return tracks;
        }

        private Edition CreateTypedAudiobookEdition()
        {
            return new Edition
            {
                Id = 30,
                BookId = _book.Id,
                Book = _book,
                Title = _book.Title,
                Format = "M4B",
                ForeignEditionId = "edition-typed-audiobook"
            };
        }

        [TestCase(false, FilterFilesType.None)]
        [TestCase(true, FilterFilesType.Known)]
        public void should_choose_the_physical_ebook_subformat_for_the_same_work_and_isbn(bool reverseOrder, FilterFilesType filter)
        {
            var tracks = GivenEbookSubformatScan(filter);
            var azw3 = CreateAzw3Edition(30, _book, _ebookEdition.Isbn13);
            GivenLocalEditionsAsCandidates(reverseOrder ? new[] { azw3, _ebookEdition } : new[] { _ebookEdition, azw3 });

            Subject.Identify(tracks, new IdentificationOverrides(), _config).Single().Edition.Should().BeSameAs(azw3);
        }

        [TestCase("different work")]
        [TestCase("different ISBN")]
        [TestCase("untrusted file ISBN")]
        [TestCase("extension-only format")]
        [TestCase("already linked")]
        [TestCase("explicit edition")]
        [TestCase("new download")]
        [TestCase("existing library excluded")]
        public void should_keep_the_identified_edition_without_matching_trusted_same_work_evidence(string condition)
        {
            var tracks = GivenEbookSubformatScan();
            var track = tracks.Single();
            var work = condition == "different work"
                ? new Book { Id = 7, CanonicalId = "different-work", Title = _book.Title, AuthorMetadata = _book.AuthorMetadata, Author = _author }
                : _book;
            var isbn = condition == "different ISBN" ? "9781455546176" : _ebookEdition.Isbn13;
            var azw3 = CreateAzw3Edition(30, work, isbn);
            if (condition == "untrusted file ISBN")
            {
                track.FileTrackInfo.IsbnConfidence = 0.55;
            }
            else if (condition == "extension-only format")
            {
                track.FileTrackInfo.Quality.QualityDetectionSource = QualityDetectionSource.Extension;
            }
            else if (condition == "already linked")
            {
                Mocker.GetMock<IMediaFileService>().Setup(service => service.GetFileWithPath(It.IsAny<List<string>>()))
                    .Returns(new List<BookFile> { new BookFile { Path = track.Path, EditionId = _ebookEdition.Id } });
            }
            else if (condition == "new download")
            {
                _config.NewDownload = true;
            }
            else if (condition == "existing library excluded")
            {
                _config.IncludeExisting = false;
            }

            var overrides = new IdentificationOverrides
            {
                Edition = condition == "explicit edition" ? _ebookEdition : null
            };
            GivenLocalEditionsAsCandidates(_ebookEdition, azw3);

            Subject.Identify(tracks, overrides, _config).Single().Edition.Should().BeSameAs(_ebookEdition);
        }

        [Test]
        public void should_hold_two_distinct_equally_matching_exact_ebook_editions()
        {
            var tracks = GivenEbookSubformatScan();
            var first = CreateAzw3Edition(30, _book, _ebookEdition.Isbn13);
            var second = CreateAzw3Edition(40, _book, _ebookEdition.Isbn13);
            GivenLocalEditionsAsCandidates(_ebookEdition, first, first, second);

            var release = Subject.Identify(tracks, new IdentificationOverrides(), _config).Single();

            release.Edition.Should().BeNull();
            release.IdentificationHoldReason.Should().Be(LibraryImportHoldReason.CompetingEditions);
            tracks.Single().Edition.Should().BeNull();
        }

        [Test]
        public void should_ignore_duplicate_projections_of_the_same_exact_ebook_edition()
        {
            var tracks = GivenEbookSubformatScan();
            var azw3 = CreateAzw3Edition(30, _book, _ebookEdition.Isbn13);
            GivenLocalEditionsAsCandidates(_ebookEdition, azw3, azw3);

            Subject.Identify(tracks, new IdentificationOverrides(), _config).Single().Edition.Should().BeSameAs(azw3);
        }

        [TestCase("exact generic ebook", true)]
        [TestCase("exact EPUB", true)]
        [TestCase("duplicate projection", true)]
        [TestCase("two matching editions", false)]
        [TestCase("cross-work ASIN collision", false)]
        [TestCase("different work", false)]
        [TestCase("explicit AZW3", false)]
        [TestCase("untrusted ASIN", false)]
        [TestCase("extension-only format", false)]
        [TestCase("already linked", false)]
        [TestCase("explicit work", false)]
        public void should_refine_an_unlinked_epub_to_a_unique_same_work_embedded_asin(string condition, bool refine)
        {
            var tracks = GivenEbookSubformatScan();
            var track = tracks.Single();
            track.Path = "/media/ebooks/Test Author - Test Book.epub";
            track.Quality = new QualityModel(Quality.EPUB)
            {
                QualityDetectionSource = condition == "extension-only format" ? QualityDetectionSource.Extension : QualityDetectionSource.TagLib
            };
            track.FileTrackInfo.Quality = track.Quality;
            track.FileTrackInfo.Isbn = null;
            track.FileTrackInfo.IsbnConfidence = 0;
            track.FileTrackInfo.Asin = "B000000001";
            track.FileTrackInfo.AsinConfidence = condition == "untrusted ASIN" ? 0.55 : 0.9;
            track.FileTrackInfo.Publisher = "ZZZZZZ";
            _ebookEdition.Format = "Ebook";
            _ebookEdition.Isbn13 = null;
            _ebookEdition.Publisher = track.FileTrackInfo.Publisher;
            var otherWork = new Book
            {
                Id = 7,
                CanonicalId = "different-work",
                Title = _book.Title,
                AuthorMetadata = _book.AuthorMetadata,
                Author = _author
            };
            var exact = CreateAzw3Edition(30, condition == "different work" ? otherWork : _book, null);
            exact.Asin = track.FileTrackInfo.Asin;
            exact.Format = condition switch
            {
                "exact EPUB" => "EPUB",
                "explicit AZW3" => "AZW3",
                _ => "Ebook"
            };
            exact.Publisher = "AAAAAA";

            var candidates = new List<Edition> { _ebookEdition, exact };
            if (condition is "two matching editions" or "cross-work ASIN collision")
            {
                var competing = CreateAzw3Edition(40, condition == "cross-work ASIN collision" ? otherWork : _book, null);
                competing.Asin = exact.Asin;
                competing.Format = exact.Format;
                competing.Publisher = exact.Publisher;
                candidates.Add(competing);
            }
            else if (condition == "duplicate projection")
            {
                candidates.Add(exact);
            }
            else if (condition == "already linked")
            {
                Mocker.GetMock<IMediaFileService>().Setup(service => service.GetFileWithPath(It.IsAny<List<string>>()))
                    .Returns(new List<BookFile> { new BookFile { Path = track.Path, EditionId = _ebookEdition.Id } });
            }

            GivenLocalEditionsAsCandidates(candidates.ToArray());
            var overrides = new IdentificationOverrides { Book = condition == "explicit work" ? _book : null };

            DistanceCalculator.BookDistance(tracks, _ebookEdition).NormalizedDistance()
                .Should().BeLessThan(DistanceCalculator.BookDistance(tracks, exact).NormalizedDistance());
            Subject.Identify(tracks, overrides, _config).Single().Edition.Should().BeSameAs(refine ? exact : _ebookEdition);
        }

        private List<LocalBook> GivenEbookSubformatScan(FilterFilesType filter = FilterFilesType.None)
        {
            _config.NewDownload = false;
            _config.Filter = filter;
            _config.IncludeExisting = true;
            _config.KeepAllEditions = true;
            _book.CanonicalId = "test-canonical-work";
            _ebookEdition.Title = _book.Title;
            _ebookEdition.Format = "EPUB";
            _ebookEdition.Isbn13 = "9781442482890";

            Mocker.GetMock<IMediaFileService>().Setup(service => service.GetFileWithPath(It.IsAny<List<string>>()))
                .Returns(new List<BookFile>());

            var tracks = CreateEbookLocalBooks();
            var track = tracks.Single();
            track.Path = "/media/ebooks/Test Author - Test Book.azw3";
            track.Quality = new QualityModel(Quality.AZW3) { QualityDetectionSource = QualityDetectionSource.TagLib };
            track.FileTrackInfo.Quality = track.Quality;
            track.FileTrackInfo.BookTitle = _book.Title;
            track.FileTrackInfo.Authors = new List<string> { _author.Name };
            track.FileTrackInfo.Isbn = "1-4424-8289-3";
            track.FileTrackInfo.IsbnConfidence = 0.97;
            return tracks;
        }

        private Edition CreateAzw3Edition(int id, Book book, string isbn)
        {
            return new Edition
            {
                Id = id,
                BookId = book.Id,
                Book = book,
                Title = _ebookEdition.Title,
                Format = "AZW3",
                IsEbook = true,
                Isbn13 = isbn,
                ForeignEditionId = $"azw3-{id}"
            };
        }

        private void GivenLocalEditionsAsCandidates(params Edition[] editions)
        {
            Mocker.GetMock<ICandidateService>()
                .Setup(service => service.GetDbCandidatesFromTags(It.IsAny<LocalEdition>(), It.IsAny<IdentificationOverrides>(), _config.IncludeExisting))
                .Returns(editions.Select(edition => new CandidateEdition(edition)).ToList());
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
