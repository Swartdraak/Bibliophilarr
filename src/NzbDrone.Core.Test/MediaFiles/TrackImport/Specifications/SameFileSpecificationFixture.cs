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
