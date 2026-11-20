using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using Dapper;
using NLog;
using NzbDrone.Core.Books.Commands;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Messaging.Commands;

namespace NzbDrone.Core.Books
{
    public class CatalogueAuthorReconciliationService : IExecute<ReconcileCatalogueAuthorsCommand>
    {
        private readonly IMainDatabase _database;
        private readonly IAuthorRepository _authors;
        private readonly IAuthorService _authorService;
        private readonly IAuthorCanonicalizationService _canonicalization;
        private readonly Logger _logger;

        public CatalogueAuthorReconciliationService(IMainDatabase database,
                                                    IAuthorRepository authors,
                                                    IAuthorService authorService,
                                                    IAuthorCanonicalizationService canonicalization,
                                                    Logger logger)
        {
            _database = database;
            _authors = authors;
            _authorService = authorService;
            _canonicalization = canonicalization;
            _logger = logger;
        }

        public void Execute(ReconcileCatalogueAuthorsCommand command)
        {
            var source = _authors.Find(command.SourceAuthorId);
            var target = _authors.Find(command.TargetAuthorId);
            if (source == null && target != null && _authors.FindByRetiredId(command.SourceAuthorId)?.Id == target.Id)
            {
                return;
            }

            CheckAuthorSettings(source, target);
            if (!_canonicalization.VerifyDistinctAuthorities(source, target))
            {
                throw new InvalidOperationException("The selected author records lack two independently verified works.");
            }

            using var connection = _database.OpenConnection();
            using var transaction = connection.BeginTransaction(IsolationLevel.Serializable);
            var currentSource = ReadAuthor(connection, transaction, source.Id);
            var currentTarget = ReadAuthor(connection, transaction, target.Id);
            CheckAuthorSettings(currentSource, currentTarget);
            if (!MatchesVerifiedRecord(source, currentSource) || !MatchesVerifiedRecord(target, currentTarget))
            {
                throw new InvalidOperationException("The selected author identity changed during verification.");
            }

            foreach (var alias in RetiredAliases(currentSource))
            {
                var owner = connection.QuerySingleOrDefault<int?>(
                    "SELECT \"AuthorMetadataId\" FROM \"AuthorRouteAliases\" WHERE \"Alias\" = @Alias",
                    new { Alias = alias },
                    transaction);
                if (owner.HasValue && owner != currentSource.AuthorMetadataId && owner != currentTarget.AuthorMetadataId)
                {
                    throw new InvalidOperationException("A retired author route has a different catalogue owner.");
                }
            }

            var sourceProfiles = ReadFormatProfiles(connection, transaction, currentSource.Id);
            var targetProfiles = ReadFormatProfiles(connection, transaction, currentTarget.Id).ToDictionary(profile => profile.FormatType);
            if (sourceProfiles.Count != targetProfiles.Count ||
                sourceProfiles.Any(profile => !targetProfiles.TryGetValue(profile.FormatType, out var existing) || !CompatibleFormat(profile, existing)))
            {
                throw new InvalidOperationException("The selected authors have conflicting format paths, profiles, or monitoring settings.");
            }

            var sourceWorks = ReadWorks(connection, transaction, currentSource.AuthorMetadataId);
            var targetWorks = ReadWorks(connection, transaction, currentTarget.AuthorMetadataId);
            var workPairs = FindCompatibleWorkPairs(sourceWorks, targetWorks);
            foreach (var pair in workPairs)
            {
                // Read again after each pair, since reconciliation can rewrite related work IDs.
                var retiring = CatalogueWorkReconciliationService.ReadWork(connection, transaction, pair.Source);
                var retained = CatalogueWorkReconciliationService.ReadWork(connection, transaction, pair.Target);
                retiring.AuthorMetadataId = retained.AuthorMetadataId;
                CatalogueWorkReconciliationService.Reconcile(connection, transaction, retiring, retained, command.DryRun);
            }

            if (command.DryRun)
            {
                _logger.Info(
                    "Verified authors {0} and {1}. The source has {2} works, including {3} corroborated duplicates.",
                    source.Id,
                    target.Id,
                    sourceWorks.Count,
                    workPairs.Count);
                return;
            }

            TransferAuthorRecords(connection, transaction, currentSource, currentTarget);
            transaction.Commit();
            _authorService.ClearAuthorCache();
            _logger.Info("Reconciled author {0} into {1}, preserving author routes, existing files and format settings.", source.Id, target.Id);
        }

