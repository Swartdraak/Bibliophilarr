using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.Books.Commands;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.Commands;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.Qualities;
using NzbDrone.Test.Common.Datastore;

namespace NzbDrone.Host.Test
{
    public partial class NestedAuthorBookAddFixture
    {
        private const string AudioForeignEditionId = "hardcover:source-edition:92632";

        [TestCase(null)]
        [TestCase("book")]
        [TestCase("author")]
        [TestCase("direct")]
        [TestCase("editor")]
        [TestCase("recycle-error")]
        [TestCase("second-file-error")]
        public async Task ShouldPreserveApiImportedEbookAndAudiobookAcrossHostReopen(string removal)
        {
            var keepFiles = removal is "book" or "author";
            var profile = Directory.CreateTempSubdirectory("bibliophilarr-dual-format-");
            var appData = profile.CreateSubdirectory("data").FullName;
            var library = profile.CreateSubdirectory("library").FullName;
            var inbox = profile.CreateSubdirectory("inbox").FullName;
            var apiKey = Guid.NewGuid().ToString("N");
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(120));
            var token = timeout.Token;

            try
            {
                new XElement("Config",
                    new XElement("BindAddress", "127.0.0.1"),
                    new XElement("Port", 0),
                    new XElement("LaunchBrowser", false),
                    new XElement("AuthenticationMethod", "Forms"),
                    new XElement("ApiKey", apiKey)).Save(Path.Combine(appData, "config.xml"));
                File.Exists(Path.Combine(appData, "bibliophilarr.db")).Should().BeFalse();

                int rootId;
                int authorId;
                int bookId;
                var unrelatedAuthorId = 0;
                List<(int FileId, int EditionId, string Path, string Digest)> imported;
                using (var host = BuildHost(appData, includeAudioEdition: true))
                {
                    await host.StartAsync(token);
                    try
                    {
                        using var client = CreateClient(host, apiKey);
                        var ebookProfile = await FindProfile(client, "qualityprofile", "eBook", token);
                        var audioProfile = await FindProfile(client, "qualityprofile", "Spoken", token);
                        var metadataProfile = await FindProfile(client, "metadataprofile", "None", token);
                        var settings = await Get(client, "config/mediamanagement", token);
                        settings.GetProperty("enableDualFormatTracking").GetBoolean().Should().BeTrue();

                        var root = await Post(
                            client,
                            "rootfolder",
                            new
                            {
                                name = "Dual format fixture",
                                path = library,
                                defaultQualityProfileId = ebookProfile,
                                defaultMetadataProfileId = metadataProfile,
                                defaultMonitorOption = "none",
                                defaultNewItemMonitorOption = "none",
                                defaultTags = Array.Empty<int>()
                            },
                            HttpStatusCode.Created,
                            token);
                        rootId = root.GetProperty("id").GetInt32();
                        var author = await Post(
                            client,
                            "author",
                            new
                            {
                                authorName = AuthorName,
                                foreignAuthorId = ForeignAuthorId,
                                rootFolderPath = library,
                                qualityProfileId = ebookProfile,
                                metadataProfileId = metadataProfile,
                                monitored = false,
                                monitorNewItems = "none",
                                addOptions = new
                                {
                                    monitor = "none",
                                    searchForMissingBooks = false,
                                    ebookQualityProfileId = ebookProfile,
                                    audiobookQualityProfileId = audioProfile,
                                    ebookRootFolderPath = library,
                                    audiobookRootFolderPath = library
                                }
                            },
                            HttpStatusCode.Created,
                            token);
                        authorId = author.GetProperty("id").GetInt32();
                        (await Get(client, $"book?authorId={authorId}", token)).GetArrayLength().Should().Be(0);

                        var added = await Post(client, "book", BookRequest(ForeignBookId, ForeignEditionId, BookTitle), HttpStatusCode.Created, token);
                        bookId = added.GetProperty("id").GetInt32();
                        added.GetProperty("authorId").GetInt32().Should().Be(authorId);

                        var ebook = Path.Combine(inbox, $"{AuthorName} - {BookTitle}.epub");
                        using (var archive = ZipFile.Open(ebook, ZipArchiveMode.Create))
                        {
                            WriteEpubEntry(archive, "mimetype", "application/epub+zip");
                            WriteEpubEntry(archive, "META-INF/container.xml", """
                                <container xmlns="urn:oasis:names:tc:opendocument:xmlns:container" version="1.0">
                                  <rootfiles><rootfile full-path="book.opf" media-type="application/oebps-package+xml"/></rootfiles>
                                </container>
                                """);
                            WriteEpubEntry(archive, "book.opf", $"""
                                <package xmlns="http://www.idpf.org/2007/opf" xmlns:dc="http://purl.org/dc/elements/1.1/" version="3.0" unique-identifier="fixture">
                                  <metadata><dc:title>{BookTitle}</dc:title><dc:creator>{AuthorName}</dc:creator><dc:identifier id="fixture">urn:test:dual-format</dc:identifier><dc:language>en</dc:language></metadata>
                                  <manifest><item id="chapter" href="chapter.xhtml" media-type="application/xhtml+xml"/></manifest>
                                  <spine><itemref idref="chapter"/></spine>
                                </package>
                                """);
                            WriteEpubEntry(archive, "chapter.xhtml", "<html xmlns=\"http://www.w3.org/1999/xhtml\"><head><title>Fixture</title></head><body><p>Dual-format fixture.</p></body></html>");
                        }

                        var audio = Path.Combine(inbox, $"{AuthorName} - {BookTitle}.mp3");
                        File.Copy(Path.Combine(TestContext.CurrentContext.TestDirectory, "DualFormatFixture.mp3"), audio);
                        using (var media = TagLib.File.Create(audio))
                        {
                            media.Tag.Title = BookTitle;
                            media.Tag.Album = BookTitle;
                            media.Tag.Performers = new[] { AuthorName };
                            media.Tag.AlbumArtists = new[] { AuthorName };
                            media.Tag.Track = 1;
                            media.Tag.TrackCount = 1;
                            media.Tag.Disc = 1;
                            media.Tag.DiscCount = 1;
                            media.Save();
                        }

                        var originalEbook = FileDigest(ebook);
                        var originalAudio = FileDigest(audio);
                        await ImportSelectedFile(client, inbox, ebook, authorId, bookId, ForeignEditionId, token);
                        await ImportSelectedFile(client, inbox, audio, authorId, bookId, AudioForeignEditionId, token);
                        FileDigest(ebook).Should().Be(originalEbook);
                        FileDigest(audio).Should().Be(originalAudio);

                        imported = await ReadDualFormatState(client, host.Services, library, rootId, authorId, bookId, token);
                        imported.Single(file => Path.GetExtension(file.Path) == ".epub").Digest.Should().Be(originalEbook);
                        imported.Single(file => Path.GetExtension(file.Path) == ".mp3").Digest.Should().Be(originalAudio);

                        if (keepFiles)
                        {
                            var unrelatedSettings = new
                            {
                                authorName = "Unrelated Keep Files Fixture",
                                foreignAuthorId = "hardcover:author:789401",
                                rootFolderPath = library,
                                qualityProfileId = ebookProfile,
                                metadataProfileId = metadataProfile,
                                monitored = false,
                                monitorNewItems = "none",
                                addOptions = new { monitor = "none", searchForMissingBooks = false }
                            };
                            var commands = host.Services.GetRequiredService<ICommandRepository>();
                            var previousCommands = commands.All().ToList();
                            var unrelated = await Post(client, "author", unrelatedSettings, HttpStatusCode.Created, token);
                            unrelatedAuthorId = unrelated.GetProperty("id").GetInt32();
                            await WaitForAuthorAddScan(client, commands, previousCommands, library, unrelatedAuthorId, token);
                            var removedId = removal == "author" ? authorId : bookId;
                            using var removalResponse = await client.DeleteAsync($"{removal}/{removedId}?deleteFiles=false", token);
                            removalResponse.IsSuccessStatusCode.Should().BeTrue();
                            await AssertUnmappedKeptFiles(client, host.Services, imported, authorId, bookId, unrelatedAuthorId, removal, token);
                        }
                    }
                    finally
                    {
                        await host.StopAsync(TimeSpan.FromSeconds(15));
                    }
                }

                SQLiteConnection.ClearAllPools();

                using var restarted = BuildHost(appData, useFixtureProvider: false);
                await restarted.StartAsync(token);
                try
                {
                    restarted.Services.GetRequiredService<IMetadataProviderRegistry>().GetAllProviders().Should().BeEmpty();
                    using var client = CreateClient(restarted, apiKey);
                    if (!keepFiles)
                    {
                        var persisted = await ReadDualFormatState(client, restarted.Services, library, rootId, authorId, bookId, token);
                        persisted.Should().Equal(imported);
                    }
                    else
                    {
                        await AssertUnmappedKeptFiles(client, restarted.Services, imported, authorId, bookId, unrelatedAuthorId, removal, token);
                    }

                    if (removal is "direct" or "editor" or "recycle-error" or "second-file-error")
                    {
                        var importedEbook = imported.Single(file => Path.GetExtension(file.Path) == ".epub").Path;
                        var sibling = Path.Combine(Path.GetDirectoryName(importedEbook), "unrelated.epub");
                        File.Copy(importedEbook, sibling);
                        using (var archive = ZipFile.Open(sibling, ZipArchiveMode.Update))
                        {
                            WriteEpubEntry(archive, "fixture-unrelated.txt", "Unrelated fixture content");
                        }

                        var siblingDigest = FileDigest(sibling);
                        var recyclePath = Path.Combine(profile.FullName, "recycle");
                        var secondFile = imported.MaxBy(file => file.FileId);
                        var blockedDestination = Path.Combine(recyclePath, Path.GetFileName(secondFile.Path));
                        if (removal == "recycle-error")
                        {
                            // A regular file at the destination makes directory creation fail even for a privileged user.
                            await File.WriteAllTextAsync(recyclePath, "blocked recycle path", token);
                        }
                        else
                        {
                            Directory.CreateDirectory(recyclePath);
                        }

                        if (removal == "second-file-error")
                        {
                            Directory.CreateDirectory(blockedDestination);
                        }

                        var config = restarted.Services.GetRequiredService<IConfigService>();
                        config.RecycleBin = recyclePath;
                        config.DeleteEmptyFolders = false;
                        using var deletion = removal == "editor"
                            ? new HttpRequestMessage(HttpMethod.Delete, "book/editor")
                            {
                                Content = JsonContent.Create(new { bookIds = new[] { bookId }, deleteFiles = true })
                            }
                            : new HttpRequestMessage(HttpMethod.Delete, $"book/{bookId}?deleteFiles=true");
                        using var response = await client.SendAsync(deletion, token);

                        if (removal is "recycle-error" or "second-file-error")
                        {
                            response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
                            (await response.Content.ReadAsStringAsync(token)).Should().Contain("Unable to delete book file");
                            if (removal == "recycle-error")
                            {
                                var afterFailure = await ReadDualFormatState(client, restarted.Services, library, rootId, authorId, bookId, token);
                                afterFailure.Should().Equal(imported);
                                File.ReadAllText(recyclePath).Should().Be("blocked recycle path");
                            }
                            else
                            {
                                var firstFile = imported.MinBy(file => file.FileId);
                                using var retainedBook = await client.GetAsync($"book/{bookId}", token);
                                retainedBook.StatusCode.Should().Be(HttpStatusCode.OK);
                                using var removedFile = await client.GetAsync($"bookfile/{firstFile.FileId}", token);
                                removedFile.StatusCode.Should().Be(HttpStatusCode.NotFound);
                                using var retainedFile = await client.GetAsync($"bookfile/{secondFile.FileId}", token);
                                retainedFile.StatusCode.Should().Be(HttpStatusCode.OK);
                                File.Exists(firstFile.Path).Should().BeFalse();
                                FileDigest(secondFile.Path).Should().Be(secondFile.Digest);
                                Directory.Exists(blockedDestination).Should().BeTrue();
                                Directory.EnumerateFiles(recyclePath, "*", SearchOption.AllDirectories).Select(FileDigest)
                                    .Should().Equal(firstFile.Digest);
                            }
                        }
                        else
                        {
                            response.IsSuccessStatusCode.Should().BeTrue();
                            using var deletedBook = await client.GetAsync($"book/{bookId}", token);
                            deletedBook.StatusCode.Should().Be(HttpStatusCode.NotFound);
                            foreach (var file in imported)
                            {
                                File.Exists(file.Path).Should().BeFalse();
                                using var deletedFile = await client.GetAsync($"bookfile/{file.FileId}", token);
                                deletedFile.StatusCode.Should().Be(HttpStatusCode.NotFound);
                            }

                            Directory.EnumerateFiles(recyclePath, "*", SearchOption.AllDirectories).Select(FileDigest)
                                .Should().BeEquivalentTo(imported.Select(file => file.Digest));
                        }

                        FileDigest(sibling).Should().Be(siblingDigest);
                    }

                    using var database = new SQLiteConnection(new SQLiteConnectionStringBuilder
                    {
                        DataSource = Path.Combine(appData, "bibliophilarr.db"),
                        FailIfMissing = true,
                        Pooling = false,
                        ReadOnly = true
                    }.ConnectionString);
                    database.Open();
                    SqliteDatabase.AssertCompleteMainMigration(database);
                }
                finally
                {
                    await restarted.StopAsync(TimeSpan.FromSeconds(15));
                }
            }
            finally
            {
                SQLiteConnection.ClearAllPools();
                NLog.LogManager.Flush(TimeSpan.FromSeconds(5));
                profile.Delete(recursive: true);
            }
        }

