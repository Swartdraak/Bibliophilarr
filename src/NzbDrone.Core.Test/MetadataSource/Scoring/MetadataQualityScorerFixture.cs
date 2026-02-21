using System;
using System.Collections.Generic;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.MetadataSource.Scoring;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MetadataSource.Scoring
{
    [TestFixture]
    public class MetadataQualityScorerFixture : CoreTest<MetadataQualityScorer>
    {
        // ── Book helpers ──────────────────────────────────────────────────────

        private static Book BuildMinimalBook()
        {
            return new Book
            {
                Title = "Foundation",
                ReleaseDate = new DateTime(1951, 1, 1),
                Genres = new List<string>(),
                AuthorMetadata = new LazyLoaded<AuthorMetadata>(new AuthorMetadata { Name = "Isaac Asimov" }),
                Editions = new LazyLoaded<List<Edition>>(new List<Edition>())
            };
        }

        private static Book BuildFullBook()
        {
            var edition = new Edition
            {
                Title = "Foundation",
                Isbn13 = "9780553293357",
                Overview = "The Fall of the Galactic Empire.",
                Publisher = "Gnome Press",
                PageCount = 244,
                Language = "eng",
                ReleaseDate = new DateTime(1951, 1, 1),
                Images = new List<MediaCover.MediaCover> { new MediaCover.MediaCover { CoverType = MediaCover.MediaCoverTypes.Cover, Url = "https://example.com/cover.jpg" } },
                Monitored = true
            };

            return new Book
            {
                Title = "Foundation",
                ReleaseDate = new DateTime(1951, 1, 1),
                Genres = new List<string> { "Science Fiction" },
                AuthorMetadata = new LazyLoaded<AuthorMetadata>(new AuthorMetadata { Name = "Isaac Asimov" }),
                Editions = new LazyLoaded<List<Edition>>(new List<Edition> { edition })
            };
        }

        // ── Author helpers ────────────────────────────────────────────────────

        private static Author BuildMinimalAuthor()
        {
            var meta = new AuthorMetadata
            {
                Name = "Isaac Asimov"
            };
            return new Author
            {
                Metadata = new LazyLoaded<AuthorMetadata>(meta)
            };
        }

        private static Author BuildFullAuthor()
        {
            var meta = new AuthorMetadata
            {
                Name = "Isaac Asimov",
                Overview = "American science-fiction author.",
                Hometown = "Petrovichi, Russia",
                Born = new DateTime(1920, 1, 2),
                Genres = new List<string> { "Science Fiction" },
                Aliases = new List<string> { "Paul French" },
                Images = new List<MediaCover.MediaCover> { new MediaCover.MediaCover { CoverType = MediaCover.MediaCoverTypes.Poster, Url = "https://example.com/asimov.jpg" } }
            };
            return new Author
            {
                Metadata = new LazyLoaded<AuthorMetadata>(meta)
            };
        }

        // ── Book score tests ──────────────────────────────────────────────────

        [Test]
        public void should_return_zero_for_null_book()
        {
            Subject.CalculateBookScore(null).Should().Be(0);
        }

        [Test]
        public void should_score_higher_for_book_with_more_fields()
        {
            var minimal = BuildMinimalBook();
            var full = BuildFullBook();

            Subject.CalculateBookScore(full).Should().BeGreaterThan(Subject.CalculateBookScore(minimal));
        }

        [Test]
        public void full_book_should_score_one_hundred()
        {
            Subject.CalculateBookScore(BuildFullBook()).Should().Be(100);
        }

        [Test]
        public void minimal_book_should_score_lower_than_fifty()
        {
            Subject.CalculateBookScore(BuildMinimalBook()).Should().BeLessThan(50);
        }

        [Test]
        public void book_with_no_author_metadata_loaded_should_not_score_author_points()
        {
            var book = new Book
            {
                Title = "Untitled",
                AuthorMetadata = new LazyLoaded<AuthorMetadata>(null),
                Editions = new LazyLoaded<List<Edition>>(new List<Edition>())
            };

            Subject.CalculateBookScore(book).Should().Be(20); // title only
        }

        [Test]
        public void book_score_should_not_exceed_one_hundred()
        {
            Subject.CalculateBookScore(BuildFullBook()).Should().BeLessOrEqualTo(100);
        }

        [Test]
        public void asin_should_satisfy_isbn_score()
        {
            var edition = new Edition
            {
                Title = "Foundation",
                Asin = "B000FC1PWU",
                Overview = "The fall.",
                Publisher = "Publisher",
                PageCount = 244,
                Language = "eng",
                Images = new List<MediaCover.MediaCover> { new MediaCover.MediaCover { CoverType = MediaCover.MediaCoverTypes.Cover, Url = "https://example.com/c.jpg" } },
                Monitored = true
            };

            var book = new Book
            {
                Title = "Foundation",
                ReleaseDate = new DateTime(1951, 1, 1),
                Genres = new List<string> { "Science Fiction" },
                AuthorMetadata = new LazyLoaded<AuthorMetadata>(new AuthorMetadata { Name = "Isaac Asimov" }),
                Editions = new LazyLoaded<List<Edition>>(new List<Edition> { edition })
            };

            Subject.CalculateBookScore(book).Should().Be(100);
        }

        // ── Author score tests ────────────────────────────────────────────────

        [Test]
        public void should_return_zero_for_null_author()
        {
            Subject.CalculateAuthorScore(null).Should().Be(0);
        }

        [Test]
        public void should_score_higher_for_author_with_more_fields()
        {
            Subject.CalculateAuthorScore(BuildFullAuthor()).Should().BeGreaterThan(Subject.CalculateAuthorScore(BuildMinimalAuthor()));
        }

        [Test]
        public void full_author_should_score_one_hundred()
        {
            Subject.CalculateAuthorScore(BuildFullAuthor()).Should().Be(100);
        }

        [Test]
        public void author_with_no_metadata_loaded_should_score_zero()
        {
            var author = new Author
            {
                Metadata = new LazyLoaded<AuthorMetadata>(null)
            };

            Subject.CalculateAuthorScore(author).Should().Be(0);
        }

        [Test]
        public void author_score_should_not_exceed_one_hundred()
        {
            Subject.CalculateAuthorScore(BuildFullAuthor()).Should().BeLessOrEqualTo(100);
        }
    }
}
