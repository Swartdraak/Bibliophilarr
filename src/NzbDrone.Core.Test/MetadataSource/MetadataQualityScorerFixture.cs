using System;
using System.Collections.Generic;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.MetadataSource;

namespace NzbDrone.Core.Test.MetadataSource
{
    [TestFixture]
    public class MetadataQualityScorerFixture
    {
        private MetadataQualityScorer _subject;

        [SetUp]
        public void Setup()
        {
            _subject = new MetadataQualityScorer();
        }

        [Test]
        public void should_return_zero_for_null_entities()
        {
            _subject.CalculateBookScore(null).Should().Be(0);
            _subject.CalculateAuthorScore(null).Should().Be(0);
            _subject.CalculateEditionScore(null).Should().Be(0);
        }

        [Test]
        public void should_calculate_expected_book_score_for_minimal_required_fields()
        {
            var book = new Book
            {
                Title = "Dune",
                ForeignBookId = "OL123W",
                AuthorMetadata = new AuthorMetadata { Name = "Frank Herbert" }
            };

            var score = _subject.CalculateBookScore(book);

            score.Should().Be(60);
            _subject.IsQualityAcceptable(score).Should().BeTrue();
        }

        [Test]
        public void should_calculate_expected_book_score_for_rich_metadata()
        {
            var book = new Book
            {
                Title = "Dune",
                ForeignBookId = "OL123W",
                ForeignEditionId = "OL456M",
                ReleaseDate = DateTime.UtcNow.Date,
                Genres = new List<string> { "Sci-Fi" },
                Links = new List<Links> { new Links() },
                RelatedBooks = new List<int> { 2 },
                Ratings = new Ratings { Votes = 100, Value = 4.7m },
                AuthorMetadata = new AuthorMetadata { Name = "Frank Herbert" },
                SeriesLinks = new List<SeriesBookLink> { new SeriesBookLink() },
                Editions = new List<Edition>
                {
                    new Edition { Images = new List<MediaCover.MediaCover> { new MediaCover.MediaCover() } },
                    new Edition()
                }
            };

            var score = _subject.CalculateBookScore(book);

            score.Should().Be(100);
        }

        [Test]
        public void should_calculate_expected_author_score_for_required_fields_only()
        {
            var author = new Author
            {
                Metadata = new AuthorMetadata
                {
                    Name = "Frank Herbert",
                    ForeignAuthorId = "OL111A"
                },
                Books = new List<Book> { new Book { Title = "Dune" } }
            };

            var score = _subject.CalculateAuthorScore(author);

            score.Should().Be(60);
        }

        [Test]
        public void should_calculate_expected_edition_score_for_isbn_and_common_fields()
        {
            var edition = new Edition
            {
                Title = "Dune",
                ForeignEditionId = "OL456M",
                Isbn13 = "9780441172719",
                ReleaseDate = DateTime.UtcNow.Date,
                Publisher = "Ace",
                PageCount = 412,
                Format = "Paperback",
                Images = new List<MediaCover.MediaCover> { new MediaCover.MediaCover() },
                Overview = "Classic science fiction",
                Ratings = new Ratings { Votes = 10, Value = 4.5m },
                Links = new List<Links> { new Links() },
                Language = "en"
            };

            var score = _subject.CalculateEditionScore(edition);

            score.Should().Be(100);
        }

        [Test]
        public void should_score_asin_lower_than_isbn_for_essential_identifier_points()
        {
            var asinEdition = new Edition
            {
                Title = "Dune",
                ForeignEditionId = "OL456M",
                Asin = "B00XYZ1234"
            };

            var isbnEdition = new Edition
            {
                Title = "Dune",
                ForeignEditionId = "OL456M",
                Isbn13 = "9780441172719"
            };

            _subject.CalculateEditionScore(asinEdition).Should().Be(55);
            _subject.CalculateEditionScore(isbnEdition).Should().Be(60);
        }

        [Test]
        public void should_enforce_quality_threshold_of_fifty()
        {
            _subject.IsQualityAcceptable(49).Should().BeFalse();
            _subject.IsQualityAcceptable(50).Should().BeTrue();
        }
    }
}
