using System.Collections.Generic;
using FizzWare.NBuilder;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.MediaFiles.BookImport.Specifications;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.RootFolders;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.MediaFiles.TrackImport.Specifications
{
    [TestFixture]
    public class AuthorPathInRootFolderFormatFixture : CoreTest<AuthorPathInRootFolderSpecification>
    {
        private Author _author;
        private LocalEdition _localEdition;

        [SetUp]
        public void Setup()
        {
            _author = Builder<Author>.CreateNew()
                .With(a => a.Id = 1)
                .With(a => a.Path = "/media/books/Author Name")
                .Build();

            var book = Builder<Book>.CreateNew()
                .With(b => b.Author = _author)
                .Build();

            var edition = Builder<Edition>.CreateNew()
                .With(e => e.Book = book)
                .With(e => e.IsEbook = true)
                .Build();

            var localBook = new LocalBook
            {
                Path = "/media/books/Author Name/Book Title.m4b",
                Quality = new QualityModel(Quality.M4B)
            };

            _localEdition = new LocalEdition(new List<LocalBook> { localBook })
            {
                Edition = edition
            };

            // Default: all root folder checks pass
            Mocker.GetMock<IRootFolderService>()
                .Setup(s => s.GetBestRootFolder(It.IsAny<string>()))
                .Returns(Builder<RootFolder>.CreateNew().Build());
        }

        [Test]
        public void should_accept_when_flag_off()
        {
            Mocker.GetMock<IConfigService>()
                .Setup(s => s.EnableDualFormatTracking).Returns(false);

            Subject.IsSatisfiedBy(_localEdition, null).Accepted.Should().BeTrue();

            // Format profile should NOT be consulted when flag is off
            Mocker.GetMock<IAuthorFormatProfileService>()
                .Verify(v => v.GetByAuthorIdAndFormat(It.IsAny<int>(), It.IsAny<FormatType>()), Times.Never());
        }

        [Test]
        public void should_accept_when_flag_on_and_no_format_profile_root_folder()
        {
            Mocker.GetMock<IConfigService>()
                .Setup(s => s.EnableDualFormatTracking).Returns(true);

            // Format profile with empty root folder path
            Mocker.GetMock<IAuthorFormatProfileService>()
                .Setup(s => s.GetByAuthorIdAndFormat(1, FormatType.Audiobook))
                .Returns(new AuthorFormatProfile { RootFolderPath = string.Empty });

            Subject.IsSatisfiedBy(_localEdition, null).Accepted.Should().BeTrue();
        }

        [Test]
        public void should_accept_when_flag_on_and_format_root_folder_is_valid()
        {
            Mocker.GetMock<IConfigService>()
                .Setup(s => s.EnableDualFormatTracking).Returns(true);

            Mocker.GetMock<IAuthorFormatProfileService>()
                .Setup(s => s.GetByAuthorIdAndFormat(1, FormatType.Audiobook))
                .Returns(new AuthorFormatProfile { RootFolderPath = "/media/audiobooks" });

            Mocker.GetMock<IRootFolderService>()
                .Setup(s => s.GetBestRootFolder("/media/audiobooks"))
                .Returns(Builder<RootFolder>.CreateNew().Build());

            Subject.IsSatisfiedBy(_localEdition, null).Accepted.Should().BeTrue();
        }

        [Test]
        public void should_reject_when_flag_on_and_format_root_folder_is_not_managed()
        {
            Mocker.GetMock<IConfigService>()
                .Setup(s => s.EnableDualFormatTracking).Returns(true);

            Mocker.GetMock<IAuthorFormatProfileService>()
                .Setup(s => s.GetByAuthorIdAndFormat(1, FormatType.Audiobook))
                .Returns(new AuthorFormatProfile { RootFolderPath = "/unmanaged/audiobooks" });

            // Generic root folder check passes
            Mocker.GetMock<IRootFolderService>()
                .Setup(s => s.GetBestRootFolder(_author.Path))
                .Returns(Builder<RootFolder>.CreateNew().Build());

            // Format-specific root folder check fails
            Mocker.GetMock<IRootFolderService>()
                .Setup(s => s.GetBestRootFolder("/unmanaged/audiobooks"))
                .Returns((RootFolder)null);

            Subject.IsSatisfiedBy(_localEdition, null).Accepted.Should().BeFalse();

            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void should_skip_format_check_when_author_is_new()
        {
            Mocker.GetMock<IConfigService>()
                .Setup(s => s.EnableDualFormatTracking).Returns(true);

            // Author with Id 0 means not yet persisted
            _author.Id = 0;

            Subject.IsSatisfiedBy(_localEdition, null).Accepted.Should().BeTrue();

            Mocker.GetMock<IAuthorFormatProfileService>()
                .Verify(v => v.GetByAuthorIdAndFormat(It.IsAny<int>(), It.IsAny<FormatType>()), Times.Never());
        }
    }
}
