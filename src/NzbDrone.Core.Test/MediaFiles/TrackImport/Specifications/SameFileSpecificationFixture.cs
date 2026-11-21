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
    }
}
