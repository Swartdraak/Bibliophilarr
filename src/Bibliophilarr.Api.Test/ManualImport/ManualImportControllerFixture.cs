using System.Collections.Generic;
using System.Linq;
using Bibliophilarr.Api.V1.ManualImport;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using NzbDrone.Common.Serializer;
using NzbDrone.Core.Books;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.DecisionEngine;
using NzbDrone.Core.MediaFiles.BookImport.Manual;

namespace NzbDrone.Api.Test.ManualImport
{
    // Issue #295 regression: POST /api/v1/manualimport (update) returns 202 while
    // importing nothing — every BookFile persists EditionId=0 (unmapped).
    //
    // The controller must NOT report a silent 202 success when no file is importable.
    // When every submitted item lacks a usable catalogue BookId/ForeignEditionId, the
    // endpoint must return 400 with per-item rejections so callers are not told a
    // no-op import succeeded.
    [TestFixture]
    public class ManualImportControllerFixture
    {
        private ManualImportController _controller;
        private Mock<IManualImportService> _manualImportService;
        private Mock<IAuthorService> _authorService;
        private Mock<IBookService> _bookService;
        private Mock<IEditionService> _editionService;
        private Mock<IAuthorFormatProfileService> _formatProfileService;
        private Mock<IConfigService> _configService;

        [SetUp]
        public void Setup()
        {
            _manualImportService = new Mock<IManualImportService>();
            _authorService = new Mock<IAuthorService>();
            _bookService = new Mock<IBookService>();
            _editionService = new Mock<IEditionService>();
            _formatProfileService = new Mock<IAuthorFormatProfileService>();
            _configService = new Mock<IConfigService>();

            _controller = new ManualImportController(
                _manualImportService.Object,
                _authorService.Object,
                _editionService.Object,
                _bookService.Object,
                _formatProfileService.Object,
                _configService.Object,
                NzbDrone.Common.Instrumentation.NzbDroneLogger.GetLogger("TestLogger"));
        }

        [Test]
        public void should_return_400_when_all_items_lack_book_id()
        {
            var resources = new List<ManualImportUpdateResource>
            {
                new ManualImportUpdateResource
                {
                    Path = "/root/Author/Book1.epub",
                    AuthorId = 1,
                    BookId = null,
                    ForeignEditionId = null
                },
                new ManualImportUpdateResource
                {
                    Path = "/root/Author/Book2.epub",
                    AuthorId = 1,
                    BookId = null,
                    ForeignEditionId = null
                }
            };

            var result = _controller.UpdateItems(resources);

            result.Should().BeOfType<BadRequestObjectResult>();
            var badRequest = (BadRequestObjectResult)result;
            badRequest.StatusCode.Should().Be(400);

            // The body should contain per-item rejection info.
            var body = badRequest.Value as IEnumerable<dynamic>;
            body.Should().NotBeNull();
            var items = body.ToList();
            items.Should().HaveCount(2);
        }

        [Test]
        public void should_return_400_when_book_id_is_zero()
        {
            var resources = new List<ManualImportUpdateResource>
            {
                new ManualImportUpdateResource
                {
                    Path = "/root/Author/Book1.epub",
                    AuthorId = 1,
                    BookId = 0,
                    ForeignEditionId = null
                }
            };

            var result = _controller.UpdateItems(resources);

            result.Should().BeOfType<BadRequestObjectResult>();
            ((BadRequestObjectResult)result).StatusCode.Should().Be(400);
        }

        [Test]
        public void should_return_202_when_at_least_one_item_has_book_id()
        {
            var mappedResource = new ManualImportUpdateResource
            {
                Path = "/root/Author/Book1.epub",
                AuthorId = 1,
                BookId = 42,
                ForeignEditionId = "OL321M"
            };

            var unmappedResource = new ManualImportUpdateResource
            {
                Path = "/root/Author/Book2.epub",
                AuthorId = 1,
                BookId = null,
                ForeignEditionId = null
            };

            var resources = new List<ManualImportUpdateResource> { mappedResource, unmappedResource };

            var mockItem = new ManualImportItem
            {
                Path = "/root/Author/Book1.epub",
                Author = new Author { Id = 1, Name = "Test Author" },
                Book = new Book { Id = 42, Title = "Test Book" },
                Edition = new Edition { Id = 9, BookId = 42, ForeignEditionId = "OL321M" }
            };
            _bookService.Setup(service => service.GetBook(42)).Returns(mockItem.Book);
            _editionService.Setup(service => service.GetEditionByForeignEditionId("OL321M")).Returns(mockItem.Edition);
            _manualImportService
                .Setup(s => s.UpdateItems(It.IsAny<List<ManualImportItem>>()))
                .Returns(new List<ManualImportItem> { mockItem });

            var result = _controller.UpdateItems(resources);

            // Not all items are unmappable (one has BookId=42), so it should be 202.
            result.Should().BeOfType<AcceptedResult>();
        }

