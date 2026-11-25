using System;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.MetadataSource;

namespace NzbDrone.Host.Test
{
    public partial class NestedAuthorBookAddFixture
    {
        private const string MarkerIncumbentAuthor = "openlibrary:author:OL991101A";
        private const string MarkerNewAuthor = "openlibrary:author:OL991102A";
        private const string MarkerIncumbentWork = "openlibrary:work:OL991111W";
        private const string MarkerConflictingWork = "openlibrary:work:OL991112W";
        private const string MarkerValidWork = "openlibrary:work:OL991113W";
        private const string MarkerOwnedEdition = "openlibrary:edition:OL991121M";
        private const string MarkerUnownedEdition = "openlibrary:edition:OL991122M";
        private const string SeededOverview = "Verified incumbent metadata without a library author.";

        [TestCase(false)]
        [TestCase(true)]
        public async Task ShouldAtomicallyReuseExactMetadataForAFirstBookAddedFromOpenLibrary(bool selectExactEdition)
        {
            var directory = Directory.CreateTempSubdirectory("bibliophilarr-marker-owner-");
            var appData = directory.CreateSubdirectory("data").FullName;
            var library = directory.CreateSubdirectory("library").FullName;
            var apiKey = Guid.NewGuid().ToString("N");
            var database = Path.Combine(appData, "bibliophilarr.db");
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

                int seedId;
                int newAuthorId;
                int validBookId;
                int validEditionId;
                using (var host = BuildHost(appData, fixtureProvider: CreateOpenLibraryWorkMarkerProvider()))
                {
                    await host.StartAsync(token);
                    try
                    {
                        using var client = CreateClient(host, apiKey);
                        var quality = await FindProfile(client, "qualityprofile", "eBook", token);
                        var metadata = await FindProfile(client, "metadataprofile", "None", token);
                        var root = new
                        {
                            name = "Open Library metadata reuse fixture",
                            path = library,
                            defaultQualityProfileId = quality,
                            defaultMetadataProfileId = metadata,
                            defaultMonitorOption = "none",
                            defaultNewItemMonitorOption = "none"
                        };
                        await Post(client, "rootfolder", root, HttpStatusCode.Created, token);

                        object AuthorSettings(string source) => new
                        {
                            foreignAuthorId = source,
                            rootFolderPath = library,
                            qualityProfileId = quality,
                            metadataProfileId = metadata,
                            monitored = false,
                            monitorNewItems = "none",
                            addOptions = new { monitor = "none", searchForMissingBooks = false }
                        };

                        var incumbent = BookRequest(MarkerIncumbentWork, MarkerOwnedEdition, "Incumbent Marker Work");
                        incumbent["author"] = AuthorSettings(MarkerIncumbentAuthor);
                        var incumbentBook = await Post(client, "book", incumbent, HttpStatusCode.Created, token);

                        var seed = CreateMarkerMetadata(MarkerNewAuthor);
                        seed.Overview = SeededOverview;
                        host.Services.GetRequiredService<IAuthorMetadataRepository>().Insert(seed);
                        seedId = seed.Id;
                        ReadMarkerAuthor(database).Should().Be((seedId, SeededOverview, 0));
                        var before = GetConflictCatalogueCounts(database);

                        var conflict = BookRequest(MarkerConflictingWork, selectExactEdition ? MarkerOwnedEdition : MarkerConflictingWork, "Colliding Marker Work");
                        if (selectExactEdition)
                        {
                            conflict.Remove("editions");
                        }

                        conflict["author"] = AuthorSettings(MarkerNewAuthor);
                        await Post(client, "book", conflict, HttpStatusCode.Conflict, token);

                        ReadMarkerAuthor(database).Should().Be((seedId, SeededOverview, 0));
                        GetConflictCatalogueCounts(database).Should().Equal(before);
                        (await Get(client, "author", token)).GetArrayLength().Should().Be(1);
                        (await Get(client, "book", token)).GetArrayLength().Should().Be(1);
                        var winnerId = incumbentBook.GetProperty("id").GetInt32();
                        var winner = await Get(client, $"book/{winnerId}", token);
                        winner.GetProperty("foreignEditionId").GetString().Should().Be(MarkerOwnedEdition);

                        var valid = BookRequest(MarkerValidWork, selectExactEdition ? MarkerUnownedEdition : MarkerValidWork, "Independent Marker Work");
                        if (selectExactEdition)
                        {
                            valid.Remove("editions");
                        }

                        valid["author"] = AuthorSettings(MarkerNewAuthor);
                        var added = await Post(client, "book", valid, HttpStatusCode.Created, token);
                        newAuthorId = added.GetProperty("authorId").GetInt32();
                        validBookId = added.GetProperty("id").GetInt32();
                        validEditionId = added.GetProperty("editions").EnumerateArray().Single().GetProperty("id").GetInt32();

                        ReadMarkerAuthor(database).Should().Be((seedId, SeededOverview, newAuthorId));
                        GetConflictCatalogueCounts(database).Should().Equal(before[0], before[1] + 1, before[2] + 2, before[3] + 1, before[4] + 1);
                        added.GetProperty("foreignEditionId").GetString().Should().Be(MarkerUnownedEdition);
                    }
                    finally
                    {
                        await host.StopAsync();
                    }
                }

                using var restarted = BuildHost(appData, useFixtureProvider: false);
                await restarted.StartAsync(token);
                try
                {
                    using var client = CreateClient(restarted, apiKey);
                    var added = await Get(client, $"book/{validBookId}", token);
                    added.GetProperty("authorId").GetInt32().Should().Be(newAuthorId);
                    added.GetProperty("foreignEditionId").GetString().Should().Be(MarkerUnownedEdition);
                    var edition = added.GetProperty("editions").EnumerateArray().Single();
                    edition.GetProperty("id").GetInt32().Should().Be(validEditionId);
                    edition.GetProperty("isEbook").GetBoolean().Should().BeTrue();
                    ReadMarkerAuthor(database).Should().Be((seedId, SeededOverview, newAuthorId));
                }
                finally
                {
                    await restarted.StopAsync();
                }
            }
            finally
            {
                SQLiteConnection.ClearAllPools();
                directory.Delete(recursive: true);
            }
        }

