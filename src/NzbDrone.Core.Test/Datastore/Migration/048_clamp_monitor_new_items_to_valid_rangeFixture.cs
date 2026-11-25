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
        [TestCase("AuthorFormatProfiles", "MonitorNewItems")]
        [TestCase("Authors", "MonitorNewItems")]
        [TestCase("RootFolders", "DefaultNewItemMonitorOption")]
        [TestCase("ImportLists", "MonitorNewItems")]
        public void should_repair_invalid_monitor_values_and_preserve_valid_rows(string table, string column)
        {
            var db = WithMigrationTestDb(migration =>
            {
                var values = new[] { -1, 3, 0, 1, 2 };
                for (var index = 0; index < values.Length; index++)
                {
                    var id = 101 + index;
                    var value = values[index];
                    var path = $"/fixture-root-{id}";
                    migration.Insert.IntoTable("Authors").Row(new
                    {
                        Id = id,
                        CleanName = $"fixture-author-{id}",
                        Path = $"{path}/author",
                        AuthorMetadataId = id,
                        MetadataProfileId = 1,
                        Monitored = true,
                        MonitorNewItems = value
                    });
                    migration.Insert.IntoTable("AuthorFormatProfiles").Row(new
                    {
                        Id = id,
                        AuthorId = id,
                        FormatType = index % 2,
                        QualityProfileId = 1,
                        RootFolderPath = path,
                        MonitorNewItems = value
                    });
                    migration.Insert.IntoTable("RootFolders").Row(new
                    {
                        Id = id,
                        Path = path,
                        IsCalibreLibrary = false,
                        DefaultNewItemMonitorOption = value
                    });
                    migration.Insert.IntoTable("ImportLists").Row(new
                    {
                        Id = id,
                        Name = $"fixture-list-{id}",
                        Implementation = "FixtureList",
                        RootFolderPath = path,
                        ShouldMonitor = 0,
                        ProfileId = 1,
                        MetadataProfileId = 1,
                        MonitorNewItems = value
                    });
                }
            });

            var rows = db.Query<MonitorRow>($"SELECT \"Id\", \"{column}\" AS \"Value\" FROM \"{table}\" ORDER BY \"Id\"");
            rows.Select(row => (row.Id, row.Value)).Should().Equal((101, 0), (102, 0), (103, 0), (104, 1), (105, 2));
        }

        private class MonitorRow
        {
            public int Id { get; set; }
            public int Value { get; set; }
        }
    }
}
