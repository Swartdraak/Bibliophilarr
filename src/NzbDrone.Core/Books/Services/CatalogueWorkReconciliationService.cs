using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using Dapper;
using Newtonsoft.Json;
using NLog;
using NzbDrone.Core.Books.Commands;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Messaging.Commands;

namespace NzbDrone.Core.Books
{
    public class CatalogueWorkReconciliationService : IExecute<ReconcileCatalogueWorksCommand>
    {
        private readonly IMainDatabase _database;
        private readonly Logger _logger;

        public CatalogueWorkReconciliationService(IMainDatabase database, Logger logger)
        {
            _database = database;
            _logger = logger;
        }

        public void Execute(ReconcileCatalogueWorksCommand command)
        {
            using var connection = _database.OpenConnection();
            using var transaction = connection.BeginTransaction(IsolationLevel.Serializable);
            var source = ReadWork(connection, transaction, command.SourceBookId);
            var target = ReadWork(connection, transaction, command.TargetBookId);

            if (source == null && target != null && command.SourceBookId > 0)
            {
                _logger.Info("Source work is already absent. No catalogue changes were made.");
                return;
            }

            Reconcile(connection, transaction, source, target, command.DryRun);
            if (command.DryRun)
            {
                _logger.Info("Works {0} and {1} have corroborated identity. The source has {2} editions to transfer.", source.Id, target.Id, source.Editions.Value.Count);
                return;
            }

            transaction.Commit();
            _logger.Info("Reconciled work {0} into {1}, retaining both formats and existing edition and file IDs.", source.Id, target.Id);
        }

        internal static void Reconcile(IDbConnection connection, IDbTransaction transaction, Book source, Book target, bool dryRun)
        {
            if (source == null || target == null || source.Id == target.Id ||
                !BookWorkIdentity.AreCorroborated(source, target))
            {
                throw new InvalidOperationException("The selected works lack corroborated identity under the same author.");
            }

            var ebook = CombineMonitoring(source, target, FormatType.Ebook);
            var audio = CombineMonitoring(source, target, FormatType.Audiobook);
            var sourceLinks = source.SeriesLinks.Value;
            var targetLinks = target.SeriesLinks.Value.ToLookup(link => link.SeriesId);
            if (sourceLinks.Any(link => targetLinks[link.SeriesId].Any(other => PositionsConflict(link, other))))
            {
                throw new InvalidOperationException("The selected works have conflicting series positions.");
            }

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
            connection.Execute("DELETE FROM \"Books\" WHERE \"Id\" = @Source", ids, transaction);
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
    }
}
