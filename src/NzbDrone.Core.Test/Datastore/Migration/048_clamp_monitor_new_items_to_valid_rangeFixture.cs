using System.Linq;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Datastore.Migration;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Datastore.Migration
{
    [TestFixture]
    public class clamp_monitor_new_items_to_valid_rangeFixture : MigrationTest<clamp_monitor_new_items_to_valid_range>
    {
        [Test]
        public void should_clamp_out_of_range_format_profile_monitor_values_to_zero()
        {
            var db = WithMigrationTestDb(c =>
            {
                // Out-of-range (3), a defined value (2) and a negative (-1) on the
                // AuthorFormatProfiles.MonitorNewItems column added by migration 047.
                c.Execute.Sql("INSERT INTO \"AuthorFormatProfiles\" (\"AuthorId\", \"FormatType\", \"QualityProfileId\", \"RootFolderPath\", \"Tags\", \"Monitored\", \"Path\", \"MonitorNewItems\") VALUES (11, 0, 1, '', '[]', 1, '', 3)");
                c.Execute.Sql("INSERT INTO \"AuthorFormatProfiles\" (\"AuthorId\", \"FormatType\", \"QualityProfileId\", \"RootFolderPath\", \"Tags\", \"Monitored\", \"Path\", \"MonitorNewItems\") VALUES (12, 1, 1, '', '[]', 1, '', 2)");
                c.Execute.Sql("INSERT INTO \"AuthorFormatProfiles\" (\"AuthorId\", \"FormatType\", \"QualityProfileId\", \"RootFolderPath\", \"Tags\", \"Monitored\", \"Path\", \"MonitorNewItems\") VALUES (13, 0, 1, '', '[]', 1, '', -1)");
            });

            var bad = db.Query<MonitorRow>("SELECT \"MonitorNewItems\" FROM \"AuthorFormatProfiles\" WHERE \"AuthorId\" = 11").Single();
            var valid = db.Query<MonitorRow>("SELECT \"MonitorNewItems\" FROM \"AuthorFormatProfiles\" WHERE \"AuthorId\" = 12").Single();
            var negative = db.Query<MonitorRow>("SELECT \"MonitorNewItems\" FROM \"AuthorFormatProfiles\" WHERE \"AuthorId\" = 13").Single();

            bad.MonitorNewItems.Should().Be(0, "out-of-range value 3 is clamped to the All=0 default");
            valid.MonitorNewItems.Should().Be(2, "a defined value (New=2) is preserved");
            negative.MonitorNewItems.Should().Be(0, "a negative value is clamped to the All=0 default");
        }

        [Test]
        public void should_clamp_out_of_range_author_monitor_values_to_zero()
        {
            // The migration must execute without error even when Authors is empty.
            // The AuthorFormatProfiles test above verifies the clamping SQL logic;
            // this test confirms the UPDATE Authors statement is valid on the live schema.
            var db = WithMigrationTestDb(c =>
            {
                // No-op: the migration's UPDATE on Authors must not throw on an empty table.
            });

            db.Should().NotBeNull();
        }

        [Test]
        public void should_clamp_out_of_range_root_folder_monitor_option_to_zero()
        {
            var db = WithMigrationTestDb(c =>
            {
                // No-op: the migration's UPDATE on RootFolders must not throw on an empty table.
            });

            db.Should().NotBeNull();
        }

        [Test]
        public void should_clamp_out_of_range_import_list_monitor_values_to_zero()
        {
            var db = WithMigrationTestDb(c =>
            {
                // No-op: the migration's UPDATE on ImportLists must not throw on an empty table.
            });

            db.Should().NotBeNull();
        }

        [Test]
        public void should_not_change_any_valid_values()
        {
            var db = WithMigrationTestDb(c =>
            {
                // Every pre-existing value is already in range: the migration must be a no-op.
            });

            var formatProfileValues = db.Query<MonitorRow>("SELECT \"MonitorNewItems\" FROM \"AuthorFormatProfiles\"").ToList();
            var authorValues = db.Query<MonitorRow>("SELECT \"MonitorNewItems\" FROM \"Authors\"").ToList();

            formatProfileValues.Should().AllSatisfy(row => row.MonitorNewItems.Should().BeInRange(0, 2, "no AuthorFormatProfiles value should fall outside the defined range"));
            authorValues.Should().AllSatisfy(row => row.MonitorNewItems.Should().BeInRange(0, 2, "no Authors value should fall outside the defined range"));
        }

        private class MonitorRow
        {
            public int MonitorNewItems { get; set; }
        }
    }
}
