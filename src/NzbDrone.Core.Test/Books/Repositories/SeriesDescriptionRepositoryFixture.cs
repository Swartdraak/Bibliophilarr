using Dapper;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.BooksTests.Repositories
{
    [TestFixture]
    public class SeriesDescriptionRepositoryFixture : DbTest<SeriesRepository, Series>
    {
        [TestCase(null)]
        [TestCase(" \t\n ")]
        public void should_fill_only_the_blank_description_of_the_existing_linked_primary_series(string previous)
        {
            var (author, series, link) = AddLinkedSeries(previous);

            Subject.TryFillHardcoverDescription(author, series, " A verified description ").Should().BeTrue();
            Subject.TryFillHardcoverDescription(author, series, "Stale replacement").Should().BeFalse();

            var saved = Subject.Find(series.Id);
            saved.Description.Should().Be("A verified description");
            saved.Id.Should().Be(series.Id);
            saved.CanonicalId.Should().Be(series.CanonicalId);
            saved.ForeignSeriesId.Should().Be(series.ForeignSeriesId);
            saved.WorkCount.Should().Be(12);
            saved.PrimaryWorkCount.Should().Be(4);
            saved.Title.Should().Be("Stored series");
            var savedLink = saved.LinkItems.Value.Should().ContainSingle().Which;
            savedLink.Id.Should().Be(link.Id);
            savedLink.BookId.Should().Be(link.BookId);
            savedLink.SeriesId.Should().Be(link.SeriesId);
            savedLink.Position.Should().Be(link.Position);
            savedLink.SeriesPosition.Should().Be(link.SeriesPosition);
            savedLink.IsPrimary.Should().BeTrue();
            using var connection = Mocker.Resolve<IMainDatabase>().OpenConnection();
            connection.ExecuteScalar<int>("SELECT COUNT(*) FROM \"SeriesSourceAssociations\"").Should().Be(1);
            connection.ExecuteScalar<int>("SELECT COUNT(*) FROM \"SeriesRouteAliases\"").Should().Be(1);
        }

        [TestCase("series-source")]
        [TestCase("author-source")]
        [TestCase("identity")]
        [TestCase("link")]
        [TestCase("nonblank")]
        public void should_hold_on_stale_authority_link_or_description(string change)
        {
            var (author, series, _) = AddLinkedSeries(null);
            using var connection = Mocker.Resolve<IMainDatabase>().OpenConnection();
            var mutation = change switch
            {
                "series-source" => """UPDATE "SeriesSourceAssociations" SET "Evidence" = 'corroborated-series-membership'""",
                "author-source" => """UPDATE "AuthorSourceAssociations" SET "Evidence" = 'legacy-linked-id'""",
                "identity" => """UPDATE "Series" SET "CanonicalId" = '112233445566475899aabbccddeeff01'""",
                "link" => """DELETE FROM "SeriesBookLink" """,
                _ => """UPDATE "Series" SET "Description" = 'Newer verified description'"""
            };
            connection.Execute(mutation);

            Subject.TryFillHardcoverDescription(author, series, "Stale response").Should().BeFalse();
            Subject.Find(series.Id).Description.Should().Be(change == "nonblank" ? "Newer verified description" : null);
        }

        private (Author Author, Series Series, SeriesBookLink Link) AddLinkedSeries(string description)
        {
            var metadata = Db.Insert(new AuthorMetadata
            {
                ForeignAuthorId = "hardcover:author:123",
                TitleSlug = "fixture-author",
                Name = "Fixture Author",
                SortName = "fixture author",
                NameLastFirst = "Author, Fixture",
                SortNameLastFirst = "author, fixture"
            });
            var author = Db.Insert(new Author { AuthorMetadataId = metadata.Id, Metadata = metadata, Path = "Fixture", CleanName = "fixtureauthor" });
            var work = Db.Insert(new Book
            {
                AuthorMetadataId = metadata.Id,
                ForeignBookId = "hardcover:work:456",
                TitleSlug = "fixture-work",
                Title = "Fixture Work",
                CleanTitle = "fixturework"
            });
            var series = Subject.Insert(new Series
            {
                ForeignSeriesId = "hardcover:series:789",
                Title = "Stored series",
                Description = description,
                Numbered = true,
                WorkCount = 12,
                PrimaryWorkCount = 4
            });
            var link = Db.Insert(new SeriesBookLink { BookId = work.Id, SeriesId = series.Id, IsPrimary = true, Position = "3", SeriesPosition = 3 });
            return (author, series, link);
        }
    }
}