        private static void TransferAuthorRecords(IDbConnection connection, IDbTransaction transaction, Author source, Author target)
        {
            var ids = new { Source = source.Id, Target = target.Id, SourceMetadata = source.AuthorMetadataId, TargetMetadata = target.AuthorMetadataId };
            connection.Execute("UPDATE \"Books\" SET \"AuthorMetadataId\" = @TargetMetadata WHERE \"AuthorMetadataId\" = @SourceMetadata", ids, transaction);
            foreach (var table in new[] { "History", "Blocklist", "MetadataFiles", "PendingReleases", "ExtraFiles", "DownloadHistory" })
            {
                connection.Execute($"UPDATE \"{table}\" SET \"AuthorId\" = @Target WHERE \"AuthorId\" = @Source", ids, transaction);
            }

            connection.Execute("DELETE FROM \"AuthorFormatProfiles\" WHERE \"AuthorId\" = @Source", ids, transaction);
            connection.Execute("UPDATE \"AuthorSourceAssociations\" SET \"AuthorMetadataId\" = @TargetMetadata WHERE \"AuthorMetadataId\" = @SourceMetadata", ids, transaction);
            var sourceIdentity = CatalogueIdentityRepository.ParseLegacySource(
                CatalogueEntityKind.Author,
                source.ForeignAuthorId,
                AuthorCanonicalizationService.BibliographyEvidence);
            if (sourceIdentity == null ||
                !CatalogueIdentityRepository.TryAddSource(connection, transaction, CatalogueEntityKind.Author, target.AuthorMetadataId, sourceIdentity))
            {
                throw new InvalidOperationException("A verified author source has a different catalogue owner.");
            }

            connection.Execute("UPDATE \"AuthorRouteAliases\" SET \"AuthorMetadataId\" = @TargetMetadata WHERE \"AuthorMetadataId\" = @SourceMetadata", ids, transaction);
            connection.Execute(
                """
                UPDATE "AuthorMetadata" AS target
                SET "Overview" = COALESCE(NULLIF(TRIM(target."Overview"), ''), source."Overview"),
                    "Born" = COALESCE(target."Born", source."Born"),
                    "Died" = COALESCE(target."Died", source."Died"),
                    "OpenLibraryAuthorId" = COALESCE(target."OpenLibraryAuthorId", source."OpenLibraryAuthorId"),
                    "Images" = CASE WHEN target."Images" IS NULL OR TRIM(target."Images") IN ('', '[]')
                                    THEN source."Images" ELSE target."Images" END
                FROM "AuthorMetadata" source
                WHERE target."Id" = @TargetMetadata AND source."Id" = @SourceMetadata
                """,
                ids,
                transaction);
            connection.Execute("DELETE FROM \"Authors\" WHERE \"Id\" = @Source", ids, transaction);
            connection.Execute("DELETE FROM \"AuthorMetadata\" WHERE \"Id\" = @SourceMetadata", ids, transaction);

            foreach (var alias in RetiredAliases(source))
            {
                if (!CatalogueIdentityRepository.TryAddRouteAlias(connection, transaction, CatalogueEntityKind.Author, target.AuthorMetadataId, alias))
                {
                    throw new InvalidOperationException("A retired author route has a different catalogue owner.");
                }
            }
        }

