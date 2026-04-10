using System;
using System.Collections.Generic;
using System.Linq;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Qualities;

namespace NzbDrone.Core.Books
{
    public interface IBookRepository : IBasicRepository<Book>
    {
        List<Book> GetBooks(int authorId);
        List<Book> GetLastBooks(IEnumerable<int> authorMetadataIds);
        List<Book> GetNextBooks(IEnumerable<int> authorMetadataIds);
        List<Book> GetBooksByAuthorMetadataId(int authorMetadataId);
        List<Book> GetBooksForRefresh(int authorMetadataId, List<string> foreignIds);
        List<Book> GetBooksByFileIds(IEnumerable<int> fileIds);
        Book FindByTitle(int authorMetadataId, string title);
        Book FindById(string foreignBookId);
        Book FindBySlug(string titleSlug);
        PagingSpec<Book> BooksWithoutFiles(PagingSpec<Book> pagingSpec);
        PagingSpec<Book> BooksWithoutFiles(PagingSpec<Book> pagingSpec, FormatType? formatType);
        PagingSpec<Book> BooksWithoutFiles(PagingSpec<Book> pagingSpec, FormatType? formatType, bool dualFormatEnabled);
        PagingSpec<Book> BooksWhereCutoffUnmet(PagingSpec<Book> pagingSpec, List<QualitiesBelowCutoff> qualitiesBelowCutoff);
        PagingSpec<Book> BooksWhereCutoffUnmet(PagingSpec<Book> pagingSpec, List<QualitiesBelowCutoff> qualitiesBelowCutoff, FormatType? formatType);
        List<Book> BooksBetweenDates(DateTime startDate, DateTime endDate, bool includeUnmonitored);
        List<Book> AuthorBooksBetweenDates(Author author, DateTime startDate, DateTime endDate, bool includeUnmonitored);
        void SetMonitoredFlat(Book book, bool monitored);
        void SetMonitored(IEnumerable<int> ids, bool monitored);
        List<Book> GetAuthorBooksWithFiles(Author author);
    }

    public class BookRepository : BasicRepository<Book>, IBookRepository
    {
        public BookRepository(IMainDatabase database, IEventAggregator eventAggregator)
            : base(database, eventAggregator)
        {
        }

        public List<Book> GetBooks(int authorId)
        {
            return Query(Builder().Join<Book, Author>((l, r) => l.AuthorMetadataId == r.AuthorMetadataId).Where<Author>(a => a.Id == authorId));
        }

        public List<Book> GetLastBooks(IEnumerable<int> authorMetadataIds)
        {
            var now = DateTime.UtcNow;

            var inner = Builder()
                .Select("MIN(\"Books\".\"Id\") as id, MAX(\"Books\".\"ReleaseDate\") as date")
                .Where<Book>(x => authorMetadataIds.Contains(x.AuthorMetadataId) && x.ReleaseDate < now)
                .GroupBy<Book>(x => x.AuthorMetadataId)
                .AddSelectTemplate(typeof(Book));

            var outer = Builder()
                .Join($"({inner.RawSql}) ids on ids.id = \"Books\".\"Id\" and ids.date = \"Books\".\"ReleaseDate\"")
                .AddParameters(inner.Parameters);

            return Query(outer);
        }

        public List<Book> GetNextBooks(IEnumerable<int> authorMetadataIds)
        {
            var now = DateTime.UtcNow;

            var inner = Builder()
                .Select("MIN(\"Books\".\"Id\") as id, MIN(\"Books\".\"ReleaseDate\") as date")
                .Where<Book>(x => authorMetadataIds.Contains(x.AuthorMetadataId) && x.ReleaseDate > now)
                .GroupBy<Book>(x => x.AuthorMetadataId)
                .AddSelectTemplate(typeof(Book));

            var outer = Builder()
                .Join($"({inner.RawSql}) ids on ids.id = \"Books\".\"Id\" and ids.date = \"Books\".\"ReleaseDate\"")
                .AddParameters(inner.Parameters);

            return Query(outer);
        }

        public List<Book> GetBooksByAuthorMetadataId(int authorMetadataId)
        {
            return Query(s => s.AuthorMetadataId == authorMetadataId);
        }

