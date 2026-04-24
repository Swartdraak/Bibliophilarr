using System.IO;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.RootFolders;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.BooksTests.Utilities
{
    [TestFixture]
    public class AuthorPathBuilderFormatFixture : CoreTest<AuthorPathBuilder>
    {
        private Author _author;

        [SetUp]
        public void Setup()
        {
            _author = new Author
            {
                Id = 1,
                Name = "Test Author",
                Path = Path.Combine("/media/books", "Test Author").AsOsAgnostic()
            };

            Mocker.GetMock<IRootFolderService>()
                .Setup(s => s.GetBestRootFolderPath(It.IsAny<string>()))
                .Returns("/media/books".AsOsAgnostic());
        }

        [Test]
        public void should_return_author_path_when_no_format_profile()
        {
            Mocker.GetMock<IAuthorFormatProfileService>()
                .Setup(s => s.GetByAuthorIdAndFormat(1, FormatType.Audiobook))
                .Returns((AuthorFormatProfile)null);

            var result = Subject.BuildFormatPath(_author, FormatType.Audiobook);

            result.Should().Be(_author.Path);
        }

        [Test]
        public void should_return_author_path_when_profile_has_empty_root_folder()
        {
            Mocker.GetMock<IAuthorFormatProfileService>()
                .Setup(s => s.GetByAuthorIdAndFormat(1, FormatType.Audiobook))
                .Returns(new AuthorFormatProfile { RootFolderPath = string.Empty });

            var result = Subject.BuildFormatPath(_author, FormatType.Audiobook);

            result.Should().Be(_author.Path);
        }

        [Test]
        public void should_use_format_root_folder_with_existing_author_folder()
        {
            var formatRootFolder = "/media/audiobooks".AsOsAgnostic();

            Mocker.GetMock<IAuthorFormatProfileService>()
                .Setup(s => s.GetByAuthorIdAndFormat(1, FormatType.Audiobook))
                .Returns(new AuthorFormatProfile { RootFolderPath = formatRootFolder });

            var result = Subject.BuildFormatPath(_author, FormatType.Audiobook);

            result.Should().Be(Path.Combine(formatRootFolder, "Test Author"));
        }

        [Test]
        public void should_return_author_path_when_author_id_is_zero()
        {
            _author.Id = 0;

            var result = Subject.BuildFormatPath(_author, FormatType.Audiobook);

            result.Should().Be(_author.Path);

            Mocker.GetMock<IAuthorFormatProfileService>()
                .Verify(v => v.GetByAuthorIdAndFormat(It.IsAny<int>(), It.IsAny<FormatType>()), Times.Never());
        }

        [Test]
        public void should_use_ebook_format_root_folder()
        {
            var formatRootFolder = "/media/ebooks".AsOsAgnostic();

            Mocker.GetMock<IAuthorFormatProfileService>()
                .Setup(s => s.GetByAuthorIdAndFormat(1, FormatType.Ebook))
                .Returns(new AuthorFormatProfile { RootFolderPath = formatRootFolder });

            var result = Subject.BuildFormatPath(_author, FormatType.Ebook);

            result.Should().Be(Path.Combine(formatRootFolder, "Test Author"));
        }
    }
}