        private static async Task AssertUnmappedKeptFiles(HttpClient client,
                                                          IServiceProvider services,
                                                          List<(int FileId, int EditionId, string Path, string Digest)> imported,
                                                          int authorId,
                                                          int bookId,
                                                          int unrelatedAuthorId,
                                                          string removal,
                                                          CancellationToken token)
        {
            using var deletedBook = await client.GetAsync($"book/{bookId}", token);
            deletedBook.StatusCode.Should().Be(HttpStatusCode.NotFound);
            using var targetAuthor = await client.GetAsync($"author/{authorId}", token);
            targetAuthor.StatusCode.Should().Be(removal == "author" ? HttpStatusCode.NotFound : HttpStatusCode.OK);
            (await Get(client, $"author/{unrelatedAuthorId}", token)).GetProperty("id").GetInt32().Should().Be(unrelatedAuthorId);
            services.GetRequiredService<IEditionService>().GetEditionsByBook(bookId).Should().BeEmpty();

            var unmapped = (await Get(client, "bookfile?unmapped=true", token)).EnumerateArray().ToList();
            unmapped.Select(file => (file.GetProperty("id").GetInt32(), file.GetProperty("path").GetString()))
                .Should().BeEquivalentTo(imported.Select(file => (file.FileId, file.Path)));
            unmapped.Should().OnlyContain(file => file.GetProperty("bookId").GetInt32() == 0);
            var files = services.GetRequiredService<IMediaFileService>();
            foreach (var original in imported)
            {
                var file = files.Get(original.FileId);
                file.EditionId.Should().Be(0);
                file.Path.Should().Be(original.Path);
                file.Quality.Quality.Should().Be(Path.GetExtension(original.Path) == ".epub" ? Quality.EPUB : Quality.MP3);
                FileDigest(file.Path).Should().Be(original.Digest);
            }
        }

