using FluentMigrator;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration
{
    [Migration(23)]
    public class postgres_update_timestamp_columns_to_with_timezone : NzbDroneMigrationBase
    {
        protected override void MainDbUpgrade()
        {
            Delete.FromTable("Commands").AllRows();

            IfDatabase("postgres").Alter.Table("Authors").AlterColumn("LastInfoSync").AsDateTimeOffset().Nullable();
            IfDatabase("postgres").Alter.Table("Authors").AlterColumn("Added").AsDateTimeOffset().Nullable();
            IfDatabase("postgres").Alter.Table("AuthorMetadata").AlterColumn("Born").AsDateTimeOffset().Nullable();
            IfDatabase("postgres").Alter.Table("AuthorMetadata").AlterColumn("Died").AsDateTimeOffset().Nullable();
            IfDatabase("postgres").Alter.Table("Blocklist").AlterColumn("Date").AsDateTimeOffset().NotNullable();
            IfDatabase("postgres").Alter.Table("Blocklist").AlterColumn("PublishedDate").AsDateTimeOffset().Nullable();
            IfDatabase("postgres").Alter.Table("Books").AlterColumn("ReleaseDate").AsDateTimeOffset().Nullable();
            IfDatabase("postgres").Alter.Table("Books").AlterColumn("LastInfoSync").AsDateTimeOffset().Nullable();
            IfDatabase("postgres").Alter.Table("Books").AlterColumn("Added").AsDateTimeOffset().Nullable();
            IfDatabase("postgres").Alter.Table("BookFiles").AlterColumn("DateAdded").AsDateTimeOffset().Nullable();
            IfDatabase("postgres").Alter.Table("BookFiles").AlterColumn("Modified").AsDateTimeOffset().Nullable();
            IfDatabase("postgres").Alter.Table("Commands").AlterColumn("QueuedAt").AsDateTimeOffset().NotNullable();
            IfDatabase("postgres").Alter.Table("Commands").AlterColumn("StartedAt").AsDateTimeOffset().Nullable();
            IfDatabase("postgres").Alter.Table("Commands").AlterColumn("EndedAt").AsDateTimeOffset().Nullable();
            IfDatabase("postgres").Alter.Table("DownloadClientStatus").AlterColumn("InitialFailure").AsDateTimeOffset().Nullable();
            IfDatabase("postgres").Alter.Table("DownloadClientStatus").AlterColumn("MostRecentFailure").AsDateTimeOffset().Nullable();
            IfDatabase("postgres").Alter.Table("DownloadClientStatus").AlterColumn("DisabledTill").AsDateTimeOffset().Nullable();
            IfDatabase("postgres").Alter.Table("Editions").AlterColumn("ReleaseDate").AsDateTimeOffset().Nullable();
            IfDatabase("postgres").Alter.Table("ExtraFiles").AlterColumn("Added").AsDateTimeOffset().NotNullable();
            IfDatabase("postgres").Alter.Table("ExtraFiles").AlterColumn("LastUpdated").AsDateTimeOffset().NotNullable();
            IfDatabase("postgres").Alter.Table("History").AlterColumn("Date").AsDateTimeOffset().NotNullable();
            IfDatabase("postgres").Alter.Table("ImportListStatus").AlterColumn("InitialFailure").AsDateTimeOffset().Nullable();
            IfDatabase("postgres").Alter.Table("ImportListStatus").AlterColumn("MostRecentFailure").AsDateTimeOffset().Nullable();
            IfDatabase("postgres").Alter.Table("ImportListStatus").AlterColumn("DisabledTill").AsDateTimeOffset().Nullable();
            IfDatabase("postgres").Alter.Table("IndexerStatus").AlterColumn("InitialFailure").AsDateTimeOffset().Nullable();
            IfDatabase("postgres").Alter.Table("IndexerStatus").AlterColumn("MostRecentFailure").AsDateTimeOffset().Nullable();
            IfDatabase("postgres").Alter.Table("IndexerStatus").AlterColumn("DisabledTill").AsDateTimeOffset().Nullable();
            IfDatabase("postgres").Alter.Table("MetadataFiles").AlterColumn("LastUpdated").AsDateTimeOffset().NotNullable();
            IfDatabase("postgres").Alter.Table("MetadataFiles").AlterColumn("Added").AsDateTimeOffset().Nullable();
            IfDatabase("postgres").Alter.Table("PendingReleases").AlterColumn("Added").AsDateTimeOffset().NotNullable();
            IfDatabase("postgres").Alter.Table("ScheduledTasks").AlterColumn("LastExecution").AsDateTimeOffset().NotNullable();
            IfDatabase("postgres").Alter.Table("ScheduledTasks").AlterColumn("LastStartTime").AsDateTimeOffset().Nullable();
            IfDatabase("postgres").Alter.Table("VersionInfo").AlterColumn("AppliedOn").AsDateTimeOffset().Nullable();
        }

        protected override void LogDbUpgrade()
        {
            IfDatabase("postgres").Alter.Table("Logs").AlterColumn("Time").AsDateTimeOffset().NotNullable();
            IfDatabase("postgres").Alter.Table("VersionInfo").AlterColumn("AppliedOn").AsDateTimeOffset().Nullable();
        }

        protected override void CacheDbUpgrade()
        {
            IfDatabase("postgres").Alter.Table("HttpResponse").AlterColumn("LastRefresh").AsDateTimeOffset().Nullable();
            IfDatabase("postgres").Alter.Table("HttpResponse").AlterColumn("Expiry").AsDateTimeOffset().Nullable();
        }
    }
}
