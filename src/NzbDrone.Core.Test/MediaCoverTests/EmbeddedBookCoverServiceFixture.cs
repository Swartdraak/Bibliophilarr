using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.Books;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.MediaCover;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.RootFolders;
using NzbDrone.Core.Test.Framework;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.PixelFormats;

namespace NzbDrone.Core.Test.MediaCoverTests
{
    [TestFixture]
    public class EmbeddedBookCoverServiceFixture : DbTest<EmbeddedBookCoverService, BookFile>
    {
        private Book _book;
        private BookFile _file;
        private Edition _edition;
        private string _library;
        private byte[] _original;

        [SetUp]
        public void SetUpEmbeddedCover()
        {
            var architecture = RuntimeInformation.ProcessArchitecture;
            if (!OperatingSystem.IsLinux() || (architecture != Architecture.X64 && architecture != Architecture.Arm64))
            {
                Assert.Ignore("The admitted media opener requires Linux x64 or arm64.");
            }

            _library = Path.Combine(TempFolder, "library");
            Directory.CreateDirectory(_library);
            var path = Path.Combine(_library, "City Lights.M4B");
            using var image = new Image<Rgba32>(40, 60);
            using var bytes = new MemoryStream();
            image.SaveAsPng(bytes);
            var fileType = Box("ftyp"u8, "M4B \0\0\0\0M4B "u8.ToArray());
            var data = Box("data"u8, new byte[] { 0, 0, 0, 14, 0, 0, 0, 0 }, bytes.ToArray());
            var metadata = Box("meta"u8, new byte[4], Box("ilst"u8, Box("covr"u8, data)));
            var movie = Box("moov"u8, Box("udta"u8, metadata));
            _original = fileType.Concat(movie).ToArray();
            File.WriteAllBytes(path, _original);

            var author = Db.Insert(new AuthorMetadata
            {
                Name = "Ada Vale",
                SortName = "ada vale",
                NameLastFirst = "Vale, Ada",
                SortNameLastFirst = "vale, ada",
                ForeignAuthorId = "hardcover:author:10",
                TitleSlug = "ada-vale"
            });
            _book = Db.Insert(new Book
            {
                AuthorMetadataId = author.Id,
                ForeignBookId = "hardcover:work:22",
                Title = "City Lights",
                CleanTitle = "City Lights",
                TitleSlug = "city-lights"
            });
            _edition = Db.Insert(new Edition { BookId = _book.Id, ForeignEditionId = "hardcover:edition:44", Title = "City Lights", TitleSlug = "city-lights-audio", Format = "Audiobook" });
            _file = Db.Insert(new BookFile
            {
                EditionId = _edition.Id,
                Path = path,
                Size = _original.Length,
                Modified = File.GetLastWriteTimeUtc(path),
                Quality = new QualityModel(Quality.M4B)
            });

            Mocker.GetMock<IAppFolderInfo>().SetupGet(folder => folder.AppDataFolder).Returns(TempFolder);
            Mocker.GetMock<IConfigFileProvider>().SetupGet(config => config.UrlBase).Returns("/read/library");
            Mocker.GetMock<IRootFolderService>().Setup(roots => roots.GetBestRootFolder(It.IsAny<string>()))
                .Returns(new RootFolder { Path = _library });
            var disk = Mocker.GetMock<IDiskProvider>();
            disk.Setup(service => service.FolderExists(It.IsAny<string>())).Returns<string>(Directory.Exists);
            disk.Setup(service => service.FileExists(It.IsAny<string>())).Returns<string>(File.Exists);
            disk.Setup(service => service.GetFileSize(It.IsAny<string>())).Returns<string>(file => new FileInfo(file).Length);
            disk.Setup(service => service.CreateFolder(It.IsAny<string>())).Callback<string>(folder => Directory.CreateDirectory(folder));
            disk.Setup(service => service.DeleteFile(It.IsAny<string>())).Callback<string>(File.Delete);
            disk.Setup(service => service.MoveFile(It.IsAny<string>(), It.IsAny<string>(), false)).Callback<string, string, bool>(File.Move);
        }

