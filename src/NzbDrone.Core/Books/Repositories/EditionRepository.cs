using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using Dapper;
using FluentValidation;
using FluentValidation.Results;
using NLog;
using NzbDrone.Common.EnsureThat;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.Books
{
    public interface IEditionRepository : IBasicRepository<Edition>
    {
        List<Edition> GetAllMonitoredEditions();
        Edition FindByForeignEditionId(string foreignEditionId);
        List<Edition> FindByBook(IEnumerable<int> ids);
        List<Edition> FindByAuthor(int id);
        List<Edition> FindByAuthorMetadataId(int id, bool onlyMonitored);
        Edition FindByTitle(int authorMetadataId, string title);
        List<Edition> GetEditionsForRefresh(int bookId, List<string> foreignEditionIds);
        List<Edition> AddEditionsForExistingBook(Book book, Edition selected, bool dualFormat);
        List<Edition> SetMonitored(Edition edition);
        List<Edition> SetMonitoredByFormat(Edition edition);
    }

    public class EditionRepository : BasicRepository<Edition>, IEditionRepository
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        public EditionRepository(IMainDatabase database, IEventAggregator eventAggregator)
            : base(database, eventAggregator)
        {
        }

        public List<Edition> GetAllMonitoredEditions()
        {
            return Query(x => x.Monitored == true);
        }

        public Edition FindByForeignEditionId(string foreignEditionId)
        {
            var edition = Query(x => x.ForeignEditionId == foreignEditionId).SingleOrDefault();

            return edition;
        }

        public List<Edition> GetEditionsForRefresh(int bookId, List<string> foreignEditionIds)
        {
            return Query(r => r.BookId == bookId || foreignEditionIds.Contains(r.ForeignEditionId));
        }

        public List<Edition> FindByBook(IEnumerable<int> ids)
        {
            // populate the books and author metadata also
            // this hopefully speeds up the track matching a lot
            var builder = new SqlBuilder(_database.DatabaseType)
                .LeftJoin<Edition, Book>((e, b) => e.BookId == b.Id)
                .LeftJoin<Book, AuthorMetadata>((b, a) => b.AuthorMetadataId == a.Id)
                .Where<Edition>(r => ids.Contains(r.BookId));

            return _database.QueryJoined<Edition, Book, AuthorMetadata>(builder, (edition, book, metadata) =>
                    {
                        if (book != null)
                        {
                            book.AuthorMetadata = metadata;
                            edition.Book = book;
                        }

                        return edition;
                    }).ToList();
        }

        public List<Edition> FindByAuthor(int id)
        {
            return Query(Builder().Join<Edition, Book>((e, b) => e.BookId == b.Id)
                         .Join<Book, Author>((b, a) => b.AuthorMetadataId == a.AuthorMetadataId)
                         .Where<Author>(a => a.Id == id));
        }

        public List<Edition> FindByAuthorMetadataId(int authorMetadataId, bool onlyMonitored)
        {
            var builder = Builder().Join<Edition, Book>((e, b) => e.BookId == b.Id)
                .Where<Book>(b => b.AuthorMetadataId == authorMetadataId);

            if (onlyMonitored)
            {
                builder = builder.OrWhere<Edition>(e => e.Monitored == true);
                builder = builder.OrWhere<Book>(b => b.AnyEditionOk == true);
            }

            return Query(builder);
        }

        public Edition FindByTitle(int authorMetadataId, string title)
        {
            return Query(Builder().Join<Edition, Book>((e, b) => e.BookId == b.Id)
                .Where<Book>(b => b.AuthorMetadataId == authorMetadataId)
                .Where<Edition>(e => e.Monitored == true)
                .Where<Edition>(e => e.Title == title))
                .FirstOrDefault();
        }

        public List<Edition> AddEditionsForExistingBook(Book book, Edition selected, bool dualFormat)
        {
            using var connection = _database.OpenConnection();
            using var transaction = connection.BeginTransaction(IsolationLevel.Serializable);
            var lockRow = _database.DatabaseType == DatabaseType.PostgreSQL ? " FOR UPDATE" : string.Empty;
            var storedBook = connection.QuerySingleOrDefault<Book>(
                """SELECT "Id", "AuthorMetadataId", "ForeignBookId", "AddOptions" FROM "Books" WHERE "Id" = @Id""" + lockRow,
                new { book.Id },
                transaction);
            if (storedBook == null || storedBook.AuthorMetadataId != book.AuthorMetadataId || storedBook.ForeignBookId != book.ForeignBookId)
            {
                throw BookEditionValidationError("The selected work changed before its editions could be added.");
            }

            var incoming = book.Editions.Value.Where(edition => edition.ForeignEditionId.IsNotNullOrWhiteSpace())
                .DistinctBy(edition => edition.ForeignEditionId, StringComparer.Ordinal).ToList();
            if (!incoming.Any(edition => edition.ForeignEditionId == selected.ForeignEditionId))
            {
                throw BookEditionValidationError("The selected edition does not have a source identity.");
            }

            var foreignIds = incoming.Select(edition => edition.ForeignEditionId).ToArray();
            var foreignIdPredicate = _database.DatabaseType == DatabaseType.PostgreSQL
                ? "\"ForeignEditionId\" = ANY(@foreignIds)"
                : "\"ForeignEditionId\" IN @foreignIds";
            var saved = connection.Query<Edition>(
                """SELECT * FROM "Editions" WHERE "BookId" = @Id OR """ + $" {foreignIdPredicate}",
                new { book.Id, foreignIds },
                transaction).ToList();

            if (saved.Any(edition => edition.BookId != book.Id))
            {
                throw BookEditionValidationError("A provider edition belongs to a different work.");
            }

            var byForeignId = saved.Where(edition => edition.ForeignEditionId.IsNotNullOrWhiteSpace())
                .ToDictionary(edition => edition.ForeignEditionId, StringComparer.Ordinal);
            if (byForeignId.TryGetValue(selected.ForeignEditionId, out var selectedEdition) &&
                (selectedEdition.IsEbook != selected.IsEbook || selectedEdition.HasUntrackedFormat() != selected.HasUntrackedFormat()))
            {
                throw BookEditionValidationError("The selected edition has a different format in the catalogue.");
            }

            foreach (var edition in incoming.Where(edition => !byForeignId.ContainsKey(edition.ForeignEditionId)))
            {
                edition.Id = 0;
                edition.BookId = book.Id;
                edition.ManualAdd = edition.ForeignEditionId == selected.ForeignEditionId;
                edition.Monitored = edition.ManualAdd;
                Insert(connection, transaction, edition);
                byForeignId.Add(edition.ForeignEditionId, edition);
                saved.Add(edition);
            }

            selectedEdition = byForeignId[selected.ForeignEditionId];
            var format = selectedEdition.IsEbook ? FormatType.Ebook : FormatType.Audiobook;
            var changed = new List<Edition>();
            foreach (var edition in saved)
            {
                var isSelected = edition.Id == selectedEdition.Id;
                var inScope = !dualFormat || (selectedEdition.HasUntrackedFormat() ? edition.HasUntrackedFormat() : edition.MatchesFormat(format));
                if (!inScope || (edition.Monitored == isSelected && (!isSelected || edition.ManualAdd)))
                {
                    continue;
                }

                edition.Monitored = isSelected;
                edition.ManualAdd |= isSelected;
                changed.Add(edition);
            }

            connection.Execute(
                """UPDATE "Editions" SET "Monitored" = @Monitored, "ManualAdd" = @ManualAdd WHERE "Id" = @Id""",
                changed,
                transaction);

            if (storedBook.AddOptions?.AddType != BookAddType.Manual)
            {
                storedBook.AddOptions ??= new AddBookOptions();
                storedBook.AddOptions.AddType = BookAddType.Manual;
                connection.Execute(
                    """UPDATE "Books" SET "AddOptions" = @AddOptions WHERE "Id" = @Id""",
                    new { book.Id, storedBook.AddOptions },
                    transaction);
            }

            transaction.Commit();
            return saved.OrderByDescending(edition => edition.Id == selectedEdition.Id).ThenBy(edition => edition.Id).ToList();
        }

        private static ValidationException BookEditionValidationError(string message) =>
            new ValidationException(new[] { new ValidationFailure("Editions", message) });

        public List<Edition> SetMonitored(Edition edition)
        {
            var allEditions = FindByBook(new[] { edition.BookId });
            allEditions.ForEach(r => r.Monitored = r.Id == edition.Id);
            Ensure.That(allEditions.Count(x => x.Monitored) == 1).IsTrue();
            UpdateMany(allEditions);
            return allEditions;
        }

        public List<Edition> SetMonitoredByFormat(Edition edition)
        {
            var allEditions = FindByBook(new[] { edition.BookId });
            var storedEdition = allEditions.SingleOrDefault(existing => existing.Id == edition.Id);
            if (storedEdition == null || storedEdition.IsEbook != edition.IsEbook)
            {
                Logger.Warn(
                    "Skipping edition monitoring update for book {0}, edition {1}: imported IsEbook={2}, stored IsEbook={3}.",
                    edition.BookId,
                    edition.Id,
                    edition.IsEbook,
                    storedEdition?.IsEbook.ToString() ?? "missing");
                return allEditions;
            }

            var format = storedEdition.IsEbook ? FormatType.Ebook : FormatType.Audiobook;
            var sameFormat = allEditions.Where(existing =>
                storedEdition.HasUntrackedFormat() ? existing.HasUntrackedFormat() : existing.MatchesFormat(format)).ToList();
            sameFormat.ForEach(existing => existing.Monitored = existing.Id == edition.Id);
            UpdateMany(sameFormat);
            return allEditions;
        }
    }
}
