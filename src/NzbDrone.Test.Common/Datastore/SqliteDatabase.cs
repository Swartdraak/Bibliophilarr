using System.Data.SQLite;
using System.IO;
using System.Linq;
using System.Reflection;
using Dapper;
using FluentMigrator;
using NUnit.Framework;
using NzbDrone.Core.Datastore.Migration;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Test.Common.Datastore
{
    public static class SqliteDatabase
    {
        public static string GetCachedDb(MigrationType type)
        {
            return Path.Combine(TestContext.CurrentContext.TestDirectory, $"cached_{type}.db");
        }

        public static void AssertCompleteMainMigration(SQLiteConnection connection)
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
