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

        [TestCase("EPUB")]
        [TestCase(" ePuB ")]
        [TestCase("KEPUB")]
        [TestCase("MOBI")]
        [TestCase("AZW3")]
        [TestCase("PDF")]
        public void should_recognize_supported_ebook_format_names_even_without_a_provider_flag(string format)
        {
            var distance = GetFormatDistance(".epub", format, false);

            distance.Penalties["ebook_format"].Should().Equal(0.0);
            distance.Penalties["wrong_format"].Should().Equal(0.0);
        }

        [TestCase("Audio CD", 1.0)]
        [TestCase("Audiobook", 1.0)]
        [TestCase("Digital Audio", 0.0)]
        [TestCase("Hardcover", 0.0)]
        public void should_preserve_format_penalties_for_non_ebook_labels_even_with_a_provider_flag(
            string format, double wrongFormatPenalty)
        {
            var distance = GetFormatDistance(".epub", format, true);

            distance.Penalties["ebook_format"].Should().Equal(1.0);
            distance.Penalties["wrong_format"].Should().Equal(wrongFormatPenalty);
        }

        [Test]
        public void should_not_treat_a_native_ebook_label_as_an_audiobook()
        {
            var distance = GetFormatDistance(".m4b", "EPUB", true);

            distance.Penalties["audio_format"].Should().Equal(1.0);
        }

        [TestCase(" m4B ")]
        [TestCase("MP3")]
        [TestCase("FLAC")]
        [TestCase("APE")]
        [TestCase("WAVPACK")]
        [TestCase("WAV")]
        [TestCase("ALAC")]
        [TestCase("MP2")]
        [TestCase("WMA")]
        [TestCase("M4A")]
        [TestCase("M4P")]
        [TestCase("AAC")]
        [TestCase("MP4A")]
        [TestCase("OGG")]
        [TestCase("OGA")]
        [TestCase("VORBIS")]
        [TestCase("Audio CD")]
        [TestCase("Audiobook")]
        public void should_recognize_supported_native_audio_and_existing_audiobook_formats(string format)
        {
            var distance = GetFormatDistance(".m4b", format, false);

            distance.Penalties["audio_format"].Should().Equal(0.0);
        }

        [TestCase("M4B")]
        [TestCase("MP3")]
        public void should_preserve_the_wrong_format_penalty_for_ebook_files_against_native_audio(string format)
        {
            var distance = GetFormatDistance(".epub", format, true);

            distance.Penalties["wrong_format"].Should().Equal(1.0);
        }

        [TestCase("Digital Audio")]
        [TestCase("MP3 Supplemental Material")]
        [TestCase("Hardcover")]
        public void should_not_treat_unknown_or_conflicting_format_labels_as_audiobooks(string format)
        {
            var distance = GetFormatDistance(".mp3", format, false);

            distance.Penalties["audio_format"].Should().Equal(1.0);
        }

        private static Distance GetFormatDistance(string extension, string format, bool isEbook)
        {
            var edition = new Edition
            {
                Title = "Example Book",
                Format = format,
                IsEbook = isEbook,
                Book = new Book { AuthorMetadata = new AuthorMetadata { Name = "Known Author" } }
            };

            var local = new LocalBook
            {
                Path = $"Example Book{extension}",
                FileTrackInfo = new ParsedTrackInfo
                {
                    Authors = new List<string> { "Known Author" },
                    BookTitle = "Example Book"
                }
            };

            return DistanceCalculator.BookDistance(new List<LocalBook> { local }, edition);
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

        [TestCase("0306406152", "9780306406157", 0.0)]
        [TestCase("9780306406157", "0306406152", 0.0)]
        [TestCase("0-8044-2957-x", "978 0 8044 2957 3", 0.0)]
        [TestCase("978-0-306-40615-7", "9780306406157", 0.0)]
        [TestCase("0306406152", "9780804429573", 1.0)]
        [TestCase("0306406152", "9790306406156", 1.0)]
        [TestCase("0306406153", "9780306406157", 1.0)]
        [TestCase("0306406152", "9780306406155", 1.0)]
        [TestCase("garbage0306406152", "9780306406157", 1.0)]
        public void should_compare_checksum_valid_isbn_representations(
            string fileIsbn, string editionIsbn, double penalty)
        {
            var edition = new Edition
            {
                Title = "Example Book",
                Isbn13 = editionIsbn,
                Book = new Book { AuthorMetadata = new AuthorMetadata { Name = "Known Author" } }
            };

            var local = new LocalBook
            {
                Path = "Example Book.epub",
                FileTrackInfo = new ParsedTrackInfo
                {
                    Authors = new List<string> { "Known Author" },
                    BookTitle = "Example Book",
                    Isbn = fileIsbn
                }
            };

            var distance = DistanceCalculator.BookDistance(new List<LocalBook> { local }, edition);

            distance.Penalties["isbn"].Should().Equal(penalty);
        }
    }
}