        public List<Book> GetBooksForRefresh(int authorMetadataId, List<string> foreignIds)
        {
            return Query(a => a.AuthorMetadataId == authorMetadataId || foreignIds.Contains(a.ForeignBookId));
        }

        public List<Book> GetBooksByFileIds(IEnumerable<int> fileIds)
        {
            return Query(new SqlBuilder(_database.DatabaseType)
                         .Join<Book, Edition>((b, e) => b.Id == e.BookId)
                         .Join<Edition, BookFile>((l, r) => l.Id == r.EditionId)
                         .Where<BookFile>(f => fileIds.Contains(f.Id)))
                .DistinctBy(x => x.Id)
                .ToList();
        }

        public Book FindById(string foreignBookId)
        {
            return Query(s => s.ForeignBookId == foreignBookId).SingleOrDefault();
        }

        public Book FindBySlug(string titleSlug)
        {
            return Query(s => s.TitleSlug == titleSlug).SingleOrDefault();
        }

        //x.Id == null is converted to SQL, so warning incorrect
#pragma warning disable CS0472
        private SqlBuilder BooksWithoutFilesBuilder(DateTime currentTime, FormatType? formatType = null, bool dualFormatEnabled = false)
        {
            // When dual-format tracking is enabled, use quality-based file existence checks
            // instead of edition-level file joins. This correctly handles the case where both
            // ebook and audiobook files are attached to the same edition (IsEbook=false).
            if (dualFormatEnabled)
            {
                return BooksWithoutFilesFormatAwareBuilder(currentTime, formatType);
            }

            var builder = Builder()
                .Join<Book, Author>((l, r) => l.AuthorMetadataId == r.AuthorMetadataId)
                .Join<Author, AuthorMetadata>((l, r) => l.AuthorMetadataId == r.Id)
                .Join<Book, Edition>((b, e) => b.Id == e.BookId)
                .LeftJoin<Edition, BookFile>((t, f) => t.Id == f.EditionId)
                .Where<BookFile>(f => f.Id == null)
                .Where<Edition>(e => e.Monitored == true)
                .Where<Book>(a => a.ReleaseDate <= currentTime);

            if (formatType.HasValue)
            {
                var isEbook = formatType.Value == FormatType.Ebook;
                builder = builder.Where<Edition>(e => e.IsEbook == isEbook);
            }

            return builder;
        }

