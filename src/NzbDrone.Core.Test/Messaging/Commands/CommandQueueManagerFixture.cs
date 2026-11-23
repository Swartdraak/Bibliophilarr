using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Books.Commands;
using NzbDrone.Core.Download;
using NzbDrone.Core.Lifecycle;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Messaging.Commands
{
    [TestFixture]
    public class CommandQueueManagerFixture : CoreTest<CommandQueueManager>
    {
        [SetUp]
        public void Setup()
        {
            var id = 0;
            var commands = new List<CommandModel>();

            Mocker.GetMock<ICommandRepository>()
                  .Setup(s => s.Insert(It.IsAny<CommandModel>()))
                  .Returns<CommandModel>(c =>
                  {
                      c.Id = id + 1;
                      commands.Add(c);
                      id++;

                      return c;
                  });

            Mocker.GetMock<ICommandRepository>()
                  .Setup(s => s.Get(It.IsAny<int>()))
                  .Returns<int>(c =>
                  {
                      return commands.SingleOrDefault(e => e.Id == c);
                  });
        }

        [TestCase(CommandStatus.Queued)]
        [TestCase(CommandStatus.Started)]
        public void should_recover_queued_commands_without_adding_another_copy_of_a_live_command(CommandStatus liveStatus)
        {
            var liveCommand = Subject.Push(new BackfillOpenLibraryIdsCommand());
            liveCommand.Status = liveStatus;
            var persistedCopy = new CommandModel
            {
                Id = liveCommand.Id,
                Name = liveCommand.Name,
                Body = new BackfillOpenLibraryIdsCommand(),
                Status = CommandStatus.Queued
            };
            var recoveredCommand = new CommandModel
            {
                Id = liveCommand.Id + 1,
                Name = "RefreshMonitoredDownloads",
                Body = new RefreshMonitoredDownloadsCommand(),
                Status = CommandStatus.Queued
            };
            Mocker.GetMock<ICommandRepository>()
                .Setup(repository => repository.Queued())
                .Returns(new List<CommandModel> { persistedCopy, recoveredCommand });

            Subject.Handle(new ApplicationStartedEvent());
            Subject.Requeue();

            Subject.All().Should().Equal(liveCommand, recoveredCommand);
            Subject.Get(liveCommand.Id).Should().BeSameAs(liveCommand);
            Subject.Get(liveCommand.Id).Status.Should().Be(liveStatus);
        }

        [Test]
        public void should_not_remove_commands_for_five_minutes_after_they_end()
        {
            var command = Subject.Push<RefreshMonitoredDownloadsCommand>(new RefreshMonitoredDownloadsCommand());

            // Start the command to mimic CommandQueue's behaviour
            command.StartedAt = DateTime.Now;
            command.Status = CommandStatus.Started;

            Subject.Start(command);
            Subject.Complete(command, "All done");
            Subject.CleanCommands();

            Subject.Get(command.Id).Should().NotBeNull();

            Mocker.GetMock<ICommandRepository>()
                  .Verify(v => v.Get(It.IsAny<int>()), Times.Never());
        }
    }
}
