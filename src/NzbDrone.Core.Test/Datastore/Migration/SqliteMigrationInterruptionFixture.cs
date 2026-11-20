using System;
using System.Data.SQLite;
using System.Diagnostics;
using System.IO;
using System.Threading;
using Dapper;
using Microsoft.Extensions.Logging.Abstractions;
using NLog;
using NUnit.Framework;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Test.Datastore.Migration
{
    [TestFixture]
    [NonParallelizable]
    [Platform("Linux")]
    public class SqliteMigrationInterruptionFixture
    {
        private const string ProfilePrefix = "bibliophilarr-migration-worker-";
        private const string WorkerProfileVariable = "BIBLIOPHILARR_MIGRATION_TEST_WORKER_PROFILE";
        private const string TransactionMarkerFile = "migration-transaction-open";
        private const string TransactionMarkerKey = "test_migration_transaction_marker";

        [Test]
        public void should_recover_after_kill_inside_a_sqlite_migration_transaction()
        {
            Assert.That(Environment.GetEnvironmentVariable(WorkerProfileVariable), Is.Null, "The migration worker cannot launch other workers");
            var profile = Directory.CreateTempSubdirectory(ProfilePrefix);
            try
            {
                var launch = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet")
                {
                    UseShellExecute = false
                };
                launch.ArgumentList.Add("vstest");
                launch.ArgumentList.Add(typeof(SqliteMigrationInterruptionFixture).Assembly.Location);
                launch.ArgumentList.Add($"--TestCaseFilter:FullyQualifiedName={typeof(SqliteMigrationInterruptionFixture).FullName}.{nameof(run_migration_worker)}");
                launch.Environment[WorkerProfileVariable] = profile.FullName;

                using (var worker = Process.Start(launch) ?? throw new InvalidOperationException("Could not start the migration worker"))
                {
                    try
                    {
                        var rendezvous = Path.Combine(profile.FullName, TransactionMarkerFile);
                        var reachedTransaction = SpinWait.SpinUntil(() => File.Exists(rendezvous) || worker.HasExited, TimeSpan.FromSeconds(45));
                        Assert.That(
                            reachedTransaction && File.Exists(rendezvous),
                            Is.True,
                            worker.HasExited ? $"Migration worker exited with {worker.ExitCode} before the transaction opened" : "Migration worker missed its transaction rendezvous");
                        Assert.That(worker.HasExited, Is.False, "Migration worker exited before it could be interrupted");
                    }
                    finally
                    {
                        if (!worker.HasExited)
                        {
                            worker.Kill(entireProcessTree: true);
                        }

                        Assert.That(worker.WaitForExit(5000), Is.True, "Interrupted migration worker did not exit");
                    }

                    Assert.That(worker.ExitCode, Is.EqualTo(128 + 9), "Child process should exit on SIGKILL");
                }

                var connectionString = GetConnectionString(profile.FullName);
                connectionString.FailIfMissing = true;
                using (var interruptedDatabase = new SQLiteConnection(connectionString.ConnectionString))
                {
                    interruptedDatabase.Open();
                    Assert.That(interruptedDatabase.Query<long>("SELECT \"Version\" FROM \"VersionInfo\" ORDER BY \"Version\""), Is.EqualTo(new[] { 1L }));
                    Assert.That(interruptedDatabase.QuerySingle<int>("SELECT COUNT(*) FROM \"Config\" WHERE \"Key\" = @key", new { key = TransactionMarkerKey }), Is.Zero);
                }

                Migrate(connectionString, new MigrationContext(MigrationType.Main));

                using var recoveredDatabase = new SQLiteConnection(connectionString.ConnectionString);
                recoveredDatabase.Open();
                SqliteMigrationRecoveryFixture.AssertCompleteMigration(recoveredDatabase);
                Assert.That(recoveredDatabase.QuerySingle<int>("SELECT COUNT(*) FROM \"Config\" WHERE \"Key\" = @key", new { key = TransactionMarkerKey }), Is.Zero);
            }
            finally
            {
                profile.Delete(recursive: true);
            }
        }

        [Test]
        [Explicit("Worker entry point for the SQLite migration interruption test")]
        public void run_migration_worker()
        {
            var workerProfile = Environment.GetEnvironmentVariable(WorkerProfileVariable);
            Assert.That(workerProfile, Is.Not.Null.And.Not.Empty);
            var profile = new DirectoryInfo(workerProfile);
            Assert.That(profile.Parent?.FullName, Is.EqualTo(Path.TrimEndingDirectorySeparator(Path.GetTempPath())));
            Assert.That(profile.Name, Does.StartWith(ProfilePrefix));
            Assert.That(profile.Attributes.HasFlag(FileAttributes.ReparsePoint), Is.False);
            Assert.That(profile.EnumerateFileSystemInfos(), Is.Empty);

            var migrationContext = new MigrationContext(MigrationType.Main)
            {
                BeforeMigration = migration =>
                {
                    if (migration.Version == 2)
                    {
                        migration.Execute.WithConnection((connection, transaction) =>
                        {
                            Assert.That(transaction?.Connection, Is.SameAs(connection));
                            var inserted = connection.Execute(
                                "INSERT INTO \"Config\" (\"Key\", \"Value\") VALUES (@key, 'uncommitted')",
                                new { key = TransactionMarkerKey },
                                transaction);
                            Assert.That(inserted, Is.EqualTo(1));
                            File.WriteAllText(Path.Combine(profile.FullName, TransactionMarkerFile), string.Empty);
                            Thread.Sleep(TimeSpan.FromSeconds(60));
                            Assert.Fail("The parent did not interrupt the open migration transaction");
                        });
                    }
                }
            };

            Migrate(GetConnectionString(profile.FullName), migrationContext);
            Assert.Fail("The migration worker exited without reaching its transaction rendezvous");
        }

        private static SQLiteConnectionStringBuilder GetConnectionString(string profile)
        {
            return new SQLiteConnectionStringBuilder
            {
                DataSource = Path.Combine(profile, "bibliophilarr.db"),
                Pooling = false,
                JournalMode = SQLiteJournalModeEnum.Wal
            };
        }

        private static void Migrate(SQLiteConnectionStringBuilder connectionString, MigrationContext migrationContext)
        {
            var controller = new MigrationController(LogManager.GetCurrentClassLogger(), NullLoggerProvider.Instance);
            controller.Migrate(connectionString.ConnectionString, migrationContext, DatabaseType.SQLite);
        }
    }
}
