using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using Dapper;
using Newtonsoft.Json;
using NLog;
using NzbDrone.Core.Books.Commands;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.BookImport;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.MetadataSource.OpenLibrary;
using BridgeIdentity =(int SourceId, int TargetId, string SourceCanonical, string TargetCanonical, int AuthorMetadataId,
    string HardcoverWork, string OpenLibraryWork, string OpenLibraryAuthor, string HardcoverAuthor, string Title, string TargetTitle);
using FileWitness =(int FileId, int EditionId, string ForeignEditionId, string Path, long Size, System.DateTime Modified,
    int Part, string Isbn, string Format);

namespace NzbDrone.Core.Books
{
    public class CatalogueWorkReconciliationService : IExecute<ReconcileCatalogueWorksCommand>
    {
        private const string UnverifiedBridge = "The selected works do not have a current, verified Open Library edition bridge.";

        private readonly IMainDatabase _database;
        private readonly IMetadataProviderRegistry _metadataProviders;
        private readonly Logger _logger;

        public CatalogueWorkReconciliationService(IMainDatabase database, IMetadataProviderRegistry metadataProviders, Logger logger)
        {
            _database = database;
            _metadataProviders = metadataProviders;
            _logger = logger;
        }

        public void Execute(ReconcileCatalogueWorksCommand command)
        {
            var verifiedBridge = command.VerifyOpenLibraryIsbnBridge ? VerifyBridge(command) : null;

            using var connection = _database.OpenConnection();
            using var transaction = connection.BeginTransaction(IsolationLevel.Serializable);
            if (verifiedBridge != null && _database.DatabaseType == DatabaseType.PostgreSQL)
            {
                LockBridgeRows(connection, transaction, verifiedBridge);
            }

            var source = ReadWork(connection, transaction, command.SourceBookId);
            var target = ReadWork(connection, transaction, command.TargetBookId);

            if (source == null && target != null && command.SourceBookId > 0)
            {
                if (verifiedBridge != null)
                {
                    throw new InvalidOperationException(UnverifiedBridge);
                }

                _logger.Info("Source work is already absent. No catalogue changes were made.");
                return;
            }

            if (command.VerifyOpenLibraryIsbnBridge &&
                (verifiedBridge == null || !verifiedBridge.Matches(ReadBridge(connection, transaction, source, target))))
            {
                throw new InvalidOperationException(UnverifiedBridge);
            }

            BindVerifiedLegacyRemap(connection, transaction, source, target);
            Reconcile(connection, transaction, source, target, command.DryRun, verifiedBridge);
            if (command.DryRun)
            {
                _logger.Info("Works {0} and {1} have corroborated identity. The source has {2} editions to transfer.", source.Id, target.Id, source.Editions.Value.Count);
                return;
            }

            transaction.Commit();
            _logger.Info("Reconciled work {0} into {1}, retaining both formats and existing edition and file IDs.", source.Id, target.Id);
        }

        private Bridge VerifyBridge(ReconcileCatalogueWorksCommand command)
        {
            if (command.Trigger != CommandTrigger.Manual)
            {
                throw new InvalidOperationException(UnverifiedBridge);
            }

            Bridge candidate;
            using (var connection = _database.OpenConnection())
            {
                var source = ReadWork(connection, null, command.SourceBookId);
                var target = ReadWork(connection, null, command.TargetBookId);
                if (source == null && target != null && command.SourceBookId > 0)
                {
                    return null;
                }

                candidate = ReadBridge(connection, null, source, target);
            }

            var provider = _metadataProviders.GetProviders().OfType<OpenLibraryProvider>().FirstOrDefault();
            if (provider?.IsEnabled != true ||
                (_metadataProviders.GetProvidersHealthStatus().TryGetValue("OpenLibrary", out var health) &&
                 health?.CooldownUntilUtc > DateTime.UtcNow) ||
                !provider.VerifyWorkForReconciliation(candidate.Identity.OpenLibraryAuthor, candidate.Identity.OpenLibraryWork, candidate.Ebook.Isbn, candidate.Identity.Title))
            {
                throw new InvalidOperationException(UnverifiedBridge);
            }

            return candidate;
        }

