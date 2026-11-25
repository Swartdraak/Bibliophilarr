using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.Books;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.MediaCover;
using NzbDrone.Core.MediaFiles;
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
