using System;
using System.Collections.Generic;
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
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.Books;
using NzbDrone.Core.ImportLists.Exclusions;
using NzbDrone.Core.MetadataSource;

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
        public async Task FreshAuthorRequiresAPathAndValidBookAndEditionPersistAcrossRestart()
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
                            qualityProfileId = ebook,
                            metadataProfileId = metadata,
                            monitored = false,
                            monitorNewItems = "none",
                            addOptions = new { monitor = "none", searchForMissingBooks = false }
                        };

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

                using var restarted = BuildHost(appData);
                await restarted.StartAsync(timeout.Token);
                try
                {
                    using var client = CreateClient(restarted, apiKey);
                    var saved = await Get(client, $"book/{bookId}", timeout.Token);
                    AssertIdentity(saved, bookId, authorId, ForeignBookId, ForeignEditionId).Should().Be(editionId);
                }
                finally
                {
                    await restarted.StopAsync();
                }
            }
            finally
            {
                Directory.Delete(directory, recursive: true);
            }
        }

        private static IHost BuildHost(string appData)
        {
            var args = new[] { $"-data={appData}", "-nobrowser", "-nosingleinstancecheck" };
            var provider = new Mock<IMetadataProvider>();
            provider.SetupGet(p => p.ProviderName).Returns("Hardcover");
            provider.SetupGet(p => p.IsEnabled).Returns(true);
            provider.SetupGet(p => p.SupportsBookSearch).Returns(true);
            provider.SetupGet(p => p.SupportsAuthorSearch).Returns(true);
            var books = provider.As<IProvideBookInfo>();
            books.Setup(p => p.GetBookInfo(ForeignBookId)).Returns(() => BookMetadata(ForeignBookId, ForeignEditionId, BookTitle));
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

            return Bootstrap.CreateConsoleHostBuilder(args, new StartupContext(args))
                .ConfigureContainer<IContainer>(container =>
                {
                    // Bootstrap may resolve the orchestrator before this callback.
                    // Update its existing registry so every consumer uses the same provider.
                    var registry = container.Resolve<IMetadataProviderRegistry>();
                    foreach (var registered in registry.GetAllProviders())
                    {
                        registry.UnregisterProvider(registered.ProviderName);
                    }

                    registry.RegisterProvider(provider.Object);
                })
                .Build();
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
            return id;
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