        private static Bridge ReadBridge(IDbConnection connection, IDbTransaction transaction, Book source, Book target)
        {
            var sourceKey = CatalogueIdentityRepository.ParseLegacySource(CatalogueEntityKind.Work, source?.ForeignBookId, null);
            var targetKey = CatalogueIdentityRepository.ParseLegacySource(CatalogueEntityKind.Work, target?.ForeignBookId, null);
            if (sourceKey?.Provider != "Hardcover" || targetKey?.Provider != "OpenLibrary" ||
                source.AuthorMetadataId <= 0 || source.AuthorMetadataId != target.AuthorMetadataId ||
                !OpenLibraryProvider.SameReconciliationTitle(source.Title, target.Title) ||
                HasConflictingOpenLibraryWorkIds(connection, transaction, source, target) ||
                !OnlySelectedLegacyOwners(
                    connection,
                    transaction,
                    source,
                    target,
                    original => (original.Provider == sourceKey.Provider && original.ProviderId == sourceKey.ProviderId) ||
                                (original.Provider == targetKey.Provider && original.ProviderId == targetKey.ProviderId)))
            {
                throw new InvalidOperationException(UnverifiedBridge);
            }

            ReadLinkAndMonitoringConstraints(source, target);

            var sources = connection.Query<(int BookId, string Provider, string ProviderId, string Evidence)>(
                """
                SELECT "BookId", "Provider", "ProviderId", "Evidence" FROM "WorkSourceAssociations"
                WHERE "BookId" IN (@Source, @Target)
                """,
                new { Source = source.Id, Target = target.Id },
                transaction).ToList();
            if (!sources.Contains((source.Id, sourceKey.Provider, sourceKey.ProviderId, CatalogueIdentityRepository.PrimaryProviderEvidence)) ||
                !sources.Contains((target.Id, targetKey.Provider, targetKey.ProviderId, CatalogueIdentityRepository.PrimaryProviderEvidence)) ||
                sources.Any(item => item.BookId == source.Id && item.Provider == "OpenLibrary"))
            {
                throw new InvalidOperationException(UnverifiedBridge);
            }

            var authorities = connection.Query<(string Provider, string ProviderId, string Evidence)>(
                """
                SELECT "Provider", "ProviderId", "Evidence" FROM "AuthorSourceAssociations" WHERE "AuthorMetadataId" = @Owner
                """,
                new { Owner = source.AuthorMetadataId },
                transaction).ToList();
            var openLibrary = authorities.Where(item => item.Provider == "OpenLibrary").ToList();
            var hardcover = authorities.Where(item => item.Provider == "Hardcover").ToList();
            if (openLibrary.Count != 1 || hardcover.Count != 1 ||
                openLibrary[0].Evidence != CatalogueIdentityRepository.PrimaryProviderEvidence ||
                hardcover[0].Evidence != AuthorCanonicalizationService.BibliographyEvidence ||
                CatalogueIdentityRepository.NormalizeSource(CatalogueEntityKind.Author, "OpenLibrary", openLibrary[0].ProviderId, null) == null)
            {
                throw new InvalidOperationException(UnverifiedBridge);
            }

            var ebook = ReadFileWitness(connection, transaction, source, FormatType.Ebook);
            var audiobook = ReadFileWitness(connection, transaction, target, FormatType.Audiobook);
            if (ebook?.Isbn == null || audiobook == null ||
                !IsHardcoverEditionWitness(ebook.Value.ForeignEditionId, sourceKey.ProviderId))
            {
                throw new InvalidOperationException(UnverifiedBridge);
            }

            BridgeIdentity identity = (source.Id, target.Id, source.CanonicalId, target.CanonicalId, source.AuthorMetadataId,
                sourceKey.ProviderId, targetKey.ProviderId, openLibrary[0].ProviderId, hardcover[0].ProviderId, source.Title, target.Title);
            return new Bridge(identity, ebook.Value, audiobook.Value);
        }

        private static bool IsHardcoverEditionWitness(string editionId, string workId)
        {
            const string prefix = "hardcover:source-edition:";
            return editionId == $"hardcover:edition:{workId}" ||
                   (editionId.StartsWith(prefix, StringComparison.Ordinal) &&
                    long.TryParse(
                        editionId.AsSpan(prefix.Length),
                        System.Globalization.NumberStyles.None,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out var id) && id > 0);
        }

