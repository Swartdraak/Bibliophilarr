using System;
using System.Diagnostics;
using System.Reflection;
using FluentMigrator;
using FluentMigrator.Runner;
using FluentMigrator.Runner.Generators;
using FluentMigrator.Runner.Generators.Postgres;
using FluentMigrator.Runner.Initialization;
using FluentMigrator.Runner.Processors;
using FluentMigrator.Runner.Processors.Postgres;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using NLog;
using NLog.Extensions.Logging;

namespace NzbDrone.Core.Datastore.Migration.Framework
{
    public interface IMigrationController
    {
        void Migrate(string connectionString, MigrationContext migrationContext, DatabaseType databaseType);
    }

    public class MigrationController : IMigrationController
    {
        private readonly Logger _logger;
        private readonly ILoggerProvider _migrationLoggerProvider;

        public MigrationController(Logger logger,
                                   ILoggerProvider migrationLoggerProvider)
        {
            _logger = logger;
            _migrationLoggerProvider = migrationLoggerProvider;
        }

        public void Migrate(string connectionString, MigrationContext migrationContext, DatabaseType databaseType)
        {
            var sw = Stopwatch.StartNew();

            _logger.Info("*** Migrating {0} ***", connectionString);

            var serviceProvider = ConfigureServices(connectionString, databaseType).BuildServiceProvider();

            using (var scope = serviceProvider.CreateScope())
            {
                var runner = scope.ServiceProvider.GetRequiredService<IMigrationRunner>();

                MigrationContext.Current = migrationContext;

                if (migrationContext.DesiredVersion.HasValue)
                {
                    runner.MigrateUp(migrationContext.DesiredVersion.Value);
                }
                else
                {
                    runner.MigrateUp();
                }

                MigrationContext.Current = null;
            }

            sw.Stop();

            _logger.Debug("Took: {0}", sw.Elapsed);
        }

        internal static IServiceCollection ConfigureServices(string connectionString, DatabaseType databaseType)
        {
            var db = databaseType == DatabaseType.SQLite ? "sqlite" : "postgres";

            return new ServiceCollection()
                .AddLogging(b => b.AddNLog())
                .AddFluentMigratorCore()
                .Configure<RunnerOptions>(cfg => cfg.IncludeUntaggedMaintenances = true)
                .ConfigureRunner(builder =>
                {
                    if (databaseType == DatabaseType.SQLite)
                    {
                        builder.AddNzbDroneSQLite();
                    }
                    else
                    {
                        builder.AddPostgres();

                        // FluentMigrator 8 exposes both concrete dialects, but AddPostgres registers
                        // PostgreSQL 15 for the selector interfaces. Replace those registrations to
                        // preserve the generic PostgreSQL dialect and the existing postgres selector.
                        builder.Services.Replace(ServiceDescriptor.Scoped<IMigrationProcessor>(sp => sp.GetRequiredService<PostgresProcessor>()));
                        builder.Services.Replace(ServiceDescriptor.Scoped<IMigrationGenerator>(sp => sp.GetRequiredService<PostgresGenerator>()));
                    }

                    builder.WithGlobalConnectionString(connectionString)
                        .ScanIn(Assembly.GetExecutingAssembly()).For.All();
                })
                .Configure<TypeFilterOptions>(opt => opt.Namespace = "NzbDrone.Core.Datastore.Migration")
                .Configure<ProcessorOptions>(opt =>
                {
                    opt.PreviewOnly = false;
                    opt.Timeout = TimeSpan.FromMinutes(5);
                })
                .Configure<SelectingProcessorAccessorOptions>(cfg =>
                {
                    cfg.ProcessorId = db;
                })
                .Configure<SelectingGeneratorAccessorOptions>(cfg =>
                {
                    cfg.GeneratorId = db;
                });
        }
    }
}
