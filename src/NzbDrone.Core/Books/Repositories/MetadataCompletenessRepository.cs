using Dapper;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.Books
{
    public interface IMetadataCompletenessRepository
    {
        MetadataCompletenessStats GetStats();
    }

    /// <summary>
    /// Computes metadata-completeness KPIs (issue #207) with a small set of aggregate
    /// queries. Read-only; safe to call from a diagnostic endpoint.
    /// </summary>
    public class MetadataCompletenessRepository : IMetadataCompletenessRepository
    {
        private readonly IMainDatabase _database;

        public MetadataCompletenessRepository(IMainDatabase database)
        {
            _database = database;
        }

        public MetadataCompletenessStats GetStats()
        {
            var stats = new MetadataCompletenessStats();

            using (var conn = _database.OpenConnection())
            {
                stats.TotalBooks = conn.QueryFirstOrDefault<int>(
                    "SELECT COUNT(*) FROM \"Books\"");

                stats.BooksWithOpenLibraryWorkId = conn.QueryFirstOrDefault<int>(
                    "SELECT COUNT(*) FROM \"Books\" WHERE \"OpenLibraryWorkId\" IS NOT NULL AND \"OpenLibraryWorkId\" <> ''");

                stats.BooksMissingOpenLibraryWorkId = stats.TotalBooks - stats.BooksWithOpenLibraryWorkId;

                stats.TotalSeries = conn.QueryFirstOrDefault<int>(
                    "SELECT COUNT(*) FROM \"Series\"");

                stats.SeriesWithLinkedBooks = conn.QueryFirstOrDefault<int>(
                    "SELECT COUNT(DISTINCT \"SeriesId\") FROM \"SeriesBookLink\"");

                stats.SeriesWithoutLinkedBooks = stats.TotalSeries - stats.SeriesWithLinkedBooks;

                stats.TotalEditions = conn.QueryFirstOrDefault<int>(
                    "SELECT COUNT(*) FROM \"Editions\"");

                stats.EditionsWithBookFile = conn.QueryFirstOrDefault<int>(
                    "SELECT COUNT(DISTINCT \"EditionId\") FROM \"BookFiles\" WHERE \"EditionId\" > 0");

                stats.EditionsWithoutBookFile = stats.TotalEditions - stats.EditionsWithBookFile;

                stats.TotalBookFiles = conn.QueryFirstOrDefault<int>(
                    "SELECT COUNT(*) FROM \"BookFiles\"");

                stats.TotalAuthors = conn.QueryFirstOrDefault<int>(
                    "SELECT COUNT(*) FROM \"Authors\"");
            }

            return stats;
        }
    }
}
