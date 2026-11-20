using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using System.Reflection;
using Dapper;
using FluentMigrator;
using Microsoft.Extensions.Logging.Abstractions;
using NLog;
using NUnit.Framework;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Datastore.Migration;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Test.Datastore.Migration
{
    [TestFixture]
    [NonParallelizable]
    public class SqliteMigrationRecoveryFixture
    {
        private DirectoryInfo _profile;
        private string _databasePath;
        private SQLiteConnectionStringBuilder _connectionString;

        [SetUp]
        public void SetUp()
        {
            _profile = Directory.CreateTempSubdirectory("bibliophilarr-migration-");
            _databasePath = Path.Combine(_profile.FullName, "bibliophilarr.db");
            _connectionString = new SQLiteConnectionStringBuilder { DataSource = _databasePath, Pooling = false };
        }

        [TearDown]
        public void TearDown()
        {
            _profile?.Delete(recursive: true);
        }

        [Test]
        public void should_apply_each_main_migration_to_a_fresh_database()
        {
            Assert.That(File.Exists(_databasePath), Is.False);

            Migrate(new MigrationContext(MigrationType.Main));

            using var connection = OpenExistingDatabase();
            AssertCompleteMigration(connection);
        }

        [Test]
        public void should_preserve_both_formats_when_reopening_a_migrated_catalogue()
        {
            Migrate(new MigrationContext(MigrationType.Main));
            var root = Path.Combine(_profile.FullName, "library");
            var authorPath = Path.Combine(root, "Recovery Author");
            var ebookPath = Path.Combine(authorPath, "Recovery Work.epub");
            var audioPath = Path.Combine(authorPath, "Recovery Work.m4b");
            using (var connection = OpenExistingDatabase())
            {
                connection.Execute("""
                    INSERT INTO "RootFolders" ("Id", "Path", "Name", "IsCalibreLibrary")
                    VALUES (1, @root, 'Recovery Library', FALSE);
                    INSERT INTO "AuthorMetadata" ("Id", "ForeignAuthorId", "TitleSlug", "Name", "Status", "Images", "Aliases", "SortName", "NameLastFirst", "SortNameLastFirst")
                    VALUES (2, 'openlibrary:author:OL123A', 'recovery-author', 'Recovery Author', 0, '[]', '[]', 'recovery author', 'Author, Recovery', 'author recovery');
                    INSERT INTO "Authors" ("Id", "AuthorMetadataId", "CleanName", "Path", "Monitored", "MetadataProfileId")
                    VALUES (3, 2, 'recoveryauthor', @authorPath, TRUE, 1);
                    INSERT INTO "Books" ("Id", "AuthorMetadataId", "ForeignBookId", "TitleSlug", "Title", "CleanTitle", "Monitored", "AnyEditionOk")
                    VALUES (4, 2, 'openlibrary:work:OL123W', 'recovery-work', 'Recovery Work', 'recoverywork', TRUE, TRUE);
                    INSERT INTO "Editions" ("Id", "BookId", "ForeignEditionId", "Title", "TitleSlug", "Format", "IsEbook", "Images", "Monitored", "ManualAdd")
                    VALUES (5, 4, 'ebook-edition', 'Recovery Work', 'recovery-work-ebook', 'Ebook', TRUE, '[]', TRUE, FALSE),
                           (6, 4, 'audio-edition', 'Recovery Work', 'recovery-work-audio', 'Audiobook', FALSE, '[]', TRUE, FALSE);
                    INSERT INTO "BookFiles" ("Id", "EditionId", "CalibreId", "Quality", "Size", "DateAdded", "Path")
                    VALUES (7, 5, 0, '{}', 1234, '2026-01-01', @ebookPath),
                           (8, 6, 0, '{}', 5678, '2026-01-01', @audioPath);
                    """, new { root, authorPath, ebookPath, audioPath });
            }

            var reappliedVersions = new List<int>();
            Migrate(new MigrationContext(MigrationType.Main)
            {
                BeforeMigration = migration => reappliedVersions.Add(migration.Version)
            });

            using var reopened = OpenExistingDatabase();
            Assert.That(reappliedVersions, Is.Empty);
            AssertCompleteMigration(reopened);
            Assert.That(reopened.Query<string>("SELECT \"Path\" FROM \"RootFolders\""), Is.EqualTo(new[] { root }));
            Assert.That(reopened.QuerySingle<(int Id, string Name, string Path)>("""
                SELECT a."Id", am."Name", a."Path"
                FROM "Authors" a JOIN "AuthorMetadata" am ON a."AuthorMetadataId" = am."Id"
                """), Is.EqualTo((3, "Recovery Author", authorPath)));
            Assert.That(reopened.QuerySingle<(int Id, int AuthorMetadataId, string Title)>("""
                SELECT "Id", "AuthorMetadataId", "Title" FROM "Books"
                """), Is.EqualTo((4, 2, "Recovery Work")));
            Assert.That(reopened.Query<(int Id, int BookId, string Format, bool IsEbook)>("""
                SELECT "Id", "BookId", "Format", "IsEbook" FROM "Editions" ORDER BY "Id"
                """), Is.EqualTo(new[] { (5, 4, "Ebook", true), (6, 4, "Audiobook", false) }));
            Assert.That(reopened.Query<(int Id, int EditionId, string Path, long Size)>("""
                SELECT "Id", "EditionId", "Path", "Size" FROM "BookFiles" ORDER BY "Id"
                """), Is.EqualTo(new[] { (7, 5, ebookPath, 1234L), (8, 6, audioPath, 5678L) }));
        }

        private void Migrate(MigrationContext migrationContext)
        {
            var controller = new MigrationController(LogManager.GetCurrentClassLogger(), NullLoggerProvider.Instance);
            controller.Migrate(_connectionString.ConnectionString, migrationContext, DatabaseType.SQLite);
        }

        private SQLiteConnection OpenExistingDatabase()
        {
            var connectionString = new SQLiteConnectionStringBuilder(_connectionString.ConnectionString) { FailIfMissing = true };
            var connection = new SQLiteConnection(connectionString.ConnectionString);
            connection.Open();
            return connection;
        }

        internal static void AssertCompleteMigration(SQLiteConnection connection)
        {
            var expectedVersions = typeof(InitialSetup).Assembly.GetTypes()
                .Where(type => type.Namespace == typeof(InitialSetup).Namespace && !type.IsAbstract && type.IsSubclassOf(typeof(NzbDroneMigrationBase)))
                .Select(type => type.GetCustomAttribute<MigrationAttribute>()?.Version
                    ?? throw new AssertionException($"Migration type {type.FullName} is missing {nameof(MigrationAttribute)}"))
                .OrderBy(version => version)
                .ToArray();

            // Migration 34 was released as version 158. Preserve the whole published baseline.
            var historicalVersions = Enumerable.Range(1, 49).Where(version => version != 34).Select(version => (long)version).Append(158);
            Assert.That(historicalVersions.Except(expectedVersions), Is.Empty);
            Assert.That(connection.Query<long>("SELECT \"Version\" FROM \"VersionInfo\" ORDER BY \"Version\""), Is.EqualTo(expectedVersions));
            Assert.That(connection.Query<string>("PRAGMA integrity_check"), Is.EqualTo(new[] { "ok" }));
        }
    }
}
