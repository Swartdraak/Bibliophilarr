using FluentMigrator;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration
{
    [Migration(053)]
    public class add_pending_author_format_defaults : NzbDroneMigrationBase
    {
        protected override void MainDbUpgrade()
        {
            Alter.Table("AuthorFormatProfiles")
                .AddColumn("PendingTypedDefault").AsBoolean().NotNullable().WithDefaultValue(false);
        }
    }
}