        private static FileWitness? ReadFileWitness(IDbConnection connection, IDbTransaction transaction, Book work, FormatType format)
        {
            var editions = work.Editions.Value.ToDictionary(item => item.Id);
            var files = connection.Query<BookFile>(
                """
                SELECT f."Id", f."EditionId", f."Path", f."Size", f."Modified", f."Part" FROM "BookFiles" f
                JOIN "Editions" e ON f."EditionId" = e."Id" WHERE e."BookId" = @WorkId
                LIMIT 2
                """,
                new { WorkId = work.Id },
                transaction).AsList();
            if (files.Count != 1)
            {
                return null;
            }

            var file = files[0];
            var edition = editions.GetValueOrDefault(file.EditionId);
            if (file.Size <= 0 || edition == null || string.IsNullOrWhiteSpace(edition.ForeignEditionId) ||
                ImportFormat.ForPath(file.Path) != format || ImportFormat.ForEdition(edition, requireExplicitFormat: true) != format ||
                edition.IsEbook != (format == FormatType.Ebook))
            {
                return null;
            }

            return (file.Id, file.EditionId, edition.ForeignEditionId, file.Path, file.Size, file.Modified, file.Part,
                Isbn.AsIsbn13(edition.Isbn13), edition.Format);
        }

        private static void LockBridgeRows(IDbConnection connection, IDbTransaction transaction, Bridge bridge)
        {
            var ids = new { Source = bridge.Identity.SourceId, Target = bridge.Identity.TargetId, Owner = bridge.Identity.AuthorMetadataId };
            connection.Query<int>(
                """SELECT "Id" FROM "Books" WHERE "Id" IN (@Source, @Target) ORDER BY "Id" FOR UPDATE""",
                ids,
                transaction).AsList();
            connection.Query<int>(
                """SELECT "Id" FROM "AuthorMetadata" WHERE "Id" = @Owner FOR UPDATE""",
                ids,
                transaction).AsList();
            connection.Query<int>(
                """SELECT "Id" FROM "AuthorSourceAssociations" WHERE "AuthorMetadataId" = @Owner ORDER BY "Id" FOR UPDATE""",
                ids,
                transaction).AsList();
            connection.Query<int>(
                """SELECT "Id" FROM "WorkSourceAssociations" WHERE "BookId" IN (@Source, @Target) ORDER BY "Id" FOR UPDATE""",
                ids,
                transaction).AsList();
            connection.Query<int>(
                """SELECT "Id" FROM "Editions" WHERE "BookId" IN (@Source, @Target) ORDER BY "Id" FOR UPDATE""",
                ids,
                transaction).AsList();
            connection.Query<int>(
                """
                SELECT f."Id" FROM "BookFiles" f JOIN "Editions" e ON f."EditionId" = e."Id"
                WHERE e."BookId" IN (@Source, @Target) ORDER BY f."Id" FOR UPDATE OF f
                """,
                ids,
                transaction).AsList();
        }

        private static void BindVerifiedLegacyRemap(IDbConnection connection, IDbTransaction transaction, Book source, Book target)
        {
            if (source == null || target == null || source.Id == target.Id ||
                source.AuthorMetadataId <= 0 || source.AuthorMetadataId != target.AuthorMetadataId)
            {
                return;
            }

            var original = CatalogueIdentityRepository.ParseLegacySource(CatalogueEntityKind.Work, source.ForeignBookId, null);
            var canonical = CatalogueIdentityRepository.ParseLegacySource(CatalogueEntityKind.Work, target.ForeignBookId, null);
            if (original?.Provider != "Hardcover" || canonical?.Provider != "Hardcover" ||
                original.ProviderId == canonical.ProviderId)
            {
                return;
            }

            var keys = new[] { original.ProviderId, canonical.ProviderId };
            var associations = connection.Query<(int BookId, string ProviderId, string Evidence)>(
                """
                SELECT "BookId", "ProviderId", "Evidence"
                FROM "WorkSourceAssociations"
                WHERE "Provider" = 'Hardcover' AND "ProviderId" IN (@Original, @Canonical)
                """,
                new { Original = original.ProviderId, Canonical = canonical.ProviderId },
                transaction).ToList();
            var originalAssociation = associations.SingleOrDefault(association => association.ProviderId == original.ProviderId);
            var canonicalAssociation = associations.SingleOrDefault(association => association.ProviderId == canonical.ProviderId);

            if (canonicalAssociation.BookId > 0 && canonicalAssociation.BookId != target.Id)
            {
                throw new InvalidOperationException("The selected target's provider identity belongs to another work.");
            }

            if (originalAssociation.BookId == 0 || originalAssociation.BookId == source.Id)
            {
                return;
            }

            if (originalAssociation.BookId != target.Id || canonicalAssociation.BookId != target.Id ||
                originalAssociation.Evidence != CatalogueIdentityRepository.HardcoverWorkMappingEvidence)
            {
                throw new InvalidOperationException("The selected source's provider identity has no verified remap to the selected target.");
            }

            // Earlier migrations can leave conflicting raw legacy keys outside the unique association table.
            // Normalize the raw rows with the same rules used to store provider identities.
            if (!OnlySelectedLegacyOwners(
                connection,
                transaction,
                source,
                target,
                primary => primary.Provider == "Hardcover" && keys.Contains(primary.ProviderId)))
            {
                throw new InvalidOperationException("The remap has competing legacy work owners.");
            }

            original.Evidence = originalAssociation.Evidence;
            target.PendingSourceAssociations.Add(original);
        }

