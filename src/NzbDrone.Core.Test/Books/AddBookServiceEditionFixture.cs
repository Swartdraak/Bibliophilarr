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
