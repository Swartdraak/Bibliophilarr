using System;
using System.Data.SQLite;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using DryIoc;
using DryIoc.Microsoft.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Composition.Extensions;
using NzbDrone.Common.Http.Dispatchers;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Host.Test
{
    public partial class FreshStartupRecoveryFixture
    {
        private const string WorkerMode = "--migration-interruption";
        private const string ProfilePrefix = "bibliophilarr-host-migration-";
        private const string TransactionReadyFile = "migration-transaction-open";
        private const string UncommittedConfigKey = "test_host_migration_transaction_marker";

        [Test]
        [Platform("Linux")]
        public async Task should_reopen_the_full_host_after_sigkill_inside_a_main_database_migration()
        {
            var profile = Directory.CreateTempSubdirectory(ProfilePrefix);
            try
            {
                var launch = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet")
                {
                    UseShellExecute = false
                };
                launch.ArgumentList.Add(typeof(FreshStartupRecoveryFixture).Assembly.Location);
                launch.ArgumentList.Add(WorkerMode);
                launch.ArgumentList.Add(profile.FullName);
                foreach (var key in launch.Environment.Keys.Where(key => key.StartsWith("BIBLIOPHILARR", StringComparison.OrdinalIgnoreCase)).ToArray())
                {
                    launch.Environment.Remove(key);
                }

                string originalKey;
                using (var child = Process.Start(launch) ?? throw new InvalidOperationException("Could not start the isolated migration host"))
                {
                    try
                    {
                        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
                        var rendezvous = Path.Combine(profile.FullName, TransactionReadyFile);
                        while (!File.Exists(rendezvous) && !child.HasExited && !timeout.IsCancellationRequested)
                        {
                            await Task.Delay(TimeSpan.FromMilliseconds(50));
                        }

                        if (child.HasExited)
                        {
                            Assert.Fail($"The isolated host exited with {child.ExitCode} before interruption");
                        }

                        Assert.That(File.Exists(rendezvous), Is.True, "The host must reach the open migration transaction");
                        originalKey = File.Exists(Path.Combine(profile.FullName, "config.xml"))
                            ? await ReadPersistedKey(profile, timeout.Token)
                            : null;
                    }
                    finally
                    {
                        if (!child.HasExited)
                        {
                            // This Process is the directly launched executable running Kestrel, with no test-runner grandchild.
                            child.Kill();
                        }

                        using var exitTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                        await child.WaitForExitAsync(exitTimeout.Token);
                    }

                    Assert.That(child.ExitCode, Is.EqualTo(128 + 9), "Only the isolated startup process should receive SIGKILL");
                }

                long[] originalDelayProfiles;
                using (var interrupted = OpenMainDatabase(profile))
                {
                    Assert.That(interrupted.Query<long>("SELECT Version FROM VersionInfo ORDER BY Version"), Is.EqualTo(new[] { 1L }));
                    Assert.That(interrupted.Query<string>("SELECT name FROM pragma_table_info('ImportLists') WHERE name = 'ShouldSearch'"), Is.Empty);
                    Assert.That(interrupted.QuerySingle<int>("SELECT COUNT(*) FROM Config WHERE Key = @key", new { key = UncommittedConfigKey }), Is.Zero);
                    originalDelayProfiles = interrupted.Query<long>("SELECT Id FROM DelayProfiles ORDER BY Id").ToArray();
                    Assert.That(originalDelayProfiles, Has.Length.EqualTo(1));
                }

                // Reopen the exact profile with normal production Bootstrap and the real migration controller.
                var recoveredKey = await AssertHealthyStartup(profile, originalKey);
                await AssertHealthyStartup(profile, recoveredKey);

                using var recovered = OpenMainDatabase(profile);
                Assert.That(recovered.Query<string>("SELECT name FROM pragma_table_info('ImportLists') WHERE name = 'ShouldSearch'"),
                    Is.EqualTo(new[] { "ShouldSearch" }));
                Assert.That(recovered.QuerySingle<int>("SELECT COUNT(*) FROM Config WHERE Key = @key", new { key = UncommittedConfigKey }), Is.Zero);
                Assert.That(recovered.Query<long>("SELECT Id FROM DelayProfiles ORDER BY Id"), Is.EqualTo(originalDelayProfiles));
                Assert.That(recovered.Query<string>("PRAGMA foreign_key_check"), Is.Empty);
            }
            finally
            {
                SQLiteConnection.ClearAllPools();
                profile.Delete(recursive: true);
            }
        }

        // Only the independent test process can select the test controller. The production executable has no pause switch.
        internal static async Task<int> Main(string[] args)
        {
            if (!OperatingSystem.IsLinux() || args.Length != 2 || args[0] != WorkerMode)
            {
                return 2;
            }

            var profile = new DirectoryInfo(args[1]);
            if (profile.Parent?.FullName != Path.TrimEndingDirectorySeparator(Path.GetTempPath()) ||
                !profile.Name.StartsWith(ProfilePrefix, StringComparison.Ordinal) ||
                !profile.Exists || profile.Attributes.HasFlag(FileAttributes.ReparsePoint) || profile.EnumerateFileSystemInfos().Any())
            {
                return 2;
            }

            var outbound = new Mock<IHttpDispatcher>(MockBehavior.Strict);

            // Bootstrap can resolve services while publishing the startup event. Decorate the
            // factory before those early resolutions instead of replacing an already captured singleton.
            var container = new Container(rules => rules.WithNzbDroneRules());
            container.RegisterInstance(profile);
            container.Register<IMigrationController, InterruptedMigration>(setup: Setup.Decorator);
            using var host = CreateIsolatedHost(profile, outbound.Object)
                .UseServiceProviderFactory(new DryIocServiceProviderFactory(container))
                .Build();
            await host.RunAsync();
            return 3;
        }

        private sealed class InterruptedMigration : IMigrationController
        {
            private readonly IMigrationController _original;
            private readonly DirectoryInfo _profile;

            public InterruptedMigration(IMigrationController original, DirectoryInfo profile)
            {
                _original = original;
                _profile = profile;
            }

            public void Migrate(string connectionString, MigrationContext context, DatabaseType databaseType)
            {
                if (context.MigrationType == MigrationType.Main)
                {
                    Assert.That(databaseType, Is.EqualTo(DatabaseType.SQLite));
                    var mainPath = Path.Combine(_profile.FullName, "bibliophilarr.db");
                    Assert.That(new SQLiteConnectionStringBuilder(connectionString).DataSource, Is.EqualTo(mainPath));
                    context.BeforeMigration = migration =>
                    {
                        if (migration.Version == 2)
                        {
                            migration.Execute.WithConnection((connection, transaction) =>
                            {
                                Assert.That(transaction?.Connection, Is.SameAs(connection));
                                var inserted = connection.Execute(
                                    "INSERT INTO Config (Key, Value) VALUES (@key, 'uncommitted')",
                                    new { key = UncommittedConfigKey },
                                    transaction);
                                Assert.That(inserted, Is.EqualTo(1));
                                File.WriteAllText(Path.Combine(_profile.FullName, TransactionReadyFile), string.Empty);
                                using var pause = new ManualResetEventSlim();
                                pause.Wait(TimeSpan.FromSeconds(30));
                                throw new InvalidOperationException("The parent did not interrupt the open database transaction");
                            });
                        }
                    };
                }

                _original.Migrate(connectionString, context, databaseType);
            }
        }
    }
}
