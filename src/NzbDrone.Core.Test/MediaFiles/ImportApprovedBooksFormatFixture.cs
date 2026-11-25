using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using FizzWare.NBuilder;
using FluentAssertions;
using Moq;
using NLog;
using NLog.Config;
using NLog.Targets;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.Books.Events;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.DecisionEngine;
using NzbDrone.Core.Exceptions;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.BookImport;
using NzbDrone.Core.MediaFiles.Events;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Profiles.Qualities;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.RootFolders;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.MediaFiles
{
    [TestFixture]
    public class ImportApprovedBooksFormatFixture : CoreTest<ImportApprovedBooks>
    {
        private List<ImportDecision<LocalBook>> _approvedDecisions;
        private Author _author;
        private Book _book;
        private Edition _ebookEdition;
        private Edition _audiobookEdition;

        private static Expression<Func<IAddAuthorService, Author>> AuthorAddition(bool unattended, Expression<Func<Author, bool>> matches)
        {
            return unattended
                ? service => service.AddAuthorFromUnattendedLibraryImport(It.Is(matches))
                : service => service.AddAuthor(It.Is(matches), false);
        }

        [SetUp]
        public void Setup()
        {
            _approvedDecisions = new List<ImportDecision<LocalBook>>();

            _author = Builder<Author>.CreateNew()
                .With(e => e.QualityProfile = new QualityProfile { Items = Qualities.QualityFixture.GetDefaultQualities() })
                .With(s => s.Path = @"C:\Test\Music\Author Name".AsOsAgnostic())
                .Build();

            _book = Builder<Book>.CreateNew()
                .With(e => e.Author = _author)
                .Build();

            _ebookEdition = Builder<Edition>.CreateNew()
                .With(e => e.Id = 10)
                .With(e => e.Book = _book)
                .With(e => e.BookId = _book.Id)
                .With(e => e.Monitored = true)
                .With(e => e.IsEbook = true)
                .With(e => e.Format = "Ebook")
                .Build();

            _audiobookEdition = Builder<Edition>.CreateNew()
                .With(e => e.Id = 20)
                .With(e => e.Book = _book)
                .With(e => e.BookId = _book.Id)
                .With(e => e.Monitored = true)
                .With(e => e.IsEbook = false)
                .With(e => e.Format = "Audiobook")
                .Build();

            _book.Editions = new List<Edition> { _ebookEdition, _audiobookEdition };

            var rootFolder = Builder<RootFolder>.CreateNew()
                .With(r => r.IsCalibreLibrary = false)
                .Build();

            Mocker.GetMock<IUpgradeMediaFiles>()
                .Setup(s => s.UpgradeBookFile(It.IsAny<BookFile>(), It.IsAny<LocalBook>(), It.IsAny<bool>(), It.IsAny<bool>()))
                .Returns(new BookFileMoveResult());

            Mocker.GetMock<IMediaFileService>()
                .Setup(s => s.GetFilesByBook(It.IsAny<int>()))
                .Returns(new List<BookFile>());

            Mocker.GetMock<IRootFolderService>()
                .Setup(s => s.GetBestRootFolder(It.IsAny<string>()))
                .Returns(rootFolder);
        }

        private void GivenAudiobookImport()
        {
            _approvedDecisions.Clear();
            _approvedDecisions.Add(new ImportDecision<LocalBook>(
                new LocalBook
                {
                    Author = _author,
                    Book = _book,
                    Edition = _audiobookEdition,
                    Part = 1,
                    Path = Path.Combine(_author.Path, "Book Title - Part 01.m4b"),
                    Quality = new QualityModel(Quality.M4B),
                    FileTrackInfo = new ParsedTrackInfo { ReleaseGroup = "TEST" }
                }));

            Mocker.GetMock<IEditionService>()
                .Setup(s => s.SetMonitoredByFormat(_audiobookEdition))
                .Returns(new List<Edition> { _ebookEdition, _audiobookEdition });

            Mocker.GetMock<IEditionService>()
                .Setup(s => s.SetMonitored(_audiobookEdition))
                .Returns(new List<Edition> { _ebookEdition, _audiobookEdition });
        }

        private void GivenEbookImport()
        {
            _approvedDecisions.Clear();
            _approvedDecisions.Add(new ImportDecision<LocalBook>(
                new LocalBook
                {
                    Author = _author,
                    Book = _book,
                    Edition = _ebookEdition,
                    Part = 1,
                    Path = Path.Combine(_author.Path, "Book Title.epub"),
                    Quality = new QualityModel(Quality.EPUB),
                    FileTrackInfo = new ParsedTrackInfo { ReleaseGroup = "TEST" }
                }));

            Mocker.GetMock<IEditionService>()
                .Setup(s => s.SetMonitoredByFormat(_ebookEdition))
                .Returns(new List<Edition> { _ebookEdition, _audiobookEdition });

            Mocker.GetMock<IEditionService>()
                .Setup(s => s.SetMonitored(_ebookEdition))
                .Returns(new List<Edition> { _ebookEdition, _audiobookEdition });
        }

        [TestCase(true)]
        [TestCase(false)]
        public void should_recheck_new_synthetic_author_after_an_authority_is_added_in_the_same_batch(bool unattended)
        {
            GivenEbookImport();
            const string authorityId = "openlibrary:author:OL1A";
            const string syntheticId = "googlebooks:author:Fixture%20Author";
            _author.Id = 0;
            _author.Name = "Fixture Author";
            _author.ForeignAuthorId = authorityId;
            _approvedDecisions[0].Item.IsUnattendedLibraryImport = unattended;
            var synthetic = new Author { Name = _author.Name, ForeignAuthorId = syntheticId, Path = _author.Path, QualityProfile = _author.QualityProfile };
            var otherWork = new Book
            {
                Id = _book.Id + 1,
                ForeignBookId = "googlebooks:work:fixture",
                Author = synthetic,
                Title = "Other Work"
            };
            var otherEdition = new Edition
            {
                Id = _ebookEdition.Id + 1,
                ForeignEditionId = "googlebooks:edition:fixture",
                BookId = otherWork.Id,
                Book = otherWork,
                IsEbook = true,
                Format = "Ebook",
                Monitored = true
            };
            var later = new ImportDecision<LocalBook>(new LocalBook
            {
                Path = Path.Combine(_author.Path, "Other Work.epub"),
                Author = synthetic,
                Book = otherWork,
                Edition = otherEdition,
                Quality = new QualityModel(Quality.EPUB),
                FileTrackInfo = new ParsedTrackInfo(),
                IsUnattendedLibraryImport = unattended
            });
            _approvedDecisions.Add(later);
            var incumbent = new Author { Id = 50, AuthorMetadataId = 50, Metadata = _author.Metadata, QualityProfile = _author.QualityProfile, Path = _author.Path };
            var addedSynthetic = new Author { Id = 60, AuthorMetadataId = 60, Metadata = synthetic.Metadata, QualityProfile = _author.QualityProfile, Path = _author.Path };
            var authorityAdded = false;
            var authors = Mocker.GetMock<IAuthorService>();
            authors.Setup(service => service.GetAuthorsByCleanName(It.IsAny<string>()))
                .Returns(() => authorityAdded ? new List<Author> { incumbent } : new List<Author>());
            authors.Setup(service => service.GetAuthor(incumbent.Id)).Returns(incumbent);
            authors.Setup(service => service.GetAuthor(addedSynthetic.Id)).Returns(addedSynthetic);
            var additions = Mocker.GetMock<IAddAuthorService>();
            additions.Setup(AuthorAddition(unattended, author => author.ForeignAuthorId == authorityId))
                .Returns(() =>
                {
                    authorityAdded = true;
                    return incumbent;
                });
            additions.Setup(AuthorAddition(unattended, author => author.ForeignAuthorId == syntheticId))
                .Returns(addedSynthetic);
            Mocker.GetMock<IEditionService>().Setup(service => service.SetMonitored(otherEdition)).Returns(new List<Edition> { otherEdition });

            Subject.Import(_approvedDecisions, false);

            authorityAdded.Should().BeTrue();
            additions.Verify(AuthorAddition(unattended, author => author.ForeignAuthorId == syntheticId),
                unattended ? Times.Never() : Times.Once());
            later.Rejections.Any(rejection => rejection.HoldReason == LibraryImportHoldReason.AuthorIdentityConfirmationRequired).Should().Be(unattended);
        }

        [TestCase("get-author-info", LibraryImportHoldReason.AuthorBibliographyUnavailable)]
        [TestCase("get-book-info", null)]
        public void should_record_only_author_bibliography_provider_failures_with_the_retry_reason(string operation, LibraryImportHoldReason? expectedReason)
        {
            GivenAudiobookImport();
            _author.Id = 0;
            _author.ForeignAuthorId = "hardcover:author:123";
            var decision = _approvedDecisions.Single();
            decision.Item.IsUnattendedLibraryImport = true;
            Mocker.GetMock<IAddAuthorService>()
                .Setup(service => service.AddAuthorFromUnattendedLibraryImport(_author))
                .Throws(new MetadataProviderUnavailableException(operation, "openlibrary:author:OL1A", new InvalidOperationException("Synthetic incomplete bibliography")));

            var result = Subject.Import(_approvedDecisions, false).Single();

            ExceptionVerification.ExpectedErrors(1);
            result.Result.Should().Be(ImportResultType.Rejected);
            var rejection = decision.Rejections.Should().ContainSingle().Which;
            rejection.Type.Should().Be(RejectionType.Temporary);
            rejection.Reason.Should().Be(expectedReason.HasValue
                ? "The metadata provider could not load the author's bibliography. Rescan to retry."
                : "Failed to add missing author");
            rejection.HoldReason.Should().Be(expectedReason);
            Mocker.GetMock<IAddAuthorService>().Verify(service => service.AddAuthorFromUnattendedLibraryImport(_author), Times.Once());
            Mocker.GetMock<IEditionService>().Verify(service => service.SetMonitored(It.IsAny<Edition>()), Times.Never());
            Mocker.GetMock<IMediaFileService>().Verify(service => service.AddMany(It.Is<List<BookFile>>(files => files.Count > 0)), Times.Never());
        }

        [TestCase(true)]
        [TestCase(false)]
        public void should_report_incomplete_bibliography_using_the_failed_provider_during_unattended_import(bool unattended)
        {
            const string privateAuthor = "Private Fixture Author";
            const string privateIncomingId = "hardcover:author:123";
            const string privateErrorId = "openlibrary:author:OL999A";
            const string privateKey = "private-fixture-provider-key";
            var messages = new MemoryTarget { Layout = "${message}|${exception:format=ToString}" };
            var logging = new LoggingConfiguration();
            logging.AddRule(LogLevel.Error, LogLevel.Error, messages);
            using var logFactory = new LogFactory { Configuration = logging };
            Mocker.SetConstant(logFactory.GetLogger(nameof(ImportApprovedBooks)));

            GivenAudiobookImport();
            _author.Id = 0;
            _author.Name = privateAuthor;
            _author.ForeignAuthorId = privateIncomingId;
            var decision = _approvedDecisions.Single();
            decision.Item.IsUnattendedLibraryImport = unattended;
            var additions = Mocker.GetMock<IAddAuthorService>();
            additions.Setup(AuthorAddition(unattended, author => author == _author))
                .Throws(new MetadataProviderUnavailableException(
                    "get-author-info",
                    privateErrorId,
                    new InvalidOperationException($"Fixture request ({decision.Item.Path}) with {privateKey}"),
                    authoritativeBibliographyEndedEarly: true,
                    authorBibliographyProgress: (101, 100, 107)));

            var result = Subject.Import(_approvedDecisions, false).Single();

            var message = messages.Logs.Should().ContainSingle().Which;
            if (unattended)
            {
                message.Should().Be(
                    "Library import held with AuthorBibliographyUnavailable: OpenLibrary author bibliography ended at offset 101 with 100 distinct entries of 107 reported.|");
                message.Should().NotContain(privateAuthor).And.NotContain(privateIncomingId).And.NotContain(privateErrorId)
                    .And.NotContain(privateKey).And.NotContain(decision.Item.Path);
            }
            else
            {
                message.Should().StartWith("Failed to add author");
            }

            result.Result.Should().Be(ImportResultType.Rejected);
            var rejection = decision.Rejections.Should().ContainSingle().Which;
            rejection.Type.Should().Be(RejectionType.Temporary);
            rejection.HoldReason.Should().Be(LibraryImportHoldReason.AuthorBibliographyUnavailable);
            additions.Verify(AuthorAddition(unattended, author => author == _author), Times.Once());
            Mocker.GetMock<IBookService>().Verify(service => service.AddBook(It.IsAny<Book>(), It.IsAny<bool>()), Times.Never());
            Mocker.GetMock<IEditionService>().Verify(service => service.SetMonitored(It.IsAny<Edition>()), Times.Never());
            Mocker.GetMock<IMediaFileService>().Verify(service => service.AddMany(It.Is<List<BookFile>>(files => files.Count > 0)), Times.Never());
        }

        [TestCase(true, true, false, 1)]
        [TestCase(true, false, false, 2)]
        [TestCase(false, true, false, 2)]
        [TestCase(true, true, true, 1)]
        [TestCase(true, false, false, 1, true)]
        [TestCase(true, false, true, 1, true)]
        public void should_hold_repeat_authoritative_bibliography_failures_only_for_the_same_unattended_source_in_one_batch(
            bool unattended, bool authoritative, bool becameKnown, int expectedAttempts, bool missingTitle = false)
        {
            const string incoming = "hardcover:author:123";
            const string equivalentIncoming = "hardcover:author:000123";
            const string independent = "hardcover:author:456";

            var first = Decision(incoming, 1);
            var repeated = Decision(equivalentIncoming, 2);
            var different = Decision(independent, 3);
            var stored = new Author
            {
                Id = 88,
                AuthorMetadataId = 99,
                Metadata = different.Item.Author.Metadata,
                QualityProfile = _author.QualityProfile,
                Path = _author.Path
            };
            var nowKnown = new Author
            {
                Id = 89,
                AuthorMetadataId = 100,
                Metadata = first.Item.Author.Metadata,
                QualityProfile = _author.QualityProfile,
                Path = _author.Path
            };

            var authors = Mocker.GetMock<IAuthorService>();
            authors.Setup(service => service.GetAuthor(stored.Id)).Returns(stored);
            if (becameKnown)
            {
                authors.Setup(service => service.FindById(equivalentIncoming)).Returns(nowKnown);
            }

            var additions = Mocker.GetMock<IAddAuthorService>();
            additions.Setup(AuthorAddition(unattended, author => author.ForeignAuthorId != independent))
                .Throws(new MetadataProviderUnavailableException(
                    "get-author-info",
                    missingTitle ? incoming : "openlibrary:author:OL1A",
                    new InvalidOperationException("Incomplete fixture bibliography"),
                    authoritative,
                    authorBibliographyProgress: missingTitle ? null : (10, 9, 12),
                    authoritativeBibliographyMissingWorkTitle: missingTitle));
            additions.Setup(AuthorAddition(unattended, author => author.ForeignAuthorId == independent))
                .Returns(stored);
            Mocker.GetMock<IEditionService>().Setup(service => service.SetMonitored(It.IsAny<Edition>()))
                .Returns((Edition edition) => new List<Edition> { edition });

            Subject.Import(new List<ImportDecision<LocalBook>> { first, repeated, different }, false);

            additions.Verify(AuthorAddition(unattended, author => author.ForeignAuthorId != independent),
                Times.Exactly(expectedAttempts));
            additions.Verify(AuthorAddition(unattended, author => author.ForeignAuthorId == independent), Times.Once());
            different.Approved.Should().BeTrue();
            different.Item.Author.Id.Should().Be(stored.Id);
            AssertHeld(first);
            if (becameKnown)
            {
                repeated.Approved.Should().BeTrue();
                repeated.Item.Author.Id.Should().Be(nowKnown.Id);
            }
            else
            {
                AssertHeld(repeated);
            }

            var later = Decision(incoming, 4);
            Subject.Import(new List<ImportDecision<LocalBook>> { later }, false);

            additions.Verify(AuthorAddition(unattended, author => author.ForeignAuthorId != independent),
                Times.Exactly(expectedAttempts + 1));
            AssertHeld(later);
            ExceptionVerification.ExpectedErrors(expectedAttempts + 1);

            ImportDecision<LocalBook> Decision(string foreignId, int number)
            {
                var remoteAuthor = new Author
                {
                    Name = "Same display name",
                    ForeignAuthorId = foreignId,
                    Path = _author.Path,
                    QualityProfile = _author.QualityProfile
                };
                var book = new Book { Id = number + 100, ForeignBookId = $"hardcover:work:{number}", Author = remoteAuthor };
                var audio = new Edition
                {
                    Id = number + 200,
                    ForeignEditionId = $"hardcover:source-edition:{number}",
                    Format = "Audiobook",
                    Book = book,
                    BookId = book.Id
                };
                return new ImportDecision<LocalBook>(new LocalBook
                {
                    Author = remoteAuthor,
                    Book = book,
                    Edition = audio,
                    Part = 1,
                    Path = Path.Combine(_author.Path, $"Book {number}.m4b"),
                    Quality = new QualityModel(Quality.M4B),
                    FileTrackInfo = new ParsedTrackInfo(),
                    IsUnattendedLibraryImport = unattended
                });
            }

            static void AssertHeld(ImportDecision<LocalBook> decision)
            {
                var rejection = decision.Rejections.Should().ContainSingle().Which;
                rejection.Type.Should().Be(RejectionType.Temporary);
                rejection.HoldReason.Should().Be(LibraryImportHoldReason.AuthorBibliographyUnavailable);
            }
        }

        [Test]
        public void should_use_SetMonitoredByFormat_when_flag_on_and_audiobook()
        {
            Mocker.GetMock<IConfigService>()
                .Setup(s => s.EnableDualFormatTracking).Returns(true);

            GivenAudiobookImport();

            Subject.Import(_approvedDecisions, false);

            Mocker.GetMock<IEditionService>()
                .Verify(v => v.SetMonitoredByFormat(_audiobookEdition), Times.Once());

            Mocker.GetMock<IEditionService>()
                .Verify(v => v.SetMonitored(It.IsAny<Edition>()), Times.Never());
        }

        [Test]
        public void should_use_SetMonitoredByFormat_when_flag_on_and_ebook()
        {
            Mocker.GetMock<IConfigService>()
                .Setup(s => s.EnableDualFormatTracking).Returns(true);

            GivenEbookImport();

            Subject.Import(_approvedDecisions, false);

            Mocker.GetMock<IEditionService>()
                .Verify(v => v.SetMonitoredByFormat(_ebookEdition), Times.Once());

            Mocker.GetMock<IEditionService>()
                .Verify(v => v.SetMonitored(It.IsAny<Edition>()), Times.Never());
        }

        [TestCase(true)]
        [TestCase(false)]
        public void should_reject_incompatible_files_before_preparing_or_moving_them(bool audiobook)
        {
            _ebookEdition.Format = "Ebook";
            _audiobookEdition.Format = "Audiobook";
            if (audiobook)
            {
                GivenAudiobookImport();
                _approvedDecisions[0].Item.Edition = _ebookEdition;
            }
            else
            {
                GivenEbookImport();
                _approvedDecisions[0].Item.Edition = _audiobookEdition;
            }

            var results = Subject.Import(_approvedDecisions, false);

            results.Should().ContainSingle(result => result.Result == ImportResultType.Rejected);
            Mocker.GetMock<IEditionService>().Verify(s => s.SetMonitored(It.IsAny<Edition>()), Times.Never());
            Mocker.GetMock<IUpgradeMediaFiles>().Verify(s => s.UpgradeBookFile(It.IsAny<BookFile>(), It.IsAny<LocalBook>(), It.IsAny<bool>(), It.IsAny<bool>()), Times.Never());
            Mocker.GetMock<IMediaFileService>().Verify(s => s.AddMany(It.Is<List<BookFile>>(files => files.Count > 0)), Times.Never());
        }

        [Test]
        public void should_prepare_and_persist_both_formats_of_one_work()
        {
            Mocker.GetMock<IConfigService>().Setup(s => s.EnableDualFormatTracking).Returns(true);
            _ebookEdition.ForeignEditionId = "ebook-edition";
            _ebookEdition.Format = "Ebook";
            _audiobookEdition.ForeignEditionId = "audiobook-edition";
            _audiobookEdition.Format = "Audiobook";
            GivenEbookImport();
            var ebookDecision = _approvedDecisions[0];
            GivenAudiobookImport();
            var audiobookDecision = _approvedDecisions[0];
            _approvedDecisions.Insert(0, ebookDecision);

            var results = Subject.Import(_approvedDecisions, false);

            results.Should().OnlyContain(result => result.Result == ImportResultType.Imported).And.HaveCount(2);
            Mocker.GetMock<IEditionService>().Verify(s => s.SetMonitoredByFormat(_ebookEdition), Times.Once());
            Mocker.GetMock<IEditionService>().Verify(s => s.SetMonitoredByFormat(_audiobookEdition), Times.Once());
            Mocker.GetMock<IMediaFileService>().Verify(s => s.AddMany(It.Is<List<BookFile>>(files =>
                files.Count == 2 &&
                files.Exists(file => file.Path == ebookDecision.Item.Path && file.EditionId == _ebookEdition.Id) &&
                files.Exists(file => file.Path == audiobookDecision.Item.Path && file.EditionId == _audiobookEdition.Id))), Times.Once());
        }

        [Test]
        public void should_add_the_selected_edition_when_the_work_already_has_a_different_format()
        {
            _ebookEdition.ForeignEditionId = "ebook-edition";
            _ebookEdition.Format = "Ebook";
            _audiobookEdition.ForeignEditionId = "audiobook-edition";
            _audiobookEdition.Format = "Audiobook";
            _book.Editions = new List<Edition> { _ebookEdition };

            GivenAudiobookImport();
            var remoteEdition = new Edition { ForeignEditionId = _audiobookEdition.ForeignEditionId, Format = "Audiobook" };
            var remoteBook = new Book
            {
                ForeignBookId = _book.ForeignBookId,
                Author = _author,
                Editions = new List<Edition> { remoteEdition }
            };
            _approvedDecisions[0].Item.Book = remoteBook;
            _approvedDecisions[0].Item.Edition = remoteEdition;

            Mocker.GetMock<IBookService>().Setup(s => s.FindById(_book.ForeignBookId)).Returns(_book);
            Mocker.GetMock<IEditionService>().SetupSequence(s => s.GetEditionByForeignEditionId(remoteEdition.ForeignEditionId))
                .Returns((Edition)null)
                .Returns(_audiobookEdition);

            var results = Subject.Import(_approvedDecisions, false);

            results.Should().ContainSingle(result => result.Result == ImportResultType.Imported);
            Mocker.GetMock<IEditionService>().Verify(s => s.InsertMany(It.Is<List<Edition>>(editions =>
                editions.Count == 1 && editions[0].ForeignEditionId == remoteEdition.ForeignEditionId && editions[0].BookId == _book.Id)), Times.Once());
            Mocker.GetMock<IMediaFileService>().Verify(s => s.AddMany(It.Is<List<BookFile>>(files =>
                files.Count == 1 && files[0].EditionId == _audiobookEdition.Id)), Times.Once());
        }

        [Test]
        public void should_attach_a_provider_alias_to_the_same_work_without_replacing_the_ebook_edition()
        {
            _book.ForeignBookId = "hardcover:work:43";
            _book.Title = "The Example";
            _book.OpenLibraryWorkId = "OL1W";
            _book.ReleaseDate = new DateTime(2022, 1, 25);
            _book.AuthorMetadataId = _author.AuthorMetadataId = 5;
            _ebookEdition.Format = "Ebook";
            _book.Editions = new List<Edition> { _ebookEdition };
            _audiobookEdition.Format = "Audiobook";
            GivenAudiobookImport();

            var remoteEdition = new Edition { ForeignEditionId = "openlibrary:edition:OL2M", Format = "Audiobook" };
            var remoteBook = new Book
            {
                Title = "The Example",
                ForeignBookId = "openlibrary:work:OL1W",
                Author = _author,
                AuthorMetadataId = _author.AuthorMetadataId,
                ReleaseDate = _book.ReleaseDate,
                Editions = new List<Edition> { remoteEdition }
            };
            _approvedDecisions[0].Item.Book = remoteBook;
            _approvedDecisions[0].Item.Edition = remoteEdition;
            _audiobookEdition.ForeignEditionId = remoteEdition.ForeignEditionId;

            Mocker.GetMock<IBookService>().Setup(s => s.GetBooksByAuthorMetadataId(_author.AuthorMetadataId)).Returns(new List<Book> { _book });
            Mocker.GetMock<IEditionService>().SetupSequence(s => s.GetEditionByForeignEditionId(remoteEdition.ForeignEditionId))
                .Returns((Edition)null)
                .Returns((Edition)null)
                .Returns(_audiobookEdition);

            var results = Subject.Import(_approvedDecisions, false);

            results.Should().ContainSingle(result => result.Result == ImportResultType.Imported);
            _approvedDecisions[0].Item.Book.Should().BeSameAs(_book);
            Mocker.GetMock<IBookService>().Verify(s => s.InsertMany(It.IsAny<List<Book>>()), Times.Never());
            Mocker.GetMock<IEditionService>().Verify(s => s.InsertMany(It.Is<List<Edition>>(editions =>
                editions.Count == 1 && editions[0].BookId == _book.Id && editions[0].Format == "Audiobook")), Times.Once());
        }

        [Test]
        public void should_leave_an_ambiguous_provider_alias_for_review()
        {
            GivenEbookImport();
            var incoming = new Book
            {
                Title = "Example Work",
                ForeignBookId = "hardcover:work:100",
                OpenLibraryWorkId = "OL100W",
                AuthorMetadataId = 5,
                ReleaseDate = new DateTime(2022, 1, 25)
            };
            _approvedDecisions[0].Item.Book = incoming;
            Mocker.GetMock<IBookService>().Setup(s => s.GetBooksByAuthorMetadataId(5)).Returns(new List<Book>
            {
                new Book { Id = 11, ForeignBookId = "hardcover:work:11", Title = incoming.Title, OpenLibraryWorkId = "OL100W" },
                new Book { Id = 12, ForeignBookId = "hardcover:work:12", Title = incoming.Title, OpenLibraryWorkId = "OL100W" }
            });

            var results = Subject.Import(_approvedDecisions, false);

            var held = results.Should().ContainSingle().Which;
            held.Result.Should().Be(ImportResultType.Rejected);
            held.ImportDecision.Rejections.Should().ContainSingle().Which.HoldReason.Should().Be(LibraryImportHoldReason.CompetingEditions);
            Mocker.GetMock<IBookService>().Verify(s => s.InsertMany(It.IsAny<List<Book>>()), Times.Never());
            Mocker.GetMock<IEditionService>().Verify(s => s.InsertMany(It.IsAny<List<Edition>>()), Times.Never());
            var mediaFiles = Mocker.GetMock<IMediaFileService>();
            mediaFiles.Verify(s => s.AddMany(It.Is<List<BookFile>>(files => files.Count > 0)), Times.Never());
            mediaFiles.Verify(s => s.TryLinkUnmappedFile(It.IsAny<BookFile>(), It.IsAny<Edition>()), Times.Never());
            mediaFiles.Verify(s => s.TryRepairUnsupportedEditionLink(It.IsAny<BookFile>(), It.IsAny<Edition>()), Times.Never());
        }

        [TestCase("Print")]
        [TestCase("Unclassified")]
        [TestCase("BOOK")]
        [TestCase(null)]
        [TestCase("Unknown")]
        public void should_record_a_local_ebook_without_claiming_the_provider_edition(string providerFormat)
        {
            GivenEbookImport();
            var selected = _ebookEdition;
            selected.ForeignEditionId = "hardcover:source-edition:33";
            selected.Format = providerFormat;
            selected.IsEbook = false;
            selected.Isbn13 = "9781455546176";
            selected.Asin = "B00EXAMPLE";
            var inserted = GivenLocalDigitalEditionStorage();

            var result = Subject.Import(_approvedDecisions, false);

            result.Should().ContainSingle(item => item.Result == ImportResultType.Imported);
            var edition = inserted.Should().ContainSingle().Which;
            edition.ForeignEditionId.Should().StartWith("bibliophilarr:edition:");
            edition.BookId.Should().Be(_book.Id);
            edition.Format.Should().Be("Ebook");
            edition.IsEbook.Should().BeTrue();
            edition.Isbn13.Should().BeNull();
            edition.Asin.Should().BeNull();
            edition.Images.Should().BeEmpty();
            _approvedDecisions[0].Item.Edition.Should().BeSameAs(edition);
            selected.Isbn13.Should().Be("9781455546176");
            selected.Asin.Should().Be("B00EXAMPLE");
            selected.Format.Should().Be(providerFormat);

            _approvedDecisions[0].Item.Edition = selected;
            Subject.Import(_approvedDecisions, false).Should().ContainSingle(item => item.Result == ImportResultType.Imported);
            inserted.Should().ContainSingle();
            _approvedDecisions[0].Item.Edition.Should().BeSameAs(edition);
        }

        [TestCase(true)]
        [TestCase(false)]
        public void should_link_local_audiobook_and_keep_the_historical_inventaire_ebook(bool currentAlias)
        {
            const string sourceWorkId = "wd:Q912001";
            const string legacyEditionId = "inventaire:edition:" + sourceWorkId;
            const string canonicalWorkId = "705b2b5da2a943319bf107448959fe9c";
            const string localAudioId = "bibliophilarr:edition:" + canonicalWorkId + ":audiobook";
            GivenEbookImport();
            var ebook = _approvedDecisions.Single();
            GivenAudiobookImport();
            var audiobook = _approvedDecisions.Single();
            _approvedDecisions.Add(ebook);

            _author.AuthorMetadataId = 22;
            _author.Metadata.Value.Id = _author.AuthorMetadataId;
            _author.Metadata.Value.CanonicalId = "185cbb81d132400fb235a95b1a264f58";
            _book.AuthorMetadataId = _author.AuthorMetadataId;
            _book.CanonicalId = canonicalWorkId;
            _book.ForeignBookId = "inventaire:work:" + sourceWorkId;
            Mocker.GetMock<IBookService>().Setup(service => service.FindById(_book.ForeignBookId)).Returns(_book);
            var identities = Mocker.GetMock<ICatalogueIdentityRepository>();
            identities.Setup(repository => repository.FindByProviderId(CatalogueEntityKind.Work, "Inventaire", sourceWorkId))
                .Returns(currentAlias ? _book.Id : (int?)null);
            identities.Setup(repository => repository.GetSources(CatalogueEntityKind.Work, _book.Id))
                .Returns(new[] { new CatalogueSourceAssociation { Provider = "Inventaire", ProviderId = sourceWorkId, Evidence = "primary-provider-id" } });
            _book.Editions = new List<Edition> { _ebookEdition };
            _ebookEdition.ForeignEditionId = legacyEditionId;
            _ebookEdition.Isbn13 = "9780140328721";
            audiobook.Item.Edition = new Edition
            {
                Book = _book,
                BookId = _book.Id,
                ForeignEditionId = "inventaire:work-summary:" + sourceWorkId,
                Title = _book.Title
            };
            ebook.Item.ExistingFile = audiobook.Item.ExistingFile = true;
            ebook.Item.IsLibraryScan = audiobook.Item.IsLibraryScan = true;
            ebook.Item.IsUnattendedLibraryImport = audiobook.Item.IsUnattendedLibraryImport = true;

            Mocker.GetMock<IConfigService>().SetupGet(config => config.EnableDualFormatTracking).Returns(true);
            var media = Mocker.GetMock<IMediaFileService>();
            var legacyFile = new BookFile { Id = 72, Path = ebook.Item.Path, EditionId = _ebookEdition.Id };
            media.Setup(service => service.GetFileWithPath(ebook.Item.Path)).Returns(legacyFile);
            media.Setup(service => service.GetUnmappedFiles()).Returns(new List<BookFile>());
            var inserted = GivenLocalDigitalEditionStorage();
            var editions = Mocker.GetMock<IEditionService>();
            editions.Setup(service => service.GetEditionsByBook(_book.Id)).Returns(() => new[] { _ebookEdition }.Concat(inserted).ToList());
            editions.Setup(service => service.SetMonitoredByFormat(It.IsAny<Edition>())).Returns(() => new[] { _ebookEdition }.Concat(inserted).ToList());

            var results = Subject.Import(_approvedDecisions, false);

            results.Should().ContainSingle(result => result.ImportDecision == ebook && result.Result == ImportResultType.Skipped);
            if (!currentAlias)
            {
                results.Should().ContainSingle(result => result.ImportDecision == audiobook && result.Result == ImportResultType.Rejected);
                audiobook.Rejections.Should().Contain(rejection => rejection.HoldReason == LibraryImportHoldReason.ConflictingIdentity);
                inserted.Should().BeEmpty();
                legacyFile.EditionId.Should().Be(_ebookEdition.Id);
                media.Verify(service => service.AddMany(It.Is<List<BookFile>>(files => files.Count > 0)), Times.Never());
                return;
            }

            results.Should().ContainSingle(result => result.ImportDecision == audiobook && result.Result == ImportResultType.Imported);
            var audioEdition = inserted.Should().ContainSingle().Which;
            audioEdition.ForeignEditionId.Should().Be(localAudioId);
            audioEdition.Id.Should().NotBe(_ebookEdition.Id);
            audioEdition.BookId.Should().Be(_book.Id);
            audioEdition.Format.Should().Be("Audiobook");
            audioEdition.IsEbook.Should().BeFalse();
            audioEdition.Isbn13.Should().BeNull();
            audioEdition.Asin.Should().BeNull();
            _ebookEdition.ForeignEditionId.Should().Be(legacyEditionId);
            _ebookEdition.Isbn13.Should().Be("9780140328721");
            _ebookEdition.IsEbook.Should().BeTrue();
            _ebookEdition.Monitored.Should().BeTrue();
            legacyFile.EditionId.Should().Be(_ebookEdition.Id);
            media.Verify(service => service.AddMany(It.Is<List<BookFile>>(files => files.Count == 1 && files[0].EditionId == audioEdition.Id)), Times.Once());
            media.Verify(service => service.Delete(legacyFile, It.IsAny<DeleteMediaFileReason>()), Times.Never());
            Mocker.GetMock<IAddAuthorService>().VerifyNoOtherCalls();
            Mocker.GetMock<IBookService>().Verify(service => service.InsertMany(It.IsAny<List<Book>>()), Times.Never());
        }

        [Test]
        public void should_separate_both_file_formats_when_the_same_print_edition_identified_the_work()
        {
            GivenEbookImport();
            var ebook = _approvedDecisions.Single();
            GivenAudiobookImport();
            var audio = _approvedDecisions.Single();
            var print = _ebookEdition;
            print.Format = "Print";
            print.IsEbook = false;
            print.ForeignEditionId = "hardcover:source-edition:33";
            ebook.Item.Edition = print;
            audio.Item.Edition = print;
            _approvedDecisions.Add(ebook);
            var inserted = GivenLocalDigitalEditionStorage();

            Subject.Import(_approvedDecisions, false).Should().OnlyContain(item => item.Result == ImportResultType.Imported);

            inserted.Select(edition => edition.Format).Should().BeEquivalentTo(new[] { "Audiobook", "Ebook" });
            ebook.Item.Edition.IsEbook.Should().BeTrue();
            audio.Item.Edition.IsEbook.Should().BeFalse();
            ebook.Item.Edition.Id.Should().NotBe(audio.Item.Edition.Id);
        }

        [TestCase(true)]
        [TestCase(false)]
        public void should_only_replace_the_print_match_with_a_digital_provider_edition_on_high_confidence_file_evidence(bool exactEvidence)
        {
            GivenEbookImport();
            var print = _ebookEdition;
            print.Format = "Print";
            print.IsEbook = false;
            var verified = new Edition { Id = 70, BookId = _book.Id, Format = "Ebook", IsEbook = true, Asin = "B00EXAMPLE" };
            _audiobookEdition.Format = "Audiobook";
            _audiobookEdition.Asin = verified.Asin;
            _approvedDecisions[0].Item.FileTrackInfo.Asin = verified.Asin;
            _approvedDecisions[0].Item.FileTrackInfo.AsinConfidence = exactEvidence ? 0.9 : 0.5;
            Mocker.GetMock<IEditionService>().Setup(service => service.GetEditionsByBook(_book.Id))
                .Returns(new List<Edition> { print, _audiobookEdition, verified });
            var inserted = GivenLocalDigitalEditionStorage();

            Subject.Import(_approvedDecisions, false).Should().ContainSingle(item => item.Result == ImportResultType.Imported);

            if (exactEvidence)
            {
                inserted.Should().BeEmpty();
                _approvedDecisions[0].Item.Edition.Should().BeSameAs(verified);
            }
            else
            {
                inserted.Should().ContainSingle();
                _approvedDecisions[0].Item.Edition.Should().BeSameAs(inserted.Single());
            }
        }

        [Test]
        public void should_capture_separate_verified_ebook_variants_and_hold_an_untagged_file_for_review()
        {
            GivenEbookImport();
            var first = _approvedDecisions.Single();
            _ebookEdition.Format = "Print";
            _ebookEdition.IsEbook = false;
            first.Item.Part = 0;
            first.Item.FileTrackInfo.Asin = "B00FIRST01";
            first.Item.FileTrackInfo.AsinConfidence = 0.95;
            var untagged = AddDigitalFallbackFile(first, "untagged.epub", new ParsedTrackInfo());
            var second = AddDigitalFallbackFile(first, "second.epub", new ParsedTrackInfo { Asin = "b00second2", AsinConfidence = 0.95 });
            var inserted = GivenLocalDigitalEditionStorage();

            var results = Subject.Import(_approvedDecisions, false);

            results.Should().ContainSingle(result => result.ImportDecision == untagged && result.Result == ImportResultType.Rejected);
            results.Count(result => result.Result == ImportResultType.Imported).Should().Be(2);
            inserted.Should().HaveCount(2).And.OnlyContain(edition => edition.Asin == null && edition.Isbn13 == null);
            inserted.Select(edition => edition.ForeignEditionId).Should().OnlyHaveUniqueItems();
            first.Item.Edition.Id.Should().NotBe(second.Item.Edition.Id);
        }

        [Test]
        public void should_hold_competing_complete_ebooks_without_file_identifiers()
        {
            GivenEbookImport();
            _ebookEdition.Format = "Print";
            AddDigitalFallbackFile(_approvedDecisions.Single(), "other.epub", new ParsedTrackInfo());
            var inserted = GivenLocalDigitalEditionStorage();

            var results = Subject.Import(_approvedDecisions, false);

            results.Should().HaveCount(2).And.OnlyContain(result => result.Result == ImportResultType.Rejected &&
                result.Errors.Contains("Competing complete ebook files require verified edition identifiers"));
            inserted.Should().BeEmpty();
        }

        [TestCase(null, null, 1000, true, true)]
        [TestCase("9781455546176", null, 1000, true, false)]
        [TestCase(null, "9781455546176", 1000, true, false)]
        [TestCase(null, null, 2000, true, true)]
        [TestCase("9781442482890", "9781442482890", 2000, true, true)]
        [TestCase("9781455546176", "9781455546176", 2000, true, false)]
        [TestCase(null, null, 1000, false, false)]
        public void should_distinguish_unverified_ebook_isbn_from_a_scan_duplicate(string retainedIsbn, string editionIsbn, int heldSize, bool existing, bool review)
        {
            GivenEbookImport();
            const string heldIsbn = "9781455546176";
            var first = _approvedDecisions.Single();
            first.Item.Size = 2000;
            first.Item.ExistingFile = true;
            first.Item.FileTrackInfo.Isbn = retainedIsbn;
            first.Item.FileTrackInfo.IsbnConfidence = 0.95;
            _ebookEdition.Isbn13 = editionIsbn;
            var second = AddDigitalFallbackFile(first, "alternate.epub", new ParsedTrackInfo { Isbn = heldIsbn, IsbnConfidence = 0.95 });
            second.Item.Size = heldSize;
            second.Item.ExistingFile = existing;

            var results = Subject.Import(_approvedDecisions, false);

            results.Should().ContainSingle(result => result.ImportDecision == first && result.Result == ImportResultType.Imported);
            var held = results.Single(result => result.ImportDecision == second);
            held.Result.Should().Be(ImportResultType.Skipped);
            held.HoldReason.Should().Be(review ? LibraryImportHoldReason.UnverifiedEditionIdentifier : LibraryImportHoldReason.DuplicateInScan);
        }

        [Test]
        public void should_keep_two_disc_slot_collision_and_log_only_safe_ordinals()
        {
            GivenAudiobookImport();
            using var logFactory = CaptureDebugMessages(out var messages);
            Mocker.GetMock<IMediaFileService>().Setup(service => service.GetUnmappedFiles()).Returns(new List<BookFile>());
            var first = _approvedDecisions.Single();
            first.Item.Size = 2000;
            first.Item.IsLibraryScan = true;
            first.Item.ExistingFile = true;
            first.Item.FileTrackInfo.DiscNumber = 1;
            first.Item.FileTrackInfo.TrackNumbers = new[] { 1 };
            first.Item.FileTrackInfo.ReleaseMBId = "private-release-id";

            var second = AddDigitalFallbackFile(first, "private-disc-2.m4b", new ParsedTrackInfo
            {
                DiscNumber = 2,
                TrackNumbers = new[] { 1 },
                ReleaseMBId = "private-release-id"
            });
            second.Item.Size = 1000;
            second.Item.IsLibraryScan = true;
            second.Item.ExistingFile = true;

            var results = Subject.Import(_approvedDecisions, false);

            results.Should().ContainSingle(result => result.ImportDecision == first && result.Result == ImportResultType.Imported);
            results.Single(result => result.ImportDecision == second).HoldReason.Should().Be(LibraryImportHoldReason.DuplicateInScan);
            var diagnostic = messages.Logs.Should().ContainSingle(message => message.StartsWith("DuplicateInScan audiobook", StringComparison.Ordinal)).Which;
            diagnostic.Should().Contain("stage=occupied-in-scan")
                .And.MatchRegex(@"candidate=\[pathSha256=[0-9a-f]{64} rawSingleDiscPart=unknown effectiveDisc=2 effectiveTrack=1 assignedPart=1\]")
                .And.MatchRegex(@"incumbent=\[pathSha256=[0-9a-f]{64} rawSingleDiscPart=unknown effectiveDisc=1 effectiveTrack=1 assignedPart=1\]")
                .And.NotContain(first.Item.Path)
                .And.NotContain(second.Item.Path)
                .And.NotContain("private-release-id");
        }

        private LogFactory CaptureDebugMessages(out MemoryTarget messages)
        {
            messages = new MemoryTarget { Layout = "${message}" };
            var logging = new LoggingConfiguration();
            logging.AddRule(LogLevel.Debug, LogLevel.Debug, messages);
            var logFactory = new LogFactory { Configuration = logging };
            Mocker.SetConstant(logFactory.GetLogger(nameof(ImportApprovedBooks)));
            return logFactory;
        }

        [Test]
        public void should_import_untagged_parts_as_part_of_one_identified_audiobook()
        {
            GivenAudiobookImport();
            var first = _approvedDecisions.Single();
            _audiobookEdition.Format = "Print";
            first.Item.Part = 0;
            first.Item.FileTrackInfo.Asin = "B00FIRST01";
            first.Item.FileTrackInfo.AsinConfidence = 0.95;
            var second = AddDigitalFallbackFile(first, "Book Title - Part 02.m4b", new ParsedTrackInfo());
            var inserted = GivenLocalDigitalEditionStorage();

            var results = Subject.Import(_approvedDecisions, false);

            results.Should().HaveCount(2).And.OnlyContain(result => result.Result == ImportResultType.Imported);
            inserted.Should().ContainSingle();
            first.Item.Edition.Id.Should().Be(second.Item.Edition.Id);
            first.Item.Part.Should().Be(1);
            second.Item.Part.Should().Be(2);
        }

        [Test]
        public void should_hold_untagged_files_when_several_complete_editions_match_the_work_and_format()
        {
            GivenEbookImport();
            _ebookEdition.Format = "Print";
            var candidates = new List<Edition>
            {
                new Edition { Id = 72, BookId = _book.Id, Format = "Ebook", IsEbook = true, Asin = "B00FIRST01" },
                new Edition { Id = 73, BookId = _book.Id, Format = "Ebook", IsEbook = true, Asin = "B00SECOND2" }
            };
            Mocker.GetMock<IEditionService>().Setup(service => service.GetEditionsByBook(_book.Id)).Returns(candidates);
            var inserted = GivenLocalDigitalEditionStorage();

            Subject.Import(_approvedDecisions, false).Should().ContainSingle(result => result.Result == ImportResultType.Rejected);

            inserted.Should().BeEmpty();
        }

        private ImportDecision<LocalBook> AddDigitalFallbackFile(ImportDecision<LocalBook> template, string fileName, ParsedTrackInfo tags)
        {
            var decision = new ImportDecision<LocalBook>(new LocalBook
            {
                Author = template.Item.Author,
                Book = template.Item.Book,
                Edition = template.Item.Edition,
                Part = template.Item.Part,
                Path = Path.Combine(_author.Path, fileName),
                Quality = template.Item.Quality,
                FileTrackInfo = tags
            });
            _approvedDecisions.Add(decision);
            return decision;
        }

        private List<Edition> GivenLocalDigitalEditionStorage()
        {
            var inserted = new List<Edition>();
            var editions = Mocker.GetMock<IEditionService>();
            editions.Setup(service => service.InsertMany(It.IsAny<List<Edition>>())).Callback<List<Edition>>(items =>
            {
                foreach (var edition in items)
                {
                    edition.Id = 100 + inserted.Count;
                    inserted.Add(edition);
                }
            });
            editions.Setup(service => service.GetEditionByForeignEditionId(It.IsAny<string>()))
                .Returns((string identity) => inserted.SingleOrDefault(edition => edition.ForeignEditionId == identity));
            return inserted;
        }

        [TestCase(true, 0, true, false, ImportResultType.Imported)]
        [TestCase(true, 0, false, false, ImportResultType.Skipped)]
        [TestCase(true, 30, true, false, ImportResultType.Skipped)]
        [TestCase(true, 30, false, true, ImportResultType.Imported)]
        [TestCase(false, 30, false, true, ImportResultType.Skipped)]
        [TestCase(false, 0, true, false, ImportResultType.Skipped)]
        public void should_preserve_the_tracked_library_row_on_an_approved_retry(bool existingLibraryFile, int editionId, bool persistedLink, bool repairedLink, ImportResultType expectedResult)
        {
            GivenEbookImport();
            var track = _approvedDecisions[0].Item;
            track.ExistingFile = existingLibraryFile;
            var stored = new BookFile { Id = 21, Path = track.Path, EditionId = editionId };
            var mediaFiles = Mocker.GetMock<IMediaFileService>();
            mediaFiles.Setup(service => service.GetFileWithPath(track.Path)).Returns(stored);
            mediaFiles.Setup(service => service.TryLinkUnmappedFile(stored, _ebookEdition)).Returns(persistedLink);
            mediaFiles.Setup(service => service.TryRepairUnsupportedEditionLink(stored, _ebookEdition)).Returns(repairedLink);

            var results = Subject.Import(_approvedDecisions, false);

            results.Should().ContainSingle(result => result.Result == expectedResult);
            mediaFiles.Verify(service => service.TryLinkUnmappedFile(stored, _ebookEdition), existingLibraryFile && editionId == 0 ? Times.Once() : Times.Never());
            mediaFiles.Verify(service => service.TryRepairUnsupportedEditionLink(stored, _ebookEdition), existingLibraryFile && editionId > 0 ? Times.Once() : Times.Never());
            mediaFiles.Verify(service => service.Delete(It.IsAny<BookFile>(), It.IsAny<DeleteMediaFileReason>()), Times.Never());
            mediaFiles.Verify(service => service.AddMany(It.Is<List<BookFile>>(files => files.Count > 0)), Times.Never());
            Mocker.GetMock<IMetadataTagService>().Verify(service => service.WriteTags(It.IsAny<BookFile>(), It.IsAny<bool>(), It.IsAny<bool>()), Times.Never());
        }

        [Test]
        public void should_preserve_a_prior_occupied_audiobook_part_on_scan_retry_without_preparing_the_book()
        {
            GivenAudiobookImport();
            using var logFactory = CaptureDebugMessages(out var messages);
            var track = _approvedDecisions[0].Item;
            track.ExistingFile = true;
            track.IsLibraryScan = true;
            track.RawTaggedPart = track.Part;
            var stored = new BookFile
            {
                Id = 21,
                Path = track.Path,
                Part = track.Part,
                UnmappedReason = LibraryImportHoldReason.DuplicateInScan
            };
            var mediaFiles = Mocker.GetMock<IMediaFileService>();
            mediaFiles.Setup(service => service.GetFileWithPath(track.Path)).Returns(stored);
            mediaFiles.Setup(service => service.GetUnmappedFiles()).Returns(new List<BookFile> { stored });
            mediaFiles.Setup(service => service.GetFileWithPath(It.IsAny<List<string>>())).Returns(new List<BookFile> { stored });
            mediaFiles.Setup(service => service.TryLinkUnmappedFile(stored, _audiobookEdition)).Returns(true);

            var result = Subject.Import(_approvedDecisions, false).Should().ContainSingle().Which;

            result.Result.Should().Be(ImportResultType.Skipped);
            result.HoldReason.Should().Be(LibraryImportHoldReason.DuplicateInScan);
            var diagnostic = messages.Logs.Should().ContainSingle(message => message.StartsWith("DuplicateInScan audiobook", StringComparison.Ordinal)).Which;
            diagnostic.Should().Contain("stage=retry-not-linked")
                .And.Contain("rawSingleDiscPart=1")
                .And.Contain("incumbent=[unobserved] retainedCandidatePart=1")
                .And.NotContain(track.Path);
            stored.EditionId.Should().Be(0);
            stored.UnmappedReason.Should().Be(LibraryImportHoldReason.DuplicateInScan);
            _audiobookEdition.Monitored.Should().BeTrue();
            _ebookEdition.Monitored.Should().BeTrue();
            mediaFiles.Verify(service => service.TryLinkUnmappedFile(stored, _audiobookEdition), Times.Never());
            mediaFiles.Verify(service => service.TryLinkUnmappedAudiobookPart(stored, _audiobookEdition), Times.Once());
            mediaFiles.Verify(service => service.AddMany(It.Is<List<BookFile>>(files => files.Count > 0)), Times.Never());
            Mocker.GetMock<IEditionService>().Verify(service => service.SetMonitored(It.IsAny<Edition>()), Times.Never());
            Mocker.GetMock<IEditionService>().Verify(service => service.SetMonitoredByFormat(It.IsAny<Edition>()), Times.Never());
            Mocker.GetMock<IEventAggregator>().Verify(events => events.PublishEvent(It.IsAny<BookEditedEvent>()), Times.Never());
            Mocker.GetMock<IEventAggregator>().Verify(events => events.PublishEvent(It.IsAny<TrackImportedEvent>()), Times.Never());
            Mocker.GetMock<IEventAggregator>().Verify(events => events.PublishEvent(It.IsAny<BookImportedEvent>()), Times.Never());
            Mocker.GetMock<IMetadataTagService>().Verify(service => service.WriteTags(It.IsAny<BookFile>(), It.IsAny<bool>(), It.IsAny<bool>()), Times.Never());
        }

        [TestCase(1, 0, true, ImportResultType.Skipped)]
        [TestCase(0, 0, true, ImportResultType.Skipped)]
        [TestCase(0, 2, true, ImportResultType.Skipped)]
        [TestCase(1, 2, true, ImportResultType.Skipped)]
        [TestCase(2, 2, true, ImportResultType.Imported)]
        [TestCase(1, 1, false, ImportResultType.Imported)]
        public void should_hold_a_changed_part_and_preserve_explicit_import_behavior(int storedPart, int decisionPart, bool fromScan, ImportResultType expected)
        {
            GivenAudiobookImport();
            var track = _approvedDecisions[0].Item;
            track.ExistingFile = true;
            track.IsLibraryScan = fromScan;
            track.Part = decisionPart;
            track.RawTaggedPart = decisionPart > 0 ? decisionPart : null;
            var stored = new BookFile
            {
                Id = 21,
                Path = track.Path,
                Part = storedPart,
                UnmappedReason = LibraryImportHoldReason.DuplicateInScan
            };
            var mediaFiles = Mocker.GetMock<IMediaFileService>();
            mediaFiles.Setup(service => service.GetFileWithPath(track.Path)).Returns(stored);
            mediaFiles.Setup(service => service.GetUnmappedFiles()).Returns(new List<BookFile> { stored });
            mediaFiles.Setup(service => service.GetFileWithPath(It.IsAny<List<string>>())).Returns(new List<BookFile> { stored });
            mediaFiles.Setup(service => service.TryLinkUnmappedFile(stored, _audiobookEdition)).Returns(true);
            mediaFiles.Setup(service => service.TryLinkUnmappedAudiobookPart(stored, _audiobookEdition)).Returns(true);

            var result = Subject.Import(_approvedDecisions, false).Should().ContainSingle().Which;

            result.Result.Should().Be(expected);
            if (expected == ImportResultType.Skipped)
            {
                result.HoldReason.Should().Be(LibraryImportHoldReason.DuplicateInScan);
            }

            mediaFiles.Verify(service => service.TryLinkUnmappedFile(stored, _audiobookEdition), fromScan ? Times.Never() : Times.Once());
            mediaFiles.Verify(service => service.TryLinkUnmappedAudiobookPart(stored, _audiobookEdition),
                fromScan && decisionPart > 0 && storedPart == decisionPart ? Times.Once() : Times.Never());
            Mocker.GetMock<IEventAggregator>().Verify(events => events.PublishEvent(It.IsAny<TrackImportedEvent>()),
                expected == ImportResultType.Imported ? Times.Once() : Times.Never());
            stored.Part.Should().Be(storedPart);
        }

        [TestCase(null, false)]
        [TestCase(2, false)]
        [TestCase(1, true)]
        public void should_keep_prior_audiobook_hold_without_corroborated_part_and_eligible_target(int? rawPart, bool missingAuthor)
        {
            GivenAudiobookImport();
            var track = _approvedDecisions.Single().Item;
            track.ExistingFile = true;
            track.IsLibraryScan = true;
            track.Part = 1;
            track.RawTaggedPart = rawPart;
            track.FileTrackInfo = new ParsedTrackInfo { TrackNumbers = new[] { 1 } };
            if (missingAuthor)
            {
                track.Author = null;
            }

            var held = new BookFile { Id = 21, Path = track.Path, Part = 1, UnmappedReason = LibraryImportHoldReason.DuplicateInScan };
            var mediaFiles = Mocker.GetMock<IMediaFileService>();
            mediaFiles.Setup(service => service.GetUnmappedFiles()).Returns(new List<BookFile> { held });
            mediaFiles.Setup(service => service.GetFileWithPath(It.IsAny<List<string>>())).Returns(new List<BookFile> { held });
            mediaFiles.Setup(service => service.TryLinkUnmappedAudiobookPart(held, _audiobookEdition)).Returns(true);
            mediaFiles.Setup(service => service.TryLinkUnmappedFile(held, _audiobookEdition)).Returns(true);

            var result = Subject.Import(_approvedDecisions, false).Should().ContainSingle().Which;

            result.Result.Should().Be(missingAuthor ? ImportResultType.Rejected : ImportResultType.Skipped);
            result.HoldReason.Should().Be(LibraryImportHoldReason.DuplicateInScan);
            held.EditionId.Should().Be(0);
            held.UnmappedReason.Should().Be(LibraryImportHoldReason.DuplicateInScan);
            mediaFiles.Verify(service => service.TryLinkUnmappedAudiobookPart(It.IsAny<BookFile>(), It.IsAny<Edition>()), Times.Never());
            mediaFiles.Verify(service => service.TryLinkUnmappedFile(It.IsAny<BookFile>(), It.IsAny<Edition>()), Times.Never());
            mediaFiles.Verify(service => service.AddMany(It.Is<List<BookFile>>(files => files.Count > 0)), Times.Never());
            Mocker.GetMock<IEditionService>().Verify(service => service.SetMonitored(It.IsAny<Edition>()), Times.Never());
            Mocker.GetMock<IEditionService>().Verify(service => service.SetMonitoredByFormat(It.IsAny<Edition>()), Times.Never());
            Mocker.GetMock<IEventAggregator>().Verify(events => events.PublishEvent(It.IsAny<BookEditedEvent>()), Times.Never());
            Mocker.GetMock<IEventAggregator>().Verify(events => events.PublishEvent(It.IsAny<TrackImportedEvent>()), Times.Never());
        }

        [Test]
        public void should_not_change_monitoring_for_a_linked_comparison_file_during_an_occupied_part_retry()
        {
            GivenAudiobookImport();
            Mocker.GetMock<IConfigService>().SetupGet(config => config.EnableDualFormatTracking).Returns(true);
            _audiobookEdition.Monitored = false;
            var track = _approvedDecisions.Single().Item;
            track.ExistingFile = true;
            track.IsLibraryScan = true;
            track.RawTaggedPart = track.Part;
            var comparison = new LocalBook
            {
                Author = _author,
                Book = _book,
                Edition = _audiobookEdition,
                Path = Path.Combine(_author.Path, "Linked Part 01.m4b"),
                Part = track.Part,
                Quality = track.Quality,
                ExistingFile = true,
                AdditionalFile = true,
                IsLibraryScan = true
            };
            _approvedDecisions.Add(new ImportDecision<LocalBook>(comparison));

            var stored = new BookFile { Id = 21, Path = track.Path, Part = track.Part, UnmappedReason = LibraryImportHoldReason.DuplicateInScan };
            var linked = new BookFile { Id = 22, Path = comparison.Path, Part = comparison.Part, EditionId = _audiobookEdition.Id };
            var mediaFiles = Mocker.GetMock<IMediaFileService>();
            mediaFiles.Setup(service => service.GetUnmappedFiles()).Returns(new List<BookFile> { stored });
            mediaFiles.Setup(service => service.GetFileWithPath(It.IsAny<List<string>>())).Returns(new List<BookFile> { stored });
            mediaFiles.Setup(service => service.GetFileWithPath(comparison.Path)).Returns(linked);
            Mocker.GetMock<IEditionService>().Setup(service => service.SetMonitoredByFormat(_audiobookEdition))
                .Callback(() => _audiobookEdition.Monitored = true)
                .Returns(new List<Edition> { _ebookEdition, _audiobookEdition });

            var results = Subject.Import(_approvedDecisions, false);

            results.Should().HaveCount(2).And.OnlyContain(result => result.Result == ImportResultType.Skipped);
            results.Single(result => result.ImportDecision.Item == track).HoldReason.Should().Be(LibraryImportHoldReason.DuplicateInScan);
            _audiobookEdition.Monitored.Should().BeFalse();
            _ebookEdition.Monitored.Should().BeTrue();
            linked.EditionId.Should().Be(_audiobookEdition.Id);
            mediaFiles.Verify(service => service.TryRepairUnsupportedEditionLink(linked, _audiobookEdition), Times.Never());
            Mocker.GetMock<IEventAggregator>().Verify(events => events.PublishEvent(It.IsAny<BookEditedEvent>()), Times.Never());
        }

        [TestCase(LibraryImportHoldReason.DuplicateInScan)]
        [TestCase(LibraryImportHoldReason.LowConfidence)]
        [TestCase(null)]
        public void should_hold_a_scan_retry_when_multiple_rows_resolve_to_its_path(LibraryImportHoldReason? otherReason)
        {
            GivenAudiobookImport();
            var track = _approvedDecisions.Single().Item;
            track.ExistingFile = true;
            track.IsLibraryScan = true;
            track.RawTaggedPart = track.Part;
            var first = new BookFile { Id = 21, Path = track.Path, Part = track.Part, UnmappedReason = LibraryImportHoldReason.DuplicateInScan };
            var variantPath = Path.Combine(Path.GetDirectoryName(track.Path), ".", Path.GetFileName(track.Path));
            var otherEditionId = otherReason == null ? _audiobookEdition.Id : 0;
            var second = new BookFile { Id = 22, Path = variantPath, Part = track.Part, UnmappedReason = otherReason, EditionId = otherEditionId };
            var mediaFiles = Mocker.GetMock<IMediaFileService>();
            mediaFiles.Setup(service => service.GetUnmappedFiles()).Returns(new List<BookFile> { first });
            mediaFiles.Setup(service => service.GetFileWithPath(It.IsAny<List<string>>())).Returns(new List<BookFile> { first, second });
            mediaFiles.Setup(service => service.TryLinkUnmappedAudiobookPart(It.IsAny<BookFile>(), _audiobookEdition)).Returns(true);

            var result = Subject.Import(_approvedDecisions, false).Should().ContainSingle().Which;

            result.Result.Should().Be(ImportResultType.Skipped);
            result.HoldReason.Should().Be(LibraryImportHoldReason.DuplicateInScan);
            first.EditionId.Should().Be(0);
            second.EditionId.Should().Be(otherEditionId);
            first.UnmappedReason.Should().Be(LibraryImportHoldReason.DuplicateInScan);
            second.UnmappedReason.Should().Be(otherReason);
            mediaFiles.Verify(service => service.TryLinkUnmappedAudiobookPart(It.IsAny<BookFile>(), It.IsAny<Edition>()), Times.Never());
            mediaFiles.Verify(service => service.TryLinkUnmappedFile(It.IsAny<BookFile>(), It.IsAny<Edition>()), Times.Never());
            mediaFiles.Verify(service => service.AddMany(It.Is<List<BookFile>>(files => files.Count > 0)), Times.Never());
            Mocker.GetMock<IEventAggregator>().Verify(events => events.PublishEvent(It.IsAny<BookEditedEvent>()), Times.Never());
            Mocker.GetMock<IEventAggregator>().Verify(events => events.PublishEvent(It.IsAny<TrackImportedEvent>()), Times.Never());
        }

        [Test]
        public void should_use_the_stored_edition_owner_after_provider_title_changes()
        {
            GivenAudiobookImport();
            _book.AuthorMetadataId = _author.AuthorMetadataId = 5;
            _audiobookEdition.ForeignEditionId = "hardcover:edition:42";
            var selected = new Edition { ForeignEditionId = _audiobookEdition.ForeignEditionId, Format = "Audiobook" };
            _approvedDecisions[0].Item.Book = new Book
            {
                Title = "Revised provider title",
                ForeignBookId = "hardcover:work:100",
                AuthorMetadataId = _book.AuthorMetadataId,
                Editions = new List<Edition> { selected }
            };
            _approvedDecisions[0].Item.Edition = selected;
            Mocker.GetMock<IEditionService>().Setup(s => s.GetEditionByForeignEditionId(selected.ForeignEditionId)).Returns(_audiobookEdition);
            Mocker.GetMock<IBookService>().Setup(s => s.GetBook(_book.Id)).Returns(_book);

            var results = Subject.Import(_approvedDecisions, false);

            results.Should().ContainSingle(result => result.Result == ImportResultType.Imported);
            _approvedDecisions[0].Item.Book.Should().BeSameAs(_book);
            Mocker.GetMock<IBookService>().Verify(s => s.InsertMany(It.IsAny<List<Book>>()), Times.Never());
        }

        [TestCase(true)]
        [TestCase(false)]
        public void should_validate_and_use_the_stored_edition_when_resolving_a_remote_edition(bool compatible)
        {
            GivenAudiobookImport();
            var storedEdition = compatible ? _audiobookEdition : _ebookEdition;
            _ebookEdition.Format = "Ebook";
            _audiobookEdition.Format = "Audiobook";
            var remoteEdition = new Edition { BookId = _book.Id, ForeignEditionId = "same-edition", Format = "Audiobook" };
            _approvedDecisions[0].Item.Edition = remoteEdition;
            storedEdition.ForeignEditionId = remoteEdition.ForeignEditionId;
            Mocker.GetMock<IEditionService>().Setup(s => s.GetEditionByForeignEditionId(remoteEdition.ForeignEditionId)).Returns(storedEdition);

            var results = Subject.Import(_approvedDecisions, false);

            results.Should().ContainSingle(result => result.Result == (compatible ? ImportResultType.Imported : ImportResultType.Rejected));
            Mocker.GetMock<IEditionService>()
                .Verify(s => s.SetMonitored(storedEdition), compatible ? Times.Once() : Times.Never());
            Mocker.GetMock<IMediaFileService>().Verify(s => s.AddMany(It.Is<List<BookFile>>(files =>
                files.Count == 1 && files[0].EditionId == storedEdition.Id)), compatible ? Times.Once() : Times.Never());
        }

        [TestCase(false)]
        [TestCase(true)]
        public void should_reject_a_selected_edition_owned_by_a_different_work(bool resolveByForeignId)
        {
            Mocker.GetMock<IConfigService>().Setup(s => s.EnableDualFormatTracking).Returns(true);
            GivenAudiobookImport();

            var otherWork = new Book { Id = _book.Id + 1, Author = _author };
            var storedEdition = new Edition
            {
                Id = 30,
                Book = otherWork,
                BookId = otherWork.Id,
                ForeignEditionId = "other-work-audio",
                Format = "Audiobook",
                Monitored = true
            };
            _approvedDecisions[0].Item.Edition = resolveByForeignId
                ? new Edition { ForeignEditionId = storedEdition.ForeignEditionId, Format = "Audiobook" }
                : storedEdition;
            Mocker.GetMock<IEditionService>().Setup(s => s.GetEditionByForeignEditionId(storedEdition.ForeignEditionId)).Returns(storedEdition);
            Mocker.GetMock<IEditionService>().Setup(s => s.SetMonitoredByFormat(storedEdition)).Returns(new List<Edition> { storedEdition });

            var result = Subject.Import(_approvedDecisions, false).Should().ContainSingle().Which;

            result.Result.Should().Be(ImportResultType.Rejected);
            result.Errors.Should().Equal("The selected edition belongs to a different work");
            result.ImportDecision.Rejections.Should().ContainSingle().Which.Type.Should().Be(RejectionType.Permanent);
            Mocker.GetMock<IEditionService>().Verify(s => s.SetMonitoredByFormat(It.IsAny<Edition>()), Times.Never());
            Mocker.GetMock<IEditionService>().Verify(s => s.SetMonitored(It.IsAny<Edition>()), Times.Never());
            Mocker.GetMock<IUpgradeMediaFiles>().Verify(s => s.UpgradeBookFile(It.IsAny<BookFile>(), It.IsAny<LocalBook>(), It.IsAny<bool>(), It.IsAny<bool>()), Times.Never());
            Mocker.GetMock<IMediaFileService>().Verify(s => s.AddMany(It.Is<List<BookFile>>(files => files.Count > 0)), Times.Never());
        }

        [TestCase(true)]
        [TestCase(false)]
        public void should_persist_later_files_when_one_approved_book_cannot_be_prepared(bool missingAuthor)
        {
            GivenAudiobookImport();
            Mocker.GetMock<IConfigService>().Setup(s => s.EnableDualFormatTracking).Returns(true);
            _book.ForeignBookId = "valid-work";
            var brokenBook = new Book { Id = 2, ForeignBookId = "broken-work", Title = "Broken Book" };
            var brokenEdition = new Edition { Id = 30, BookId = brokenBook.Id, Format = "Ebook", IsEbook = true };
            var brokenDecision = new ImportDecision<LocalBook>(new LocalBook
            {
                Author = missingAuthor ? null : _author,
                Book = brokenBook,
                Edition = brokenEdition,
                Part = 1,
                ExistingFile = true,
                Path = Path.Combine(_author.Path, "Broken Book.epub"),
                Quality = new QualityModel(Quality.EPUB),
                FileTrackInfo = new ParsedTrackInfo()
            });
            _approvedDecisions.Insert(0, brokenDecision);
            Mocker.GetMock<IEditionService>()
                .Setup(s => s.SetMonitoredByFormat(brokenEdition))
                .Throws(new System.InvalidOperationException("Unable to update edition"));

            var results = Subject.Import(_approvedDecisions, false);

            results.Should().ContainSingle(result => result.ImportDecision == brokenDecision && result.Result == ImportResultType.Rejected);
            results.Should().ContainSingle(result => result.ImportDecision == _approvedDecisions[1] && result.Result == ImportResultType.Imported);
            Mocker.GetMock<IMediaFileService>().Verify(s => s.AddMany(It.Is<List<BookFile>>(files =>
                files.Count == 1 && files[0].EditionId == _audiobookEdition.Id)), Times.Once());
            if (!missingAuthor)
            {
                ExceptionVerification.ExpectedErrors(1);
            }
        }

        [Test]
        public void should_use_SetMonitored_when_flag_off()
        {
            Mocker.GetMock<IConfigService>()
                .Setup(s => s.EnableDualFormatTracking).Returns(false);

            GivenAudiobookImport();

            Subject.Import(_approvedDecisions, false);

            Mocker.GetMock<IEditionService>()
                .Verify(v => v.SetMonitored(_audiobookEdition), Times.Once());

            Mocker.GetMock<IEditionService>()
                .Verify(v => v.SetMonitoredByFormat(It.IsAny<Edition>()), Times.Never());
        }

        [Test]
        public void should_use_SetMonitored_when_flag_off_ebook()
        {
            Mocker.GetMock<IConfigService>()
                .Setup(s => s.EnableDualFormatTracking).Returns(false);

            GivenEbookImport();

            Subject.Import(_approvedDecisions, false);

            Mocker.GetMock<IEditionService>()
                .Verify(v => v.SetMonitored(_ebookEdition), Times.Once());
            Mocker.GetMock<IEditionService>()
                .Verify(v => v.SetMonitoredByFormat(It.IsAny<Edition>()), Times.Never());
        }

        [Test]
        public void should_not_silently_drop_a_book_when_author_monitor_new_items_is_out_of_range()
        {
            Mocker.GetMock<IConfigService>()
                .Setup(s => s.EnableDualFormatTracking).Returns(false);

            // Regression (issue #256): a corrupted AuthorFormatProfiles/Authors
            // MonitorNewItems value (e.g. 3) used to make MonitorNewBookService throw
            // NotImplementedException. ImportApprovedBooks.EnsureBookAdded caught and logged
            // that exception and rejected the book, so the import reported 100% match rate
            // yet persisted no books. The service now falls back to a safe default, so the
            // import must complete without throwing and without rejecting the book.
            _author.MonitorNewItems = (NewItemMonitorTypes)3;
            _book.Id = 0;
            _book.ForeignBookId = "test-foreign-book-id";
            _ebookEdition.ForeignEditionId = "fixture-ebook-edition";
            _audiobookEdition.ForeignEditionId = "fixture-audiobook-edition";

            Mocker.SetConstant<IMonitorNewBookService>(new MonitorNewBookService(TestLogger));
            Mocker.GetMock<IBookService>()
                .SetupSequence(service => service.FindById(_book.ForeignBookId))
                .Returns((Book)null)
                .Returns(_book);
            Mocker.GetMock<IBookService>()
                .Setup(s => s.GetBooksByAuthor(It.IsAny<int>()))
                .Returns(new List<Book>());
            Mocker.GetMock<IBookService>()
                .Setup(s => s.InsertMany(It.IsAny<List<Book>>()))
                .Callback<List<Book>>(b =>
                {
                    if (b != null && b.Count > 0)
                    {
                        b[0].Id = 1000;
                    }
                });

            GivenEbookImport();

            var results = Subject.Import(_approvedDecisions, false);

            results.Should().ContainSingle().Which.Result.Should().Be(ImportResultType.Imported);
            _book.Id.Should().Be(1000);
            _book.Monitored.Should().BeFalse();
            var importedPath = _approvedDecisions[0].Item.Path;
            Mocker.GetMock<IMediaFileService>()
                .Verify(service => service.AddMany(It.Is<List<BookFile>>(files =>
                    files.Count == 1 && files[0].Path == importedPath && files[0].EditionId == _ebookEdition.Id)), Times.Once);
            ExceptionVerification.ExpectedWarns(1, "Unknown NewItemMonitorTypes value");
        }
    }
}
