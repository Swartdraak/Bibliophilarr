using System;
using System.Linq;
using System.Reflection;
using FluentAssertions;
using FluentMigrator;
using FluentMigrator.Runner.Generators;
using FluentMigrator.Runner.Generators.Postgres;
using FluentMigrator.Runner.Generators.SQLite;
using FluentMigrator.Runner.Initialization;
using FluentMigrator.Runner.Processors;
using FluentMigrator.Runner.Processors.Postgres;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Datastore.Migration;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Test.Datastore.Migration
{
    [TestFixture]
    public class issue98FluentMigratorMigrationChainFixture
    {
        private const string ExpectedFluentMigratorVersion = "8.0.1";

        [Test]
        public void fluentmigrator_runner_version_should_match_validated_version()
        {
            var fmVersion = typeof(FluentMigrator.Migration).Assembly
                .GetCustomAttribute<AssemblyFileVersionAttribute>()?.Version
                ?? typeof(FluentMigrator.Migration).Assembly
                    .GetName().Version.ToString();

            fmVersion.Should().StartWith(ExpectedFluentMigratorVersion,
                "SQLite and PostgreSQL migrations must use the intentionally selected FluentMigrator release");
        }

        [Test]
        public void migration_namespace_should_be_stable_for_discovery()
        {
            var migrationAssembly = typeof(InitialSetup).Assembly;
            var migrations = migrationAssembly.GetTypes()
                .Where(t => t.Namespace == "NzbDrone.Core.Datastore.Migration" &&
                            !t.IsAbstract &&
                            typeof(NzbDroneMigrationBase).IsAssignableFrom(t))
                .ToList();

            migrations.Should().NotBeEmpty("migration classes must exist in the NzbDrone.Core.Datastore.Migration namespace for FluentMigrator discovery");
            migrations.Count.Should().BeGreaterThan(39,
                "at least 40 migration classes are expected (001-048+); a count drop indicates a namespace or base-class change");
        }

        [Test]
        public void all_migrations_should_have_unique_increasing_versions()
        {
            var migrationAssembly = typeof(InitialSetup).Assembly;
            var migrations = migrationAssembly.GetTypes()
                .Where(t => t.Namespace == "NzbDrone.Core.Datastore.Migration" &&
                            !t.IsAbstract &&
                            typeof(NzbDroneMigrationBase).IsAssignableFrom(t))
                .ToList();

            var versions = migrations
                .Select(t => (int)((MigrationAttribute)Attribute.GetCustomAttribute(t, typeof(MigrationAttribute))).Version)
                .OrderBy(v => v)
                .ToList();

            versions.Count.Should().Be(versions.Distinct().Count(),
                "all migration versions must be unique (no duplicate version numbers)");

            for (var i = 1; i < versions.Count; i++)
            {
                versions[i].Should().BeGreaterThan(versions[i - 1],
                    $"migration version {versions[i]} must be greater than the previous {versions[i - 1]} (strictly increasing order)");
            }
        }

        [Test]
        public void migration_down_should_throw_not_implemented()
        {
            var migrationAssembly = typeof(InitialSetup).Assembly;
            var migrations = migrationAssembly.GetTypes()
                .Where(t => t.Namespace == "NzbDrone.Core.Datastore.Migration" &&
                            !t.IsAbstract &&
                            typeof(NzbDroneMigrationBase).IsAssignableFrom(t))
                .ToList();

            migrations.Should().AllSatisfy(migrationType =>
            {
                var baseInstance = (NzbDroneMigrationBase)Activator.CreateInstance(migrationType);

                baseInstance.Invoking(m => m.Down())
                    .Should().Throw<NotImplementedException>(
                        $"Down() on {migrationType.Name} must remain unimplemented (no rollback support in this repository)");
            });
        }

        [TestCase(DatabaseType.SQLite, "Data Source=:memory:", typeof(NzbDroneSQLiteProcessor), typeof(SQLiteGenerator))]
        [TestCase(DatabaseType.PostgreSQL, "Host=localhost;Database=migration_fixture", typeof(PostgresProcessor), typeof(PostgresGenerator))]
        public void should_register_and_select_only_the_configured_dialect(DatabaseType databaseType, string connectionString, Type processorType, Type generatorType)
        {
            using var provider = MigrationController.ConfigureServices(connectionString, databaseType).BuildServiceProvider();
            using var scope = provider.CreateScope();
            var services = scope.ServiceProvider;
            var processor = services.GetRequiredService<IProcessorAccessor>().Processor;
            var generator = services.GetRequiredService<IGeneratorAccessor>().Generator;

            processor.Should().BeOfType(processorType);
            generator.Should().BeOfType(generatorType);
            services.GetServices<IMigrationProcessor>().Should().ContainSingle().Which.Should().BeSameAs(processor);
            services.GetServices<IMigrationGenerator>().Should().ContainSingle().Which.Should().BeSameAs(generator);
            services.GetRequiredService<IConnectionStringAccessor>().ConnectionString.Should().Be(connectionString);
        }
    }
}