        [TestCase(true, true, 422)]
        [TestCase(true, false, 202)]
        [TestCase(false, true, 202)]
        [TestCase(false, false, 202)]
        public void should_keep_per_item_rejections_and_only_change_status_when_requested(bool allRejected, bool reportRejectionStatus, int status)
        {
            var files = new List<ManualImportItem>
            {
                new ManualImportItem
                {
                    Id = 101,
                    Path = "/root/Author/Rejected.epub",
                    Rejections = new List<Rejection> { new Rejection("Couldn't find similar book") }
                },
                new ManualImportItem
                {
                    Id = 102,
                    Path = "/root/Author/Second.epub",
                    Rejections = allRejected
                        ? new List<Rejection> { new Rejection("File format does not match the selected edition") }
                        : new List<Rejection>()
                }
            };
            _manualImportService.Setup(service => service.UpdateItems(It.IsAny<List<ManualImportItem>>())).Returns(files);

            var result = (ObjectResult)_controller.UpdateItems(files.Select(file => new ManualImportUpdateResource
            {
                Id = file.Id,
                Path = file.Path,
                AuthorId = 1,
                BookId = 42
            }).ToList(), reportRejectionStatus);

            result.StatusCode.Should().Be(status);
            var rows = ((IEnumerable<ManualImportResource>)result.Value).ToList();
            rows.Select(row => row.Id).Should().Equal(101, 102);
            rows[0].Rejections.Select(rejection => rejection.Reason).Should().Equal("Couldn't find similar book");
            rows[1].Rejections.Should().HaveCount(allRejected ? 1 : 0);
            rows.Select(row => JObject.Parse(row.ToJson()).Value<string>("selectionRejection"))
                .Should().OnlyContain(rejection => rejection == null);
        }

        [TestCase("selected-edition", null, true, 422, "could not be resolved")]
        [TestCase("selected-edition", null, false, 202, "could not be resolved")]
        [TestCase("selected-edition", 43, true, 422, "belongs to a different book")]
        [TestCase("selected-edition", 42, true, 422, "File format does not match")]
        [TestCase(" ", 42, true, 422, "could not be resolved")]
        public void should_hold_an_unavailable_or_incompatible_selected_edition(string selectedEditionId, int? editionBookId, bool reportRejectionStatus, int status, string reason)
        {
            var preferred = new Edition { BookId = 42, ForeignEditionId = "existing-ebook", IsEbook = true };
            _bookService.Setup(service => service.GetBook(42))
                .Returns(new Book { Id = 42, Editions = new List<Edition> { preferred } });
            if (editionBookId.HasValue)
            {
                _editionService.Setup(service => service.GetEditionByForeignEditionId(selectedEditionId))
                    .Returns(new Edition { BookId = editionBookId.Value, ForeignEditionId = selectedEditionId, Format = "Audiobook" });
            }

            var result = (ObjectResult)_controller.UpdateItems(new List<ManualImportUpdateResource>
            {
                new ManualImportUpdateResource
                {
                    Id = 101,
                    Path = "/root/Author/Book.epub",
                    BookId = 42,
                    ForeignEditionId = selectedEditionId
                }
            }, reportRejectionStatus);

            result.StatusCode.Should().Be(status);
            var row = ((IEnumerable<ManualImportResource>)result.Value).Should().ContainSingle().Which;
            row.Id.Should().Be(101);
            row.ForeignEditionId.Should().Be(selectedEditionId);
            var rejection = row.Rejections.Should().ContainSingle().Which.Reason;
            rejection.Should().Contain(reason);
            JObject.Parse(row.ToJson()).Value<string>("selectionRejection").Should().Be(rejection);
            _manualImportService.Verify(service => service.UpdateItems(It.IsAny<List<ManualImportItem>>()), Times.Never);
        }

