using System;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Messaging.Commands
{
    [TestFixture]
    public class CommandRepositoryFixture : DbTest<CommandRepository, CommandModel>
    {
        [Test]
        public void should_orphan_interrupted_commands_and_preserve_live_started_and_queued_commands()
        {
            var previous = Insert(CommandStatus.Started);
            var live = Insert(CommandStatus.Started);
            var queued = Insert(CommandStatus.Queued);

            Subject.OrphanStarted(new[] { live.Id });

            var interrupted = Subject.Get(previous.Id);
            interrupted.Status.Should().Be(CommandStatus.Orphaned);
            interrupted.EndedAt.Should().NotBeNull();

            var preserved = Subject.Get(live.Id);
            preserved.Status.Should().Be(CommandStatus.Started);
            preserved.StartedAt.Should().Be(live.StartedAt);
            preserved.EndedAt.Should().BeNull();
            Subject.Get(queued.Id).Status.Should().Be(CommandStatus.Queued);
        }

        [TestCase(CommandResult.Successful)]
        [TestCase(CommandResult.Unsuccessful)]
        [TestCase(CommandResult.Unknown)]
        public void should_preserve_the_terminal_result_with_the_completion_fields(CommandResult result)
        {
            var body = new MessagingCleanupCommand { Trigger = CommandTrigger.Manual };
            var queued = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            var command = Subject.Insert(new CommandModel
            {
                Name = body.Name,
                Body = body,
                Status = CommandStatus.Queued,
                Result = CommandResult.Unknown,
                Trigger = CommandTrigger.Manual,
                QueuedAt = queued
            });

            command.Status = CommandStatus.Started;
            command.StartedAt = queued.AddSeconds(1);
            Subject.Start(command);

            command.Status = CommandStatus.Completed;
            command.Result = result;
            command.Duration = TimeSpan.FromSeconds(3);
            command.EndedAt = command.StartedAt + command.Duration;
            Subject.End(command);

            var reader = new CommandRepository(Mocker.Resolve<IMainDatabase>(), Mocker.Resolve<IEventAggregator>());
            var saved = reader.Get(command.Id);
            saved.Status.Should().Be(CommandStatus.Completed);
            saved.Result.Should().Be(result);
            saved.StartedAt.Should().Be(command.StartedAt);
            saved.EndedAt.Should().Be(command.EndedAt);
            saved.Duration.Should().Be(command.Duration);
            saved.Body.Should().BeOfType<MessagingCleanupCommand>();
        }

        private CommandModel Insert(CommandStatus status)
        {
            var body = new MessagingCleanupCommand { Trigger = CommandTrigger.Manual };
            var time = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            return Subject.Insert(new CommandModel
            {
                Name = body.Name,
                Body = body,
                Status = status,
                Result = CommandResult.Unknown,
                Trigger = CommandTrigger.Manual,
                QueuedAt = time,
                StartedAt = status == CommandStatus.Started ? time.AddSeconds(1) : null
            });
        }
    }
}
