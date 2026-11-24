using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using DryIoc;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Common.Http.Dispatchers;
using NzbDrone.Common.Options;
using NzbDrone.Core.Books;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.ImportLists.Exclusions;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.Qualities;

namespace NzbDrone.Host.Test
{
    [TestFixture]
    [NonParallelizable]
    public class NestedAuthorBookAddFixture
    {
        private const string ForeignAuthorId = "hardcover:author:92627";
        private const string ForeignBookId = "hardcover:work:92628";
        private const string ForeignEditionId = "hardcover:edition:92629";
        private const string OtherForeignBookId = "hardcover:work:92630";
        private const string OtherForeignEditionId = "hardcover:edition:92631";
        private const string AuthorName = "Nested Author Fixture";
        private const string BookTitle = "The Nested Author Fixture";
        private const string OtherBookTitle = "The Companion Fixture";

        [Test]
        public async Task AddBookRequiresAnEditionAndAnAuthorPathAndPersistsExplicitSelectionAcrossRestart()
        {
            var directory = Directory.CreateTempSubdirectory("bibliophilarr-add-book-").FullName;
            var appData = Directory.CreateDirectory(Path.Combine(directory, "data")).FullName;
            var library = Directory.CreateDirectory(Path.Combine(directory, "library")).FullName;
            var apiKey = Guid.NewGuid().ToString("N");
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));

            new XElement("Config",
                new XElement("BindAddress", "127.0.0.1"),
                new XElement("Port", 0),
                new XElement("LaunchBrowser", false),
                new XElement("AuthenticationMethod", "Forms"),
                new XElement("ApiKey", apiKey)).Save(Path.Combine(appData, "config.xml"));

