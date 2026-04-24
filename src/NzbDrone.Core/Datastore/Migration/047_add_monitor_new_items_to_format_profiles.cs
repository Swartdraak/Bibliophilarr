using FluentMigrator;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration
{
    [Migration(047)]
    public class add_monitor_new_items_to_format_profiles : NzbDroneMigrationBase
    {
        protected override void MainDbUpgrade()
        {
            Alter.Table("AuthorFormatProfiles")
                 .AddColumn("MonitorNewItems").AsInt32().WithDefaultValue(0);

            // Sync Author.Monitored from format profiles: if any format profile
            // is monitored the author should also be marked monitored.
            Execute.Sql(@"UPDATE Authors SET Monitored = 1
                          WHERE Id IN (
                              SELECT DISTINCT AuthorId FROM AuthorFormatProfiles WHERE Monitored = 1
                          ) AND Monitored = 0");
        }
    }
}
