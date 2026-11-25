using Bibliophilarr.Http;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.Books;

namespace Bibliophilarr.Api.V1.Books
{
    [V1ApiController("book/editor")]
    public class BookEditorController : Controller
    {
        private readonly IBookService _bookService;

        public BookEditorController(IBookService bookService)
        {
            _bookService = bookService;
        }

        [HttpPut]
        public IActionResult SaveAll([FromBody] BookEditorResource resource)
        {
            var booksToUpdate = _bookService.GetBooks(resource.BookIds);

            foreach (var book in booksToUpdate)
            {
                if (resource.Monitored.HasValue)
                {
                    book.Monitored = resource.Monitored.Value;
                }
            }

            _bookService.UpdateMany(booksToUpdate);
            return Accepted(booksToUpdate.ToResource());
        }

        [HttpDelete]
        public void DeleteBook([FromBody] BookEditorResource resource, [FromServices] IRemoveBookFromLibrary bookRemoval)
        {
            foreach (var bookId in resource.BookIds)
            {
                bookRemoval.Remove(bookId, resource.DeleteFiles ?? false, resource.AddImportListExclusion ?? false);
            }
        }
    }
}
