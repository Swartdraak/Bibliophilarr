using System.Data;
using FluentMigrator;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration
{
    [Migration(052)]
    public class add_book_author_credits : NzbDroneMigrationBase
    {
        protected override void MainDbUpgrade()
        {
            Create.TableForModel("BookAuthorCredits")
                .WithColumn("BookId").AsInt32().NotNullable().ForeignKey("Books", "Id").OnDelete(Rule.Cascade)
                .WithColumn("AuthorMetadataId").AsInt32().NotNullable().ForeignKey("AuthorMetadata", "Id").OnDelete(Rule.Cascade)
                .WithColumn("Provider").AsString(32).NotNullable()
                .WithColumn("ProviderAuthorId").AsString().NotNullable()
                .WithColumn("ProviderWorkId").AsString().NotNullable();

            Create.Index("IX_BookAuthorCredits_Author").OnTable("BookAuthorCredits").OnColumn("AuthorMetadataId").Ascending();
            Create.Index("UX_BookAuthorCredits_Source").OnTable("BookAuthorCredits")
                .OnColumn("BookId").Ascending().OnColumn("AuthorMetadataId").Ascending().OnColumn("Provider").Ascending()
                .OnColumn("ProviderAuthorId").Ascending().OnColumn("ProviderWorkId").Ascending().WithOptions().Unique();
        }
    }
}
