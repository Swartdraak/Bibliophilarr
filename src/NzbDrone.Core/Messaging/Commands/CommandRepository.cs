using System;
using System.Collections.Generic;
using System.Linq;
using Dapper;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.Messaging.Commands
{
    public interface ICommandRepository : IBasicRepository<CommandModel>
    {
        void Trim();
        void OrphanStarted(IReadOnlyCollection<int> liveCommandIds);
        List<CommandModel> Queued();
        void Start(CommandModel command);
        void End(CommandModel command);
    }

    public class CommandRepository : BasicRepository<CommandModel>, ICommandRepository
    {
        public CommandRepository(IMainDatabase database, IEventAggregator eventAggregator)
            : base(database, eventAggregator)
        {
        }

        public void Trim()
        {
            var date = DateTime.UtcNow.AddDays(-1);

            Delete(c => c.EndedAt < date);
        }

        public void OrphanStarted(IReadOnlyCollection<int> liveCommandIds)
        {
            const string select = @"SELECT ""Id"" FROM ""Commands"" WHERE ""Status"" = @Started";
            const string update = @"UPDATE ""Commands"" SET ""Status"" = @Orphaned, ""EndedAt"" = @Ended WHERE ""Id"" = @Id AND ""Status"" = @Started";
            const int started = (int)CommandStatus.Started;
            var ended = DateTime.UtcNow;

            using var conn = _database.OpenConnection();
            using var transaction = conn.BeginTransaction();
            var interrupted = conn.Query<int>(select, new { Started = started }, transaction)
                .Except(liveCommandIds)
                .Select(id => new { Id = id, Started = started, Ended = ended, Orphaned = (int)CommandStatus.Orphaned });
            conn.Execute(update, interrupted, transaction);
            transaction.Commit();
        }

        public List<CommandModel> Queued()
        {
            return Query(c => c.Status == CommandStatus.Queued);
        }

        public void Start(CommandModel command)
        {
            SetFields(command, c => c.StartedAt, c => c.Status);
        }

        public void End(CommandModel command)
        {
            SetFields(command, c => c.EndedAt, c => c.Status, c => c.Result, c => c.Duration, c => c.Exception);
        }
    }
}
