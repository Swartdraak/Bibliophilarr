using System.Collections.Generic;
using Bibliophilarr.Api.V1.Books;
using NUnit.Framework;
using NzbDrone.Core.Books;

namespace NzbDrone.Api.Test.Books
{
    /// <summary>
    /// Issue #279 regression: POST /api/v1/book returned 500 (expected 201) at head
    /// 8d2281e4c for a valid add-book payload that includes editions but omits the
    /// optional addOptions field (the API schema does not require addOptions).
    ///
    /// Root cause (service-layer null dereference, distinct from issue #276's
    /// validation/mapping-layer defects): BookResourceMapper.ToModel set
    /// "AddOptions = resource.AddOptions" with no null guard. When the client omits
    /// addOptions, resource.AddOptions is null, and the mapper copied null over the
    /// Book constructor's "AddOptions = new AddBookOptions();" initialization.
    /// AddBookService.AddBook then executed
    /// "book.AddOptions.AddType = BookAddType.Manual;" (AddBookService.cs:63),
    /// dereferencing the null AddOptions -> NullReferenceException -> 500.
    ///
    /// Fix (option A from issue #279): BookResourceMapper.ToModel now substitutes a
    /// fresh AddBookOptions when resource.AddOptions is null:
    /// "AddOptions = resource.AddOptions ?? new AddBookOptions();".
    ///
    /// These fixtures pin the repaired behavior:
    ///   - a BookResource with null AddOptions maps to a Book whose AddOptions is
    ///     non-null (a fresh AddBookOptions with the default AddType.Automatic);
    ///   - a BookResource with a non-null AddOptions preserves the provided instance
    ///     (no data loss for clients that DO send addOptions);
    ///   - the add-book payload shape from issue #279 (editions present, addOptions
    ///     omitted) maps cleanly through ToModel without throwing.
    /// </summary>
    [TestFixture]
    public class BookResourceMapperIssue279Fixture
    {
        [Test]
        public void to_model_with_null_add_options_should_substitute_fresh_add_book_options_not_null()
        {
            // A payload that omits addOptions (optional per the API schema) — the
            // exact shape that produced the 500 NRE at head 8d2281e4c (issue #279).
            var resource = new BookResource
            {
                Title = "Add-book payload without addOptions",
                ForeignBookId = "hardcover:work:test-work-id",
                Editions = new List<EditionResource>
                {
                    new EditionResource
                    {
                        ForeignEditionId = "hardcover:edition:test-edition-id",
                        Monitored = true,
                    },
                },
            };

            // AddOptions intentionally left null (client omitted the optional field) —
            // the exact shape that produced the 500 NRE at head 8d2281e4c (issue #279).
            // The service-layer invariant is that AddOptions is never null in the
            // add-book path, so AddBookService.AddBook's
            // "book.AddOptions.AddType = Manual" cannot throw NullReferenceException.
            Book book = null;
            Assert.DoesNotThrow(() => book = resource.ToModel(),
                "BookResource.ToModel with null AddOptions must not throw (issue #279)");

            Assert.NotNull(book);
            Assert.NotNull(book.AddOptions,
                "AddOptions must never be null after ToModel when the client omits addOptions (issue #279)");

            // A fresh AddBookOptions() defaults AddType to Automatic (the model's
            // constructor default); AddBookService.AddBook then overwrites it to
            // Manual. What matters here is non-null, not the pre-service value.
            Assert.AreEqual(BookAddType.Automatic, book.AddOptions.AddType);
        }

        [Test]
        public void to_model_with_omitted_add_options_and_editions_should_not_throw()
        {
            // The minimal issue-#279 payload: a title, a foreign id, and editions,
            // with no addOptions field at all. This is the payload shape the
            // corrected browser harness (dc7c8008) sent that 500'd at head
            // 8d2281e4c; the mapping must now complete.
            var resource = new BookResource
            {
                Title = "Minimal add-book payload",
                ForeignBookId = "hardcover:work:minimal",
                Author = new Bibliophilarr.Api.V1.Author.AuthorResource
                {
                    AuthorName = "Unknown Author",
                },
                Editions = new List<EditionResource>
                {
                    new EditionResource
                    {
                        ForeignEditionId = "hardcover:edition:minimal",
                        Monitored = true,
                    },
                },
            };

            Book book = null;
            Assert.DoesNotThrow(() => book = resource.ToModel(),
                "issue #279 payload (editions present, addOptions omitted) must map without throwing");

            Assert.NotNull(book);
            Assert.NotNull(book.AddOptions, "add-book path must never yield a null AddOptions (issue #279)");
            Assert.AreEqual(1, book.Editions.Value.Count);
            Assert.AreEqual("hardcover:work:minimal", book.ForeignBookId);
        }

        [Test]
        public void to_model_with_provided_add_options_should_preserve_instance()
        {
            // Regression guard: the fix must not discard a client-provided addOptions.
            // When the payload DOES carry addOptions, the provided instance (with its
            // configured values) must be preserved, not replaced by a fresh default.
            var provided = new AddBookOptions
            {
                AddType = BookAddType.Manual,
                SearchForNewBook = true,
            };

            var resource = new BookResource
            {
                Title = "Add-book payload with addOptions",
                ForeignBookId = "hardcover:work:with-options",
                AddOptions = provided,
            };

            var book = resource.ToModel();

            // The fix must not discard a client-provided addOptions instance.
            Assert.NotNull(book);
            Assert.AreSame(provided, book.AddOptions);
            Assert.AreEqual(BookAddType.Manual, book.AddOptions.AddType);
            Assert.IsTrue(book.AddOptions.SearchForNewBook);
        }
    }
}
