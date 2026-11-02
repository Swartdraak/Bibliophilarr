using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using NzbDrone.Common.Serializer;
using NzbDrone.Core.Datastore.Migration;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Datastore.Migration
{
    [TestFixture]
    public class cdh_per_downloadclientFixture : MigrationTest<cdh_per_downloadclient>
    {
        [Test]
        public void should_set_cdh_to_enabled()
        {
            var db = WithMigrationTestDb(c =>
            {
                c.Insert.IntoTable("DownloadClients").Row(new
                {
                    Enable = true,
                    Name = "Deluge",
                    Implementation = "Deluge",
                    Priority = 1,
                    Settings = new DelugeSettings34
                    {
                        Host = "127.0.0.1",
                        TvCategory = "abc",
                        UrlBase = "/my/"
                    }.ToJson(),
                    ConfigContract = "DelugeSettings"
                });
            });

            var items = db.Query<DownloadClientDefinition34>("SELECT * FROM \"DownloadClients\"");

            items.Should().HaveCount(1);
            items.First().RemoveCompletedDownloads.Should().BeFalse();
            items.First().RemoveFailedDownloads.Should().BeTrue();
        }

        [Test]
        public void should_set_cdh_to_disabled_when_globally_disabled()
        {
            var db = WithMigrationTestDb(c =>
            {
                c.Insert.IntoTable("Config").Row(new
                {
                    Key = "removecompleteddownloads",
                    Value = "True"
                });

                c.Insert.IntoTable("DownloadClients").Row(new
                {
                    Enable = true,
                    Name = "Deluge",
                    Implementation = "Deluge",
                    Priority = 1,
                    Settings = new DelugeSettings34
                    {
                        Host = "127.0.0.1",
                        TvCategory = "abc",
                        UrlBase = "/my/"
                    }.ToJson(),
                    ConfigContract = "DelugeSettings"
                });
            });

            var items = db.Query<DownloadClientDefinition34>("SELECT * FROM \"DownloadClients\"");

            items.Should().HaveCount(1);
            items.First().RemoveCompletedDownloads.Should().BeTrue();
            items.First().RemoveFailedDownloads.Should().BeTrue();
        }

        [Test]
        public void should_disable_remove_for_existing_rtorrent()
        {
            var db = WithMigrationTestDb(c =>
            {
                c.Insert.IntoTable("DownloadClients").Row(new
                {
                    Enable = true,
                    Name = "RTorrent",
                    Implementation = "RTorrent",
                    Priority = 1,
                    Settings = new RTorrentSettings34
                    {
                        Host = "127.0.0.1",
                        MusicCategory = "abc",
                        UrlBase = "/my/"
                    }.ToJson(),
                    ConfigContract = "RTorrentSettings"
                });
            });

            var items = db.Query<DownloadClientDefinition34>("SELECT * FROM \"DownloadClients\"");

            items.Should().HaveCount(1);
            items.First().RemoveCompletedDownloads.Should().BeFalse();
            items.First().RemoveFailedDownloads.Should().BeTrue();
        }

        // Issue #344: re-run / already-columnized case. The test DB is built at the pre-158 schema, so
        // DownloadClients exists but lacks RemoveCompletedDownloads/RemoveFailedDownloads (migration 158
        // is what adds them). To reproduce a prior-schema DB that ALREADY carries the columns, add them
        // here (raw DDL, executed inside the same migration transaction before MainDbUpgrade) and seed a
        // real Deluge row whose values DIFFER from the column defaults (RemoveCompletedDownloads=true /
        // RemoveFailedDownloads=true), so a successful migration must have both skipped the AddColumn
        // (live PRAGMA table_info check on the runner connection) AND preserved the row's data through
        // MoveRemoveSettings. Unfixed, the bare AddColumn throws "duplicate column name" and aborts
        // startup; with the guard migration 158 completes. Deluge is not RTorrent/Flood, so the CASE
        // keeps the row's seeded RemoveCompletedDownloads=false; no Config keys means
        // RemoveFailedDownloads=true, which the row already carries.
        [Test]
        public void should_migrate_idempotently_when_columns_already_present()
        {
            var db = WithMigrationTestDb(c =>
            {
                // Pre-columnize DownloadClients to reproduce the already-columnized prior-schema DB.
                c.Execute.Sql("ALTER TABLE \"DownloadClients\" ADD COLUMN \"RemoveCompletedDownloads\" INTEGER NOT NULL DEFAULT 1");
                c.Execute.Sql("ALTER TABLE \"DownloadClients\" ADD COLUMN \"RemoveFailedDownloads\" INTEGER NOT NULL DEFAULT 1");

                // Seed a real row whose RemoveCompletedDownloads DIFFERS from the column default (1),
                // so the assertion below proves the data migration preserved the value rather than
                // resetting it to the default.
                c.Insert.IntoTable("DownloadClients").Row(new
                {
                    Enable = true,
                    Name = "Deluge",
                    Implementation = "Deluge",
                    Priority = 1,
                    Settings = new DelugeSettings34
                    {
                        Host = "127.0.0.1",
                        TvCategory = "abc",
                        UrlBase = "/my/"
                    }.ToJson(),
                    ConfigContract = "DelugeSettings",
                    RemoveCompletedDownloads = false,
                    RemoveFailedDownloads = true
                });
            });

            var items = db.Query<DownloadClientDefinition34>("SELECT * FROM \"DownloadClients\"");

            items.Should().HaveCount(1);

            // RemoveCompletedDownloads is false, not the column default true: the column was not
            // re-created/reset, and MoveRemoveSettings preserved the seeded value (Deluge is not
            // RTorrent/Flood, so the CASE keeps it false).
            items.First().RemoveCompletedDownloads.Should().BeFalse();
            items.First().RemoveFailedDownloads.Should().BeTrue();
        }
    }

    public class DelugeSettings34
    {
        public string Host { get; set; }
        public int Port { get; set; }
        public string UrlBase { get; set; }
        public string Password { get; set; }
        public string TvCategory { get; set; }
        public int RecentTvPriority { get; set; }
        public int OlderTvPriority { get; set; }
        public bool UseSsl { get; set; }
    }

    public class RTorrentSettings34
    {
        public string Host { get; set; }
        public int Port { get; set; }
        public string UrlBase { get; set; }
        public string MusicCategory { get; set; }
    }

    public class DownloadClientDefinition34
    {
        public int Id { get; set; }
        public bool Enable { get; set; }
        public int Priority { get; set; }
        public string Name { get; set; }
        public string Implementation { get; set; }
        public JObject Settings { get; set; }
        public string ConfigContract { get; set; }
        public bool RemoveCompletedDownloads { get; set; }
        public bool RemoveFailedDownloads { get; set; }
        public List<string> Tags { get; set; }
    }
}