        private SqlBuilder BooksWithoutFilesFormatAwareBuilder(DateTime currentTime, FormatType? formatType)
        {
            // Quality IDs: Ebook = 0-4 (Unknown, PDF, MOBI, EPUB, AZW3)
            //              Audiobook = 10-13 (MP3, FLAC, M4B, UnknownAudio)
            var ebookQualityLikes = string.Join(" OR ",
                Enumerable.Range(0, 5).Select(q => $"\"BookFiles\".\"Quality\" LIKE '%_quality_: {q},%'"));

            var audiobookQualityLikes = string.Join(" OR ",
                Enumerable.Range(10, 4).Select(q => $"\"BookFiles\".\"Quality\" LIKE '%_quality_: {q},%'"));

            var builder = Builder()
                .Join<Book, Author>((l, r) => l.AuthorMetadataId == r.AuthorMetadataId)
                .Join<Author, AuthorMetadata>((l, r) => l.AuthorMetadataId == r.Id)
                .Where<Book>(a => a.ReleaseDate <= currentTime);

            if (formatType.HasValue)
            {
                // Specific format filter: find books where the author has a monitored format
                // profile for this type AND no BookFiles with matching quality exist.
                var ft = (int)formatType.Value;
                var qualityLikes = formatType.Value == FormatType.Ebook ? ebookQualityLikes : audiobookQualityLikes;

                builder = builder
                    .Where($"EXISTS (SELECT 1 FROM \"AuthorFormatProfiles\" WHERE \"AuthorFormatProfiles\".\"AuthorId\" = \"Authors\".\"Id\" AND \"AuthorFormatProfiles\".\"FormatType\" = {ft} AND \"AuthorFormatProfiles\".\"Monitored\" = 1)")
                    .Where($"NOT EXISTS (SELECT 1 FROM \"BookFiles\" INNER JOIN \"Editions\" ON \"BookFiles\".\"EditionId\" = \"Editions\".\"Id\" WHERE \"Editions\".\"BookId\" = \"Books\".\"Id\" AND ({qualityLikes}))");
            }
            else
            {
                // No format filter: find books missing files for ANY monitored format.
                // A book is included if (author has monitored ebook profile AND no ebook files)
                // OR (author has monitored audiobook profile AND no audiobook files).
                var missingEbook = $"(EXISTS (SELECT 1 FROM \"AuthorFormatProfiles\" WHERE \"AuthorFormatProfiles\".\"AuthorId\" = \"Authors\".\"Id\" AND \"AuthorFormatProfiles\".\"FormatType\" = 0 AND \"AuthorFormatProfiles\".\"Monitored\" = 1) AND NOT EXISTS (SELECT 1 FROM \"BookFiles\" INNER JOIN \"Editions\" ON \"BookFiles\".\"EditionId\" = \"Editions\".\"Id\" WHERE \"Editions\".\"BookId\" = \"Books\".\"Id\" AND ({ebookQualityLikes})))";
                var missingAudiobook = $"(EXISTS (SELECT 1 FROM \"AuthorFormatProfiles\" WHERE \"AuthorFormatProfiles\".\"AuthorId\" = \"Authors\".\"Id\" AND \"AuthorFormatProfiles\".\"FormatType\" = 1 AND \"AuthorFormatProfiles\".\"Monitored\" = 1) AND NOT EXISTS (SELECT 1 FROM \"BookFiles\" INNER JOIN \"Editions\" ON \"BookFiles\".\"EditionId\" = \"Editions\".\"Id\" WHERE \"Editions\".\"BookId\" = \"Books\".\"Id\" AND ({audiobookQualityLikes})))";

                builder = builder.Where($"({missingEbook} OR {missingAudiobook})");
            }

            return builder;
        }
#pragma warning restore CS0472

        public PagingSpec<Book> BooksWithoutFiles(PagingSpec<Book> pagingSpec)
        {
            return BooksWithoutFiles(pagingSpec, null, false);
        }

        public PagingSpec<Book> BooksWithoutFiles(PagingSpec<Book> pagingSpec, FormatType? formatType)
        {
            return BooksWithoutFiles(pagingSpec, formatType, false);
        }

        public PagingSpec<Book> BooksWithoutFiles(PagingSpec<Book> pagingSpec, FormatType? formatType, bool dualFormatEnabled)
        {
            var currentTime = DateTime.UtcNow;

            pagingSpec.Records = GetPagedRecords(BooksWithoutFilesBuilder(currentTime, formatType, dualFormatEnabled), pagingSpec, PagedQuery);
            pagingSpec.TotalRecords = GetPagedRecordCount(BooksWithoutFilesBuilder(currentTime, formatType, dualFormatEnabled).SelectCountDistinct<Book>(x => x.Id), pagingSpec);

            return pagingSpec;
        }

        private SqlBuilder BooksWhereCutoffUnmetBuilder(List<QualitiesBelowCutoff> qualitiesBelowCutoff, FormatType? formatType = null)
        {
            var builder = Builder()
                .Join<Book, Author>((l, r) => l.AuthorMetadataId == r.AuthorMetadataId)
                .Join<Author, AuthorMetadata>((l, r) => l.AuthorMetadataId == r.Id)
                .Join<Book, Edition>((b, e) => b.Id == e.BookId)
                .LeftJoin<Edition, BookFile>((t, f) => t.Id == f.EditionId)
                .Where<Edition>(e => e.Monitored == true)
                .Where(BuildQualityCutoffWhereClause(qualitiesBelowCutoff));

            if (formatType.HasValue)
            {
                var isEbook = formatType.Value == FormatType.Ebook;
                builder = builder.Where<Edition>(e => e.IsEbook == isEbook);
            }

            return builder;
        }

