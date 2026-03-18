using FluentMigrator;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration
{
    [Migration(042)]
    public class add_open_library_ids : NzbDroneMigrationBase
    {
        protected override void MainDbUpgrade()
        {
            if (!Schema.Table("Books").Column("OpenLibraryWorkId").Exists())
            {
                Alter.Table("Books").AddColumn("OpenLibraryWorkId").AsString().Nullable();
            }

            if (!Schema.Table("AuthorMetadata").Column("OpenLibraryAuthorId").Exists())
            {
                Alter.Table("AuthorMetadata").AddColumn("OpenLibraryAuthorId").AsString().Nullable();
            }

            if (!Schema.Table("Books").Index("IX_Books_OpenLibraryWorkId").Exists())
            {
                Create.Index("IX_Books_OpenLibraryWorkId")
                    .OnTable("Books")
                    .OnColumn("OpenLibraryWorkId")
                    .Ascending()
                    .WithOptions()
                    .NonClustered();
            }

            if (!Schema.Table("AuthorMetadata").Index("IX_AuthorMetadata_OpenLibraryAuthorId").Exists())
            {
                Create.Index("IX_AuthorMetadata_OpenLibraryAuthorId")
                    .OnTable("AuthorMetadata")
                    .OnColumn("OpenLibraryAuthorId")
                    .Ascending()
                    .WithOptions()
                    .NonClustered();
            }
        }
    }
}
