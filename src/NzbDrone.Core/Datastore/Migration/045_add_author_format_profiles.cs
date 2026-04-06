using System.Data;
using Dapper;
using FluentMigrator;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration
{
    [Migration(045)]
    public class add_author_format_profiles : NzbDroneMigrationBase
    {
        protected override void MainDbUpgrade()
        {
            Create.TableForModel("AuthorFormatProfiles")
                  .WithColumn("AuthorId").AsInt32().NotNullable()
                  .WithColumn("FormatType").AsInt32().NotNullable()
                  .WithColumn("QualityProfileId").AsInt32().NotNullable()
                  .WithColumn("RootFolderPath").AsString().NotNullable()
                  .WithColumn("Tags").AsString().NotNullable().WithDefaultValue("[]")
                  .WithColumn("Monitored").AsBoolean().NotNullable().WithDefaultValue(true)
                  .WithColumn("Path").AsString().NotNullable().WithDefaultValue("");

            Create.Index("IX_AuthorFormatProfiles_AuthorId_FormatType")
                  .OnTable("AuthorFormatProfiles")
                  .OnColumn("AuthorId").Ascending()
                  .OnColumn("FormatType").Ascending()
                  .WithOptions().Unique();

            Execute.WithConnection(PopulateFromExistingAuthors);
        }

        private void PopulateFromExistingAuthors(IDbConnection conn, IDbTransaction tran)
        {
            // Auto-populate one format profile per existing author from current config.
            // Detect format type from quality profile allowed qualities:
            //   Audio qualities (MP3=10, FLAC=11, M4B=12, UnknownAudio=13) → Audiobook (1)
            //   Ebook qualities (PDF=1, MOBI=2, EPUB=3, AZW3=4) → Ebook (0)
            // Fallback: if quality profile allows audio items, classify as Audiobook.
            var sql = @"
                INSERT INTO ""AuthorFormatProfiles""
                    (""AuthorId"", ""FormatType"", ""QualityProfileId"", ""RootFolderPath"", ""Tags"", ""Monitored"", ""Path"")
                SELECT
                    a.""Id"",
                    CASE
                        WHEN EXISTS (
                            SELECT 1 FROM json_each(qp.""Items"")
                            WHERE json_extract(value, '$.quality') IN (10, 11, 12, 13)
                              AND json_extract(value, '$.allowed') = 1
                        ) THEN 1
                        ELSE 0
                    END,
                    a.""QualityProfileId"",
                    '',
                    COALESCE(a.""Tags"", '[]'),
                    1,
                    COALESCE(a.""Path"", '')
                FROM ""Authors"" a
                JOIN ""QualityProfiles"" qp ON qp.""Id"" = a.""QualityProfileId""";

            conn.Execute(sql, transaction: tran);
        }
    }
}
