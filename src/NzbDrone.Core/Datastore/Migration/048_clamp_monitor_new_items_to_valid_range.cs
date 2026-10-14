using FluentMigrator;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration
{
    /// <summary>
    /// Data repair for issue #256.
    ///
    /// Out-of-range values for the NewItemMonitorTypes columns (valid set is All=0, None=1,
    /// New=2) were observed in the wild (e.g. AuthorFormatProfiles.MonitorNewItems=3) and
    /// caused MonitorNewBookService to throw NotImplementedException, which the import and
    /// refresh pipelines caught and only logged — silently dropping every new book.
    ///
    /// This migration clamps any out-of-range stored value to the safe default 0 (All).
    /// It is idempotent and only touches rows whose value is not a defined member of the
    /// enum, so already-valid databases are unaffected.
    /// </summary>
    [Migration(048)]
    public class clamp_monitor_new_items_to_valid_range : NzbDroneMigrationBase
    {
        protected override void MainDbUpgrade()
        {
            // Clamp each NewItemMonitorTypes column to the valid range 0..2 (values outside
            // that range are out-of-range for the enum and are reset to the All=0 default).
            Execute.Sql("UPDATE AuthorFormatProfiles SET MonitorNewItems = 0 WHERE MonitorNewItems IS NOT NULL AND (MonitorNewItems < 0 OR MonitorNewItems > 2)");
            Execute.Sql("UPDATE Authors SET MonitorNewItems = 0 WHERE MonitorNewItems IS NOT NULL AND (MonitorNewItems < 0 OR MonitorNewItems > 2)");
            Execute.Sql("UPDATE RootFolders SET DefaultNewItemMonitorOption = 0 WHERE DefaultNewItemMonitorOption IS NOT NULL AND (DefaultNewItemMonitorOption < 0 OR DefaultNewItemMonitorOption > 2)");
            Execute.Sql("UPDATE ImportLists SET MonitorNewItems = 0 WHERE MonitorNewItems IS NOT NULL AND (MonitorNewItems < 0 OR MonitorNewItems > 2)");
        }
    }
}
