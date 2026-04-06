using FluentMigrator;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration
{
    [Migration(046)]
    public class add_format_type_to_remote_path_mappings : NzbDroneMigrationBase
    {
        protected override void MainDbUpgrade()
        {
            Alter.Table("RemotePathMappings")
                 .AddColumn("FormatType").AsInt32().Nullable();
        }
    }
}
