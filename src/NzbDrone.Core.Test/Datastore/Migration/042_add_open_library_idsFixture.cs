using System.Linq;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Datastore.Migration;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Datastore.Migration
{
    [TestFixture]
    public class add_open_library_idsFixture : MigrationTest<add_open_library_ids>
    {
        [Test]
        public void should_add_openlibrary_identifier_columns_and_indexes()
        {
            var db = WithMigrationTestDb();

            var bookColumns = db.Query<TableInfoRow>("PRAGMA table_info('Books')").Select(row => row.name).ToList();
            var authorColumns = db.Query<TableInfoRow>("PRAGMA table_info('AuthorMetadata')").Select(row => row.name).ToList();
            var indexes = db.Query<IndexInfoRow>("SELECT name FROM sqlite_master WHERE type='index'").Select(row => row.name).ToList();

            bookColumns.Should().Contain("OpenLibraryWorkId");
            authorColumns.Should().Contain("OpenLibraryAuthorId");
            indexes.Should().Contain("IX_Books_OpenLibraryWorkId");
            indexes.Should().Contain("IX_AuthorMetadata_OpenLibraryAuthorId");
        }

        private class TableInfoRow
        {
            public int cid { get; set; }
            public string name { get; set; }
            public string type { get; set; }
            public int notnull { get; set; }
            public string dflt_value { get; set; }
            public int pk { get; set; }
        }

        private class IndexInfoRow
        {
            public string name { get; set; }
        }
    }
}
