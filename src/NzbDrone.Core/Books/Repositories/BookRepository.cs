using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Net;
using Dapper;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Exceptions;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Qualities;

namespace NzbDrone.Core.Books
{
    public interface IBookRepository : IBasicRepository<Book>
    {
        void InsertWithEditions(Book book);
        void InsertWithNewAuthor(Book book, Author author, IReadOnlyList<AuthorFormatProfile> profiles);
        void DeleteKeepingFiles(IEnumerable<int> bookIds);
        bool DeleteIfNoLinkedFiles(int bookId);
        List<Book> GetBooks(int authorId);
        List<Book> GetArtworkRetryPage(int afterId, int pageSize);
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
        PagingSpec<Book> BooksWhereCutoffUnmet(PagingSpec<Book> pagingSpec, List<QualitiesBelowCutoff> qualitiesBelowCutoff, FormatType format, bool? monitored, bool dualFormatEnabled);
        List<Book> BooksBetweenDates(DateTime startDate, DateTime endDate, bool includeUnmonitored);
        List<Book> AuthorBooksBetweenDates(Author author, DateTime startDate, DateTime endDate, bool includeUnmonitored);
        void SetMonitoredFlat(Book book, bool monitored);
        void SetMonitored(IEnumerable<int> ids, bool monitored);
        void SetFormatMonitored(IEnumerable<int> ids, FormatType formatType, bool monitored);
        List<Book> GetAuthorBooksWithFiles(Author author);
    }

    public class BookRepository : BasicRepository<Book>, IBookRepository
    {
        private readonly IEditionRepository _editionRepository;
        private readonly BasicRepository<Author> _newAuthors;
        private readonly BasicRepository<AuthorMetadata> _newAuthorMetadata;
        private readonly BasicRepository<AuthorFormatProfile> _newProfiles;

        public BookRepository(IMainDatabase database, IEventAggregator eventAggregator, IEditionRepository editionRepository)
            : base(database, eventAggregator)
        {
            _editionRepository = editionRepository;
            _newAuthors = new BasicRepository<Author>(database, eventAggregator);
            _newAuthorMetadata = new BasicRepository<AuthorMetadata>(database, eventAggregator);
            _newProfiles = new BasicRepository<AuthorFormatProfile>(database, eventAggregator);
        }

        public void InsertWithEditions(Book book) => InsertWithEditions(book, null, Array.Empty<AuthorFormatProfile>());

        public void InsertWithNewAuthor(Book book, Author author, IReadOnlyList<AuthorFormatProfile> profiles)
        {
            InsertWithEditions(book, author, profiles);
        }

        private void InsertWithEditions(Book book, Author author, IReadOnlyList<AuthorFormatProfile> profiles)
        {
            var editions = book.Editions?.Value;
            if (book.Id != 0 || editions == null || editions.Count == 0 || editions.Any(edition => edition is not { Id: 0 }))
            {
                throw new InvalidOperationException("A new work requires a fresh list of editions.");
            }

            var metadata = author?.Metadata?.Value;
            if (author != null && (author.Id != 0 || metadata is not { Id: 0 } ||
                                   profiles.Any(profile => profile.Id != 0 || profile.AuthorId != 0)))
            {
                throw new InvalidOperationException("The work's new author and profiles must have fresh identities.");
            }

            var originalAuthorMetadataId = book.AuthorMetadataId;
            var selected = editions.GetPreferredEdition();
            var original = editions.Select(edition => (Edition: edition, edition.BookId, edition.Monitored)).ToList();
            try
            {
                using var connection = _database.OpenConnection();
                using var transaction = connection.BeginTransaction(IsolationLevel.ReadCommitted);
                if (author == null)
                {
                    Insert(connection, transaction, book);
                }
                else
                {
                    InsertNewAuthor(connection, transaction, author, metadata, profiles);
                    book.AuthorMetadataId = metadata.Id;
                    if (!TryInsertFresh(connection, transaction, book))
                    {
                        throw new NzbDroneClientException(HttpStatusCode.Conflict, "The selected work is already present in the catalogue.");
                    }
                }

                foreach (var edition in editions)
                {
                    edition.BookId = book.Id;
                    edition.Monitored = ReferenceEquals(edition, selected);
                    if (!_editionRepository.TryInsertForFreshBook(connection, transaction, edition))
                    {
                        throw new NzbDroneClientException(HttpStatusCode.Conflict, "A provider edition is already present in the catalogue.");
                    }
                }

                if (author != null)
                {
                    connection.Execute(
                        "DELETE FROM \"ImportListExclusions\" WHERE \"ForeignId\" IN (@WorkId, @AuthorId)",
                        new { WorkId = book.ForeignBookId, AuthorId = metadata.ForeignAuthorId },
                        transaction);
                }

                transaction.Commit();
            }
            catch
            {
                book.Id = 0;
                book.AuthorMetadataId = originalAuthorMetadataId;
                foreach (var (edition, bookId, monitored) in original)
                {
                    edition.Id = 0;
                    edition.BookId = bookId;
                    edition.Monitored = monitored;
                }

                if (author != null)
                {
                    author.Id = 0;
                    author.AuthorMetadataId = 0;
                    metadata.Id = 0;
                    foreach (var profile in profiles)
                    {
                        profile.Id = 0;
                        profile.AuthorId = 0;
                    }
                }

                throw;
            }

            ModelCreated(book);
        }

