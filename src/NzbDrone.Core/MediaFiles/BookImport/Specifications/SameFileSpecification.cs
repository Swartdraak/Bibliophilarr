using System.Linq;
using NLog;
using NzbDrone.Common;
using NzbDrone.Core.Books;
using NzbDrone.Core.DecisionEngine;
using NzbDrone.Core.Download;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.MediaFiles.BookImport.Specifications
{
    public class SameFileSpecification : IImportDecisionEngineSpecification<LocalBook>
    {
        private readonly Logger _logger;

        public SameFileSpecification(Logger logger)
        {
            _logger = logger;
        }

        public Decision IsSatisfiedBy(LocalBook localBook, DownloadClientItem downloadClientItem)
        {
            var bookFiles = localBook.Book?.BookFiles?.Value;

            if (bookFiles == null || !bookFiles.Any())
            {
                _logger.Debug("No existing book file, skipping");
                return Decision.Accept();
            }

            var incomingFormat = localBook.Size > 0 ? ImportFormat.ForPath(localBook.Path) : null;
            foreach (var bookFile in bookFiles)
            {
                if (bookFile == null)
                {
                    var book = localBook.Book;
                    _logger.Trace("Unable to get book file details from the DB. BookId: {0}", book.Id);

                    return Decision.Accept();
                }

                var storedFormat = ImportFormat.ForPath(bookFile.Path);
                if (bookFile.Size != localBook.Size ||
                    (incomingFormat.HasValue && storedFormat.HasValue && incomingFormat != storedFormat) ||
                    (localBook.ExistingFile && PathEqualityComparer.Instance.Equals(bookFile.Path, localBook.Path)))
                {
                    continue;
                }

                if (incomingFormat == FormatType.Ebook && storedFormat == FormatType.Ebook &&
                    HasDistinctVerifiedEbookEdition(localBook, bookFile))
                {
                    continue;
                }

                _logger.Debug("'{0}' Has the same filesize as existing file", localBook.Path);
                return Decision.RejectWithHoldReason("Has the same filesize as existing file", LibraryImportHoldReason.SameSizeAsExisting);
            }

            return Decision.Accept();
        }

        private static bool HasDistinctVerifiedEbookEdition(LocalBook localBook, BookFile bookFile)
        {
            var edition = localBook.Edition;
            var tags = localBook.FileTrackInfo;
            if (!localBook.ExistingFile || edition is not { Id: > 0, BookId: > 0 } ||
                bookFile.EditionId <= 0 || bookFile.EditionId == edition.Id ||
                tags?.IsbnConfidence is not >= 0.9 || ImportFormat.ForEdition(edition) != FormatType.Ebook)
            {
                return false;
            }

            var existingEdition = bookFile.Edition?.Value;
            var incomingIsbn = Isbn.AsIsbn13(edition.Isbn13);
            var existingIsbn = Isbn.AsIsbn13(existingEdition?.Isbn13);

            return existingEdition?.Id == bookFile.EditionId && existingEdition.BookId == edition.BookId &&
                   ImportFormat.ForEdition(existingEdition) == FormatType.Ebook && incomingIsbn != null &&
                   existingIsbn != null && incomingIsbn != existingIsbn && Isbn.AreEquivalent(tags.Isbn, incomingIsbn);
        }
    }
}
