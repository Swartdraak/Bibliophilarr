using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Dapper;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.Books
{
    public interface ICatalogueIdentityRepository
    {
        IReadOnlyList<CatalogueSourceAssociation> GetSources(CatalogueEntityKind kind, int ownerId);
        int? FindByProviderId(CatalogueEntityKind kind, string provider, string providerId);
        int? FindByRoute(CatalogueEntityKind kind, string identifier);
        int? FindByLegacyProviderId(CatalogueEntityKind kind, string identifier);
        bool TryAddSource(CatalogueEntityKind kind, int ownerId, string provider, string providerId, string evidence);
        bool TryAddRouteAlias(CatalogueEntityKind kind, int ownerId, string alias);
    }

    public sealed class CatalogueIdentityRepository : ICatalogueIdentityRepository
    {
        internal const string LegacyProviderEvidence = "legacy-provider-id";
        internal const string LegacyLinkedEvidence = "legacy-linked-id";
        internal const string PrimaryProviderEvidence = "primary-provider-id";
        internal const string HardcoverWorkMappingEvidence = "hardcover-original-book-mapping";

        private readonly IMainDatabase _database;

        public CatalogueIdentityRepository(IMainDatabase database)
        {
            _database = database;
        }

        internal static (string Entity, string Source, string Route, string Owner, string Primary) Tables(CatalogueEntityKind kind)
        {
            return kind switch
            {
                CatalogueEntityKind.Author => ("AuthorMetadata", "AuthorSourceAssociations", "AuthorRouteAliases", "AuthorMetadataId", "ForeignAuthorId"),
                CatalogueEntityKind.Work => ("Books", "WorkSourceAssociations", "WorkRouteAliases", "BookId", "ForeignBookId"),
                CatalogueEntityKind.Series => ("Series", "SeriesSourceAssociations", "SeriesRouteAliases", "SeriesId", "ForeignSeriesId"),
                _ => throw new ArgumentOutOfRangeException(nameof(kind))
            };
        }

        public IReadOnlyList<CatalogueSourceAssociation> GetSources(CatalogueEntityKind kind, int ownerId)
        {
            var tables = Tables(kind);
            using var connection = _database.OpenConnection();
            return connection.Query<CatalogueSourceAssociation>(
                $"SELECT \"Provider\", \"ProviderId\", \"Evidence\" FROM \"{tables.Source}\" WHERE \"{tables.Owner}\" = @ownerId ORDER BY \"Provider\", \"ProviderId\"",
                new { ownerId }).ToList();
        }

        public int? FindByProviderId(CatalogueEntityKind kind, string provider, string providerId)
        {
            var association = NormalizeSource(kind, provider, providerId, null);
            if (association == null)
            {
                return null;
            }

            using var connection = _database.OpenConnection();
            return FindByProviderId(connection, null, kind, association);
        }

        public int? FindByLegacyProviderId(CatalogueEntityKind kind, string identifier)
        {
            return FindByLegacyProviderId(_database, kind, identifier);
        }

        internal static int? FindByLegacyProviderId(IDatabase database, CatalogueEntityKind kind, string identifier)
        {
            var association = ParseLegacySource(kind, identifier, null);
            if (association == null)
            {
                return null;
            }

            using var connection = database.OpenConnection();
            return FindByProviderId(connection, null, kind, association);
        }

        private static int? FindByProviderId(IDbConnection connection, IDbTransaction transaction, CatalogueEntityKind kind, CatalogueSourceAssociation association)
        {
            var tables = Tables(kind);
            return connection.QuerySingleOrDefault<int?>(
                $"SELECT \"{tables.Owner}\" FROM \"{tables.Source}\" WHERE \"Provider\" = @Provider AND \"ProviderId\" = @ProviderId",
                association,
                transaction);
        }

        public int? FindByRoute(CatalogueEntityKind kind, string identifier)
        {
            return FindByRoute(_database, kind, identifier);
        }

        internal static int? FindByRoute(IDatabase database, CatalogueEntityKind kind, string identifier)
        {
            if (string.IsNullOrWhiteSpace(identifier))
            {
                return null;
            }

            var tables = Tables(kind);
            using var connection = database.OpenConnection();
            var owner = FindCanonicalOwner(connection, null, kind, identifier);
            if (owner.HasValue)
            {
                return owner;
            }

            if (Guid.TryParseExact(identifier, "N", out var retiredId))
            {
                identifier = retiredId.ToString("N");
            }

            var aliasOwner = connection.QuerySingleOrDefault<int?>(
                $"SELECT \"{tables.Owner}\" FROM \"{tables.Route}\" WHERE \"Alias\" = @identifier",
                new { identifier });
            if (aliasOwner.HasValue)
            {
                return aliasOwner;
            }

            var source = ParseLegacySource(kind, identifier, null);
            return source == null ? null : FindByProviderId(connection, null, kind, source);
        }

        private static int? FindCanonicalOwner(IDbConnection connection, IDbTransaction transaction, CatalogueEntityKind kind, string identifier)
        {
            if (!Guid.TryParseExact(identifier, "N", out var guid))
            {
                return null;
            }

            var tables = Tables(kind);
            return connection.QuerySingleOrDefault<int?>(
                $"SELECT \"Id\" FROM \"{tables.Entity}\" WHERE \"CanonicalId\" = @canonicalId",
                new { canonicalId = guid.ToString("N") },
                transaction);
        }

        public bool TryAddSource(CatalogueEntityKind kind, int ownerId, string provider, string providerId, string evidence)
        {
            var association = NormalizeSource(kind, provider, providerId, evidence);
            if (association == null || ownerId <= 0 || string.IsNullOrWhiteSpace(association.Evidence))
            {
                return false;
            }

            using var connection = _database.OpenConnection();
            using var transaction = connection.BeginTransaction(IsolationLevel.ReadCommitted);
            var added = TryAddSource(connection, transaction, kind, ownerId, association);
            transaction.Commit();
            return added;
        }

        internal static bool TryAddSource(IDbConnection connection, IDbTransaction transaction, CatalogueEntityKind kind, int ownerId, CatalogueSourceAssociation association)
        {
            var tables = Tables(kind);
            var parameters = new { ownerId, association.Provider, association.ProviderId, association.Evidence };
            var changed = connection.Execute(
                $"""
                 INSERT INTO "{tables.Source}" ("{tables.Owner}", "Provider", "ProviderId", "Evidence")
                 SELECT @ownerId, @Provider, @ProviderId, @Evidence FROM "{tables.Entity}" WHERE "Id" = @ownerId
                 ON CONFLICT ("Provider", "ProviderId") DO UPDATE SET "Evidence" = EXCLUDED."Evidence"
                 WHERE "{tables.Source}"."{tables.Owner}" = EXCLUDED."{tables.Owner}"
                   AND "{tables.Source}"."Evidence" IN ('legacy-provider-id', 'legacy-linked-id', 'primary-provider-id')
                   AND EXCLUDED."Evidence" NOT IN ('legacy-provider-id', 'legacy-linked-id', 'primary-provider-id')
                 """,
                parameters,
                transaction);

            return changed > 0 || FindByProviderId(connection, transaction, kind, association) == ownerId;
        }

        public bool TryAddRouteAlias(CatalogueEntityKind kind, int ownerId, string alias)
        {
            using var connection = _database.OpenConnection();
            using var transaction = connection.BeginTransaction(IsolationLevel.ReadCommitted);
            var added = TryAddRouteAlias(connection, transaction, kind, ownerId, alias);
            transaction.Commit();
            return added;
        }

        internal static bool TryAddRouteAlias(IDbConnection connection, IDbTransaction transaction, CatalogueEntityKind kind, int ownerId, string alias)
        {
            if (ownerId <= 0 || string.IsNullOrWhiteSpace(alias))
            {
                return false;
            }

            // Record retired canonical IDs in the same form as the canonical lookup.
            if (Guid.TryParseExact(alias, "N", out var guid))
            {
                alias = guid.ToString("N");
                var canonicalOwner = FindCanonicalOwner(connection, transaction, kind, alias);
                if (canonicalOwner.HasValue)
                {
                    return canonicalOwner == ownerId;
                }
            }

            var tables = Tables(kind);
            var parameters = new { ownerId, alias };
            var changed = connection.Execute(
                $"""
                 INSERT INTO "{tables.Route}" ("{tables.Owner}", "Alias")
                 SELECT @ownerId, @alias FROM "{tables.Entity}" WHERE "Id" = @ownerId
                 ON CONFLICT ("Alias") DO NOTHING
                 """,
                parameters,
                transaction);
            return changed > 0 || connection.QuerySingleOrDefault<int?>(
                $"SELECT \"{tables.Owner}\" FROM \"{tables.Route}\" WHERE \"Alias\" = @alias",
                parameters,
                transaction) == ownerId;
        }

        public static CatalogueSourceAssociation NormalizeProviderSearchId(CatalogueEntityKind kind, string identifier)
        {
            return ParseLegacySource(kind, identifier, null);
        }

        public static bool IsVerifiedAuthorSearchEvidence(string evidence)
        {
            return evidence is PrimaryProviderEvidence or LegacyProviderEvidence or AuthorCanonicalizationService.BibliographyEvidence;
        }

        internal static CatalogueSourceAssociation ParseLegacySource(CatalogueEntityKind kind, string scopedId, string evidence)
        {
            if (string.IsNullOrWhiteSpace(scopedId))
            {
                return null;
            }

            var separator = scopedId.IndexOf(':');
            return separator < 0
                ? NormalizeSource(kind, "OpenLibrary", scopedId, evidence)
                : NormalizeSource(kind, scopedId[..separator], scopedId, evidence);
        }

        internal static CatalogueSourceAssociation NormalizeSource(CatalogueEntityKind kind, string provider, string providerId, string evidence)
        {
            if (string.IsNullOrWhiteSpace(provider) || string.IsNullOrWhiteSpace(providerId))
            {
                return null;
            }

            provider = provider.Trim().ToLowerInvariant();
            providerId = providerId.Trim();
            if (providerId.Length > 255 || evidence?.Length > 128)
            {
                return null;
            }

            var scope = kind.ToString().ToLowerInvariant();
            var providerPrefix = $"{provider}:";
            if (providerId.StartsWith(providerPrefix, StringComparison.OrdinalIgnoreCase))
            {
                providerId = providerId[providerPrefix.Length..];
                var scopedPrefix = $"{scope}:";
                if (providerId.StartsWith(scopedPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    providerId = providerId[scopedPrefix.Length..];
                }
                else if (provider != "hardcover" || kind != CatalogueEntityKind.Author)
                {
                    return null;
                }
            }

            switch (provider)
            {
                case "hardcover" when long.TryParse(providerId, NumberStyles.None, CultureInfo.InvariantCulture, out var id) && id > 0:
                    provider = "Hardcover";
                    providerId = id.ToString(CultureInfo.InvariantCulture);
                    break;
                case "openlibrary" when kind != CatalogueEntityKind.Series:
                    provider = "OpenLibrary";
                    providerId = providerId.ToUpperInvariant();
                    var suffix = kind == CatalogueEntityKind.Author ? "A" : "W";
                    if (!Regex.IsMatch(providerId, $@"\AOL[0-9]+{suffix}\z"))
                    {
                        return null;
                    }

                    break;
                case "googlebooks" when kind == CatalogueEntityKind.Work && Regex.IsMatch(providerId, @"\A[A-Za-z0-9_-]+\z"):
                    provider = "GoogleBooks";
                    break;
                case "inventaire" when kind == CatalogueEntityKind.Work && Regex.IsMatch(providerId, @"\A(?:wd:Q[0-9]+|inv:[A-Za-z0-9-]+)\z"):
                    provider = "Inventaire";
                    break;
                default:
                    return null;
            }

            return new CatalogueSourceAssociation { Provider = provider, ProviderId = providerId, Evidence = evidence?.Trim() };
        }
    }
}
