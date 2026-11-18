using System;
using System.Data.SQLite;
using Npgsql;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Configuration;

namespace NzbDrone.Core.Datastore
{
    public interface IConnectionStringFactory
    {
        DatabaseConnectionInfo MainDbConnection { get; }
        DatabaseConnectionInfo LogDbConnection { get; }
        DatabaseConnectionInfo CacheDbConnection { get; }
        string GetDatabasePath(string connectionString);
    }

    public class ConnectionStringFactory : IConnectionStringFactory
    {
        private readonly IConfigFileProvider _configFileProvider;

        public ConnectionStringFactory(IAppFolderInfo appFolderInfo, IConfigFileProvider configFileProvider)
        {
            _configFileProvider = configFileProvider;

            MainDbConnection = _configFileProvider.PostgresHost.IsNotNullOrWhiteSpace() ? GetPostgresConnectionString(_configFileProvider.PostgresMainDb) :
                GetConnectionString(appFolderInfo.GetDatabase());

            LogDbConnection = _configFileProvider.PostgresHost.IsNotNullOrWhiteSpace() ? GetPostgresConnectionString(_configFileProvider.PostgresLogDb) :
                GetConnectionString(appFolderInfo.GetLogDatabase());

            CacheDbConnection = _configFileProvider.PostgresHost.IsNotNullOrWhiteSpace() ? GetPostgresConnectionString(_configFileProvider.PostgresCacheDb) :
                GetConnectionString(appFolderInfo.GetCacheDatabase());
        }

        public DatabaseConnectionInfo MainDbConnection { get; private set; }
        public DatabaseConnectionInfo LogDbConnection { get; private set; }
        public DatabaseConnectionInfo CacheDbConnection { get; private set; }

        public string GetDatabasePath(string connectionString)
        {
            var connectionBuilder = new SQLiteConnectionStringBuilder(connectionString);

            return connectionBuilder.DataSource;
        }

        private static DatabaseConnectionInfo GetConnectionString(string dbPath)
        {
            // Issue #270: the macOS SQLite provider rejects the page-cache
            // keyword, so the -20000 hint is only emitted for non-osx hosts
            // (see the osx gate below); every other keyword is unchanged.
            var connectionBuilder = new SQLiteConnectionStringBuilder
            {
                DataSource = dbPath,
                DateTimeKind = DateTimeKind.Utc,
                JournalMode = OsInfo.IsOsx ? SQLiteJournalModeEnum.Truncate : SQLiteJournalModeEnum.Wal,
                Pooling = true,
                Version = 3,
                BusyTimeout = 100
            };

            if (OsInfo.IsOsx)
            {
                connectionBuilder.Add("Full FSync", true);
            }
            else
            {
                connectionBuilder.CacheSize = -20000;
            }

            return new DatabaseConnectionInfo(DatabaseType.SQLite, connectionBuilder.ConnectionString);
        }

        private DatabaseConnectionInfo GetPostgresConnectionString(string dbName)
        {
            var connectionBuilder = new NpgsqlConnectionStringBuilder
            {
                Database = dbName,
                Host = _configFileProvider.PostgresHost,
                Username = _configFileProvider.PostgresUser,
                Password = _configFileProvider.PostgresPassword,
                Port = _configFileProvider.PostgresPort,
                Enlist = false
            };

            return new DatabaseConnectionInfo(DatabaseType.PostgreSQL, connectionBuilder.ConnectionString);
        }
    }
}
