using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NLog;
using NLog.Config;
using NLog.Targets;
using NUnit.Framework;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Test.Datastore.Migration
{
    [TestFixture]
    public class MigrationControllerLoggingFixture
    {
        [Test]
        public void should_log_migration_context_without_connection_details()
        {
            const string connectionValue = "migration-fixture-private-connection-value";
            var messages = new MemoryTarget { Layout = "${message}" };
            var configuration = new LoggingConfiguration();
            configuration.AddRuleForAllLevels(messages);
            using var logFactory = new LogFactory { Configuration = configuration };
            var controller = new MigrationController(logFactory.GetLogger(nameof(MigrationController)), NullLoggerProvider.Instance);

            controller.Migrate($"Data Source={connectionValue};Mode=Memory", new MigrationContext(MigrationType.Main, 1), DatabaseType.SQLite);

            messages.Logs.Should().NotContain(message => message.Contains(connectionValue));
            messages.Logs.Should().Contain(message => message.Contains("Migrating") && message.Contains("Main") && message.Contains("SQLite"));
        }
    }
}