        private void InsertNewAuthor(IDbConnection connection,
                                     IDbTransaction transaction,
                                     Author author,
                                     AuthorMetadata metadata,
                                     IReadOnlyList<AuthorFormatProfile> profiles)
        {
            if (!_newAuthorMetadata.TryInsertFresh(connection, transaction, metadata))
            {
                // Metadata can outlive its local author. Reuse only its exact provider identity.
                metadata.Id = connection.QuerySingleOrDefault<int>(
                    "SELECT \"Id\" FROM \"AuthorMetadata\" WHERE \"ForeignAuthorId\" = @ForeignAuthorId",
                    new { metadata.ForeignAuthorId },
                    transaction);
                if (metadata.Id == 0)
                {
                    throw new NzbDroneClientException(HttpStatusCode.Conflict, "The selected author metadata conflicts with an existing record.");
                }
            }

            author.AuthorMetadataId = metadata.Id;
            if (!_newAuthors.TryInsertFresh(connection, transaction, author))
            {
                throw new NzbDroneClientException(HttpStatusCode.Conflict, "The selected author is already present in the catalogue.");
            }

            foreach (var profile in profiles)
            {
                profile.AuthorId = author.Id;
                if (!_newProfiles.TryInsertFresh(connection, transaction, profile))
                {
                    throw new InvalidOperationException("The new author's format profile conflicts with an existing row.");
                }
            }
        }

        public bool DeleteIfNoLinkedFiles(int bookId)
        {
            using var connection = _database.OpenConnection();
            return connection.Execute(
                """
                DELETE FROM "Books" WHERE "Id" = @BookId AND NOT EXISTS (
                    SELECT 1 FROM "BookFiles" files
                    INNER JOIN "Editions" editions ON files."EditionId" = editions."Id"
                    WHERE editions."BookId" = @BookId
                )
                """,
                new { BookId = bookId }) == 1;
        }

        public void DeleteKeepingFiles(IEnumerable<int> bookIds)
        {
            var batches = bookIds.Distinct().Chunk(400).ToList();
            if (batches.Count == 0)
            {
                return;
            }

            // Npgsql binds integer arrays directly. Dapper expands lists for SQLite.
            var bookIdsPredicate = _database.DatabaseType == DatabaseType.PostgreSQL ? "= ANY(@BookIds)" : "IN @BookIds";
            using var connection = _database.OpenConnection();
            using var transaction = connection.BeginTransaction(IsolationLevel.ReadCommitted);
            foreach (var ids in batches)
            {
                var parameters = new { BookIds = ids };
                connection.Execute(
                    $"""
                    UPDATE "BookFiles" SET "EditionId" = 0
                    WHERE "EditionId" IN (SELECT "Id" FROM "Editions" WHERE "BookId" {bookIdsPredicate})
                    """,
                    parameters,
                    transaction);
                connection.Execute($"DELETE FROM \"Books\" WHERE \"Id\" {bookIdsPredicate}", parameters, transaction);
            }

            transaction.Commit();
        }

