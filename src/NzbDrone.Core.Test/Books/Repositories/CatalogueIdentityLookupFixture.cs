using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.BooksTests.Repositories
{
    [TestFixture]
    public class CatalogueIdentityLookupFixture : DbTest
    {
        [Test]
        public void should_resolve_accepted_secondary_sources_from_existing_repository_lookups()
        {
            var metadata = CreateAuthor("hardcover:author:55");
            Db.Insert(metadata);
            var author = Db.Insert(new Author { AuthorMetadataId = metadata.Id, CleanName = "samename", Path = "/a" });
            var work = Db.Insert(new Book
            {
                AuthorMetadataId = metadata.Id,
                ForeignBookId = "hardcover:work:55",
                Title = "Sample",
                TitleSlug = "sample",
                CleanTitle = "sample"
            });

            var identity = Mocker.Resolve<CatalogueIdentityRepository>();
            identity.TryAddSource(CatalogueEntityKind.Author, metadata.Id, "OpenLibrary", "OL55A", "corroborated").Should().BeTrue();
            identity.TryAddSource(CatalogueEntityKind.Work, work.Id, "OpenLibrary", "OL55W", "corroborated").Should().BeTrue();

            Mocker.Resolve<AuthorRepository>().FindById("openlibrary:author:ol55a").Id.Should().Be(author.Id);
            Mocker.Resolve<BookRepository>().FindById("openlibrary:work:ol55w").Id.Should().Be(work.Id);
            Mocker.Resolve<BookRepository>().FindById("openlibrary:edition:OL55M").Should().BeNull();
        }

        [Test]
        public void should_resolve_a_later_partial_series_refresh_through_its_retained_alias()
        {
            var series = Db.Insert(new Series { ForeignSeriesId = "hardcover:series:80", Title = "The Saga" });
            var repository = Mocker.Resolve<SeriesRepository>();
            series.PendingAliases.Add("openlibrary:series:ol80a:the-saga");

            repository.Update(series);

            var found = repository.FindById("openlibrary:series:ol80a:the-saga");
            found.CanonicalId.Should().Be(series.CanonicalId);
            found.Id.Should().Be(series.Id);
            Db.All<Series>().Should().ContainSingle();
        }

        [Test]
        public void should_upsert_a_corroborated_alternate_author_without_changing_primary_or_slug()
        {
            var prior = Db.Insert(CreateAuthor("hardcover:author:60"));
            var identity = Mocker.Resolve<CatalogueIdentityRepository>();
            identity.TryAddSource(CatalogueEntityKind.Author, prior.Id, "OpenLibrary", "OL60A", "corroborated").Should().BeTrue();

            var incoming = CreateAuthor("openlibrary:author:OL60A");
            incoming.TitleSlug = "openlibrary-author-ol60a";
            incoming.Overview = "Provider supplied biography";

            Mocker.Resolve<AuthorMetadataRepository>().UpsertMany(new List<AuthorMetadata> { incoming }).Should().BeTrue();

            var result = Db.All<AuthorMetadata>().Single();
            result.Id.Should().Be(prior.Id);
            result.CanonicalId.Should().Be(prior.CanonicalId);
            result.ForeignAuthorId.Should().Be(prior.ForeignAuthorId);
            result.TitleSlug.Should().Be(prior.TitleSlug);
            result.Overview.Should().Be(incoming.Overview);
        }

        [TestCase("hardcover:author:60", null)]
        [TestCase("hardcover:author:Same%20Name", null)]
        [TestCase("hardcover:author:Same%20Name", "OL61A")]
        public void should_keep_distinct_same_name_authors_and_generate_a_unique_slug(string previousId, string conflictingOpenLibraryId)
        {
            var previous = CreateAuthor(previousId);
            previous.OpenLibraryAuthorId = conflictingOpenLibraryId;
            Db.Insert(previous);

            var incoming = CreateAuthor("hardcover:author:62");
            incoming.OpenLibraryAuthorId = conflictingOpenLibraryId == null ? null : "OL62A";
            Mocker.Resolve<AuthorMetadataRepository>().UpsertMany(new List<AuthorMetadata> { incoming }).Should().BeTrue();

            var rows = Db.All<AuthorMetadata>();
            rows.Should().HaveCount(2);
            rows.Select(row => row.Id).Should().Contain(previous.Id).And.Contain(incoming.Id);
            incoming.CanonicalId.Should().NotBe(previous.CanonicalId);
            incoming.TitleSlug.Should().Be("same-name-2");
        }

        private static AuthorMetadata CreateAuthor(string foreignId)
        {
            return new AuthorMetadata
            {
                ForeignAuthorId = foreignId,
                Name = "Same Name",
                SortName = "same name",
                NameLastFirst = "Name, Same",
                SortNameLastFirst = "name, same",
                TitleSlug = "same-name"
            };
        }
    }
}