        [Test]
        public void should_recover_one_persisted_audio_cover_as_a_private_jpeg_and_reuse_it()
        {
            Subject.GetUrl(_book.Id).Should().BeNull();

            Subject.Recover(_book.Id).Should().BeTrue();
            var url = Subject.GetUrl(_book.Id);
            url.Should().StartWith($"/read/library/api/v1/embeddedbookcover/{_book.Id}/{_file.Id}/");
            var path = Subject.GetPath(_book.Id, _file.Id, url.Split('/').Last());
            path.Should().StartWith(Path.Combine(TempFolder, "EmbeddedBookCovers"));
            using var cover = Image.Load(path);
            cover.Width.Should().Be(40);
            cover.Height.Should().Be(60);
            cover.Metadata.DecodedImageFormat.Should().BeSameAs(JpegFormat.Instance);
            Subject.Recover(_book.Id).Should().BeTrue();

            Mocker.GetMock<IDiskProvider>().Verify(service => service.MoveFile(It.IsAny<string>(), It.IsAny<string>(), false), Times.Once());
            Db.Single<BookFile>().EditionId.Should().Be(_edition.Id);
            Db.Single<Edition>().Images.Should().BeEmpty();
            File.ReadAllBytes(_file.Path).Should().Equal(_original);
        }

        [Test]
        public void should_require_exact_current_edition_ownership_after_publication()
        {
            Subject.Recover(_book.Id).Should().BeTrue();
            var token = Subject.GetUrl(_book.Id).Split('/').Last();
            var other = Db.Insert(new Book
            {
                AuthorMetadataId = _book.AuthorMetadataId,
                ForeignBookId = "hardcover:work:23",
                Title = "Other work",
                CleanTitle = "Other work",
                TitleSlug = "other-work"
            });
            _edition.BookId = other.Id;
            Db.Update(_edition);

            Subject.GetPath(_book.Id, _file.Id, token).Should().BeNull();
            Subject.GetUrl(_book.Id).Should().BeNull();
            Subject.GetPath(other.Id, _file.Id, token).Should().BeNull();
            File.ReadAllBytes(_file.Path).Should().Equal(_original);
        }

        [Test]
        public void should_prefer_a_provider_cover_on_any_owned_edition()
        {
            Subject.Recover(_book.Id).Should().BeTrue();
            var token = Subject.GetUrl(_book.Id).Split('/').Last();
            Db.Insert(new Edition
            {
                BookId = _book.Id,
                ForeignEditionId = "hardcover:edition:45",
                Title = "City Lights",
                TitleSlug = "city-lights-digital",
                Format = "EPUB",
                IsEbook = true,
                Images = new List<NzbDrone.Core.MediaCover.MediaCover>
                {
                    new NzbDrone.Core.MediaCover.MediaCover(MediaCoverTypes.Cover, "https://fixture.example.invalid/cover.jpg")
                }
            });

            Subject.Recover(_book.Id).Should().BeFalse();
            Subject.GetUrl(_book.Id).Should().BeNull();
            Subject.GetPath(_book.Id, _file.Id, token).Should().BeNull();
        }

        [Test]
        public void should_decline_when_two_current_audio_files_are_eligible()
        {
            var other = Path.Combine(_library, "Other part.m4b");
            File.WriteAllBytes(other, _original);
            Db.Insert(new BookFile
            {
                EditionId = _edition.Id,
                Path = other,
                Size = _original.Length,
                Modified = File.GetLastWriteTimeUtc(other),
                Quality = new QualityModel(Quality.M4B)
            });

            Subject.Recover(_book.Id).Should().BeFalse();
            Subject.GetUrl(_book.Id).Should().BeNull();
        }

