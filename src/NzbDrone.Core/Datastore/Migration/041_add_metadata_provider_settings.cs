using FluentMigrator;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration
{
    [Migration(041)]
    public class add_metadata_provider_settings : NzbDroneMigrationBase
    {
        protected override void MainDbUpgrade()
        {
            Create.TableForModel("MetadataProviderSettings")
                  .WithColumn("ProviderName").AsString().NotNullable().Unique()
                  .WithColumn("IsEnabled").AsBoolean().WithDefaultValue(true)
                  .WithColumn("Priority").AsInt32().WithDefaultValue(10);
        }
    }
}