        private static IEnumerable<string> RetiredAliases(Author author)
        {
            var metadata = author.Metadata.Value;
            return new[] { metadata.CanonicalId, metadata.ForeignAuthorId, metadata.TitleSlug, AuthorRepository.RetiredRoute(author.Id) }
                .Where(alias => !string.IsNullOrWhiteSpace(alias));
        }

        private static List<(int Source, int Target)> FindCompatibleWorkPairs(List<Book> sourceWorks, List<Book> targetWorks)
        {
            var pairs = new List<(int Source, int Target)>();
            var used = new HashSet<int>();
            foreach (var source in sourceWorks)
            {
                var matches = targetWorks.Where(target => BookWorkIdentity.AreCorroboratedForSameAuthor(source, target)).Take(2).ToList();
                if (matches.Count == 0)
                {
                    continue;
                }

                if (matches.Count > 1 || !used.Add(matches[0].Id))
                {
                    throw new InvalidOperationException("Work reconciliation is ambiguous for the selected author pair.");
                }

                pairs.Add((source.Id, matches[0].Id));
            }

            return pairs;
        }

        private static void CheckAuthorSettings(Author source, Author target)
        {
            if (source == null || target == null || source.Id == target.Id)
            {
                throw new InvalidOperationException("Select two different tracked authors.");
            }

            if (string.IsNullOrWhiteSpace(source.Path) || !string.Equals(source.Path, target.Path, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("The selected authors have different library paths or have no confirmed path.");
            }

            if (source.QualityProfileId != target.QualityProfileId || source.MetadataProfileId != target.MetadataProfileId ||
                source.Monitored != target.Monitored || source.MonitorNewItems != target.MonitorNewItems ||
                !source.Tags.SetEquals(target.Tags) || source.AddOptions != null || target.AddOptions != null)
            {
                throw new InvalidOperationException("The selected authors have different profile or monitoring settings, or a pending add action.");
            }
        }

        private static bool MatchesVerifiedRecord(Author verified, Author current)
        {
            return current.AuthorMetadataId == verified.AuthorMetadataId &&
                   current.Metadata.Value.CanonicalId == verified.Metadata.Value.CanonicalId &&
                   current.ForeignAuthorId == verified.ForeignAuthorId;
        }

        private static bool CompatibleFormat(AuthorFormatProfile source, AuthorFormatProfile target)
        {
            return source.RootFolderPath == target.RootFolderPath && source.Path == target.Path &&
                   source.QualityProfileId == target.QualityProfileId && source.Monitored == target.Monitored &&
                   source.MonitorNewItems == target.MonitorNewItems && source.Tags.SetEquals(target.Tags);
        }

        private static Author ReadAuthor(IDbConnection connection, IDbTransaction transaction, int id)
        {
            return connection.Query<Author, AuthorMetadata, Author>(
                """
                SELECT a.*, m.* FROM "Authors" a JOIN "AuthorMetadata" m ON m."Id" = a."AuthorMetadataId"
                WHERE a."Id" = @Id
                """,
                (author, metadata) =>
                {
                    author.Metadata = metadata;
                    return author;
                },
                new { Id = id },
                transaction).SingleOrDefault();
        }

        private static List<AuthorFormatProfile> ReadFormatProfiles(IDbConnection connection, IDbTransaction transaction, int id)
        {
            return connection.Query<AuthorFormatProfile>(
                "SELECT * FROM \"AuthorFormatProfiles\" WHERE \"AuthorId\" = @Id",
                new { Id = id },
                transaction).ToList();
        }

        private static List<Book> ReadWorks(IDbConnection connection, IDbTransaction transaction, int authorMetadataId)
        {
            return connection.Query<int>(
                "SELECT \"Id\" FROM \"Books\" WHERE \"AuthorMetadataId\" = @Id ORDER BY \"Id\"",
                new { Id = authorMetadataId },
                transaction).Select(id => CatalogueWorkReconciliationService.ReadWork(connection, transaction, id)).ToList();
        }
    }
}