        private static bool OnlySelectedLegacyOwners(
            IDbConnection connection, IDbTransaction transaction, Book source, Book target, Func<CatalogueSourceAssociation, bool> matches)
        {
            var owners = connection.Query<(int Id, string ForeignBookId)>(
                    """SELECT "Id", "ForeignBookId" FROM "Books" """,
                    transaction: transaction,
                    buffered: false)
                .Where(row => CatalogueIdentityRepository.ParseLegacySource(CatalogueEntityKind.Work, row.ForeignBookId, null) is { } primary &&
                              matches(primary))
                .Select(row => row.Id)
                .Take(3)
                .ToList();
            return owners.Count == 2 && owners.Contains(source.Id) && owners.Contains(target.Id);
        }

        internal static void Reconcile(IDbConnection connection, IDbTransaction transaction, Book source, Book target, bool dryRun)
        {
            Reconcile(connection, transaction, source, target, dryRun, null);
        }

        private static void Reconcile(IDbConnection connection, IDbTransaction transaction, Book source, Book target, bool dryRun, Bridge verifiedBridge)
        {
            const string insufficientEvidence = "The selected works lack corroborated identity under the same author.";
            if (source == null || target == null || source.Id == target.Id)
            {
                throw new InvalidOperationException(insufficientEvidence);
            }

            if (HasConflictingOpenLibraryWorkIds(connection, transaction, source, target))
            {
                throw new InvalidOperationException("The selected works have conflicting Open Library work IDs. Verify an official work alias before reconciling them.");
            }

            if (verifiedBridge == null && !BookWorkIdentity.AreCorroborated(source, target))
            {
                throw new InvalidOperationException(insufficientEvidence);
            }

            var (ebook, audio) = ReadLinkAndMonitoringConstraints(source, target);
            var sourceLinks = source.SeriesLinks.Value;
            var targetLinks = target.SeriesLinks.Value.ToLookup(link => link.SeriesId);

            if (dryRun)
            {
                return;
            }

            var ids = new { Source = source.Id, Target = target.Id };
            connection.Execute("UPDATE \"Editions\" SET \"BookId\" = @Target WHERE \"BookId\" = @Source", ids, transaction);
            connection.Execute("UPDATE \"History\" SET \"BookId\" = @Target WHERE \"BookId\" = @Source", ids, transaction);
            connection.Execute("UPDATE \"ExtraFiles\" SET \"BookId\" = @Target WHERE \"BookId\" = @Source", ids, transaction);
            connection.Execute("UPDATE \"MetadataFiles\" SET \"BookId\" = @Target WHERE \"BookId\" = @Source", ids, transaction);
            connection.Execute("UPDATE \"WorkSourceAssociations\" SET \"BookId\" = @Target WHERE \"BookId\" = @Source", ids, transaction);
            connection.Execute("UPDATE \"WorkRouteAliases\" SET \"BookId\" = @Target WHERE \"BookId\" = @Source", ids, transaction);
            PreserveRoute(connection, transaction, target.Id, source.CanonicalId);
            PreserveRoute(connection, transaction, target.Id, source.TitleSlug);
            PreserveRoute(connection, transaction, target.Id, source.ForeignBookId);
            MoveSeriesLinks(connection, transaction, sourceLinks, targetLinks, target.Id);

            // File rows reference edition IDs, which stay stable during this operation.
            var related = (target.RelatedBooks ?? new List<int>()).Concat(source.RelatedBooks ?? new List<int>())
                .Where(id => id != source.Id && id != target.Id).Distinct();
            connection.Execute(
                """
                UPDATE "Books" SET "Monitored" = @Monitored, "EbookMonitoringOverride" = @Ebook,
                    "AudiobookMonitoringOverride" = @Audio, "RelatedBooks" = @Related,
                    "OpenLibraryWorkId" = @OpenLibrary
                WHERE "Id" = @Target
                """,
                new
                {
                    Target = target.Id,
                    Monitored = source.Monitored || target.Monitored,
                    Ebook = ebook,
                    Audio = audio,
                    Related = JsonConvert.SerializeObject(related),
                    OpenLibrary = target.OpenLibraryWorkId ?? source.OpenLibraryWorkId
                },
                transaction);
            RewriteBookReferences(connection, transaction, source.Id, target.Id);
            BookRepository.TransferAuthorCredits(connection, transaction, source.Id, target.Id, transferWork: true);
            connection.Execute("DELETE FROM \"Books\" WHERE \"Id\" = @Source", ids, transaction);
        }

