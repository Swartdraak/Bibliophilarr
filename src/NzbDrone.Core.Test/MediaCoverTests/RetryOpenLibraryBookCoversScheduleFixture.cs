using System;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Jobs;
using NzbDrone.Core.Lifecycle;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.MediaCoverTests
{
    [TestFixture]
    public class RetryOpenLibraryBookCoversScheduleFixture : TestBase<TaskManager>
    {
        [Test]
        public void should_schedule_missing_cover_recovery_without_running_it_at_startup()
        {
            Mocker.GetMock<IScheduledTaskRepository>().Setup(repository => repository.All()).Returns(Array.Empty<ScheduledTask>());

            Subject.Handle(new ApplicationStartedEvent());

            const string command = "NzbDrone.Core.MediaCover.RetryOpenLibraryBookCoversCommand";
            var scheduled = Subject.GetAll().Should().ContainSingle(task => task.TypeName == command).Subject;
            scheduled.Interval.Should().Be(1);
            scheduled.Priority.Should().Be(CommandPriority.Low);
            Subject.GetPending().Should().NotContain(task => task.TypeName == command);
        }
    }
}
