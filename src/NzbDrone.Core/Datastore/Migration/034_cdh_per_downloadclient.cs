using System;
using System.Data;
using Dapper;
using FluentMigrator;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration
{
    [Migration(158)]
    public class cdh_per_downloadclient : NzbDroneMigrationBase
    {
        protected override void MainDbUpgrade()
        {
            // Issue #344: make migration 158 idempotent. A prior-schema (pre-158)
            // DownloadClients table that already carries RemoveCompletedDownloads /
            // RemoveFailedDownloads must not make MigrateUp throw "duplicate column name"
            // and abort startup. The columns are added only when still absent. The check
            // runs as a live PRAGMA table_info query on the runner's own connection,
            // because the fluent Schema.Table(...).Column(...).Exists() check is blind to
            // columns added outside FluentMigrator (e.g. by a prior partial run) and
            // would re-issue the ALTER and crash. MoveRemoveSettings below is unchanged
            // and is itself idempotent (Config-key read + delete).
            Execute.WithConnection(AddColumnsWhenMissing);

            Execute.WithConnection(MoveRemoveSettings);
        }

        private void AddColumnsWhenMissing(IDbConnection conn, IDbTransaction tran)
        {
            var removeCompletedDownloadsExists = ColumnExists(conn, tran, "RemoveCompletedDownloads");
            var removeFailedDownloadsExists = ColumnExists(conn, tran, "RemoveFailedDownloads");

            if (!removeCompletedDownloadsExists)
            {
                var addRemoveCompletedDownloads = conn.CreateCommand(tran, "ALTER TABLE \"DownloadClients\" ADD COLUMN \"RemoveCompletedDownloads\" INTEGER NOT NULL DEFAULT 1");
                addRemoveCompletedDownloads.ExecuteNonQuery();
            }

            if (!removeFailedDownloadsExists)
            {
                var addRemoveFailedDownloads = conn.CreateCommand(tran, "ALTER TABLE \"DownloadClients\" ADD COLUMN \"RemoveFailedDownloads\" INTEGER NOT NULL DEFAULT 1");
                addRemoveFailedDownloads.ExecuteNonQuery();
            }
        }

        private static bool ColumnExists(IDbConnection conn, IDbTransaction tran, string column)
        {
            using (var command = conn.CreateCommand(tran, "PRAGMA table_info(DownloadClients)"))
            using (var reader = command.ExecuteReader())
            {
                while (reader.Read())
                {
                    // PRAGMA table_info column layout: 0=cid, 1=name, 2=type, 3=notnull, 4=dflt_value, 5=pk
                    if (string.Equals(reader.GetString(1), column, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private void MoveRemoveSettings(IDbConnection conn, IDbTransaction tran)
        {
            var removeCompletedDownloads = false;
            var removeFailedDownloads = true;

            using (var removeCompletedDownloadsCmd = conn.CreateCommand(tran, "SELECT \"Value\" FROM \"Config\" WHERE \"Key\" = 'removecompleteddownloads'"))
            {
                if ((removeCompletedDownloadsCmd.ExecuteScalar() as string)?.ToLower() == "true")
                {
                    removeCompletedDownloads = true;
                }
            }

            using (var removeFailedDownloadsCmd = conn.CreateCommand(tran, "SELECT \"Value\" FROM \"Config\" WHERE \"Key\" = 'removefaileddownloads'"))
            {
                if ((removeFailedDownloadsCmd.ExecuteScalar() as string)?.ToLower() == "false")
                {
                    removeFailedDownloads = false;
                }
            }

            var parameters = new { RemoveFailedDownloads = removeFailedDownloads, RemoveCompletedDownloads = removeCompletedDownloads };
            var updateSql = $"UPDATE \"DownloadClients\" SET \"RemoveCompletedDownloads\" = (CASE WHEN \"Implementation\" IN ('RTorrent', 'Flood') THEN 'false' ELSE @RemoveCompletedDownloads END), \"RemoveFailedDownloads\" = @RemoveFailedDownloads";
            conn.Execute(updateSql, parameters, transaction: tran);

            conn.Execute("DELETE FROM \"Config\" WHERE \"Key\" IN ('removecompleteddownloads', 'removefaileddownloads')");
        }
    }
}