        private static bool HasConflictingOpenLibraryWorkIds(IDbConnection connection, IDbTransaction transaction, Book source, Book target)
        {
            const string provider = "OpenLibrary";
            var stored = connection.Query<string>(
                """
                SELECT "ProviderId" FROM "WorkSourceAssociations"
                WHERE "Provider" = @Provider AND ("BookId" = @Source OR "BookId" = @Target)
                """,
                new { Provider = provider, Source = source.Id, Target = target.Id },
                transaction);

            return stored.Select(id => CatalogueIdentityRepository.NormalizeSource(CatalogueEntityKind.Work, provider, id, null)?.ProviderId)
                .Append(BookWorkIdentity.GetOpenLibraryWorkId(source))
                .Append(BookWorkIdentity.GetOpenLibraryWorkId(target))
                .Where(id => id != null)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(2)
                .Count() == 2;
        }

        internal static Book ReadWork(IDbConnection connection, IDbTransaction transaction, int id)
        {
            var book = connection.QuerySingleOrDefault<Book>("SELECT * FROM \"Books\" WHERE \"Id\" = @Id", new { Id = id }, transaction);
            if (book == null)
            {
                return null;
            }

            book.Editions = connection.Query<Edition>("SELECT * FROM \"Editions\" WHERE \"BookId\" = @Id", new { Id = id }, transaction).ToList();
            book.SeriesLinks = connection.Query<SeriesBookLink, Series, SeriesBookLink>(
                """
                SELECT l.*, s.* FROM "SeriesBookLink" l
                JOIN "Series" s ON s."Id" = l."SeriesId"
                WHERE l."BookId" = @Id
                """,
                (link, series) =>
                {
                    link.Series = series;
                    return link;
                },
                new { Id = id },
                transaction,
                splitOn: "Id").ToList();
            return book;
        }

        private static (bool? Ebook, bool? Audio) ReadLinkAndMonitoringConstraints(Book source, Book target)
        {
            var ebook = CombineMonitoring(source, target, FormatType.Ebook);
            var audio = CombineMonitoring(source, target, FormatType.Audiobook);
            var targetLinks = target.SeriesLinks.Value.ToLookup(link => link.SeriesId);
            if (source.SeriesLinks.Value.Any(link => targetLinks[link.SeriesId].Any(other => PositionsConflict(link, other))))
            {
                throw new InvalidOperationException("The selected works have conflicting series positions.");
            }

            return (ebook, audio);
        }

        private static bool? CombineMonitoring(Book source, Book target, FormatType format)
        {
            var original = format == FormatType.Ebook ? source.EbookMonitoringOverride : source.AudiobookMonitoringOverride;
            var retained = format == FormatType.Ebook ? target.EbookMonitoringOverride : target.AudiobookMonitoringOverride;
            if (original.HasValue && retained.HasValue && original != retained)
            {
                throw new InvalidOperationException("The selected works have conflicting format monitoring overrides.");
            }

            if (source.IsFormatMonitored(format) || target.IsFormatMonitored(format))
            {
                return retained == true || original == true ? true : null;
            }

            return source.Monitored || target.Monitored ? false : retained ?? original;
        }

