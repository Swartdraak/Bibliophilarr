using FluentMigrator;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration
{
    [Migration(043)]
    public class update_metadata_provider_priority_default : NzbDroneMigrationBase
    {
        private const string ConfigKey = "MetadataProviderPriorityOrder";
        private const string LegacyDefault = "OpenLibrary,GoogleBooks,Inventaire";
        private const string HardcoverFirstDefault = "Hardcover,OpenLibrary,GoogleBooks,Inventaire";

        protected override void MainDbUpgrade()
        {
            // Only migrate untouched legacy defaults so customized orders are preserved.
            Update.Table("Config")
                  .Set(new { Value = HardcoverFirstDefault })
                  .Where(new { Key = ConfigKey, Value = LegacyDefault });
        }
    }
}
