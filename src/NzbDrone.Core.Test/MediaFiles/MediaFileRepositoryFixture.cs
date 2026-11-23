using System.Collections.Generic;
using System.Linq;
using FizzWare.NBuilder;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.DecisionEngine;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.MediaFiles
{
    [TestFixture]
    public class MediaFileRepositoryFixture : DbTest<MediaFileRepository, BookFile>
    {
        private Author _author;
        private Book _book;
        private Edition _edition;

        [SetUp]
        public void Setup()
        {
            var meta = Builder<AuthorMetadata>.CreateNew()
                .With(a => a.Id = 0)
                .Build();
            Db.Insert(meta);

            _author = Builder<Author>.CreateNew()
                .With(a => a.AuthorMetadataId = meta.Id)
                .With(a => a.Id = 0)
                .Build();
            Db.Insert(_author);

            _book = Builder<Book>.CreateNew()
                .With(a => a.Id = 0)
                .With(a => a.AuthorMetadataId = _author.AuthorMetadataId)
                .Build();
            Db.Insert(_book);

            _edition = Builder<Edition>.CreateNew()
                .With(a => a.Id = 0)
                .With(a => a.BookId = _book.Id)
                .Build();
            Db.Insert(_edition);

            var files = Builder<BookFile>.CreateListOfSize(10)
                .All()
                .With(c => c.Id = 0)
                .With(c => c.UnmappedReason = null)
                .With(c => c.Quality = new QualityModel(Quality.MP3))
                .TheFirst(5)
                .With(c => c.EditionId = _edition.Id)
                .TheRest()
                .With(c => c.EditionId = 0)
                .TheFirst(1)
                .With(c => c.Path = @"C:\Test\Path\Author\somefile1.flac".AsOsAgnostic())
                .TheNext(1)
                .With(c => c.Path = @"C:\Test\Path\Author\somefile2.flac".AsOsAgnostic())
                .BuildListOfNew();
            Db.InsertMany(files);
        }

        [Test]
        public void get_files_by_author()
        {
            VerifyData();
            var authorFiles = Subject.GetFilesByAuthor(_author.Id);
            VerifyEagerLoaded(authorFiles);

            authorFiles.Should().OnlyContain(c => c.Author.Value.Id == _author.Id);
        }

        [Test]
        public void get_unmapped_files()
        {
            VerifyData();
            var unmappedfiles = Subject.GetUnmappedFiles();
            VerifyUnmapped(unmappedfiles);

            unmappedfiles.Should().HaveCount(5);
        }

        [TestCase(LibraryImportHoldReason.NoAcceptedWork)]
        [TestCase(LibraryImportHoldReason.ProviderIdentityConflict)]
        [TestCase(LibraryImportHoldReason.AuthorBibliographyUnavailable)]
        [TestCase(LibraryImportHoldReason.AuthorIdentityConfirmationRequired)]
        public void should_persist_updated_reasons_only_while_the_same_file_is_unmapped(LibraryImportHoldReason initialReason)
        {
            var files = Subject.GetUnmappedFiles();
            var file = files.First();
            var untouched = files.Last();
            file.UnmappedReason.Should().BeNull();

            file.UnmappedReason = initialReason;
            untouched.UnmappedReason = LibraryImportHoldReason.SameSizeAsExisting;
            Subject.SetUnmappedReasons(new List<BookFile> { file, untouched });
            Subject.Get(file.Id).UnmappedReason.Should().Be(initialReason);

            file.UnmappedReason = LibraryImportHoldReason.LowConfidence;
            Subject.SetUnmappedReasons(new List<BookFile> { file });
            Subject.Get(file.Id).UnmappedReason.Should().Be(LibraryImportHoldReason.LowConfidence);
            Subject.Get(untouched.Id).UnmappedReason.Should().Be(LibraryImportHoldReason.SameSizeAsExisting);

            Subject.TryLinkUnmappedFile(file, _edition).Should().BeTrue();
            Subject.SetUnmappedReasons(new List<BookFile> { file });
            file.Size = 1024;
            Subject.SetFields(new List<BookFile> { file }, stored => stored.Size);
            Subject.Get(file.Id).UnmappedReason.Should().BeNull();
            Subject.Get(file.Id).EditionId.Should().Be(_edition.Id);
            Subject.Get(file.Id).Size.Should().Be(1024);
        }

        [Test]
        public void should_link_the_original_row_only_when_its_path_is_still_unmapped_and_the_edition_has_the_expected_owner()
        {
            var file = Subject.GetUnmappedFiles().First();
            var invalidOwner = new Edition { Id = _edition.Id, BookId = _book.Id + 1 };
            var stalePath = new BookFile { Id = file.Id, Path = file.Path + ".moved" };

            Subject.TryLinkUnmappedFile(file, invalidOwner).Should().BeFalse();
            Subject.TryLinkUnmappedFile(stalePath, _edition).Should().BeFalse();
            Subject.TryLinkUnmappedFile(file, _edition).Should().BeTrue();
            Subject.TryLinkUnmappedFile(file, _edition).Should().BeFalse();

            var stored = Subject.Get(file.Id);
            stored.EditionId.Should().Be(_edition.Id);
            stored.Path.Should().Be(file.Path);
            stored.DateAdded.Should().Be(file.DateAdded);
            stored.Modified.Should().Be(file.Modified);
            stored.CalibreId.Should().Be(file.CalibreId);
            Db.All<BookFile>().Should().HaveCount(10);
        }

        [Test]
        public void should_link_only_an_unchanged_unmapped_audio_part_that_is_free_in_the_exact_edition()
        {
            var audio = Db.Insert(new Edition
            {
                BookId = _book.Id,
                ForeignEditionId = "fixture-audio-edition",
                Title = "Fixture audio",
                TitleSlug = "fixture-audio",
                Format = "Audiobook"
            });
            var other = Db.Insert(new Edition
            {
                BookId = _book.Id,
                ForeignEditionId = "fixture-ebook-edition",
                Title = "Fixture ebook",
                TitleSlug = "fixture-ebook",
                Format = "Ebook",
                IsEbook = true
            });
            Db.Insert(new BookFile { Path = "/library/existing.m4b", EditionId = audio.Id, Part = 1, Quality = new QualityModel(Quality.M4B) });
            Db.Insert(new BookFile { Path = "/library/unrelated.epub", EditionId = other.Id, Part = 2, Quality = new QualityModel(Quality.EPUB) });
            var collision = Db.Insert(new BookFile
            {
                Path = "/library/hold.m4b",
                Part = 1,
                UnmappedReason = LibraryImportHoldReason.DuplicateInScan,
                Quality = new QualityModel(Quality.M4B)
            });
            var free = Db.Insert(new BookFile
            {
                Path = "/library/free.m4b",
                Part = 2,
                UnmappedReason = LibraryImportHoldReason.DuplicateInScan,
                Quality = new QualityModel(Quality.M4B)
            });
            var competing = Db.Insert(new BookFile
            {
                Path = "/library/competing.m4b",
                Part = 2,
                UnmappedReason = LibraryImportHoldReason.DuplicateInScan,
                Quality = new QualityModel(Quality.M4B)
            });
            var wrongOwner = new Edition { Id = audio.Id, BookId = _book.Id + 1, Format = audio.Format };
            var changedFormat = new Edition { Id = audio.Id, BookId = _book.Id, Format = "MP3" };
            var stalePart = new BookFile { Id = free.Id, Path = free.Path, Part = 3, UnmappedReason = free.UnmappedReason };
            var stalePath = new BookFile { Id = free.Id, Path = free.Path + ".moved.m4b", Part = free.Part, UnmappedReason = free.UnmappedReason };

            Subject.TryLinkUnmappedAudiobookPart(collision, audio).Should().BeFalse();
            Subject.TryLinkUnmappedAudiobookPart(free, wrongOwner).Should().BeFalse();
            Subject.TryLinkUnmappedAudiobookPart(free, changedFormat).Should().BeFalse();
            Subject.TryLinkUnmappedAudiobookPart(stalePart, audio).Should().BeFalse();
            Subject.TryLinkUnmappedAudiobookPart(stalePath, audio).Should().BeFalse();
            Subject.TryLinkUnmappedAudiobookPart(free, audio).Should().BeTrue();
            Subject.TryLinkUnmappedAudiobookPart(competing, audio).Should().BeFalse();

            Subject.Get(collision.Id).EditionId.Should().Be(0);
            Subject.Get(collision.Id).UnmappedReason.Should().Be(LibraryImportHoldReason.DuplicateInScan);
            Subject.Get(free.Id).EditionId.Should().Be(audio.Id);
            Subject.Get(free.Id).Part.Should().Be(2);
            Subject.Get(free.Id).UnmappedReason.Should().BeNull();
            Subject.Get(competing.Id).EditionId.Should().Be(0);
            Subject.GetFilesByEdition(other.Id).Should().ContainSingle();
        }

        [TestCase("Print")]
        [TestCase(null)]
        public void should_repair_only_the_unchanged_file_and_edition_in_the_same_work(string previousFormat)
        {
            var previous = Db.Insert(new Edition
            {
                BookId = _book.Id,
                ForeignEditionId = "previous-edition",
                Title = "Previous",
                TitleSlug = "previous-edition",
                Format = previousFormat
            });
            var digital = Db.Insert(new Edition
            {
                BookId = _book.Id,
                ForeignEditionId = "digital-edition",
                Title = "Digital",
                TitleSlug = "digital-edition",
                Format = "Ebook",
                IsEbook = true
            });
            var tracked = Db.Insert(new BookFile { Path = "/library/actual.epub", EditionId = previous.Id, Quality = new QualityModel(Quality.EPUB) });
            var wrongWork = new Edition { Id = digital.Id, BookId = _book.Id + 1, Format = digital.Format, IsEbook = true };
            var changedPrevious = new Edition { Id = previous.Id, BookId = _book.Id, Format = "Audiobook" };

            Subject.TryRepairUnsupportedEditionLink(tracked, previous, wrongWork).Should().BeFalse();
            Subject.TryRepairUnsupportedEditionLink(tracked, changedPrevious, digital).Should().BeFalse();
            Subject.TryRepairUnsupportedEditionLink(tracked, previous, digital).Should().BeTrue();
            Subject.TryRepairUnsupportedEditionLink(tracked, previous, digital).Should().BeFalse();

            var repaired = Subject.Get(tracked.Id);
            repaired.EditionId.Should().Be(digital.Id);
            repaired.DateAdded.Should().Be(tracked.DateAdded);
            repaired.Path.Should().Be(tracked.Path);
        }

        [TestCase("C:\\Test\\Path")]
        [TestCase("C:\\Test\\Path\\")]
        public void get_files_by_base_path_should_cope_with_trailing_slash(string dir)
        {
            VerifyData();
            var firstReleaseFiles = Subject.GetFilesWithBasePath(dir.AsOsAgnostic());

            firstReleaseFiles.Should().HaveCount(2);
        }

        [TestCase("C:\\Test\\Path")]
        [TestCase("C:\\Test\\Path\\")]
        public void get_files_by_base_path_should_not_get_files_for_partial_path(string dir)
        {
            VerifyData();

            var files = Builder<BookFile>.CreateListOfSize(2)
                .All()
                .With(c => c.Id = 0)
                .With(c => c.Quality = new QualityModel(Quality.MP3))
                .TheFirst(1)
                .With(c => c.Path = @"C:\Test\Path2\Author\somefile1.flac".AsOsAgnostic())
                .TheNext(1)
                .With(c => c.Path = @"C:\Test\Path2\Author\somefile2.flac".AsOsAgnostic())
                .BuildListOfNew();
            Db.InsertMany(files);

            var firstReleaseFiles = Subject.GetFilesWithBasePath(dir.AsOsAgnostic());
            firstReleaseFiles.Should().HaveCount(2);
        }

        [Test]
        public void get_file_by_path()
        {
            VerifyData();
            var file = Subject.GetFileWithPath(@"C:\Test\Path\Author\somefile2.flac".AsOsAgnostic());

            file.Should().NotBeNull();
            file.Edition.IsLoaded.Should().BeTrue();
            file.Edition.Value.Should().NotBeNull();
            file.Author.IsLoaded.Should().BeTrue();
            file.Author.Value.Should().NotBeNull();
        }

        [Test]
        public void get_files_by_book()
        {
            VerifyData();
            var files = Subject.GetFilesByBook(_book.Id);
            VerifyEagerLoaded(files);

            files.Should().OnlyContain(c => c.EditionId == _book.Id);
        }

        private void VerifyData()
        {
            Db.All<Author>().Should().HaveCount(1);
            Db.All<Book>().Should().HaveCount(1);
            Db.All<BookFile>().Should().HaveCount(10);
        }

        private void VerifyEagerLoaded(List<BookFile> files)
        {
            foreach (var file in files)
            {
                file.Edition.IsLoaded.Should().BeTrue();
                file.Edition.Value.Should().NotBeNull();
                file.Author.IsLoaded.Should().BeTrue();
                file.Author.Value.Should().NotBeNull();
                file.Author.Value.Metadata.IsLoaded.Should().BeTrue();
                file.Author.Value.Metadata.Value.Should().NotBeNull();
            }
        }

        private void VerifyUnmapped(List<BookFile> files)
        {
            foreach (var file in files)
            {
                file.Edition.IsLoaded.Should().BeFalse();
                file.Edition.Value.Should().BeNull();
                file.Author.IsLoaded.Should().BeFalse();
                file.Author.Value.Should().BeNull();
            }
        }

        [Ignore("Doesn't make sense now we link to edition")]
        [Test]
        public void delete_files_by_book_should_work_if_join_fails()
        {
            Db.Delete(_book);
            Subject.DeleteFilesByBook(_book.Id);

            Db.All<BookFile>().Where(x => x.EditionId == _book.Id).Should().HaveCount(0);
        }
    }
}
