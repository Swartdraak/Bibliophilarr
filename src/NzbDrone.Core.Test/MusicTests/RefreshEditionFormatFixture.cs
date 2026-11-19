using System;
using System.Collections.Generic;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.MusicTests
{
    [TestFixture]
    public class RefreshEditionFormatFixture : CoreTest<RefreshEditionService>
    {
        [TestCase("book.epub", "Ebook", true, "Audiobook", false)]
        [TestCase("book.m4b", "Audiobook", false, "Ebook", true)]
        public void should_preserve_linked_edition_metadata_when_provider_format_conflicts(
            string filePath, string localFormat, bool localIsEbook, string remoteFormat, bool remoteIsEbook)
        {
            var existing = new Edition
            {
                Id = 10,
                ForeignEditionId = "provider:edition:20",
                Format = localFormat,
                IsEbook = localIsEbook,
                Asin = "ORIGINAL"
            };
            var remote = new Edition
            {
                ForeignEditionId = existing.ForeignEditionId,
                Format = remoteFormat,
                IsEbook = remoteIsEbook,
                Asin = "DIFFERENT"
            };
            Mocker.GetMock<IMediaFileService>().Setup(service => service.GetFilesByEdition(existing.Id))
                .Returns(new List<BookFile> { new BookFile { EditionId = existing.Id, Path = filePath } });

            Refresh(existing, remote);

            existing.Format.Should().Be(localFormat);
            existing.IsEbook.Should().Be(localIsEbook);
            existing.Asin.Should().Be("ORIGINAL");
            ExceptionVerification.ExpectedWarns(1);
        }

        [TestCase("book.epub")]
        [TestCase(null)]
        public void should_update_edition_metadata_when_provider_format_is_supported_or_no_file_is_linked(string filePath)
        {
            var existing = new Edition
            {
                Id = 10,
                ForeignEditionId = "provider:edition:20",
                Format = Edition.UnclassifiedFormat
            };
            var remote = new Edition
            {
                ForeignEditionId = existing.ForeignEditionId,
                Format = "Ebook",
                IsEbook = true,
                Asin = "UPDATED"
            };
            var files = filePath == null ? new List<BookFile>() : new List<BookFile> { new BookFile { EditionId = existing.Id, Path = filePath } };
            Mocker.GetMock<IMediaFileService>().Setup(service => service.GetFilesByEdition(existing.Id)).Returns(files);

            Refresh(existing, remote);

            existing.Format.Should().Be("Ebook");
            existing.IsEbook.Should().BeTrue();
            existing.Asin.Should().Be("UPDATED");
        }

        [Test]
        public void should_persist_canonical_owner_on_the_existing_edition()
        {
            var existing = new Edition { Id = 10, BookId = 20, ForeignEditionId = "provider:edition:30" };
            var canonical = new Book { Id = 40 };
            var remote = new Edition { BookId = canonical.Id, Book = canonical, ForeignEditionId = existing.ForeignEditionId };

            Refresh(existing, remote);

            existing.Id.Should().Be(10);
            existing.BookId.Should().Be(canonical.Id);
            existing.Book.Value.Should().BeSameAs(canonical);
        }

        private void Refresh(Edition existing, Edition remote)
        {
            Subject.RefreshEditionInfo(
                new List<Edition>(),
                new List<Edition> { existing },
                new List<Tuple<Edition, Edition>>(),
                new List<Edition>(),
                new List<Edition>(),
                new List<Edition> { remote },
                false);
        }
    }
}