        public List<Book> GetArtworkRetryPage(int afterId, int pageSize)
        {
            var page = new PagingSpec<Book>
            {
                Page = 1,
                PageSize = Math.Clamp(pageSize, 1, 128),
                SortKey = "Books.Id",
                SortDirection = SortDirection.Ascending
            };
            page.FilterExpressions.Add(book => book.Id > afterId);
            return GetPagedRecords(Builder(), page, PagedQuery);
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
                builder = builder.Where<Edition>(e => e.IsEbook == isEbook &&
                    (isEbook || e.Format == null || (e.Format != Edition.PrintFormat && e.Format != Edition.UnclassifiedFormat)));
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

            string MissingFormat(FormatType format, string qualityLikes)
            {
                var preference = format == FormatType.Ebook ? nameof(Book.EbookMonitoringOverride) : nameof(Book.AudiobookMonitoringOverride);
                return $"(COALESCE(\"Books\".\"{preference}\", TRUE) AND EXISTS (SELECT 1 FROM \"AuthorFormatProfiles\" WHERE \"AuthorFormatProfiles\".\"AuthorId\" = \"Authors\".\"Id\" AND \"AuthorFormatProfiles\".\"FormatType\" = {(int)format} AND \"AuthorFormatProfiles\".\"Monitored\" = TRUE) AND NOT EXISTS (SELECT 1 FROM \"BookFiles\" INNER JOIN \"Editions\" ON \"BookFiles\".\"EditionId\" = \"Editions\".\"Id\" WHERE \"Editions\".\"BookId\" = \"Books\".\"Id\" AND ({qualityLikes})))";
            }

            var missingEbook = MissingFormat(FormatType.Ebook, ebookQualityLikes);
            var missingAudiobook = MissingFormat(FormatType.Audiobook, audiobookQualityLikes);

            return formatType switch
            {
                FormatType.Ebook => builder.Where(missingEbook),
                FormatType.Audiobook => builder.Where(missingAudiobook),
                _ => builder.Where($"({missingEbook} OR {missingAudiobook})")
            };
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

        private SqlBuilder BooksWhereCutoffUnmetBuilder(List<QualitiesBelowCutoff> qualitiesBelowCutoff, FormatType? formatType)
        {
            var builder = Builder()
                .Join<Book, Author>((book, author) => book.AuthorMetadataId == author.AuthorMetadataId)
                .Join<Author, AuthorMetadata>((author, metadata) => author.AuthorMetadataId == metadata.Id)
                .Join<Book, Edition>((book, edition) => book.Id == edition.BookId)
                .LeftJoin<Edition, BookFile>((edition, file) => edition.Id == file.EditionId)
                .Where<Edition>(edition => edition.Monitored == true)
                .Where(BuildQualityCutoffWhereClause(qualitiesBelowCutoff, """ "Authors"."QualityProfileId" """));

            if (formatType.HasValue)
            {
                var isEbook = formatType.Value == FormatType.Ebook;
                builder.Where<Edition>(edition => edition.IsEbook == isEbook &&
                    (isEbook || edition.Format == null || (edition.Format != Edition.PrintFormat && edition.Format != Edition.UnclassifiedFormat)));
            }

            return builder;
        }

        private SqlBuilder ScopedCutoffBuilder(List<QualitiesBelowCutoff> qualitiesBelowCutoff, FormatType format, bool? monitored, bool dualFormatEnabled)
        {
            var builder = Builder()
                .Join<Book, Author>((book, author) => book.AuthorMetadataId == author.AuthorMetadataId)
                .Join<Author, AuthorMetadata>((author, metadata) => author.AuthorMetadataId == metadata.Id);
            var profileColumn = """ "Authors"."QualityProfileId" """;
            var isMonitored = """ "Books"."Monitored" AND "Authors"."Monitored" """;
            var editionCondition = format == FormatType.Ebook
                ? """ "Editions"."Monitored" = TRUE AND "Editions"."IsEbook" = TRUE """
                : $""" "Editions"."Monitored" = TRUE AND "Editions"."IsEbook" = FALSE AND ("Editions"."Format" IS NULL OR "Editions"."Format" NOT IN ('{Edition.PrintFormat}', '{Edition.UnclassifiedFormat}')) """;

            if (dualFormatEnabled)
            {
                builder.Join<Author, AuthorFormatProfile>((author, profile) => author.Id == profile.AuthorId)
                    .Where<AuthorFormatProfile>(profile => profile.FormatType == format);

                var preference = format == FormatType.Ebook ? nameof(Book.EbookMonitoringOverride) : nameof(Book.AudiobookMonitoringOverride);
                isMonitored += $""" AND "AuthorFormatProfiles"."Monitored" AND COALESCE("Books"."{preference}", TRUE) """;
                profileColumn = """ "AuthorFormatProfiles"."QualityProfileId" """;

                // Older imports may link both media formats to the same edition.
                editionCondition = "TRUE";
            }

            if (monitored.HasValue)
            {
                builder.Where(monitored.Value ? $"({isMonitored})" : $"NOT ({isMonitored})");
            }

            // Known file extensions take precedence over contradictory legacy quality metadata.
            var oppositeExtensions = format == FormatType.Ebook ? MediaFileExtensions.AudioExtensions : MediaFileExtensions.TextExtensions;
            var oppositePath = string.Join(" OR ", oppositeExtensions.OrderBy(extension => extension, StringComparer.Ordinal)
                .Select(extension => $""" LOWER(COALESCE("BookFiles"."Path", '')) LIKE '%{extension}' """));
            var actualFormatCondition = $"{editionCondition} AND NOT ({oppositePath})";

            // Only monitored editions can qualify for a dual-format upgrade. A better
            // physical copy still meets the target regardless of its edition's monitoring.
            var upgradeCondition = dualFormatEnabled
                ? $""" {actualFormatCondition} AND "Editions"."Monitored" = TRUE """
                : actualFormatCondition;
            var belowCutoff = BuildQualityCutoffWhereClause(qualitiesBelowCutoff, profileColumn);
            var meetsCutoff = BuildQualityCutoffWhereClause(qualitiesBelowCutoff, profileColumn, meetsCutoff: true);
            return builder.Where($"{HasScopedFile(upgradeCondition, belowCutoff)} AND NOT {HasScopedFile(actualFormatCondition, meetsCutoff)}");
        }

        private static string HasScopedFile(string condition, string qualities)
        {
            return $"""
                EXISTS (SELECT 1 FROM "BookFiles"
                    INNER JOIN "Editions" ON "Editions"."Id" = "BookFiles"."EditionId"
                    WHERE "Editions"."BookId" = "Books"."Id" AND {condition} AND {qualities})
                """;
        }

        private static string BuildQualityCutoffWhereClause(List<QualitiesBelowCutoff> qualitiesBelowCutoff, string profileColumn, bool meetsCutoff = false)
        {
            var clauses = new List<string>();
            foreach (var profile in qualitiesBelowCutoff)
            {
                var qualityIds = meetsCutoff ? profile.QualityIdsMeetingCutoff : profile.QualityIds;
                foreach (var qualityId in qualityIds)
                {
                    clauses.Add(FormattableString.Invariant($""" ({profileColumn} = {profile.ProfileId} AND "BookFiles"."Quality" LIKE '%_quality_: {qualityId},%') """));
                }
            }

            return clauses.Count == 0 ? "FALSE" : $"({string.Join(" OR ", clauses)})";
        }

        public PagingSpec<Book> BooksWhereCutoffUnmet(PagingSpec<Book> pagingSpec, List<QualitiesBelowCutoff> qualitiesBelowCutoff)
        {
            return BooksWhereCutoffUnmet(pagingSpec, qualitiesBelowCutoff, null);
        }

        public PagingSpec<Book> BooksWhereCutoffUnmet(PagingSpec<Book> pagingSpec, List<QualitiesBelowCutoff> qualitiesBelowCutoff, FormatType? formatType)
        {
            return GetCutoffPage(pagingSpec, () => BooksWhereCutoffUnmetBuilder(qualitiesBelowCutoff, formatType));
        }

        public PagingSpec<Book> BooksWhereCutoffUnmet(PagingSpec<Book> pagingSpec, List<QualitiesBelowCutoff> qualitiesBelowCutoff, FormatType format, bool? monitored, bool dualFormatEnabled)
        {
            return GetCutoffPage(pagingSpec, () => ScopedCutoffBuilder(qualitiesBelowCutoff, format, monitored, dualFormatEnabled));
        }

        private PagingSpec<Book> GetCutoffPage(PagingSpec<Book> pagingSpec, Func<SqlBuilder> builder)
        {
            pagingSpec.Records = GetPagedRecords(builder(), pagingSpec, PagedQuery);

            var countTemplate = $"SELECT COUNT(*) FROM (SELECT /**select**/ FROM \"{TableMapping.Mapper.TableNameMapping(typeof(Book))}\" /**join**/ /**innerjoin**/ /**leftjoin**/ /**where**/ /**groupby**/ /**having**/) AS \"Inner\"";
            pagingSpec.TotalRecords = GetPagedRecordCount(builder().Select(typeof(Book)), pagingSpec, countTemplate);

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

        public void SetFormatMonitored(IEnumerable<int> ids, FormatType formatType, bool monitored)
        {
            var books = ids.Select(id => new Book
            {
                Id = id,
                EbookMonitoringOverride = monitored,
                AudiobookMonitoringOverride = monitored
            }).ToList();

            if (formatType == FormatType.Ebook)
            {
                SetFields(books, book => book.EbookMonitoringOverride);
            }
            else
            {
                SetFields(books, book => book.AudiobookMonitoringOverride);
            }
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