        [Test]
        public void should_keep_a_file_relinked_during_decode_out_of_the_cache()
        {
            Mocker.GetMock<IDiskProvider>().Setup(service => service.CreateFolder(It.IsAny<string>()))
                .Callback<string>(folder =>
                {
                    Directory.CreateDirectory(folder);
                    _file.EditionId = 0;
                    Db.Update(_file);
                });

            Subject.Recover(_book.Id).Should().BeFalse();

            Subject.GetUrl(_book.Id).Should().BeNull();
            Directory.GetFiles(Path.Combine(TempFolder, "EmbeddedBookCovers"), "*", SearchOption.AllDirectories).Should().BeEmpty();
            File.ReadAllBytes(_file.Path).Should().Equal(_original);
        }

        [Test]
        public void should_discard_staging_file_if_atomic_publication_fails()
        {
            Mocker.GetMock<IDiskProvider>().Setup(service => service.MoveFile(It.IsAny<string>(), It.IsAny<string>(), false))
                .Throws(new IOException("Fixture publish failure."));

            Subject.Recover(_book.Id).Should().BeFalse();

            Subject.GetUrl(_book.Id).Should().BeNull();
            Directory.GetFiles(Path.Combine(TempFolder, "EmbeddedBookCovers"), "*", SearchOption.AllDirectories).Should().BeEmpty();
            File.ReadAllBytes(_file.Path).Should().Equal(_original);
        }

        [Test]
        public void should_decline_when_the_linked_file_version_has_changed()
        {
            File.SetLastWriteTimeUtc(_file.Path, _file.Modified.AddSeconds(10));

            Subject.Recover(_book.Id).Should().BeFalse();

            Subject.GetUrl(_book.Id).Should().BeNull();
        }

        [TestCase(false)]
        [TestCase(true)]
        public void should_decline_symbolic_links_in_the_source_path(bool linkDirectory)
        {
            var otherFolder = Path.Combine(TempFolder, "outside");
            Directory.CreateDirectory(otherFolder);
            var realFile = Path.Combine(otherFolder, "Other.m4b");
            File.WriteAllBytes(realFile, _original);

            if (linkDirectory)
            {
                var link = Path.Combine(_library, "linked-folder");
                Directory.CreateSymbolicLink(link, otherFolder);
                _file.Path = Path.Combine(link, "Other.m4b");
            }
            else
            {
                File.Delete(_file.Path);
                File.CreateSymbolicLink(_file.Path, realFile);
            }

            _file.Modified = File.GetLastWriteTimeUtc(realFile);
            Db.Update(_file);

            Subject.Recover(_book.Id).Should().BeFalse();

            Subject.GetUrl(_book.Id).Should().BeNull();
        }

        [TestCase(true)]
        [TestCase(false)]
        public void should_refuse_stale_automatic_file_identity_before_opening_original_media(bool relink)
        {
            var stored = Db.Single<BookFile>();
            var command = ForImport(_book, stored);
            if (relink)
            {
                stored.EditionId = 0;
            }
            else
            {
                stored.Modified = stored.Modified.AddSeconds(2);
            }

            Db.Update(stored);

            Subject.RecoverImported(command).Should().BeFalse();

            Mocker.GetMock<IRootFolderService>().Verify(roots => roots.GetBestRootFolder(It.IsAny<string>()), Times.Never);
            Mocker.GetMock<IDiskProvider>().Verify(disk => disk.CreateFolder(It.IsAny<string>()), Times.Never);
        }

        [Test]
        public void should_refuse_the_worker_entry_for_a_manually_submitted_command()
        {
            var command = ForImport(_book, Db.Single<BookFile>());
            command.Trigger = CommandTrigger.Manual;

            Subject.RecoverImported(command).Should().BeFalse();

            Mocker.GetMock<IRootFolderService>().Verify(roots => roots.GetBestRootFolder(It.IsAny<string>()), Times.Never);
        }

