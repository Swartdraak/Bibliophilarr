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

        [TestCase("/library/source.epub", "/library/new.M4B", 2048, 2048, true)]
        [TestCase("/library/source.WAV", "/library/new.kepub", 2048, 2048, true)]
        [TestCase("/library/source.mobi", "/library/new.epub", 2048, 2048, false)]
        [TestCase("/library/source.flac", "/library/new.wav", 2048, 2048, false)]
        [TestCase("/library/source.bin", "/library/new.epub", 2048, 2048, false)]
        [TestCase("/library/source.epub", "/library/new.bin", 2048, 2048, false)]
        [TestCase(null, "/library/new.epub", 2048, 2048, false)]
        [TestCase("/library/source.epub", "/library/new.m4b", 0, 0, false)]
        [TestCase("/library/source.epub", "/library/new.epub", 0, 2048, true)]
        [TestCase("/library/source.epub", "/library/new.epub", 2048, 4096, true)]
        public void should_only_ignore_known_opposite_formats_with_positive_equal_sizes(
            string storedPath,
            string incomingPath,
            int storedSize,
            int incomingSize,
            bool accepted)
        {
            var stored = new BookFile { Id = 31, EditionId = 47, Path = storedPath, Size = storedSize };
            var item = new LocalBook
            {
                Path = incomingPath,
                Size = incomingSize,
                Book = new Book { Id = 12, BookFiles = new List<BookFile> { stored } }
            };

            var decision = Subject.IsSatisfiedBy(item, null);

            decision.Accepted.Should().Be(accepted);
            if (!accepted)
            {
                decision.Reason.Should().Be("Has the same filesize as existing file");
            }

            item.Book.BookFiles.Value.Should().ContainSingle().Which.Should().BeSameAs(stored);
            stored.Id.Should().Be(31);
            stored.EditionId.Should().Be(47);
            stored.Path.Should().Be(storedPath);
            stored.Size.Should().Be(storedSize);
        }

        [Test]
        public void should_keep_checking_for_a_same_format_file_after_an_opposite_format_match()
        {
            var item = new LocalBook
            {
                Path = "/library/incoming.m4b",
                Size = 2048,
                Book = new Book
                {
                    BookFiles = new List<BookFile>
                    {
                        new BookFile { Path = "/library/existing.epub", Size = 2048 },
                        new BookFile { Path = "/library/existing.wav", Size = 2048 }
                    }
                }
            };

            var decision = Subject.IsSatisfiedBy(item, null);

            decision.Accepted.Should().BeFalse();
        }
    }
}