        private static IMetadataProvider CreateOpenLibraryWorkMarkerProvider()
        {
            var provider = new Mock<IMetadataProvider>();
            provider.SetupGet(value => value.ProviderName).Returns("OpenLibrary");
            provider.SetupGet(value => value.IsEnabled).Returns(true);
            provider.SetupGet(value => value.SupportsBookSearch).Returns(true);
            var books = provider.As<IProvideBookInfo>();
            books.Setup(value => value.GetBookInfo("OL991111W"))
                .Returns(() => MarkerWorkMetadata(MarkerIncumbentWork, MarkerOwnedEdition, MarkerIncumbentAuthor, "Incumbent Marker Work"));
            books.Setup(value => value.GetBookInfo("OL991112W"))
                .Returns(() => MarkerWorkMetadata(MarkerConflictingWork, MarkerOwnedEdition, MarkerNewAuthor, "Colliding Marker Work"));
            books.Setup(value => value.GetBookInfo("OL991113W"))
                .Returns(() => MarkerWorkMetadata(MarkerValidWork, MarkerUnownedEdition, MarkerNewAuthor, "Independent Marker Work"));
            return provider.Object;
        }

        private static Tuple<string, Book, System.Collections.Generic.List<AuthorMetadata>> MarkerWorkMetadata(
            string work, string edition, string author, string title)
        {
            var result = BookMetadata(work, edition, title);
            var metadata = CreateMarkerMetadata(author);
            metadata.Overview = "New provider metadata should not overwrite the seeded row in this run.";
            result.Item2.AuthorMetadata = metadata;
            result.Item3[0] = metadata;
            return Tuple.Create(metadata.ForeignAuthorId, result.Item2, result.Item3);
        }

        private static AuthorMetadata CreateMarkerMetadata(string source)
        {
            var metadata = AuthorMetadata();
            metadata.ForeignAuthorId = source;
            metadata.Name = source == MarkerNewAuthor ? "New Marker Fixture Author" : "Incumbent Marker Fixture Author";
            metadata.TitleSlug = source == MarkerNewAuthor ? "new-marker-fixture-author" : "incumbent-marker-fixture-author";
            return metadata;
        }

        private static (int Id, string Overview, int OwnerId) ReadMarkerAuthor(string database)
        {
            using var connection = new SQLiteConnection(new SQLiteConnectionStringBuilder { DataSource = database, ReadOnly = true }.ConnectionString);
            connection.Open();
            using var query = connection.CreateCommand();
            query.CommandText = """
                SELECT metadata.Id, metadata.Overview, COALESCE(author.Id, 0)
                FROM AuthorMetadata AS metadata
                LEFT JOIN Authors AS author ON author.AuthorMetadataId = metadata.Id
                WHERE metadata.ForeignAuthorId = @source
                """;
            query.Parameters.AddWithValue("@source", MarkerNewAuthor);
            using var rows = query.ExecuteReader();
            rows.Read().Should().BeTrue();
            var found = (rows.GetInt32(0), rows.GetString(1), rows.GetInt32(2));
            rows.Read().Should().BeFalse();
            return found;
        }
    }
}