        [Test]
        public async Task should_attempt_another_book_after_manual_recovery_releases_the_gate()
        {
            var other = Db.Insert(new Book
            {
                AuthorMetadataId = _book.AuthorMetadataId,
                ForeignBookId = "hardcover:work:23",
                Title = "Other work",
                CleanTitle = "Other work",
                TitleSlug = "other-work"
            });
            var edition = Db.Insert(new Edition
            {
                BookId = other.Id,
                ForeignEditionId = "hardcover:edition:45",
                Title = "Other work",
                TitleSlug = "other-work-audio",
                Format = "Audiobook"
            });
            var path = Path.Combine(_library, "Other work.m4b");
            File.WriteAllBytes(path, _original);
            var otherFile = Db.Insert(new BookFile
            {
                EditionId = edition.Id,
                Path = path,
                Size = _original.Length,
                Modified = File.GetLastWriteTimeUtc(path),
                Quality = new QualityModel(Quality.M4B)
            });
            var current = Db.All<BookFile>().Single(file => file.Id == otherFile.Id);
            var command = ForImport(other, current);

            using var insideManual = new ManualResetEventSlim();
            using var releaseManual = new ManualResetEventSlim();
            using var startedWorker = new ManualResetEventSlim();
            Mocker.GetMock<IDiskProvider>().Setup(disk => disk.CreateFolder(It.IsAny<string>()))
                .Callback<string>(folder =>
                {
                    if (folder == Path.Combine(TempFolder, "EmbeddedBookCovers", _book.Id.ToString(System.Globalization.CultureInfo.InvariantCulture)))
                    {
                        insideManual.Set();
                        if (!releaseManual.Wait(TimeSpan.FromSeconds(10)))
                        {
                            throw new IOException("Timed out waiting for the test to release manual recovery.");
                        }
                    }

                    Directory.CreateDirectory(folder);
                });

            var service = Subject;
            var manual = Task.Run(() => service.Recover(_book.Id));
            Task<bool> worker = null;
            try
            {
                insideManual.Wait(TimeSpan.FromSeconds(5)).Should().BeTrue();
                worker = Task.Factory.StartNew(
                    () =>
                    {
                        startedWorker.Set();
                        return service.RecoverImported(command);
                    },
                    CancellationToken.None,
                    TaskCreationOptions.LongRunning,
                    TaskScheduler.Default);
                startedWorker.Wait(TimeSpan.FromSeconds(5)).Should().BeTrue();
                worker.Wait(TimeSpan.FromMilliseconds(200)).Should().BeFalse();
            }
            finally
            {
                releaseManual.Set();
                await manual.WaitAsync(TimeSpan.FromSeconds(5));
                if (worker != null)
                {
                    await worker.WaitAsync(TimeSpan.FromSeconds(5));
                }
            }

            manual.Result.Should().BeTrue();
            worker.Result.Should().BeTrue();
            service.GetUrl(_book.Id).Should().NotBeNull();
            service.GetUrl(other.Id).Should().NotBeNull();
            service.RecoverImported(command).Should().BeFalse("an exact duplicate must not publish another image event");
        }

        [Test]
        public void should_keep_a_recovered_audio_cover_when_a_valid_ebook_is_added()
        {
            Subject.Recover(_book.Id).Should().BeTrue();
            var audioUrl = Subject.GetUrl(_book.Id);
            var audioToken = audioUrl.Split('/').Last();
            var audioPath = Subject.GetPath(_book.Id, _file.Id, audioToken);
            var originalCover = File.ReadAllBytes(audioPath);

            var ebook = LinkEpub();

            Subject.RecoverImported(ForImport(_book, ebook)).Should().BeFalse();
            Subject.Recover(_book.Id).Should().BeTrue();
            Subject.GetUrl(_book.Id).Should().Be(audioUrl);
            Subject.GetPath(_book.Id, _file.Id, audioToken).Should().Be(audioPath);
            Subject.GetPath(_book.Id, ebook.Id, audioToken).Should().BeNull();
            File.ReadAllBytes(audioPath).Should().Equal(originalCover);
            Mocker.GetMock<IDiskProvider>().Verify(disk => disk.MoveFile(It.IsAny<string>(), It.IsAny<string>(), false), Times.Once());
        }

