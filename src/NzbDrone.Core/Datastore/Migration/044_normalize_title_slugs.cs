using System.Data;
using Dapper;
using FluentMigrator;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration
{
    [Migration(044)]
    public class normalize_title_slugs : NzbDroneMigrationBase
    {
        protected override void MainDbUpgrade()
        {
            Execute.WithConnection(NormalizeAuthorSlugs);
            Execute.WithConnection(NormalizeBookSlugs);
            Execute.WithConnection(NormalizeEditionSlugs);
        }

        private void NormalizeAuthorSlugs(IDbConnection conn, IDbTransaction tran)
        {
            var rows = conn.Query<SlugRow>(
                "SELECT \"Id\", \"TitleSlug\" FROM \"AuthorMetadata\"",
                transaction: tran);

            foreach (var row in rows)
            {
                row.TitleSlug = row.TitleSlug.ToUrlSlug();
            }

            conn.Execute(
                "UPDATE \"AuthorMetadata\" SET \"TitleSlug\" = @TitleSlug WHERE \"Id\" = @Id",
                rows,
                transaction: tran);
        }

        private void NormalizeBookSlugs(IDbConnection conn, IDbTransaction tran)
        {
            var rows = conn.Query<SlugRow>(
                "SELECT \"Id\", \"TitleSlug\" FROM \"Books\"",
                transaction: tran);

            foreach (var row in rows)
            {
                row.TitleSlug = row.TitleSlug.ToUrlSlug();
            }

            conn.Execute(
                "UPDATE \"Books\" SET \"TitleSlug\" = @TitleSlug WHERE \"Id\" = @Id",
                rows,
                transaction: tran);
        }

        private void NormalizeEditionSlugs(IDbConnection conn, IDbTransaction tran)
        {
            var rows = conn.Query<SlugRow>(
                "SELECT \"Id\", \"TitleSlug\" FROM \"Editions\"",
                transaction: tran);

            foreach (var row in rows)
            {
                row.TitleSlug = row.TitleSlug.ToUrlSlug();
            }

            conn.Execute(
                "UPDATE \"Editions\" SET \"TitleSlug\" = @TitleSlug WHERE \"Id\" = @Id",
                rows,
                transaction: tran);
        }

        private class SlugRow : ModelBase
        {
            public string TitleSlug { get; set; }
        }
    }
}
