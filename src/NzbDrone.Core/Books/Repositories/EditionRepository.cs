using System.Collections.Generic;
using System.Linq;
using NLog;
using NzbDrone.Common.EnsureThat;
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
            var sameFormat = allEditions.Where(e => e.IsEbook == edition.IsEbook).ToList();

            sameFormat.ForEach(r => r.Monitored = r.Id == edition.Id);

            // Defensive: if no same-format edition ended up monitored (the imported edition's
            // IsEbook does not match the stored editions, or its id is not present in the book's
            // editions), do NOT throw. Throwing here aborts the entire RescanFolders batch and
            // leaves every remaining file un-imported (see issue #203). Log a diagnostic and
            // fall back to the format-agnostic monitored update.
            if (sameFormat.Count(x => x.Monitored) != 1)
            {
                Logger.Warn(
                    "SetMonitoredByFormat: no same-format edition matched for book {0} (edition {1}, IsEbook {2}); " +
                    "found {3} same-format edition(s). Falling back to SetMonitored.",
                    edition.BookId,
                    edition.Id,
                    edition.IsEbook,
                    sameFormat.Count);

                // The format-agnostic fallback can also fail to match (edition id not present in
                // the book's editions). Guard against that so we never throw here.
                if (allEditions.Any(x => x.Id == edition.Id))
                {
                    return SetMonitored(edition);
                }

                Logger.Warn(
                    "SetMonitoredByFormat: edition {0} is not present in book {1}; no monitored update applied.",
                    edition.Id,
                    edition.BookId);

                return allEditions;
            }

            UpdateMany(sameFormat);
            return allEditions;
        }
    }
}
