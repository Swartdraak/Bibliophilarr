using Dapper;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.Housekeeping.Housekeepers
{
    public class FixMultipleMonitoredEditions : IHousekeepingTask
    {
        private readonly IMainDatabase _database;
        private readonly IConfigService _configService;

        public FixMultipleMonitoredEditions(IMainDatabase database, IConfigService configService)
        {
            _database = database;
            _configService = configService;
        }

        public void Clean()
        {
            using var mapper = _database.OpenConnection();

            var groupBy = _configService.EnableDualFormatTracking
                ? @"""BookId"", ""IsEbook"""
                : @"""BookId""";

            if (_database.DatabaseType == DatabaseType.PostgreSQL)
            {
                mapper.Execute($@"UPDATE ""Editions""
                                SET ""Monitored"" = true
                                WHERE ""Id"" IN (
                                    SELECT MIN(""Id"")
                                    FROM ""Editions""
                                    WHERE ""Monitored"" = true
                                    GROUP BY {groupBy}
                                    HAVING COUNT(""BookId"") > 1
                                )");
            }
            else
            {
                mapper.Execute($@"UPDATE ""Editions""
                                SET ""Monitored"" = 0
                                WHERE ""Id"" IN (
                                    SELECT MIN(""Id"")
                                    FROM ""Editions""
                                    WHERE ""Monitored"" = 1
                                    GROUP BY {groupBy}
                                    HAVING COUNT(""BookId"") > 1
                                )");
            }
        }
    }
}
