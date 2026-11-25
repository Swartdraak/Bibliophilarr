using System;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.Messaging.Commands;

namespace NzbDrone.Host.Test
{
    public partial class NestedAuthorBookAddFixture
    {
        [TestCase(false, false)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        public async Task DeleteAuthorRemovesOnlyItsFormatProfilesAndRollsBackOnFailure(bool rejectAuthorRowDelete, bool unavailableRoot)
        {
            var directory = Directory.CreateTempSubdirectory("bibliophilarr-author-delete-");
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

                using var host = BuildHost(appData, useFixtureProvider: false);
                await host.StartAsync(token);
                try
                {
                    using var client = CreateClient(host, apiKey);
                    var ebook = await FindProfile(client, "qualityprofile", "eBook", token);
                    var metadataProfile = await FindProfile(client, "metadataprofile", "None", token);
                    var root = new
                    {
                        name = "Author deletion fixture",
                        path = library,
                        defaultQualityProfileId = ebook,
                        defaultMetadataProfileId = metadataProfile,
                        defaultMonitorOption = "none",
                        defaultNewItemMonitorOption = "none"
                    };
                    await Post(client, "rootfolder", root, HttpStatusCode.Created, token);

                    Task<JsonElement> AddAuthor(string source, string name)
                    {
                        var settings = new
                        {
                            foreignAuthorId = source,
                            authorName = name,
                            rootFolderPath = library,
                            qualityProfileId = ebook,
                            metadataProfileId = metadataProfile,
                            monitored = false,
                            monitorNewItems = "none",
                            addOptions = new { monitor = "none", searchForMissingBooks = false }
                        };
                        return Post(client, "author", settings, HttpStatusCode.Created, token);
                    }

                    const string removedSource = "hardcover:author:741101";
                    const string retainedSource = "hardcover:author:741102";
                    var commands = host.Services.GetRequiredService<ICommandRepository>();
                    var previousCommands = commands.All().ToList();
                    var removedAuthor = await AddAuthor(removedSource, "Removed Fixture Author");
                    var removedId = removedAuthor.GetProperty("id").GetInt32();
                    await WaitForAuthorAddScan(client, commands, previousCommands, library, removedId, token);
                    previousCommands = commands.All().ToList();
                    var retainedAuthor = await AddAuthor(retainedSource, "Retained Fixture Author");
                    var retainedId = retainedAuthor.GetProperty("id").GetInt32();
                    await WaitForAuthorAddScan(client, commands, previousCommands, library, retainedId, token);
                    var profiles = host.Services.GetRequiredService<IAuthorFormatProfileRepository>();
                    var metadata = host.Services.GetRequiredService<IAuthorMetadataRepository>();
                    var removedProfiles = profiles.GetByAuthorId(removedId).Select(profile => profile.Id).OrderBy(id => id).ToArray();
                    var retainedProfiles = profiles.GetByAuthorId(retainedId).Select(profile => profile.Id).OrderBy(id => id).ToArray();
                    removedProfiles.Should().HaveCount(2);
                    retainedProfiles.Should().HaveCount(2);
                    var sources = new[] { removedSource, retainedSource }.ToList();
                    var metadataIds = metadata.FindById(sources).Select(row => row.Id).OrderBy(id => id).ToArray();
                    metadataIds.Should().HaveCount(2);

                    var removedPath = removedAuthor.GetProperty("path").GetString();
                    Directory.CreateDirectory(removedPath);
                    var witness = Path.Combine(removedPath, "keep.txt");
                    await File.WriteAllTextAsync(witness, "disposable media witness", token);

                    if (rejectAuthorRowDelete)
                    {
                        var connectionString = new SQLiteConnectionStringBuilder
                        {
                            DataSource = Path.Combine(appData, "bibliophilarr.db")
                        }.ConnectionString;
                        using var connection = new SQLiteConnection(connectionString);
                        connection.Open();
                        using var command = connection.CreateCommand();
                        command.CommandText = $"""
                            CREATE TRIGGER RefuseFixtureAuthorDelete
                            BEFORE DELETE ON Authors WHEN OLD.Id = {removedId}
                            BEGIN
                                SELECT RAISE(ABORT, 'fixture author row delete rejected');
                            END;
                            """;
                        command.ExecuteNonQuery();
                    }

                    if (unavailableRoot)
                    {
                        using var anonymous = CreateClient(host, apiKey);
                        anonymous.DefaultRequestHeaders.Remove("X-Api-Key");
                        foreach (var deleteFiles in new[] { "false", "true" })
                        {
                            using var unauthenticated = await anonymous.DeleteAsync($"author/{removedId}?deleteFiles={deleteFiles}", token);
                            unauthenticated.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
                        }

                        var offline = Path.Combine(directory.FullName, "offline-root");
                        Directory.Move(library, offline);
                        try
                        {
                            using var rejected = await client.DeleteAsync($"author/{removedId}?deleteFiles=true", token);
                            rejected.StatusCode.Should().Be(HttpStatusCode.Conflict);
                            using var stillPresent = await client.GetAsync($"author/{removedId}", token);
                            stillPresent.StatusCode.Should().Be(HttpStatusCode.OK);
                            profiles.GetByAuthorId(removedId).Select(profile => profile.Id).OrderBy(id => id)
                                .Should().Equal(removedProfiles);
                        }
                        finally
                        {
                            Directory.Move(offline, library);
                        }
                    }

                    using var response = await client.DeleteAsync($"author/{removedId}?deleteFiles=false", token);
                    if (rejectAuthorRowDelete)
                    {
                        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
                        using var stillPresent = await client.GetAsync($"author/{removedId}", token);
                        stillPresent.StatusCode.Should().Be(HttpStatusCode.OK);
                    }
                    else
                    {
                        response.IsSuccessStatusCode.Should().BeTrue();
                        using var absent = await client.GetAsync($"author/{removedId}", token);
                        absent.StatusCode.Should().Be(HttpStatusCode.NotFound);
                    }

                    profiles.GetByAuthorId(removedId).Select(profile => profile.Id).OrderBy(id => id)
                        .Should().Equal(rejectAuthorRowDelete ? removedProfiles : Array.Empty<int>());
                    profiles.GetByAuthorId(retainedId).Select(profile => profile.Id).OrderBy(id => id)
                        .Should().Equal(retainedProfiles);
                    metadata.FindById(sources).Select(row => row.Id).OrderBy(id => id).Should().Equal(metadataIds);
                    (await Get(client, $"author/{retainedId}", token)).GetProperty("id").GetInt32().Should().Be(retainedId);
                    (await File.ReadAllTextAsync(witness, token)).Should().Be("disposable media witness");
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
    }
}
