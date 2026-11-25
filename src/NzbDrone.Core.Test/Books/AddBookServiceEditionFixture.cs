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