        [Test]
        public void should_keep_the_valid_preview_in_a_batch_with_a_missing_selection()
        {
            var book = new Book { Id = 42, Title = "Selected book" };
            var edition = new Edition { BookId = 42, ForeignEditionId = "known-ebook", IsEbook = true };
            _bookService.Setup(service => service.GetBook(42)).Returns(book);
            _editionService.Setup(service => service.GetEditionByForeignEditionId(edition.ForeignEditionId)).Returns(edition);
            _manualImportService.Setup(service => service.UpdateItems(It.IsAny<List<ManualImportItem>>()))
                .Returns((List<ManualImportItem> files) => files);

            var result = (ObjectResult)_controller.UpdateItems(new List<ManualImportUpdateResource>
            {
                new ManualImportUpdateResource { Id = 101, Path = "/root/Author/Held.epub", BookId = 42, ForeignEditionId = "missing" },
                new ManualImportUpdateResource { Id = 102, Path = "/root/Author/Valid.epub", BookId = 42, ForeignEditionId = edition.ForeignEditionId }
            }, true);

            result.StatusCode.Should().Be(202);
            var rows = ((IEnumerable<ManualImportResource>)result.Value).ToDictionary(row => row.Id);
            rows[101].ForeignEditionId.Should().Be("missing");
            var rejection = rows[101].Rejections.Should().ContainSingle().Which.Reason;
            JObject.Parse(rows[101].ToJson()).Value<string>("selectionRejection").Should().Be(rejection);
            JObject.Parse(rows[102].ToJson()).Value<string>("selectionRejection").Should().BeNull();
            rows[102].ForeignEditionId.Should().Be(edition.ForeignEditionId);
            rows[102].Rejections.Should().BeNullOrEmpty();
            _manualImportService.Verify(service => service.UpdateItems(It.Is<List<ManualImportItem>>(files =>
                files.Count == 1 && files[0].Id == 102 && files[0].Edition == edition)), Times.Once);
        }

        [TestCase(null)]
        [TestCase(" ")]
        public void should_hold_a_missing_path_and_keep_the_valid_preview_in_a_mixed_batch(string path)
        {
            var book = new Book { Id = 42, Title = "Selected book" };
            var edition = new Edition { BookId = 42, ForeignEditionId = "known-ebook", IsEbook = true };
            _bookService.Setup(service => service.GetBook(42)).Returns(book);
            _editionService.Setup(service => service.GetEditionByForeignEditionId(edition.ForeignEditionId)).Returns(edition);
            _manualImportService.Setup(service => service.UpdateItems(It.IsAny<List<ManualImportItem>>()))
                .Returns((List<ManualImportItem> files) => files);

            var result = (ObjectResult)_controller.UpdateItems(new List<ManualImportUpdateResource>
            {
                new ManualImportUpdateResource { Id = 101, Path = path, BookId = 42, ForeignEditionId = edition.ForeignEditionId },
                new ManualImportUpdateResource { Id = 102, Path = "/root/Author/Valid.epub", BookId = 42, ForeignEditionId = edition.ForeignEditionId }
            }, true);

            result.StatusCode.Should().Be(202);
            var rows = ((IEnumerable<ManualImportResource>)result.Value).ToDictionary(row => row.Id);
            rows[101].ForeignEditionId.Should().Be(edition.ForeignEditionId);
            var rejection = rows[101].Rejections.Should().ContainSingle().Which.Reason;
            rejection.Should().Contain("file path is required");
            JObject.Parse(rows[101].ToJson()).Value<string>("selectionRejection").Should().Be(rejection);
            JObject.Parse(rows[102].ToJson()).Value<string>("selectionRejection").Should().BeNull();
            rows[102].Rejections.Should().BeNullOrEmpty();
            _manualImportService.Verify(service => service.UpdateItems(It.Is<List<ManualImportItem>>(files =>
                files.Count == 1 && files[0].Id == 102)), Times.Once);
        }

        [Test]
        public void should_keep_an_empty_preview_accepted()
        {
            _manualImportService.Setup(service => service.UpdateItems(It.IsAny<List<ManualImportItem>>()))
                .Returns(new List<ManualImportItem>());

            _controller.UpdateItems(new List<ManualImportUpdateResource>()).Should().BeOfType<AcceptedResult>();
        }

        [Test]
        public void should_not_call_service_when_all_items_unmappable()
        {
            var resources = new List<ManualImportUpdateResource>
            {
                new ManualImportUpdateResource
                {
                    Path = "/root/Author/Book1.epub",
                    AuthorId = null,
                    BookId = null,
                    ForeignEditionId = null
                }
            };

            _controller.UpdateItems(resources);

            // The service should NOT be called when all items are unmappable —
            // the 400 short-circuits before any import work.
            _manualImportService.Verify(
                s => s.UpdateItems(It.IsAny<List<ManualImportItem>>()),
                Times.Never);
        }
    }
}
