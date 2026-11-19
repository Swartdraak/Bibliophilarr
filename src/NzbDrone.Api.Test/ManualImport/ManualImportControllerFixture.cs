using System.Collections.Generic;
using System.Linq;
using Bibliophilarr.Api.V1.ManualImport;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.Configuration;
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
                Edition = new Edition { Id = 9, ForeignEditionId = "OL321M" }
            };
            _manualImportService
                .Setup(s => s.UpdateItems(It.IsAny<List<ManualImportItem>>()))
                .Returns(new List<ManualImportItem> { mockItem });

            var result = _controller.UpdateItems(resources);

            // Not all items are unmappable (one has BookId=42), so it should be 202.
            result.Should().BeOfType<AcceptedResult>();
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
