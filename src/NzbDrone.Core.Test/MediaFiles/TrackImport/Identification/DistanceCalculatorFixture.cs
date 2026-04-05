using System.Collections.Generic;
using FizzWare.NBuilder;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.MediaFiles.BookImport.Identification;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.MediaFiles.BookImport.Identification
{
    [TestFixture]
    public class DistanceCalculatorFixture : TestBase
    {
        [Test]
        public void should_reverse_single_reversed_author()
        {
            var input = new List<string> { "Last, First" };
            var authors = DistanceCalculator.GetAuthorVariants(input);

            authors.Should().Contain("First Last");
        }

        [Test]
        public void should_reverse_two_reversed_author()
        {
            var input = new List<string>
            {
                "Last, First",
                "Last2, First2"
            };

            var authors = DistanceCalculator.GetAuthorVariants(input);

            authors.Should().HaveCount(4);
            authors.Should().Contain("First Last");
            authors.Should().Contain("First2 Last2");
            authors.Should().Contain("Last, First");
            authors.Should().Contain("Last2, First2");
        }

        [Test]
        public void should_not_reverse_single_author()
        {
            var input = new List<string> { "First Last" };
            var authors = DistanceCalculator.GetAuthorVariants(input);

            authors.Should().HaveCount(1);
            authors.Should().Contain("First Last");
        }

        [TestCase("First1 Last1, First2 Last2", "First1 Last1", "First2 Last2")]
        [TestCase("First1 Last1; First2 Last2", "First1 Last1", "First2 Last2")]
        [TestCase("First1 Last1 & First2 Last2", "First1 Last1", "First2 Last2")]
        [TestCase("First1 Last1 / First2 Last2", "First1 Last1", "First2 Last2")]
        [TestCase("First1 Last1 and First2 Last2", "First1 Last1", "First2 Last2")]
        public void should_split_concatenated_author(string inputString, string first, string second)
        {
            var input = new List<string> { inputString };
            var authors = DistanceCalculator.GetAuthorVariants(input);

            authors.Should().Contain(inputString);
            authors.Should().Contain(first);
            authors.Should().Contain(second);
            authors.Should().HaveCount(3);
        }

        [Test]
        public void should_split_concatenated_with_trailing_and()
        {
            var inputString = "First Last, First2 Last2 & First3 Last3";
            var input = new List<string> { inputString };
            var authors = DistanceCalculator.GetAuthorVariants(input);

            authors.Should().Contain(inputString);
            authors.Should().Contain("First Last");
            authors.Should().Contain("First2 Last2");
            authors.Should().Contain("First3 Last3");
            authors.Should().HaveCount(4);
        }

        [Test]
        public void should_not_split_if_multiple_input()
        {
            var input = new List<string>
            {
                "First Last",
                "Second Third, Fourth Fifth"
            };

            var authors = DistanceCalculator.GetAuthorVariants(input);

            authors.Should().HaveCount(2);
            authors.Should().Contain("First Last");
            authors.Should().Contain("Second Third, Fourth Fifth");
        }

        [Test]
        public void should_reduce_book_title_weight_for_low_confidence_embedded_labels()
        {
            var edition = Builder<Edition>.CreateNew()
                .With(x => x.Title = "The Real Book")
                .Build();

            edition.Book = Builder<Book>.CreateNew()
                .With(x => x.AuthorMetadata = Builder<AuthorMetadata>.CreateNew().With(a => a.Name = "Known Author").Build())
                .Build();

            var confident = new LocalBook
            {
                Path = "The Real Book.m4b",
                FileTrackInfo = new ParsedTrackInfo
                {
                    Authors = new List<string> { "Known Author" },
                    BookTitle = "World 1",
                    BookTitleConfidence = 1.0,
                    AuthorConfidence = 1.0
                }
            };

            var lowConfidence = new LocalBook
            {
                Path = "The Real Book (legacy).m4b",
                FileTrackInfo = new ParsedTrackInfo
                {
                    Authors = new List<string> { "Known Author" },
                    BookTitle = "World 1",
                    BookTitleConfidence = 0.2,
                    AuthorConfidence = 1.0,
                    IdentitySource = "ffprobe:legacy-tags"
                }
            };

            var confidentDistance = DistanceCalculator.BookDistance(new List<LocalBook> { confident }, edition);
            var lowConfidenceDistance = DistanceCalculator.BookDistance(new List<LocalBook> { lowConfidence }, edition);

            confidentDistance.Penalties.Should().ContainKey("book");
            lowConfidenceDistance.Penalties.Should().ContainKey("book_low_confidence");
            lowConfidenceDistance.RawDistance().Should().BeLessThan(confidentDistance.RawDistance());
        }
    }
}
