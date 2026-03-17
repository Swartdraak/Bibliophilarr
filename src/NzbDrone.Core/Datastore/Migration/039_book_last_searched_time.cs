using FluentMigrator;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration
{
    [Migration(039)]
    public class book_last_searched_time : NzbDroneMigrationBase
    {
        protected override void MainDbUpgrade()
        {
            IfDatabase("postgres").Alter.Table("Books").AddColumn("LastSearchTime").AsDateTimeOffset().Nullable();
            IfDatabase("sqlite").Alter.Table("Books").AddColumn("LastSearchTime").AsDateTime().Nullable();
        }
    }
}
