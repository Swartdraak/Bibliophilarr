using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.Datastore.Migration;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Datastore.Migration
{
    [TestFixture]
    public class add_pending_author_format_defaultsFixture : MigrationTest<add_pending_author_format_defaults>
    {
        [Test]
        public void should_leave_profiles_created_before_the_new_migration_unmarked()
        {
            var database = WithMigrationTestDb(migration =>
                migration.Execute.Sql("""
                    INSERT INTO "AuthorFormatProfiles" ("Id", "AuthorId", "FormatType", "QualityProfileId", "RootFolderPath", "Path", "Monitored")
                    VALUES (125, 412, 1, 37, '/fixture-first', '/fixture-first/preserved', FALSE)
                    """));

            var profile = database.Query<AuthorFormatProfile>("""
                SELECT "Id", "RootFolderPath", "Path", "QualityProfileId", "Monitored", "PendingTypedDefault"
                FROM "AuthorFormatProfiles" WHERE "Id" = 125
                """).Should().ContainSingle().Which;

            profile.PendingTypedDefault.Should().BeFalse();
            profile.RootFolderPath.Should().Be("/fixture-first");
            profile.Path.Should().Be("/fixture-first/preserved");
            profile.QualityProfileId.Should().Be(37);
            profile.Monitored.Should().BeFalse();
        }
    }
}
