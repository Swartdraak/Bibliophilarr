using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.MetadataSource.OpenLibrary;
using NzbDrone.Core.MetadataSource.OpenLibrary.Resources;

namespace NzbDrone.Core.Test.MetadataSource.OpenLibrary
{
    [TestFixture]
    public class OpenLibraryMapperFixture
    {
        private static string LoadFixture(string name)
        {
            var dir = Path.Combine(
                TestContext.CurrentContext.TestDirectory,
                "MetadataSource",
                "OpenLibrary",
                "Fixtures");
            return File.ReadAllText(Path.Combine(dir, name));
        }

        private static T Deserialize<T>(string json)
        {
            return JsonSerializer.Deserialize<T>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
        }

        // ── MapSearchDocToBook ────────────────────────────────────────────────
        [Test]
        public void map_search_doc_produces_valid_book()
        {
            var json = LoadFixture("search_tolkien.json");
            var response = Deserialize<OlSearchResponse>(json);
            var doc = response.Docs[0];

            var book = OpenLibraryMapper.MapSearchDocToBook(doc);

            book.Should().NotBeNull();
            book.ForeignBookId.Should().Be("openlibrary:work:OL45883W");
            book.OpenLibraryWorkId.Should().Be("OL45883W");
            book.Title.Should().Be("The Lord of the Rings");
            book.ReleaseDate.Should().NotBeNull();
            book.ReleaseDate!.Value.Year.Should().Be(1954);
            book.Editions.Value.Should().HaveCount(1);
            book.Author.Value.Should().NotBeNull();
            book.AuthorMetadata.Value.ForeignAuthorId.Should().Be("openlibrary:author:OL26320A");
            book.AuthorMetadata.Value.OpenLibraryAuthorId.Should().Be("OL26320A");
            book.AuthorMetadata.Value.Name.Should().Be("J.R.R. Tolkien");
        }

        [Test]
        public void map_search_doc_handles_null_isbn()
        {
            var doc = new OlSearchDoc
            {
                Key = "/works/OL99W",
                Title = "No ISBN Book",
                AuthorName = new List<string> { "Test Author" },
                AuthorKey = new List<string> { "/authors/OL1A" }
            };

            var book = OpenLibraryMapper.MapSearchDocToBook(doc);

            book.Should().NotBeNull();
            book.Editions.Value[0].Isbn13.Should().BeNull();
        }

        [Test]
        public void map_search_doc_should_use_reading_engagement_for_popularity_when_higher_than_ratings()
        {
            var doc = new OlSearchDoc
            {
                Key = "/works/OL100W",
                Title = "Engagement Heavy Title",
                AuthorName = new List<string> { "Author" },
                AuthorKey = new List<string> { "/authors/OL1A" },
                RatingsAverage = 4.0,
                RatingsCount = 10,
                WantToReadCount = 420,
                CurrentlyReadingCount = 30,
                AlreadyReadCount = 50,
                Language = new List<string> { "spa", "eng" }
            };

            var book = OpenLibraryMapper.MapSearchDocToBook(doc);

            book.Should().NotBeNull();
            book.Ratings.Popularity.Should().BeGreaterThan(350);
            book.Editions.Value[0].Language.Should().Be("eng");
        }

        [Test]
        public void map_search_doc_with_null_returns_null()
        {
            var result = OpenLibraryMapper.MapSearchDocToBook(null);
            result.Should().BeNull();
        }

        // ── MapWorkToBook ─────────────────────────────────────────────────────
        [Test]
        public void map_work_to_book_with_typed_description()
        {
            var json = LoadFixture("work_OL45883W.json");
            var work = Deserialize<OlWorkResource>(json);
            var authorJson = LoadFixture("author_OL26320A.json");
            var author = Deserialize<OlAuthorResource>(authorJson);

            var book = OpenLibraryMapper.MapWorkToBook(work, author);

            book.Should().NotBeNull();
            book.ForeignBookId.Should().Be("openlibrary:work:OL45883W");
            book.OpenLibraryWorkId.Should().Be("OL45883W");
            book.AuthorMetadata.Value.ForeignAuthorId.Should().Be("openlibrary:author:OL26320A");
            book.Title.Should().Be("The Lord of the Rings");
            book.ReleaseDate!.Value.Year.Should().Be(1954);
            book.Editions.Value.Should().HaveCount(1);
            book.Editions.Value[0].Overview.Should().Contain("epic");
        }