        private static bool PositionsConflict(SeriesBookLink left, SeriesBookLink right)
        {
            return !string.IsNullOrWhiteSpace(left.Position) && !string.IsNullOrWhiteSpace(right.Position) &&
                   !string.Equals(left.Position, right.Position, StringComparison.Ordinal);
        }

        private static void MoveSeriesLinks(IDbConnection connection, IDbTransaction transaction, List<SeriesBookLink> source, ILookup<int, SeriesBookLink> target, int targetId)
        {
            foreach (var link in source)
            {
                var retained = target[link.SeriesId].FirstOrDefault();
                if (retained == null)
                {
                    connection.Execute("UPDATE \"SeriesBookLink\" SET \"BookId\" = @Target WHERE \"Id\" = @Id", new { Target = targetId, link.Id }, transaction);
                    continue;
                }

                var useSourcePosition = string.IsNullOrWhiteSpace(retained.Position);
                connection.Execute(
                    "UPDATE \"SeriesBookLink\" SET \"Position\" = @Position, \"SeriesPosition\" = @Number, \"IsPrimary\" = @Primary WHERE \"Id\" = @Id",
                    new
                    {
                        retained.Id,
                        Position = useSourcePosition ? link.Position : retained.Position,
                        Number = useSourcePosition ? link.SeriesPosition : retained.SeriesPosition,
                        Primary = retained.IsPrimary || link.IsPrimary
                    },
                    transaction);
                connection.Execute("DELETE FROM \"SeriesBookLink\" WHERE \"Id\" = @Id", new { link.Id }, transaction);
            }
        }

        private static void PreserveRoute(IDbConnection connection, IDbTransaction transaction, int target, string alias)
        {
            if (string.IsNullOrWhiteSpace(alias))
            {
                return;
            }

            connection.Execute(
                "INSERT INTO \"WorkRouteAliases\" (\"BookId\", \"Alias\") VALUES (@BookId, @Alias) ON CONFLICT (\"Alias\") DO NOTHING",
                new { BookId = target, Alias = alias },
                transaction);
            var owner = connection.QuerySingle<int>("SELECT \"BookId\" FROM \"WorkRouteAliases\" WHERE \"Alias\" = @Alias", new { Alias = alias }, transaction);
            if (owner != target)
            {
                throw new InvalidOperationException("A legacy route is associated with another work.");
            }
        }

        private static void RewriteBookReferences(IDbConnection connection, IDbTransaction transaction, int source, int target)
        {
            foreach (var (table, column) in new[] { ("Books", "RelatedBooks"), ("Blocklist", "BookIds") })
            {
                var rows = connection.Query<(int Id, string Value)>(
                    $"SELECT \"Id\", \"{column}\" FROM \"{table}\" WHERE \"{column}\" LIKE @Pattern",
                    new { Pattern = $"%{source}%" },
                    transaction);
                foreach (var row in rows)
                {
                    var references = JsonConvert.DeserializeObject<List<int>>(row.Value);
                    if (references?.Contains(source) != true)
                    {
                        continue;
                    }

                    var updated = references.Select(id => id == source ? target : id)
                        .Where(id => table != "Books" || id != row.Id).Distinct();
                    connection.Execute($"UPDATE \"{table}\" SET \"{column}\" = @Value WHERE \"Id\" = @Id",
                        new { row.Id, Value = JsonConvert.SerializeObject(updated) },
                        transaction);
                }
            }
        }

        private sealed class Bridge
        {
            public Bridge(BridgeIdentity identity, FileWitness ebook, FileWitness audiobook)
            {
                Identity = identity;
                Ebook = ebook;
                Audiobook = audiobook;
            }

            public BridgeIdentity Identity { get; }
            public FileWitness Ebook { get; }
            public FileWitness Audiobook { get; }

            public bool Matches(Bridge other)
            {
                return other != null && Identity == other.Identity && Ebook == other.Ebook && Audiobook == other.Audiobook;
            }
        }
    }
}
