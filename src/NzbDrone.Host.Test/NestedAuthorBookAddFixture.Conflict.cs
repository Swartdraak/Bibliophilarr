using System;
using System.Data.SQLite;
using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using NzbDrone.Core.ImportLists.Exclusions;

namespace NzbDrone.Host.Test
{
    public partial class NestedAuthorBookAddFixture
    {
        [Test]
        public async Task ShouldKeepNewAuthorAndFormatProfilesOutOfTheLibraryWhenTheFirstEditionConflicts()
        {
            var directory = Directory.CreateTempSubdirectory("bibliophilarr-add-conflict-");
            var appData = directory.CreateSubdirectory("data").FullName;
            var library = directory.CreateSubdirectory("library").FullName;
            var apiKey = Guid.NewGuid().ToString("N");
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
            var token = timeout.Token;

            try
            {
                new XElement("Config",
                    new XElement("BindAddress", "127.0.0.1"),
                    new XElement("Port", 0),
                    new XElement("LaunchBrowser", false),
                    new XElement("AuthenticationMethod", "Forms"),
                    new XElement("ApiKey", apiKey)).Save(Path.Combine(appData, "config.xml"));

                using var host = BuildHost(appData, includeAudioEdition: true);
                await host.StartAsync(token);
                try
                {
                    using var client = CreateClient(host, apiKey);
                    var ebook = await FindProfile(client, "qualityprofile", "eBook", token);
                    var metadata = await FindProfile(client, "metadataprofile", "None", token);
                    var root = new
                    {
                        name = "Edition conflict fixture",
                        path = library,
                        defaultQualityProfileId = ebook,
                        defaultMetadataProfileId = metadata,
                        defaultMonitorOption = "none",
                        defaultNewItemMonitorOption = "none"
                    };
                    await Post(client, "rootfolder", root, HttpStatusCode.Created, token);

                    object AuthorSettings(string foreignAuthorId) => new
                    {
                        foreignAuthorId,
                        rootFolderPath = library,
                        qualityProfileId = ebook,
                        metadataProfileId = metadata,
                        monitored = false,
                        monitorNewItems = "none",
                        addOptions = new { monitor = "none", searchForMissingBooks = false }
                    };

                    var incumbent = BookRequest(ForeignBookId, ForeignEditionId, BookTitle);
                    incumbent["author"] = AuthorSettings(ForeignAuthorId);
                    var first = await Post(client, "book", incumbent, HttpStatusCode.Created, token);
                    var firstId = first.GetProperty("id").GetInt32();
                    var exclusions = host.Services.GetRequiredService<IImportListExclusionService>();
                    var authorExclusion = exclusions.Add(new ImportListExclusion
                    {
                        ForeignId = ConflictingForeignAuthorId,
                        Name = "Independent Author Fixture"
                    });
                    var workExclusion = exclusions.Add(new ImportListExclusion
                    {
                        ForeignId = ConflictingForeignBookId,
                        Name = "The Conflicting Work"
                    });

                    var before = await Get(client, "author", token);
                    before.GetArrayLength().Should().Be(1);
                    var database = Path.Combine(appData, "bibliophilarr.db");
                    var beforeCounts = GetConflictCatalogueCounts(database);
                    var rejected = BookRequest(ConflictingForeignBookId, ForeignEditionId, "The Conflicting Work");
                    rejected["author"] = AuthorSettings(ConflictingForeignAuthorId);

                    await Post(client, "book", rejected, HttpStatusCode.Conflict, token);

                    GetConflictCatalogueCounts(database).Should().Equal(beforeCounts);
                    (await Get(client, "author", token)).GetArrayLength().Should().Be(1);
                    (await Get(client, "book", token)).GetArrayLength().Should().Be(1);
                    (await Get(client, $"book/{firstId}", token)).GetProperty("foreignBookId").GetString().Should().Be(ForeignBookId);
                    exclusions.FindByForeignId(ConflictingForeignAuthorId).Id.Should().Be(authorExclusion.Id);
                    exclusions.FindByForeignId(ConflictingForeignBookId).Id.Should().Be(workExclusion.Id);
                }
                finally
                {
                    await host.StopAsync();
                }
            }
            finally
            {
                SQLiteConnection.ClearAllPools();
                directory.Delete(recursive: true);
            }
        }

        private static long[] GetConflictCatalogueCounts(string database)
        {
            using var connection = new SQLiteConnection(new SQLiteConnectionStringBuilder { DataSource = database, ReadOnly = true }.ConnectionString);
            connection.Open();
            using var query = connection.CreateCommand();
            query.CommandText = """
                SELECT (SELECT COUNT(*) FROM AuthorMetadata),
                       (SELECT COUNT(*) FROM Authors),
                       (SELECT COUNT(*) FROM AuthorFormatProfiles),
                       (SELECT COUNT(*) FROM Books),
                       (SELECT COUNT(*) FROM Editions)
                """;
            using var rows = query.ExecuteReader();
            rows.Read().Should().BeTrue();
            return new[] { rows.GetInt64(0), rows.GetInt64(1), rows.GetInt64(2), rows.GetInt64(3), rows.GetInt64(4) };
        }
    }
}
