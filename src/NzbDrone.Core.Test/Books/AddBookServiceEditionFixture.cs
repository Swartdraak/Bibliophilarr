using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using FluentAssertions;
using FluentValidation;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.Exceptions;
using NzbDrone.Core.ImportLists.Exclusions;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.BooksTests
{
    [TestFixture]
    public class AddBookServiceEditionFixture : CoreTest<AddBookService>
    {
        private const string WorkId = "OL101W";
        private const string AuthorId = "OL23A";
        private Edition _first;
        private Edition _second;

        [SetUp]
        public void SetUp()
        {
            var authorMetadata = new AuthorMetadata { ForeignAuthorId = AuthorId };
            _first = new Edition { ForeignEditionId = "OL202M" };
            _second = new Edition { ForeignEditionId = "OL303M" };
            var providerBook = new Book { ForeignBookId = WorkId, Editions = new List<Edition> { _first, _second } };

            Mocker.GetMock<IMetadataProviderOrchestrator>()
                .Setup(service => service.GetBookInfo(WorkId))
                .Returns(Tuple.Create(AuthorId, providerBook, new List<AuthorMetadata> { authorMetadata }));
            Mocker.GetMock<IAuthorService>()
                .Setup(service => service.FindById(AuthorId))
                .Returns(new Author { Id = 9, AuthorMetadataId = 7, Metadata = authorMetadata });
        }

        [TestCase("OL404M")]
        [TestCase("other:edition:/OL303M")]
        [TestCase("openlibrary:work:OL303M")]
        public void should_reject_a_missing_explicit_edition_before_discovering_or_writing_an_author(string editionId)
        {
            var request = RequestBook(editionId);
            var add = () => Subject.AddBook(request);

            var error = add.Should().Throw<ValidationException>().Which;
            error.Errors.Should().ContainSingle().Which.PropertyName.Should().Be("Editions");
            Mocker.GetMock<IAuthorService>().VerifyNoOtherCalls();
            Mocker.GetMock<IAddAuthorService>().VerifyNoOtherCalls();
            Mocker.GetMock<IImportListExclusionService>().VerifyNoOtherCalls();
            Mocker.GetMock<IBookService>().VerifyNoOtherCalls();
        }

        [TestCase("OL303M")]
        [TestCase("openlibrary:edition:OL303M")]
        [TestCase("/books/OL303M")]
        public void should_keep_the_requested_edition(string editionId)
        {
            var result = Subject.AddBook(RequestBook(editionId));

            result.Editions.Value.Where(edition => edition.Monitored).Should().ContainSingle().Which.Should().BeSameAs(_second);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void should_report_the_requested_work_field_for_a_missing_provider_work(bool scalar)
        {
            const string workId = "hardcover:work:301";
            const string editionId = "hardcover:source-edition:302";
            var request = new Book
            {
                ForeignBookId = workId,
                ForeignEditionId = editionId,
                Editions = new List<Edition> { new Edition { ForeignEditionId = editionId, Monitored = true } }
            };
            Mocker.GetMock<IMetadataProviderOrchestrator>()
                .Setup(provider => provider.GetBookInfo(workId))
                .Throws(new BookNotFoundException(workId));

            var add = () => scalar ? Subject.AddBookByExactEdition(request, 0) : Subject.AddBook(request);
            var error = add.Should().Throw<ValidationException>().Which;

            var failure = error.Errors.Should().ContainSingle().Which;
            failure.PropertyName.Should().Be(nameof(Book.ForeignBookId));
            failure.AttemptedValue.Should().Be(workId);
            Mocker.GetMock<IAddAuthorService>().VerifyNoOtherCalls();
            Mocker.GetMock<IImportListExclusionService>().VerifyNoOtherCalls();
            Mocker.GetMock<IBookService>().VerifyNoOtherCalls();
        }

        [TestCase("hardcover:edition:301", "Ebook", true)]
        [TestCase("hardcover:source-edition:302", "Print", false)]
        [TestCase("hardcover:source-edition:302", null, true)]
        [TestCase("hardcover:source-edition:302", "Audiobook", true)]
        public void should_reject_scalar_editions_without_a_verified_source_and_consistent_media_format(string editionId, string format, bool isEbook)
        {
            const string workId = "hardcover:work:301";
            var author = new AuthorMetadata { ForeignAuthorId = "hardcover:author:41" };
            var work = new Book
            {
                ForeignBookId = workId,
                TitleSlug = "hardcover-work-301",
                AuthorMetadata = author,
                Editions = new List<Edition> { new Edition { ForeignEditionId = editionId, Format = format, IsEbook = isEbook } }
            };
            Mocker.GetMock<IMetadataProviderOrchestrator>()
                .Setup(service => service.GetBookInfo(workId))
                .Returns(Tuple.Create(author.ForeignAuthorId, work, new List<AuthorMetadata> { author }));

            var add = () => Subject.AddBookByExactEdition(new Book { ForeignBookId = workId, ForeignEditionId = editionId }, 0);

            add.Should().Throw<ValidationException>().Which.Errors.Should().ContainSingle().Which.PropertyName.Should().Be("ForeignEditionId");
            Mocker.GetMock<IAuthorService>().VerifyNoOtherCalls();
            Mocker.GetMock<IAddAuthorService>().VerifyNoOtherCalls();
            Mocker.GetMock<IImportListExclusionService>().VerifyNoOtherCalls();
            Mocker.GetMock<IBookService>().VerifyNoOtherCalls();
        }

        [TestCase("openlibrary:work:OL801W", "OL801W", "openlibrary:author:OL802A", "openlibrary:edition:OL803M", "Ebook", true)]
        [TestCase("hardcover:work:801", "hardcover:work:801", "hardcover:author:802", "hardcover:source-edition:803", "Audiobook", false)]
        public void should_atomically_add_the_first_book_with_a_verified_scalar_edition(
            string workId, string lookupId, string authorId, string editionId, string format, bool isEbook)
        {
            var verifiedAuthor = new AuthorMetadata { ForeignAuthorId = authorId, Name = "Verified provider author" };
            var edition = new Edition { ForeignEditionId = editionId, Format = format, IsEbook = isEbook };
            var request = RequestScalarFirstBook(workId, editionId, authorId);
            var claim = request.Author.Value;
            claim.AuthorMetadataId = 733;
            claim.Tags.Add(17);
            claim.Monitored = true;
            claim.MonitorNewItems = NewItemMonitorTypes.None;
            claim.AddOptions.EbookRootFolderPath = "/ebooks";
            claim.AddOptions.AudiobookRootFolderPath = "/audio";
            Mocker.GetMock<IMetadataProviderOrchestrator>()
                .Setup(service => service.GetBookInfo(lookupId))
                .Returns(ScalarProviderResult(workId, verifiedAuthor, edition, new List<AuthorMetadata> { verifiedAuthor }));
            var authorAdder = Mocker.GetMock<IAddAuthorService>();
            Author persistedAuthor = null;
            authorAdder.Setup(service => service.AddNewAuthorWithBook(It.IsAny<Author>(), request, true))
                .Callback<Author, Book, bool>((author, _, _) => persistedAuthor = author)
                .Returns(request);

            var result = Subject.AddBookByExactEdition(request, 0);

            result.Should().BeSameAs(request);
            result.AuthorMetadata.Value.Should().BeSameAs(verifiedAuthor);
            result.Editions.Value.Should().ContainSingle().Which.Should().BeSameAs(edition);
            result.ForeignEditionId.Should().Be(editionId);
            edition.Monitored.Should().BeTrue();
            edition.ManualAdd.Should().BeTrue();
            persistedAuthor.Should().NotBeSameAs(claim);
            persistedAuthor.AuthorMetadataId.Should().Be(0);
            persistedAuthor.Metadata.Value.Should().BeSameAs(verifiedAuthor);
            result.Author.Value.Should().BeSameAs(persistedAuthor);
            persistedAuthor.RootFolderPath.Should().Be(claim.RootFolderPath);
            persistedAuthor.QualityProfileId.Should().Be(claim.QualityProfileId);
            persistedAuthor.MetadataProfileId.Should().Be(claim.MetadataProfileId);
            persistedAuthor.Tags.Should().BeEquivalentTo(claim.Tags);
            persistedAuthor.Monitored.Should().BeTrue();
            persistedAuthor.MonitorNewItems.Should().Be(NewItemMonitorTypes.None);
            persistedAuthor.AddOptions.EbookQualityProfileId.Should().Be(5);
            persistedAuthor.AddOptions.AudiobookQualityProfileId.Should().Be(6);
            persistedAuthor.AddOptions.EbookRootFolderPath.Should().Be("/ebooks");
            persistedAuthor.AddOptions.AudiobookRootFolderPath.Should().Be("/audio");
            authorAdder.Verify(service => service.AddNewAuthorWithBook(persistedAuthor, request, true), Times.Once());
            authorAdder.VerifyNoOtherCalls();
            Mocker.GetMock<IBookService>().Verify(service => service.FindById(workId), Times.Once());
            Mocker.GetMock<IBookService>().VerifyNoOtherCalls();
            Mocker.GetMock<IImportListExclusionService>().VerifyNoOtherCalls();
            Mocker.GetMock<IMetadataProviderOrchestrator>().Verify(service => service.GetBookInfo(lookupId), Times.Once());
            Mocker.GetMock<IMetadataProviderOrchestrator>().VerifyNoOtherCalls();
        }

        [TestCase("missing metadata")]
        [TestCase("ambiguous metadata")]
        [TestCase("missing name")]
        [TestCase("different author claim")]
        [TestCase("nested stored identity")]
        [TestCase("top-level stored identity")]
        [TestCase("malformed source")]
        public void should_reject_a_scalar_first_book_without_a_verified_new_author(string scenario)
        {
            const string workId = "hardcover:work:801";
            const string editionId = "hardcover:source-edition:803";
            var authorId = scenario == "malformed source" ? "hardcover:author:not-numeric" : "hardcover:author:802";
            var verifiedAuthor = new AuthorMetadata
            {
                ForeignAuthorId = authorId,
                Name = scenario == "missing name" ? string.Empty : "Verified provider author"
            };
            var authors = new List<AuthorMetadata> { verifiedAuthor };
            if (scenario == "missing metadata")
            {
                authors.Clear();
            }
            else if (scenario == "ambiguous metadata")
            {
                authors.Add(new AuthorMetadata { ForeignAuthorId = authorId, Name = "Second provider record" });
            }

            var edition = new Edition { ForeignEditionId = editionId, Format = "Audiobook" };
            var request = RequestScalarFirstBook(workId, editionId, scenario == "different author claim" ? "hardcover:author:805" : authorId);
            request.Author.Value.Id = scenario == "nested stored identity" ? 31 : 0;
            var claimedAuthorId = scenario == "top-level stored identity" ? 31 : 0;
            Mocker.GetMock<IMetadataProviderOrchestrator>()
                .Setup(service => service.GetBookInfo(workId))
                .Returns(ScalarProviderResult(workId, verifiedAuthor, edition, authors));

            var add = () => Subject.AddBookByExactEdition(request, claimedAuthorId);

            add.Should().Throw<ValidationException>().Which.Errors.Should().ContainSingle().Which.PropertyName.Should().Be("Author.ForeignAuthorId");
            Mocker.GetMock<IAddAuthorService>().VerifyNoOtherCalls();
            Mocker.GetMock<IBookService>().VerifyNoOtherCalls();
            Mocker.GetMock<IImportListExclusionService>().VerifyNoOtherCalls();
        }

        [TestCase(0, 0, true)]
        [TestCase(42, 42, true)]
        [TestCase(43, 0, false)]
        [TestCase(0, 43, false)]
        public void should_verify_numeric_claims_against_the_stored_provider_owner(int nestedClaim, int resourceClaim, bool accepted)
        {
            const string workId = "hardcover:work:801";
            const string authorId = "hardcover:author:802";
            const string editionId = "hardcover:source-edition:803";
            var metadata = new AuthorMetadata { Id = 24, ForeignAuthorId = authorId, Name = "Stored author" };
            var owner = new Author { Id = 42, AuthorMetadataId = metadata.Id, Metadata = metadata };
            var providerAuthor = new AuthorMetadata { ForeignAuthorId = authorId, Name = "Verified provider author" };
            var edition = new Edition { ForeignEditionId = editionId, Format = "Audiobook" };
            var request = RequestScalarFirstBook(workId, editionId, authorId);
            request.Author.Value.Id = nestedClaim;
            Mocker.GetMock<IAuthorService>().Setup(service => service.FindById(authorId)).Returns(owner);
            Mocker.GetMock<IMetadataProviderOrchestrator>().Setup(service => service.GetBookInfo(workId))
                .Returns(ScalarProviderResult(workId, providerAuthor, edition, new List<AuthorMetadata> { providerAuthor }));
            var bookService = Mocker.GetMock<IBookService>();

            if (accepted)
            {
                var stored = Subject.AddBookByExactEdition(request, resourceClaim);
                stored.Author.Value.Should().BeSameAs(owner);
                stored.AuthorMetadata.Value.Should().BeSameAs(metadata);
                stored.AuthorMetadataId.Should().Be(metadata.Id);
                bookService.Verify(service => service.FindById(workId), Times.Once());
                bookService.Verify(service => service.AddBook(stored, true), Times.Once());
            }
            else
            {
                var add = () => Subject.AddBookByExactEdition(request, resourceClaim);
                add.Should().Throw<ValidationException>().Which.Errors.Should().ContainSingle().Which.PropertyName.Should().Be("Author.ForeignAuthorId");
                Mocker.GetMock<IImportListExclusionService>().VerifyNoOtherCalls();
            }

            Mocker.GetMock<IAuthorService>().Verify(service => service.FindById(authorId), Times.Once());
            Mocker.GetMock<IAddAuthorService>().VerifyNoOtherCalls();
            bookService.VerifyNoOtherCalls();
        }

        private static Tuple<string, Book, List<AuthorMetadata>> ScalarProviderResult(
            string workId, AuthorMetadata author, Edition edition, List<AuthorMetadata> authors)
        {
            var work = new Book
            {
                ForeignBookId = workId,
                TitleSlug = workId.Replace(':', '-').ToLowerInvariant(),
                AuthorMetadata = author,
                Editions = new List<Edition> { edition }
            };
            return Tuple.Create(author.ForeignAuthorId, work, authors);
        }

        private static Book RequestScalarFirstBook(string workId, string editionId, string authorId)
        {
            var author = new Author
            {
                Metadata = new AuthorMetadata { ForeignAuthorId = authorId, Name = "Caller-supplied name" },
                RootFolderPath = "/library",
                QualityProfileId = 2,
                MetadataProfileId = 3,
                AddOptions = new AddAuthorOptions { EbookQualityProfileId = 5, AudiobookQualityProfileId = 6 }
            };
            return new Book { ForeignBookId = workId, ForeignEditionId = editionId, Author = author };
        }

        [Test]
        public void should_keep_exclusions_when_the_book_could_not_be_saved()
        {
            var conflict = new NzbDroneClientException(HttpStatusCode.Conflict, "A provider edition is already present in the catalogue.");
            Mocker.GetMock<IBookService>()
                .Setup(service => service.AddBook(It.IsAny<Book>(), true))
                .Throws(conflict);

            Assert.Throws<NzbDroneClientException>(() => Subject.AddBook(RequestBook("OL303M"))).Should().BeSameAs(conflict);

            Mocker.GetMock<IImportListExclusionService>().VerifyNoOtherCalls();
        }

        [Test]
        public void import_lists_can_still_use_the_default_provider_edition()
        {
            var added = Subject.AddBooks(new List<Book> { RequestBook("OL404M") });

            var result = added.Should().ContainSingle().Which;
            result.Editions.Value.Where(edition => edition.Monitored).Should().ContainSingle().Which.Should().BeSameAs(_first);
        }

        [TestCase("Ebook", true, false)]
        [TestCase("Audiobook", false, true)]
        public void should_resolve_the_work_marker_to_the_first_tracked_provider_representative(string format, bool isEbook, bool representativeIsTracked)
        {
            _first.Format = "Print";
            _first.Monitored = !representativeIsTracked;
            _second.Format = format;
            _second.IsEbook = isEbook;
            _second.Monitored = representativeIsTracked;
            var alternate = new Edition { ForeignEditionId = "OL505M", Format = isEbook ? "Audiobook" : "Ebook", IsEbook = !isEbook };
            SetupWorkMarkerResult(new List<Edition> { _first, _second, alternate });
            var request = RequestWorkMarker();

            var result = Subject.AddBook(request);

            result.ForeignBookId.Should().Be("openlibrary:work:OL101W");
            result.ForeignEditionId.Should().Be(_second.ForeignEditionId);
            result.AuthorMetadataId.Should().Be(7);
            result.Editions.Value.Where(edition => edition.Monitored).Should().ContainSingle().Which.Should().BeSameAs(_second);
            _second.Format.Should().Be(format);
            _second.ManualAdd.Should().BeTrue();
            Mocker.GetMock<IMetadataProviderOrchestrator>().Verify(provider => provider.GetBookInfo(WorkId), Times.Once());
            Mocker.GetMock<IMetadataProviderOrchestrator>().VerifyNoOtherCalls();
        }

        [TestCase("different work")]
        [TestCase("different author")]
        [TestCase("unknown author")]
        [TestCase("no edition")]
        [TestCase("print")]
        [TestCase("unknown format")]
        [TestCase("format conflicts with ebook flag")]
        [TestCase("ebook flag without an explicit format")]
        [TestCase("provider placeholder")]
        [TestCase("other provider's edition")]
        [TestCase("unrelated marker")]
        public void should_reject_an_unverified_work_marker_before_writes(string scenario)
        {
            var request = RequestWorkMarker();
            _second.Format = "Audiobook";
            var choices = new List<Edition> { _second };
            var returnedWorkId = "openlibrary:work:OL101W";
            var returnedAuthorId = AuthorId;
            switch (scenario)
            {
                case "different work":
                    returnedWorkId = "openlibrary:work:OL999W";
                    break;
                case "different author":
                    returnedAuthorId = "OL999A";
                    break;
                case "unknown author":
                    request.AuthorMetadata = null;
                    returnedAuthorId = "OL-unknown";
                    break;
                case "no edition":
                    choices.Clear();
                    break;
                case "print":
                    _second.Format = "Print";
                    break;
                case "unknown format":
                    _second.Format = null;
                    break;
                case "format conflicts with ebook flag":
                    _second.IsEbook = true;
                    break;
                case "ebook flag without an explicit format":
                    _second.Format = "Digital Audiobook";
                    _second.IsEbook = true;
                    break;
                case "provider placeholder":
                    _second.ForeignEditionId = "openlibrary:work:OL101W";
                    break;
                case "other provider's edition":
                    _second.ForeignEditionId = "hardcover:edition:123";
                    break;
                case "unrelated marker":
                    request.ForeignEditionId = request.Editions.Value.Single().ForeignEditionId = "openlibrary:work:OL999W";
                    break;
            }

            SetupWorkMarkerResult(choices, returnedWorkId, returnedAuthorId);

            var ex = Assert.Throws<ValidationException>(() => Subject.AddBook(request));
            ex.Errors.Should().ContainSingle().Which.PropertyName.Should().Be("Editions");
            Mocker.GetMock<IAuthorService>().VerifyNoOtherCalls();
            Mocker.GetMock<IAddAuthorService>().VerifyNoOtherCalls();
            Mocker.GetMock<IImportListExclusionService>().VerifyNoOtherCalls();
            Mocker.GetMock<IBookService>().VerifyNoOtherCalls();
        }

        [Test]
        public void should_not_treat_a_non_ui_marker_without_matching_scalar_as_a_default()
        {
            var request = RequestWorkMarker();
            request.ForeignEditionId = null;
            _second.Format = "Audiobook";
            SetupWorkMarkerResult(new List<Edition> { _second });

            var ex = Assert.Throws<ValidationException>(() => Subject.AddBook(request));
            ex.Errors.Should().ContainSingle().Which.PropertyName.Should().Be("Editions");
        }

        private void SetupWorkMarkerResult(List<Edition> editions, string workId = "openlibrary:work:OL101W", string authorId = AuthorId)
        {
            var owner = new AuthorMetadata { ForeignAuthorId = authorId };
            var work = new Book { ForeignBookId = workId, AuthorMetadata = owner, Editions = editions };
            Mocker.GetMock<IMetadataProviderOrchestrator>()
                .Setup(service => service.GetBookInfo(WorkId))
                .Returns(Tuple.Create(authorId, work, new List<AuthorMetadata> { owner }));
        }

        private static Book RequestWorkMarker()
        {
            const string marker = "openlibrary:work:OL101W";
            var book = RequestBook(marker);
            book.ForeignBookId = marker;
            book.ForeignEditionId = marker;
            book.AuthorMetadata = new AuthorMetadata { ForeignAuthorId = AuthorId };
            return book;
        }

        private static Book RequestBook(string editionId)
        {
            return new Book
            {
                ForeignBookId = WorkId,
                Editions = new List<Edition> { new Edition { ForeignEditionId = editionId, Monitored = true } }
            };
        }
    }
}
