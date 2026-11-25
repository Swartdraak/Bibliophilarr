using System.Collections.Generic;
using System.Data;
using System.Linq;
using Dapper;
using NLog;
using NzbDrone.Common.EnsureThat;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.Books
{
    public interface IEditionRepository : IBasicRepository<Edition>
    {
        List<Edition> GetAllMonitoredEditions();
        bool TryInsertForFreshBook(IDbConnection connection, IDbTransaction transaction, Edition edition);
        Edition FindByForeignEditionId(string foreignEditionId);
        List<Edition> FindByBook(IEnumerable<int> ids);
        List<Edition> FindByAuthor(int id);
        List<Edition> FindByAuthorMetadataId(int id, bool onlyMonitored);
        Edition FindByTitle(int authorMetadataId, string title);
        List<Edition> GetEditionsForRefresh(int bookId, List<string> foreignEditionIds);
        List<Edition> SetMonitored(Edition edition);
        List<Edition> SetMonitoredByFormat(Edition edition);
    }

    public class EditionRepository : BasicRepository<Edition>, IEditionRepository
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
        private readonly string _claimEditionSql;

        public EditionRepository(IMainDatabase database, IEventAggregator eventAggregator)
            : base(database, eventAggregator)
        {
            _claimEditionSql = GetInsertSql(nameof(Edition.ForeignEditionId));
        }

        public bool TryInsertForFreshBook(IDbConnection connection, IDbTransaction transaction, Edition edition)
        {
            var id = connection.QuerySingleOrDefault<int>(_claimEditionSql, edition, transaction);
            if (id == 0)
            {
                return false;
            }

            edition.Id = id;
            return true;
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
