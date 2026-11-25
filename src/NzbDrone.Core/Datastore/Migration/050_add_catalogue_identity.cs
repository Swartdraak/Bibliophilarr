using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using Dapper;
using FluentMigrator;
using NzbDrone.Core.Books;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration
{
    [Migration(050)]
    public class add_catalogue_identity : NzbDroneMigrationBase
    {
        protected override void MainDbUpgrade()
        {
            AddIdentity(CatalogueEntityKind.Author, "ForeignAuthorId", "OpenLibraryAuthorId", "TitleSlug");
            AddIdentity(CatalogueEntityKind.Work, "ForeignBookId", "OpenLibraryWorkId", "TitleSlug");
            AddIdentity(CatalogueEntityKind.Series, "ForeignSeriesId");
        }

        private void AddIdentity(CatalogueEntityKind kind, string legacyProviderColumn, string openLibraryColumn = null, string slugColumn = null)
        {
            var tables = CatalogueIdentityRepository.Tables(kind);

            // Add in place so the SQLite upgrade does not rebuild existing catalogue
            // tables or touch their dependent rows. Repository inserts always assign an ID.
            Alter.Table(tables.Entity).AddColumn("CanonicalId").AsString(32).Nullable();
            Create.Index($"UX_{tables.Entity}_CanonicalId").OnTable(tables.Entity).OnColumn("CanonicalId").Ascending().WithOptions().Unique();

            Create.TableForModel(tables.Source)
                .WithColumn(tables.Owner).AsInt32().NotNullable().ForeignKey(tables.Entity, "Id").OnDelete(Rule.Cascade)
                .WithColumn("Provider").AsString(32).NotNullable()
                .WithColumn("ProviderId").AsString().NotNullable()
                .WithColumn("Evidence").AsString(128).NotNullable();
            Create.Index($"IX_{tables.Source}_Owner").OnTable(tables.Source).OnColumn(tables.Owner).Ascending();
            Create.Index($"UX_{tables.Source}_ProviderIdentity").OnTable(tables.Source)
                .OnColumn("Provider").Ascending().OnColumn("ProviderId").Ascending().WithOptions().Unique();

            Create.TableForModel(tables.Route)
                .WithColumn(tables.Owner).AsInt32().NotNullable().ForeignKey(tables.Entity, "Id").OnDelete(Rule.Cascade)
                .WithColumn("Alias").AsString().NotNullable();
            Create.Index($"IX_{tables.Route}_Owner").OnTable(tables.Route).OnColumn(tables.Owner).Ascending();
            Create.Index($"UX_{tables.Route}_Alias").OnTable(tables.Route).OnColumn("Alias").Ascending().WithOptions().Unique();

            var fields = $"\"Id\", \"{legacyProviderColumn}\" AS \"LegacyProviderId\"";
            if (openLibraryColumn != null)
            {
                fields += $", \"{openLibraryColumn}\" AS \"OpenLibraryId\"";
            }

            if (slugColumn != null)
            {
                fields += $", \"{slugColumn}\" AS \"LegacySlug\"";
            }

            Execute.WithConnection((connection, transaction) =>
            {
                var rows = connection.Query<LegacyIdentityRow>(
                    $"SELECT {fields} FROM \"{tables.Entity}\" WHERE \"CanonicalId\" IS NULL ORDER BY \"Id\"",
                    transaction: transaction).ToList();

                connection.Execute(
                    $"UPDATE \"{tables.Entity}\" SET \"CanonicalId\" = @CanonicalId WHERE \"Id\" = @Id",
                    rows.Select(row => new { row.Id, CanonicalId = Guid.NewGuid().ToString("N") }),
                    transaction);

                var claims = new List<(int OwnerId, CatalogueSourceAssociation Association, string RouteAlias)>();
                foreach (var row in rows)
                {
                    var current = CatalogueIdentityRepository.ParseLegacySource(kind, row.LegacyProviderId, CatalogueIdentityRepository.LegacyProviderEvidence);
                    if (current != null)
                    {
                        claims.Add((row.Id, current, row.LegacyProviderId));
                    }

                    var linked = CatalogueIdentityRepository.NormalizeSource(kind, "OpenLibrary", row.OpenLibraryId, CatalogueIdentityRepository.LegacyLinkedEvidence);
                    if (linked != null)
                    {
                        claims.Add((row.Id, linked, null));
                    }

                    CatalogueIdentityRepository.TryAddRouteAlias(connection, transaction, kind, row.Id, row.LegacySlug);
                }

                var conflicts = 0;
                foreach (var matches in claims.GroupBy(claim => (claim.Association.Provider, claim.Association.ProviderId)))
                {
                    var first = matches.First();
                    if (matches.Any(claim => claim.OwnerId != first.OwnerId))
                    {
                        conflicts++;
                        continue;
                    }

                    CatalogueIdentityRepository.TryAddSource(connection, transaction, kind, first.OwnerId, first.Association);
                    CatalogueIdentityRepository.TryAddRouteAlias(connection, transaction, kind, first.OwnerId, first.RouteAlias);
                }

                if (conflicts > 0)
                {
                    _logger.Warn("Kept {0} competing legacy {1} source identities on existing rows for review.", conflicts, kind);
                }
            });
        }

        private sealed class LegacyIdentityRow
        {
            public int Id { get; set; }
            public string LegacyProviderId { get; set; }
            public string OpenLibraryId { get; set; }
            public string LegacySlug { get; set; }
        }
    }
}
