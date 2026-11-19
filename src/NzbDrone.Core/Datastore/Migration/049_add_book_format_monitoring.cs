using FluentMigrator;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration
{
    [Migration(049)]
    public class add_book_format_monitoring : NzbDroneMigrationBase
    {
        protected override void MainDbUpgrade()
        {
            Alter.Table("Books").AddColumn("EbookMonitoringOverride").AsBoolean().Nullable();
            Alter.Table("Books").AddColumn("AudiobookMonitoringOverride").AsBoolean().Nullable();
        }
    }
}