        private static void WriteEpubEntry(ZipArchive archive, string path, string content)
        {
            using var writer = new StreamWriter(archive.CreateEntry(path, CompressionLevel.NoCompression).Open());
            writer.Write(content);
        }

        private static async Task ImportSelectedFile(HttpClient client, string inbox, string path, int authorId, int bookId, string foreignEditionId, CancellationToken token)
        {
            var results = await Get(client, $"manualimport?folder={Uri.EscapeDataString(inbox)}&authorId={authorId}&filterExistingFiles=false&replaceExistingFiles=false", token);
            var file = results.EnumerateArray().Single(candidate => candidate.GetProperty("path").GetString() == path);
            var selection = new
            {
                id = file.GetProperty("id").GetInt32(),
                path,
                name = file.GetProperty("name").GetString(),
                authorId,
                bookId,
                foreignEditionId,
                quality = file.GetProperty("quality"),
                indexerFlags = 0,
                replaceExistingFiles = false
            };
            var validated = (await Post(client, "manualimport", new[] { selection }, HttpStatusCode.Accepted, token)).EnumerateArray().Single();
            validated.GetProperty("foreignEditionId").GetString().Should().Be(foreignEditionId);
            validated.GetProperty("rejections").GetArrayLength().Should().Be(0);
            var command = await Post(
                client,
                "command",
                new
                {
                    name = "ManualImport",
                    importMode = "copy",
                    files = new[] { selection },
                    replaceExistingFiles = false
                },
                HttpStatusCode.Created,
                token);

            await WaitForSuccessfulCommand(client, command.GetProperty("id").GetInt32(), token);
        }

