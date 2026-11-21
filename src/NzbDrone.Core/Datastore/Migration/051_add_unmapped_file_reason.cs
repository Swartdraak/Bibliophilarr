using FluentMigrator;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration
{
    [Migration(051)]
    public class add_unmapped_file_reason : NzbDroneMigrationBase
    {
        protected override void MainDbUpgrade()
        {
            Alter.Table("BookFiles").AddColumn("UnmappedReason").AsInt32().Nullable();
        }
    }
}