        [Test]
        public void should_attest_the_exact_ebook_import_and_keep_the_result_when_its_audio_job_follows()
        {
            var ebook = LinkEpub();
            var originalEpub = File.ReadAllBytes(ebook.Path);
            var audio = Db.All<BookFile>().Single(file => file.Id == _file.Id);

            Subject.RecoverImported(ForImport(_book, ebook)).Should().BeTrue();
            Subject.RecoverImported(ForImport(_book, audio)).Should().BeFalse();
            Subject.RecoverImported(ForImport(_book, ebook)).Should().BeFalse();
            var url = Subject.GetUrl(_book.Id);
            url.Should().StartWith($"/read/library/api/v1/embeddedbookcover/{_book.Id}/{ebook.Id}/");
            var token = url.Split('/').Last();
            Subject.GetPath(_book.Id, audio.Id, token).Should().BeNull();
            using var cover = Image.Load(Subject.GetPath(_book.Id, ebook.Id, token));
            cover.Width.Should().Be(24);
            cover.Height.Should().Be(36);
            cover.Metadata.DecodedImageFormat.Should().BeSameAs(JpegFormat.Instance);

            File.ReadAllBytes(ebook.Path).Should().Equal(originalEpub);
            File.ReadAllBytes(audio.Path).Should().Equal(_original);
            Db.All<BookFile>().Should().Contain(file => file.Id == ebook.Id && file.EditionId == ebook.EditionId);
            Db.All<BookFile>().Should().Contain(file => file.Id == audio.Id && file.EditionId == _edition.Id);
            Db.All<Edition>().Should().OnlyContain(edition => edition.BookId == _book.Id && !edition.Images.Any());
        }

        [Test]
        public void should_keep_ebook_cache_current_when_its_audio_counterpart_is_restored()
        {
            var ebook = LinkEpub();
            _file.EditionId = 0;
            Db.Update(_file);
            Subject.RecoverImported(ForImport(_book, ebook)).Should().BeTrue();
            var ebookUrl = Subject.GetUrl(_book.Id);
            _file.EditionId = _edition.Id;
            Db.Update(_file);

            Subject.Recover(_book.Id).Should().BeTrue();
            Subject.GetUrl(_book.Id).Should().Be(ebookUrl);
            Mocker.GetMock<IDiskProvider>().Verify(disk => disk.MoveFile(It.IsAny<string>(), It.IsAny<string>(), false), Times.Once());
        }

        [TestCase(false)]
        [TestCase(true)]
        public void should_try_audio_separately_when_the_epub_lacks_a_cover(bool scheduled)
        {
            var ebook = LinkEpub(declareCover: false);
            Subject.RecoverImported(ForImport(_book, ebook)).Should().BeFalse();
            Subject.GetUrl(_book.Id).Should().BeNull();

            var recovered = scheduled
                ? Subject.RecoverImported(ForImport(_book, Db.All<BookFile>().Single(file => file.Id == _file.Id)))
                : Subject.Recover(_book.Id);
            recovered.Should().BeTrue();
            Subject.GetUrl(_book.Id).Should().StartWith($"/read/library/api/v1/embeddedbookcover/{_book.Id}/{_file.Id}/");
        }

        [Test]
        public void should_reassess_lease_and_provider_precedence_for_a_published_ebook()
        {
            var ebook = LinkEpub();
            Subject.RecoverImported(ForImport(_book, ebook)).Should().BeTrue();
            var token = Subject.GetUrl(_book.Id).Split('/').Last();
            Subject.GetPath(_book.Id, ebook.Id, token).Should().NotBeNull();

            ebook.Modified = ebook.Modified.AddSeconds(3);
            Db.Update(ebook);
            Subject.GetPath(_book.Id, ebook.Id, token).Should().BeNull();
            Subject.GetUrl(_book.Id).Should().BeNull();

            _edition.Images.Add(new NzbDrone.Core.MediaCover.MediaCover(MediaCoverTypes.Cover, "https://example.invalid/provider.jpg"));
            Db.Update(_edition);
            Subject.RecoverImported(ForImport(_book, ebook)).Should().BeFalse();
            Subject.GetUrl(_book.Id).Should().BeNull();
        }