        private static async Task WaitForAuthorAddScan(HttpClient client,
                                                       ICommandRepository commands,
                                                       List<CommandModel> previousCommands,
                                                       string library,
                                                       int authorId,
                                                       CancellationToken token)
        {
            var beforeAdd = previousCommands.Select(command => command.Id).DefaultIfEmpty().Max();
            var refresh = commands.All().Single(command => command.Id > beforeAdd &&
                command.Body is RefreshAuthorCommand { IsNewAuthor: true } body && body.AuthorId == authorId);
            await WaitForSuccessfulCommand(client, refresh.Id, token);

            // The refresh enqueues its scan before completion. It may instead reuse an active matched scan of the same root.
            var activeAtAdd = previousCommands.Where(command => command.Status is CommandStatus.Queued or CommandStatus.Started)
                .Select(command => command.Id)
                .ToHashSet();
            var scans = commands.All().Where(command =>
                (command.Id > beforeAdd || activeAtAdd.Contains(command.Id)) &&
                command.Body is RescanFoldersCommand { Filter: FilterFilesType.Matched, AddNewAuthors: false } scan &&
                scan.Folders?.SequenceEqual(new[] { library }) == true)
                .ToList();
            scans.Should().NotBeEmpty($"adding author {authorId} schedules or reuses a matched scan of the fixture library");
            foreach (var scan in scans)
            {
                await WaitForSuccessfulCommand(client, scan.Id, token);
            }
        }

