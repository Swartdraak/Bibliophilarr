using System.Collections.Generic;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.BookImport.Specifications;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MediaFiles.BookImport.Specifications
{
    [TestFixture]
    public class SameFileSpecificationFixture : CoreTest<SameFileSpecification>
    {
        [TestCase("/library/original.epub", true, true)]
        [TestCase("/library/original.epub", false, false)]
        [TestCase("/library/different.epub", true, false)]
        [TestCase("/library/audiobook.m4b", false, true)]
        public void distinguishes_a_library_rescan_from_a_duplicate_path_or_format(string path, bool existingLibraryFile, bool accepted)
        {
            var candidate = new LocalBook
            {
                ExistingFile = existingLibraryFile,
                Path = path,
                Size = 2048,
                Book = new Book
                {
                    BookFiles = new List<BookFile>
                    {
                        new BookFile { Path = "/library/original.epub", Size = 2048 }
                    }
                }
            };

            Subject.IsSatisfiedBy(candidate, null).Accepted.Should().Be(accepted);
        }

        [TestCase("9781442482890", "9780140328721", 0.97, true, 2, true)]
        [TestCase("9781442482890", "9781442482890", 0.97, true, 2, false)]
        [TestCase("9781442482890", "9780140328721", 0.50, true, 2, false)]
        [TestCase("9781442482890", "9780140328721", 0.97, false, 2, false)]
        [TestCase("9781442482890", "9780140328721", 0.97, true, 1, false)]
        [TestCase("9781442482890", null, 0.97, true, 2, false)]
        public void distinguishes_independently_verified_ebook_editions_in_the_library(
            string embeddedIsbn,
            string storedIsbn,
            double confidence,
            bool existingLibraryFile,
            int candidateEditionId,
            bool accepted)
        {
            var storedEdition = new Edition { Id = 1, BookId = 42, IsEbook = true, Isbn13 = storedIsbn };
            var candidate = new LocalBook
            {
                ExistingFile = existingLibraryFile,
                Path = "/library/new-edition.epub",
                Size = 2048,
                Edition = new Edition { Id = candidateEditionId, BookId = 42, IsEbook = true, Isbn13 = "9781442482890" },
                FileTrackInfo = new ParsedTrackInfo { Isbn = embeddedIsbn, IsbnConfidence = confidence },
                Book = new Book
                {
                    Id = 42,
                    BookFiles = new List<BookFile>
                    {
                        new BookFile { Path = "/library/original.epub", Size = 2048, EditionId = storedEdition.Id, Edition = storedEdition }
                    }
                }
            };

            Subject.IsSatisfiedBy(candidate, null).Accepted.Should().Be(accepted);
        }
    }
}
