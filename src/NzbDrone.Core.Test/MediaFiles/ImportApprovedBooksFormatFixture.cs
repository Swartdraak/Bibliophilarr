using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FizzWare.NBuilder;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.DecisionEngine;
using NzbDrone.Core.Exceptions;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.BookImport;
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
            additions.Setup(service => service.AddAuthor(It.Is<Author>(author => author.ForeignAuthorId == authorityId), false))
                .Returns(() =>
                {
                    authorityAdded = true;
                    return incumbent;
                });
            additions.Setup(service => service.AddAuthor(It.Is<Author>(author => author.ForeignAuthorId == syntheticId), false))
                .Returns(addedSynthetic);
            Mocker.GetMock<IEditionService>().Setup(service => service.SetMonitored(otherEdition)).Returns(new List<Edition> { otherEdition });

            Subject.Import(_approvedDecisions, false);

            authorityAdded.Should().BeTrue();
            additions.Verify(service => service.AddAuthor(It.Is<Author>(author => author.ForeignAuthorId == syntheticId), false),
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
                .Setup(service => service.AddAuthor(_author, false))
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
            Mocker.GetMock<IAddAuthorService>().Verify(service => service.AddAuthor(_author, false), Times.Once());
            Mocker.GetMock<IEditionService>().Verify(service => service.SetMonitored(It.IsAny<Edition>()), Times.Never());
            Mocker.GetMock<IMediaFileService>().Verify(service => service.AddMany(It.Is<List<BookFile>>(files => files.Count > 0)), Times.Never());
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