        [TestCase(false, "EPUB")]
        [TestCase(true, "Audiobook")]
        public void should_not_read_an_epub_when_the_linked_edition_does_not_establish_ebook_type(bool isEbook, string format)
        {
            var ebook = LinkEpub(isEbook: isEbook, format: format);
            Subject.RecoverImported(ForImport(_book, ebook)).Should().BeFalse();
            Subject.GetUrl(_book.Id).Should().BeNull();
            Mocker.GetMock<IRootFolderService>().Verify(roots => roots.GetBestRootFolder(It.IsAny<string>()), Times.Never);
        }

        private BookFile LinkEpub(bool declareCover = true, bool isEbook = true, string format = "EPUB")
        {
            var edition = Db.Insert(new Edition
            {
                BookId = _book.Id,
                ForeignEditionId = "hardcover:edition:43",
                Title = "City Lights",
                TitleSlug = "city-lights-ebook",
                Format = format,
                IsEbook = isEbook
            });
            var path = Path.Combine(_library, "City Lights.EPUB");
            using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
            {
                AddEntry(zip, "mimetype", "application/epub+zip"u8);
                AddEntry(zip, "META-INF/container.xml", """
                    <container xmlns="urn:oasis:names:tc:opendocument:xmlns:container">
                      <rootfiles><rootfile full-path="OEBPS/content.opf" media-type="application/oebps-package+xml"/></rootfiles>
                    </container>
                    """u8);
                var opf = declareCover
                    ? """<package xmlns="http://www.idpf.org/2007/opf" version="3.0"><metadata/><manifest><item id="cover" href="cover.png" media-type="image/png" properties="cover-image"/></manifest></package>"""u8
                    : """<package xmlns="http://www.idpf.org/2007/opf" version="3.0"><metadata/><manifest/></package>"""u8;
                AddEntry(zip, "OEBPS/content.opf", opf);
                using var image = new Image<Rgba32>(24, 36);
                using var member = zip.CreateEntry("OEBPS/cover.png").Open();
                image.SaveAsPng(member);
            }

            var file = Db.Insert(new BookFile
            {
                EditionId = edition.Id,
                Path = path,
                Size = new FileInfo(path).Length,
                Modified = File.GetLastWriteTimeUtc(path),
                Quality = new QualityModel(Quality.EPUB)
            });
            return Db.All<BookFile>().Single(current => current.Id == file.Id);
        }

        private static void AddEntry(ZipArchive archive, string name, ReadOnlySpan<byte> data)
        {
            using var entry = archive.CreateEntry(name).Open();
            entry.Write(data);
        }

        private static RecoverImportedBookCoverCommand ForImport(Book book, BookFile file)
        {
            return new RecoverImportedBookCoverCommand
            {
                BookId = book.Id,
                CanonicalId = book.CanonicalId,
                EditionId = file.EditionId,
                FileId = file.Id,
                Size = file.Size,
                ModifiedTicks = file.Modified.Ticks,
                Trigger = CommandTrigger.Scheduled
            };
        }

        private static byte[] Box(ReadOnlySpan<byte> type, params byte[][] contents)
        {
            var box = new byte[8 + contents.Sum(item => item.Length)];
            BinaryPrimitives.WriteUInt32BigEndian(box, (uint)box.Length);
            type.CopyTo(box.AsSpan(4));
            var offset = 8;
            foreach (var item in contents)
            {
                item.CopyTo(box, offset);
                offset += item.Length;
            }

            return box;
        }
    }
}
