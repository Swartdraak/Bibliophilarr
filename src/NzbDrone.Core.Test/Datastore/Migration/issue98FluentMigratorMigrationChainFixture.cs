using System;
using System.Linq;
using System.Reflection;
using FluentAssertions;
using FluentMigrator;
using NUnit.Framework;
using NzbDrone.Core.Datastore.Migration;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Test.Datastore.Migration
{
    [TestFixture]
    public class issue98FluentMigratorMigrationChainFixture
    {
        private const string ExpectedFluentMigratorVersion = "3.3.2";

        [Test]
        public void fluentmigrator_runner_version_should_be_3_3_2()
        {
            var fmVersion = typeof(FluentMigrator.Migration).Assembly
                .GetCustomAttribute<AssemblyFileVersionAttribute>()?.Version
                ?? typeof(FluentMigrator.Migration).Assembly
                    .GetName().Version.ToString();

            fmVersion.Should().StartWith(ExpectedFluentMigratorVersion,
                "FluentMigrator must remain on the 3.3.x line until the coordinated 4.x upgrade (issue #98, epic #96) is executed");
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

        [Test]
        public void sqlite_processor_should_be_registered()
        {
            var processorType = typeof(NzbDroneSQLiteProcessor);
            processorType.Should().NotBeNull("NzbDroneSQLiteProcessor must exist");
            processorType.IsAbstract.Should().BeFalse("NzbDroneSQLiteProcessor must be instantiable");
        }
    }
}