        private static async Task WaitForSuccessfulCommand(HttpClient client, int commandId, CancellationToken token)
        {
            while (true)
            {
                var current = await Get(client, $"command/{commandId}", token);
                var status = current.GetProperty("status").GetString();
                if (status == "completed")
                {
                    current.GetProperty("result").GetString().Should().Be("successful", $"command {commandId} must finish successfully");
                    return;
                }

                status.Should().BeOneOf("queued", "started");
                await Task.Delay(TimeSpan.FromMilliseconds(100), token);
            }
        }

        private static async Task<List<(int FileId, int EditionId, string Path, string Digest)>> ReadDualFormatState(HttpClient client, IServiceProvider services, string library, int rootId, int authorId, int bookId, CancellationToken token)
        {
            var root = (await Get(client, "rootfolder", token)).EnumerateArray().Single();
            rootId.Should().BePositive();
            root.GetProperty("id").GetInt32().Should().Be(rootId);
            root.GetProperty("path").GetString().Should().Be(library);
            var author = (await Get(client, "author", token)).EnumerateArray().Single();
            author.GetProperty("id").GetInt32().Should().Be(authorId);
            author.GetProperty("foreignAuthorId").GetString().Should().Be(ForeignAuthorId);
            var book = (await Get(client, $"book?authorId={authorId}", token)).EnumerateArray().Single();
            book.GetProperty("id").GetInt32().Should().Be(bookId);
            book.GetProperty("foreignBookId").GetString().Should().Be(ForeignBookId);
            var editions = book.GetProperty("editions").EnumerateArray().ToDictionary(
                edition => edition.GetProperty("foreignEditionId").GetString(),
                edition => edition.GetProperty("id").GetInt32());
            editions.Keys.Should().BeEquivalentTo(new[] { ForeignEditionId, AudioForeignEditionId });
            editions.Values.Should().OnlyContain(id => id > 0).And.OnlyHaveUniqueItems();

            var status = book.GetProperty("formatStatuses").EnumerateArray().ToList();
            status.Select(format => format.GetProperty("formatType").GetString()).Should().BeEquivalentTo(new[] { "ebook", "audiobook" });
            status.Should().OnlyContain(format => format.GetProperty("hasFile").GetBoolean() && format.GetProperty("fileCount").GetInt32() == 1);

            var files = (await Get(client, $"bookfile?bookId={bookId}", token)).EnumerateArray().ToList();
            files.Should().HaveCount(2);
            files.Should().OnlyContain(file => file.GetProperty("bookId").GetInt32() == bookId);
            var persisted = services.GetRequiredService<IMediaFileService>().GetFilesByBook(bookId);
            persisted.Select(file => (file.Id, file.Path)).Should().BeEquivalentTo(
                files.Select(file => (file.GetProperty("id").GetInt32(), file.GetProperty("path").GetString())));
            persisted.Select(file => file.Quality.Quality).Should().BeEquivalentTo(new[] { Quality.EPUB, Quality.MP3 });

            return persisted.OrderBy(file => file.Id).Select(file =>
            {
                var format = Quality.GetFormatType(file.Quality.Quality);
                var expectedEdition = format == NzbDrone.Core.Books.FormatType.Ebook ? ForeignEditionId : AudioForeignEditionId;
                var extension = format == NzbDrone.Core.Books.FormatType.Ebook ? ".epub" : ".mp3";
                file.EditionId.Should().Be(editions[expectedEdition]);
                Path.GetExtension(file.Path).Should().Be(extension);
                var relative = Path.GetRelativePath(library, file.Path);
                Path.IsPathRooted(relative).Should().BeFalse();
                relative.Should().NotStartWith($"..{Path.DirectorySeparatorChar}");
                File.Exists(file.Path).Should().BeTrue();
                return (file.Id, file.EditionId, file.Path, FileDigest(file.Path));
            }).ToList();
        }

        private static string FileDigest(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
    }
}