        private string BuildQualityCutoffWhereClause(List<QualitiesBelowCutoff> qualitiesBelowCutoff)
        {
            var clauses = new List<string>();

            foreach (var profile in qualitiesBelowCutoff)
            {
                foreach (var belowCutoff in profile.QualityIds)
                {
                    clauses.Add(string.Format("(\"Authors\".\"QualityProfileId\" = {0} AND \"BookFiles\".\"Quality\" LIKE '%_quality_: {1},%')", profile.ProfileId, belowCutoff));
                }
            }

            return string.Format("({0})", string.Join(" OR ", clauses));
        }

        public PagingSpec<Book> BooksWhereCutoffUnmet(PagingSpec<Book> pagingSpec, List<QualitiesBelowCutoff> qualitiesBelowCutoff)
        {
            return BooksWhereCutoffUnmet(pagingSpec, qualitiesBelowCutoff, null);
        }

        public PagingSpec<Book> BooksWhereCutoffUnmet(PagingSpec<Book> pagingSpec, List<QualitiesBelowCutoff> qualitiesBelowCutoff, FormatType? formatType)
        {
            pagingSpec.Records = GetPagedRecords(BooksWhereCutoffUnmetBuilder(qualitiesBelowCutoff, formatType), pagingSpec, PagedQuery);

            var countTemplate = $"SELECT COUNT(*) FROM (SELECT /**select**/ FROM \"{TableMapping.Mapper.TableNameMapping(typeof(Book))}\" /**join**/ /**innerjoin**/ /**leftjoin**/ /**where**/ /**groupby**/ /**having**/) AS \"Inner\"";
            pagingSpec.TotalRecords = GetPagedRecordCount(BooksWhereCutoffUnmetBuilder(qualitiesBelowCutoff, formatType).Select(typeof(Book)), pagingSpec, countTemplate);

            return pagingSpec;
        }

        public List<Book> BooksBetweenDates(DateTime startDate, DateTime endDate, bool includeUnmonitored)
        {
            var builder = Builder().Where<Book>(rg => rg.ReleaseDate >= startDate && rg.ReleaseDate <= endDate);

            if (!includeUnmonitored)
            {
                builder = builder.Where<Book>(e => e.Monitored == true)
                    .Join<Book, Author>((l, r) => l.AuthorMetadataId == r.AuthorMetadataId)
                    .Where<Author>(e => e.Monitored == true);
            }

            return Query(builder);
        }

        public List<Book> AuthorBooksBetweenDates(Author author, DateTime startDate, DateTime endDate, bool includeUnmonitored)
        {
            var builder = Builder().Where<Book>(rg => rg.ReleaseDate >= startDate &&
                                                 rg.ReleaseDate <= endDate &&
                                                 rg.AuthorMetadataId == author.AuthorMetadataId);

            if (!includeUnmonitored)
            {
                builder = builder.Where<Book>(e => e.Monitored == true)
                    .Join<Book, Author>((l, r) => l.AuthorMetadataId == r.AuthorMetadataId)
                    .Where<Author>(e => e.Monitored == true);
            }

            return Query(builder);
        }

        public void SetMonitoredFlat(Book book, bool monitored)
        {
            book.Monitored = monitored;
            SetFields(book, p => p.Monitored);

            ModelUpdated(book, true);
        }

        public void SetMonitored(IEnumerable<int> ids, bool monitored)
        {
            var books = ids.Select(x => new Book { Id = x, Monitored = monitored }).ToList();
            SetFields(books, p => p.Monitored);
        }

        public Book FindByTitle(int authorMetadataId, string title)
        {
            var cleanTitle = Parser.Parser.CleanAuthorName(title);

            if (string.IsNullOrEmpty(cleanTitle))
            {
                cleanTitle = title;
            }

            return Query(s => (s.CleanTitle == cleanTitle || s.Title == title) && s.AuthorMetadataId == authorMetadataId)
                .ExclusiveOrDefault();
        }

        public List<Book> GetAuthorBooksWithFiles(Author author)
        {
            return Query(Builder()
                         .Join<Book, Edition>((b, e) => b.Id == e.BookId)
                         .Join<Edition, BookFile>((t, f) => t.Id == f.EditionId)
                         .Where<Book>(x => x.AuthorMetadataId == author.AuthorMetadataId)
                         .Where<Edition>(e => e.Monitored == true));
        }
    }
}
