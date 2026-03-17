using FluentMigrator;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration
{
    [Migration(041)]
    public class add_open_library_ids : NzbDroneMigrationBase
    {
        protected override void MainDbUpgrade()
        {
            // Add Open Library work ID to the Books table.
            // NULL-able; additive only — no existing rows are touched.
            Alter.Table("Books").AddColumn("OpenLibraryWorkId").AsString().Nullable();

            // Add Open Library author ID to the AuthorMetadata table.
            Alter.Table("AuthorMetadata").AddColumn("OpenLibraryAuthorId").AsString().Nullable();

            // Create indexes for efficient identifier lookups.
            Create.Index("IX_Books_OpenLibraryWorkId")
                .OnTable("Books")
                .OnColumn("OpenLibraryWorkId")
                .Ascending()
                .WithOptions()
                .NonClustered();

            Create.Index("IX_AuthorMetadata_OpenLibraryAuthorId")
                .OnTable("AuthorMetadata")
                .OnColumn("OpenLibraryAuthorId")
                .Ascending()
                .WithOptions()
                .NonClustered();
        }
    }
}
