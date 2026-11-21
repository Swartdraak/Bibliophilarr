using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using NLog;
using NLog.Targets;
using NUnit.Framework;
using NzbDrone.Common.Instrumentation;
using NzbDrone.Core.Datastore.Migration.Framework;
using NzbDrone.Core.Instrumentation;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.Instrumentation
{
    [TestFixture]
    public class DatabaseTargetFixture : DbTest<DatabaseTarget, Log>
    {
        private string _uniqueMessage;
        private Logger _logger;
        private Target _databaseLogTarget;

        protected override MigrationType MigrationType => MigrationType.Log;

        [SetUp]
        public void Setup()
        {
            Mocker.Resolve<ILogRepository, LogRepository>();
            var databaseTarget = Mocker.Resolve<DatabaseTarget>();
            databaseTarget.Register();
            _databaseLogTarget = databaseTarget.Rule.Targets.Single();

            LogManager.ReconfigExistingLoggers();

            _logger = NzbDroneLogger.GetLogger(this);

            _uniqueMessage = "Unique message: " + Guid.NewGuid();
        }

        [Test]
        public void write_log()
        {
            _logger.Info(_uniqueMessage);

            FlushDatabaseLogs();

            StoredModel.Message.Should().Be(_uniqueMessage);
            VerifyLog(StoredModel, LogLevel.Info);
        }

        [Test]
        public void write_long_log()
        {
            var message = string.Empty;
            for (var i = 0; i < 100; i++)
            {
                message += Guid.NewGuid();
            }

            _logger.Info(message);

            FlushDatabaseLogs();

            StoredModel.Message.Should().HaveLength(message.Length);
            StoredModel.Message.Should().Be(message);
            VerifyLog(StoredModel, LogLevel.Info);
        }

        [Test]
        public void write_log_exception()
        {
            var ex = new InvalidOperationException("Fake Exception");

            _logger.Error(ex, _uniqueMessage);

            FlushDatabaseLogs();

            VerifyLog(StoredModel, LogLevel.Error);
            StoredModel.Message.Should().Be(_uniqueMessage + ": " + ex.Message);
            StoredModel.ExceptionType.Should().Be(ex.GetType().ToString());
            StoredModel.Exception.Should().Be(ex.ToString());

            ExceptionVerification.ExpectedErrors(1);
        }

        [Test]
        public void exception_log_with_no_message_should_use_exceptions_message()
        {
            var ex = new InvalidOperationException("Fake Exception");
            _uniqueMessage = string.Empty;

            _logger.Error(ex, _uniqueMessage);

            FlushDatabaseLogs();

            StoredModel.Message.Should().Be(ex.Message);

            VerifyLog(StoredModel, LogLevel.Error);

            ExceptionVerification.ExpectedErrors(1);
        }

        [Test]
        public void should_store_a_message_with_a_null_format_argument()
        {
            var epFile = new BookFile();
            _logger.Info("File [{0}] no longer exists on disk.", epFile.Path);

            FlushDatabaseLogs();

            StoredModel.Message.Should().Be("File [] no longer exists on disk.");
            VerifyLog(StoredModel, LogLevel.Info);
        }

        [TearDown]
        public void Teardown()
        {
            Mocker.Resolve<DatabaseTarget>().UnRegister();
        }

        private void FlushDatabaseLogs()
        {
            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _databaseLogTarget.Flush(error =>
            {
                if (error == null)
                {
                    completion.TrySetResult();
                }
                else
                {
                    completion.TrySetException(error);
                }
            });

            completion.Task.WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
        }

        private void VerifyLog(Log logItem, LogLevel level)
        {
            logItem.Time.Should().BeWithin(TimeSpan.FromSeconds(2));
            logItem.Logger.Should().Be(GetType().Name);
            logItem.Level.Should().Be(level.Name);
            _logger.Name.Should().EndWith(logItem.Logger);
        }
    }
}