            try
            {
                int bookId;
                int authorId;
                int editionId;
                using (var host = BuildHost(appData))
                {
                    await host.StartAsync(timeout.Token);
                    try
                    {
                        host.Services.GetRequiredService<IMetadataProviderRegistry>().GetAllProviders()
                            .Should().ContainSingle().Which.ProviderName.Should().Be("Hardcover");
                        using var client = CreateClient(host, apiKey);
                        var ebook = await FindProfile(client, "qualityprofile", "eBook", timeout.Token);
                        var metadata = await FindProfile(client, "metadataprofile", "None", timeout.Token);

                        var root = new
                        {
                            name = "Nested author fixture",
                            path = library,
                            defaultQualityProfileId = ebook,
                            defaultMetadataProfileId = metadata,
                            defaultMonitorOption = "none",
                            defaultNewItemMonitorOption = "none",
                            defaultTags = Array.Empty<int>()
                        };
                        await Post(client, "rootfolder", root, HttpStatusCode.Created, timeout.Token);

                        var exclusions = host.Services.GetRequiredService<IImportListExclusionService>();
                        var exclusion = exclusions.Add(new ImportListExclusion { ForeignId = ForeignBookId, Name = BookTitle });

                        var request = BookRequest(ForeignBookId, ForeignEditionId, BookTitle);
                        var errors = await Post(client, "book", request, HttpStatusCode.BadRequest, timeout.Token);
                        errors.EnumerateArray().Should().Contain(error =>
                            error.GetProperty("propertyName").GetString() == "Author.RootFolderPath");
                        (await Get(client, "author", timeout.Token)).GetArrayLength().Should().Be(0);
                        (await Get(client, "book", timeout.Token)).GetArrayLength().Should().Be(0);
                        var retained = exclusions.FindByForeignId(ForeignBookId);
                        retained.Should().NotBeNull();
                        retained.Id.Should().Be(exclusion.Id);

                        request["author"] = new
                        {
                            foreignAuthorId = ForeignAuthorId,
                            rootFolderPath = library,
                            monitored = false,
                            monitorNewItems = "none"
                        };

                        var missingProfiles = await Post(client, "book", request, HttpStatusCode.BadRequest, timeout.Token);
                        missingProfiles.EnumerateArray()
                            .Select(error => error.GetProperty("propertyName").GetString())
                            .Should().BeEquivalentTo(new[] { "Author.QualityProfileId", "Author.MetadataProfileId" });
                        (await Get(client, "author", timeout.Token)).GetArrayLength().Should().Be(0);
                        (await Get(client, "book", timeout.Token)).GetArrayLength().Should().Be(0);
                        exclusions.FindByForeignId(ForeignBookId).Id.Should().Be(exclusion.Id);

                        request["author"] = new
                        {
                            foreignAuthorId = ForeignAuthorId,
                            rootFolderPath = library,
                            qualityProfileId = ebook,
                            metadataProfileId = metadata,
                            monitored = false,
                            monitorNewItems = "none",
                            addOptions = new { monitor = "none", searchForMissingBooks = false }
                        };

                        var incompleteRequest = new Dictionary<string, object>(request);
                        incompleteRequest.Remove("editions").Should().BeTrue();
                        await AssertMissingEditions(client, incompleteRequest, timeout.Token);
                        incompleteRequest["editions"] = null;
                        await AssertMissingEditions(client, incompleteRequest, timeout.Token);
                        incompleteRequest["editions"] = Array.Empty<object>();
                        await AssertMissingEditions(client, incompleteRequest, timeout.Token);

                        (await Get(client, "author", timeout.Token)).GetArrayLength().Should().Be(0);
                        (await Get(client, "book", timeout.Token)).GetArrayLength().Should().Be(0);
                        exclusions.FindByForeignId(ForeignBookId).Id.Should().Be(exclusion.Id);

                        var added = await Post(client, "book", request, HttpStatusCode.Created, timeout.Token);
                        exclusions.FindByForeignId(ForeignBookId).Should().BeNull();
                        bookId = added.GetProperty("id").GetInt32();
                        authorId = added.GetProperty("authorId").GetInt32();
                        editionId = AssertIdentity(added, bookId, authorId, ForeignBookId, ForeignEditionId);
                        var saved = await Get(client, $"book/{bookId}", timeout.Token);
                        AssertIdentity(saved, bookId, authorId, ForeignBookId, ForeignEditionId).Should().Be(editionId);
                        var author = await Get(client, $"author/{authorId}", timeout.Token);
                        author.GetProperty("foreignAuthorId").GetString().Should().Be(ForeignAuthorId);
                        Path.GetDirectoryName(author.GetProperty("path").GetString()).Should().Be(library);

                        var otherRequest = BookRequest(OtherForeignBookId, OtherForeignEditionId, OtherBookTitle);
                        var other = await Post(client, "book", otherRequest, HttpStatusCode.Created, timeout.Token);
                        other.GetProperty("id").GetInt32().Should().NotBe(bookId);
                        AssertIdentity(other, other.GetProperty("id").GetInt32(), authorId, OtherForeignBookId, OtherForeignEditionId);
                        (await Get(client, "author", timeout.Token)).GetArrayLength().Should().Be(1);
                    }
                    finally
                    {
                        await host.StopAsync();
                    }
                }

                SQLiteConnection.ClearAllPools();

                using var restarted = BuildHost(appData, useFixtureProvider: false);
                await restarted.StartAsync(timeout.Token);
                try
                {
                    restarted.Services.GetRequiredService<IMetadataProviderRegistry>().GetAllProviders().Should().BeEmpty();
                    using var client = CreateClient(restarted, apiKey);
                    var saved = await Get(client, $"book/{bookId}", timeout.Token);
                    AssertIdentity(saved, bookId, authorId, ForeignBookId, ForeignEditionId).Should().Be(editionId);

                    var author = await Get(client, $"author/{authorId}", timeout.Token);
                    author.GetProperty("id").GetInt32().Should().Be(authorId);
                    author.GetProperty("foreignAuthorId").GetString().Should().Be(ForeignAuthorId);
                    author.GetProperty("authorName").GetString().Should().Be(AuthorName);
                    Path.GetDirectoryName(author.GetProperty("path").GetString()).Should().Be(library);
                }
                finally
                {
                    await restarted.StopAsync();
                }
            }
            finally
            {
                SQLiteConnection.ClearAllPools();
                Directory.Delete(directory, recursive: true);
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task ShouldAddASelectedAudiobookToAnExistingEbookOrRejectAnEditionOwnedByAnotherWork(bool conflictingEditionOwner)
        {
            const string audiobookId = "hardcover:source-edition:92632";
            var selectedId = conflictingEditionOwner ? OtherForeignEditionId : audiobookId;
            var profile = Directory.CreateTempSubdirectory("bibliophilarr-existing-work-edition-");
            var appData = profile.CreateSubdirectory("data").FullName;
            var library = profile.CreateSubdirectory("library").FullName;
            var key = Guid.NewGuid().ToString("N");
            string offeredAudiobook = null;
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
            var token = timeout.Token;

            try
            {
                new XElement("Config",
                    new XElement("AuthenticationMethod", "Forms"),
                    new XElement("ApiKey", key)).Save(Path.Combine(appData, "config.xml"));
                using var host = BuildHost(appData, offeredAudiobook: () => offeredAudiobook);
                await host.StartAsync(token);
                try
                {
                    using var client = CreateClient(host, key);
                    var quality = await FindProfile(client, "qualityprofile", "eBook", token);
                    var metadata = await FindProfile(client, "metadataprofile", "None", token);
                    var rootRequest = new
                    {
                        name = "Existing work",
                        path = library,
                        defaultQualityProfileId = quality,
                        defaultMetadataProfileId = metadata,
                        defaultMonitorOption = "none",
                        defaultNewItemMonitorOption = "none",
                        defaultTags = Array.Empty<int>()
                    };
                    await Post(client, "rootfolder", rootRequest, HttpStatusCode.Created, token);

                    var first = BookRequest(ForeignBookId, ForeignEditionId, BookTitle);
                    first["author"] = new
                    {
                        foreignAuthorId = ForeignAuthorId,
                        rootFolderPath = library,
                        qualityProfileId = quality,
                        metadataProfileId = metadata,
                        monitored = false,
                        monitorNewItems = "none"
                    };
                    var added = await Post(client, "book", first, HttpStatusCode.Created, token);
                    var bookId = added.GetProperty("id").GetInt32();
                    var authorId = added.GetProperty("authorId").GetInt32();
                    var ebookId = AssertIdentity(added, bookId, authorId, ForeignBookId, ForeignEditionId);
                    var queue = host.Services.GetRequiredService<IManageCommandQueue>();
                    while (!queue.All().Any(command => command.Name == "RefreshAuthor" && command.EndedAt.HasValue))
                    {
                        await Task.Delay(TimeSpan.FromMilliseconds(100), token);
                    }

                    var filePath = Path.Combine(library, "fixture.epub");
                    File.WriteAllText(filePath, "Existing test-owned file");
                    var files = host.Services.GetRequiredService<IMediaFileService>();
                    var ebookFile = files.Add(new BookFile { EditionId = ebookId, Path = filePath, Quality = new QualityModel(Quality.EPUB) });
                    ebookFile.Id.Should().BePositive();

                    var otherBookId = 0;
                    var otherEditionId = 0;
                    if (conflictingEditionOwner)
                    {
                        var other = await Post(client, "book", BookRequest(OtherForeignBookId, OtherForeignEditionId, OtherBookTitle), HttpStatusCode.Created, token);
                        otherBookId = other.GetProperty("id").GetInt32();
                        otherEditionId = AssertIdentity(other, otherBookId, authorId, OtherForeignBookId, OtherForeignEditionId);
                    }

                    var exclusions = host.Services.GetRequiredService<IImportListExclusionService>();
                    var exclusion = exclusions.Add(new ImportListExclusion { ForeignId = ForeignBookId, Name = BookTitle });
                    offeredAudiobook = selectedId;
                    var selection = BookRequest(ForeignBookId, selectedId, BookTitle);
                    var lastCommandId = queue.All().Select(command => command.Id).DefaultIfEmpty().Max();
                    var result = await Post(client, "book", selection, conflictingEditionOwner ? HttpStatusCode.BadRequest : HttpStatusCode.Created, token);
                    if (!conflictingEditionOwner)
                    {
                        while (!queue.All().Any(command => command.Id > lastCommandId && command.Name == "RefreshAuthor" && command.EndedAt.HasValue))
                        {
                            await Task.Delay(TimeSpan.FromMilliseconds(100), token);
                        }
                    }

                    var readback = await Get(client, $"book/{bookId}", token);
                    readback.GetProperty("foreignBookId").GetString().Should().Be(ForeignBookId);
                    readback.GetProperty("authorId").GetInt32().Should().Be(authorId);
                    var editions = host.Services.GetRequiredService<IEditionService>().GetEditionsByBook(bookId);
                    var ebook = editions.Single(edition => edition.ForeignEditionId == ForeignEditionId);
                    ebook.Id.Should().Be(ebookId);
                    ebook.ManualAdd.Should().BeTrue();
                    ebook.Monitored.Should().BeTrue();
                    files.Get(ebookFile.Id).EditionId.Should().Be(ebookId);
                    files.Get(ebookFile.Id).Path.Should().Be(filePath);
                    File.ReadAllText(filePath).Should().Be("Existing test-owned file");

                    if (conflictingEditionOwner)
                    {
                        result.EnumerateArray().Should().Contain(error => error.GetProperty("propertyName").GetString() == "Editions");
                        editions.Should().ContainSingle();
                        AssertIdentity(await Get(client, $"book/{otherBookId}", token), otherBookId, authorId, OtherForeignBookId, OtherForeignEditionId)
                            .Should().Be(otherEditionId);
                        exclusions.FindByForeignId(ForeignBookId).Id.Should().Be(exclusion.Id);
                    }
                    else
                    {
                        result.GetProperty("id").GetInt32().Should().Be(bookId);
                        editions.Should().HaveCount(2);
                        var audiobook = editions.Single(edition => edition.ForeignEditionId == audiobookId);
                        audiobook.Id.Should().BePositive().And.NotBe(ebookId);
                        audiobook.Format.Should().Be("Audiobook");
                        audiobook.ManualAdd.Should().BeTrue();
                        audiobook.Monitored.Should().BeTrue();
                        var overview = await Get(client, $"book/{bookId}/overview", token);
                        overview.GetProperty("id").GetInt32().Should().Be(bookId);
                        exclusions.FindByForeignId(ForeignBookId).Should().BeNull();
                    }
                }
                finally
                {
                    await host.StopAsync(TimeSpan.FromSeconds(15));
                }
            }
            finally
            {
                NLog.LogManager.Flush(TimeSpan.FromSeconds(5));
                SQLiteConnection.ClearAllPools();
                profile.Delete(recursive: true);
            }
        }

        private static IHost BuildHost(string appData, bool useFixtureProvider = true, Func<string> offeredAudiobook = null)
        {
            var args = new[] { $"-data={appData}", "-nobrowser", "-nosingleinstancecheck" };
            return Bootstrap.CreateConsoleHostBuilder(args, new StartupContext(args))
                .ConfigureServices(services =>
                {
                    services.PostConfigure<LogOptions>(options => options.AnalyticsEnabled = false);
                    services.PostConfigure<PostgresOptions>(options => options.Host = null);
                    services.PostConfigure<UpdateOptions>(options => options.PublicReleaseDiscovery = false);
                })
                .ConfigureWebHost(webHost => webHost.UseUrls("http://127.0.0.1:0"))
                .ConfigureContainer<IContainer>(container =>
                {
                    var dispatcher = new Mock<IHttpDispatcher>(MockBehavior.Strict);
                    container.RegisterInstance(dispatcher.Object, ifAlreadyRegistered: IfAlreadyRegistered.Replace);

                    // Bootstrap may resolve the orchestrator before this callback.
                    // Update its existing registry so every consumer uses the same provider.
                    var registry = container.Resolve<IMetadataProviderRegistry>();
                    foreach (var registered in registry.GetAllProviders())
                    {
                        registry.UnregisterProvider(registered.ProviderName);
                    }

                    if (useFixtureProvider)
                    {
                        registry.RegisterProvider(CreateMetadataProvider(offeredAudiobook));
                    }
                })
                .Build();
        }

        private static IMetadataProvider CreateMetadataProvider(Func<string> offeredAudiobook)
        {
            var provider = new Mock<IMetadataProvider>();
            provider.SetupGet(p => p.ProviderName).Returns("Hardcover");
            provider.SetupGet(p => p.IsEnabled).Returns(true);
            provider.SetupGet(p => p.SupportsBookSearch).Returns(true);
            provider.SetupGet(p => p.SupportsAuthorSearch).Returns(true);
            var books = provider.As<IProvideBookInfo>();
            books.Setup(p => p.GetBookInfo(ForeignBookId)).Returns(() =>
            {
                var result = BookMetadata(ForeignBookId, ForeignEditionId, BookTitle);
                if (offeredAudiobook?.Invoke() is { } editionId)
                {
                    result.Item2.Editions.Value.Add(new Edition
                    {
                        ForeignEditionId = editionId,
                        Format = "Audiobook",
                        Title = BookTitle,
                        TitleSlug = "existing-work-audiobook"
                    });
                }

                return result;
            });
            books.Setup(p => p.GetBookInfo(OtherForeignBookId)).Returns(() => BookMetadata(OtherForeignBookId, OtherForeignEditionId, OtherBookTitle));
            provider.As<IProvideAuthorInfo>().Setup(p => p.GetAuthorInfo(ForeignAuthorId, It.IsAny<bool>())).Returns(() =>
            {
                var metadata = AuthorMetadata();
                var book = BookMetadata(ForeignBookId, ForeignEditionId, BookTitle).Item2;
                book.AuthorMetadata = metadata;
                book.Author.Value.Metadata = metadata;

                return new Author
                {
                    Metadata = metadata,
                    Books = new List<Book> { book },
                    Series = new List<Series>()
                };
            });

            return provider.Object;
        }

        private static AuthorMetadata AuthorMetadata()
        {
            return new AuthorMetadata
            {
                ForeignAuthorId = ForeignAuthorId,
                Name = AuthorName,
                SortName = AuthorName.ToLowerInvariant(),
                NameLastFirst = "Fixture, Nested Author",
                SortNameLastFirst = "fixture, nested author",
                TitleSlug = "nested-author-fixture"
            };
        }

        private static Tuple<string, Book, List<AuthorMetadata>> BookMetadata(string bookId, string editionId, string title)
        {
            var author = AuthorMetadata();
            var slug = title.ToLowerInvariant().Replace(' ', '-');
            var book = new Book
            {
                ForeignBookId = bookId,
                ForeignEditionId = editionId,
                Title = title,
                TitleSlug = slug,
                CleanTitle = title,
                AuthorMetadata = author,
                Editions = new List<Edition>
                {
                    new Edition
                    {
                        ForeignEditionId = editionId,
                        Format = "Ebook",
                        IsEbook = true,
                        Language = "en",
                        Title = title,
                        TitleSlug = slug,
                        Monitored = true
                    }
                }
            };
            return Tuple.Create(ForeignAuthorId, book, new List<AuthorMetadata> { author });
        }

        private static Dictionary<string, object> BookRequest(string workId, string editionId, string title)
        {
            return new Dictionary<string, object>
            {
                ["title"] = title,
                ["foreignBookId"] = workId,
                ["foreignEditionId"] = editionId,
                ["editions"] = new[] { new { foreignEditionId = editionId, monitored = true } },
                ["monitored"] = false,
                ["addOptions"] = new { searchForNewBook = false }
            };
        }

        private static HttpClient CreateClient(IHost host, string apiKey)
        {
            var addresses = host.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>();
            var client = new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = false, UseProxy = false })
            {
                BaseAddress = new Uri(addresses.Addresses.Single() + "/api/v1/")
            };
            client.DefaultRequestHeaders.Add("X-Api-Key", apiKey);
            return client;
        }

        private static int AssertIdentity(JsonElement book, int bookId, int authorId, string workId, string editionId)
        {
            bookId.Should().BePositive();
            authorId.Should().BePositive();
            book.GetProperty("id").GetInt32().Should().Be(bookId);
            book.GetProperty("authorId").GetInt32().Should().Be(authorId);
            book.GetProperty("foreignBookId").GetString().Should().Be(workId);

            var edition = book.GetProperty("editions").EnumerateArray().Single();
            edition.GetProperty("foreignEditionId").GetString().Should().Be(editionId);
            var id = edition.GetProperty("id").GetInt32();
            id.Should().BePositive();
            edition.GetProperty("monitored").GetBoolean().Should().BeTrue();
            return id;
        }

        private static async Task AssertMissingEditions(HttpClient client, Dictionary<string, object> request, CancellationToken cancellationToken)
        {
            var errors = await Post(client, "book", request, HttpStatusCode.BadRequest, cancellationToken);
            var error = errors.EnumerateArray().Should().ContainSingle().Which;
            error.GetProperty("propertyName").GetString().Should().Be("Editions");
        }

        private static async Task<int> FindProfile(HttpClient client, string path, string name, CancellationToken cancellationToken)
        {
            var profiles = await Get(client, path, cancellationToken);
            return profiles.EnumerateArray()
                .Single(profile => name.Equals(profile.GetProperty("name").GetString(), StringComparison.OrdinalIgnoreCase))
                .GetProperty("id").GetInt32();
        }

        private static async Task<JsonElement> Get(HttpClient client, string path, CancellationToken cancellationToken)
        {
            using var response = await client.GetAsync(path, cancellationToken);
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            return await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        }

        private static async Task<JsonElement> Post(HttpClient client, string path, object payload, HttpStatusCode status, CancellationToken cancellationToken)
        {
            using var response = await client.PostAsJsonAsync(path, payload, cancellationToken);
            var result = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
            response.StatusCode.Should().Be(status, $"POST {path} returned {result.GetRawText()}");
            return result;
        }
    }
}