        [Test]
        public void map_work_with_null_subjects_does_not_throw()
        {
            var json = LoadFixture("work_null_subjects.json");
            var work = Deserialize<OlWorkResource>(json);

            // act — should not throw
            var book = OpenLibraryMapper.MapWorkToBook(work, null);

            book.Should().NotBeNull();
            book.Genres.Should().NotBeNull();
        }

        [Test]
        public void map_work_with_null_author_uses_placeholder()
        {
            var json = LoadFixture("work_OL45883W.json");
            var work = Deserialize<OlWorkResource>(json);

            var book = OpenLibraryMapper.MapWorkToBook(work, null);

            book.Should().NotBeNull();
            book.AuthorMetadata.Value.ForeignAuthorId.Should().Be("OL-unknown");
        }

        // ── MapAuthorToMetadata ───────────────────────────────────────────────
        [Test]
        public void map_author_populates_all_fields()
        {
            var json = LoadFixture("author_OL26320A.json");
            var author = Deserialize<OlAuthorResource>(json);

            var metadata = OpenLibraryMapper.MapAuthorToMetadata(author);

            metadata.Should().NotBeNull();
            metadata.ForeignAuthorId.Should().Be("openlibrary:author:OL26320A");
            metadata.OpenLibraryAuthorId.Should().Be("OL26320A");
            metadata.Name.Should().Be("J.R.R. Tolkien");
            metadata.Overview.Should().Contain("philologist");
            metadata.Born.Should().NotBeNull();
            metadata.Born!.Value.Year.Should().Be(1892);
            metadata.Died.Should().NotBeNull();
            metadata.Died!.Value.Year.Should().Be(1973);
            metadata.Links.Should().Contain(l => l.Name == "Wikipedia");
        }

        [Test]
        public void map_author_with_plain_string_bio()
        {
            var author = new OlAuthorResource
            {
                Key = "/authors/OL1A",
                Name = "Plain Bio Author",
                Bio = "A plain string bio, no typed wrapper."
            };

            var metadata = OpenLibraryMapper.MapAuthorToMetadata(author);

            metadata.Should().NotBeNull();
            metadata.Overview.Should().Be("A plain string bio, no typed wrapper.");
        }

        [Test]
        public void map_author_with_null_returns_null()
        {
            OpenLibraryMapper.MapAuthorToMetadata(null).Should().BeNull();
        }

        // ── MapEdition ────────────────────────────────────────────────────────
        [Test]
        public void map_edition_populates_isbn_and_publisher()
        {
            var json = LoadFixture("edition_OL7353617M.json");
            var edition = Deserialize<OlEditionResource>(json);

            var result = OpenLibraryMapper.MapEdition(edition);

            result.Should().NotBeNull();
            result.ForeignEditionId.Should().Be("openlibrary:edition:OL7353617M");
            result.Isbn13.Should().Be("9780618346257");
            result.Publisher.Should().Be("Houghton Mifflin");
            result.PageCount.Should().Be(398);
            result.Format.Should().Be("Hardcover");
        }

        [Test]
        public void map_edition_with_no_isbn13_falls_back_to_isbn10()
        {
            var edition = new OlEditionResource
            {
                Key = "/books/OL1M",
                Title = "Test",
                Isbn10 = new List<string> { "0618346252" }
            };

            var result = OpenLibraryMapper.MapEdition(edition);

            result.Should().NotBeNull();
            result.Isbn13.Should().Be("0618346252");
        }

        [Test]
        public void map_edition_should_map_language_and_detect_ebook_format()
        {
            var edition = new OlEditionResource
            {
                Key = "/books/OL3M",
                Title = "Digital Title",
                PhysicalFormat = "EBOOK",
                Languages = new List<OlKeyRef>
                {
                    new OlKeyRef { Key = "/languages/fre" },
                    new OlKeyRef { Key = "/languages/eng" }
                }
            };

            var result = OpenLibraryMapper.MapEdition(edition);

            result.Should().NotBeNull();
            result.Language.Should().Be("eng");
            result.IsEbook.Should().BeTrue();
        }

        [Test]
        public void map_edition_publish_date_extracts_year()
        {
            var edition = new OlEditionResource
            {
                Key = "/books/OL2M",
                Title = "Test Date",
                PublishDate = "October 2004"
            };

            var result = OpenLibraryMapper.MapEdition(edition);

            result.Should().NotBeNull();
            result.ReleaseDate.Should().NotBeNull();
            result.ReleaseDate!.Value.Year.Should().Be(2004);
        }

        [Test]
        public void map_edition_with_null_returns_null()
        {
            OpenLibraryMapper.MapEdition(null).Should().BeNull();
        }
    }
}
