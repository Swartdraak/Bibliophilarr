using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MusicTests
{
    [TestFixture]
    public class AuthorFormatProfileUpdateFixture : DbTest
    {
        [TestCase(false)]
        [TestCase(true)]
        public void should_only_synchronize_monitoring_when_the_format_monitor_setting_changes(bool changeMonitoring)
        {
            var metadata = Db.Insert(new AuthorMetadata
            {
                ForeignAuthorId = "hardcover:author:741",
                Name = "Fixture Author",
                SortName = "fixture author",
                NameLastFirst = "Author, Fixture",
                SortNameLastFirst = "author, fixture",
                TitleSlug = "fixture-author"
            });
            var author = Db.Insert(new Author
            {
                AuthorMetadataId = metadata.Id,
                CleanName = "fixtureauthor",
                Path = "/library/author",
                Monitored = !changeMonitoring
            });
            var ebook = Db.Insert(new AuthorFormatProfile
            {
                AuthorId = author.Id,
                FormatType = FormatType.Ebook,
                QualityProfileId = 1,
                RootFolderPath = "/ebooks",
                Path = "/ebooks/author",
                Tags = new HashSet<int> { 7 },
                Monitored = false
            });
            var audio = Db.Insert(new AuthorFormatProfile
            {
                AuthorId = author.Id,
                FormatType = FormatType.Audiobook,
                QualityProfileId = 2,
                RootFolderPath = "/audio",
                Path = "/audio/author",
                Tags = new HashSet<int> { 8 },
                Monitored = !changeMonitoring
            });
            var book = Db.Insert(new Book
            {
                AuthorMetadataId = metadata.Id,
                ForeignBookId = "hardcover:work:742",
                Title = "Fixture Work",
                TitleSlug = "fixture-work",
                CleanTitle = "fixturework",
                Monitored = false
            });
            var audioFile = AddLinkedFile(book, "audio", false, "/audio/author/work.m4b", Quality.M4B);
            var ebookFile = AddLinkedFile(book, "ebook", true, "/ebooks/author/work.epub", Quality.EPUB);

            Mocker.SetConstant<IAuthorFormatProfileRepository>(Mocker.Resolve<AuthorFormatProfileRepository>());
            Mocker.GetMock<IConfigService>().SetupGet(service => service.EnableDualFormatTracking).Returns(true);
            var authors = Mocker.GetMock<IAuthorService>();
            authors.Setup(service => service.GetAuthor(author.Id)).Returns(author);
            var books = Mocker.GetMock<IBookService>();
            books.Setup(service => service.GetBooksByAuthor(author.Id)).Returns(() => Db.All<Book>().ToList());
            var bookRepository = Mocker.Resolve<BookRepository>();
            books.Setup(service => service.SetMonitored(It.IsAny<IEnumerable<int>>(), true))
                .Callback<IEnumerable<int>, bool>(bookRepository.SetMonitored);
            var service = Mocker.Resolve<AuthorFormatProfileService>();

            audio.RootFolderPath = "/new-audio";
            audio.Path = "/new-audio/author";
            audio.QualityProfileId = 3;
            audio.Monitored = true;
            service.Update(audio);

            var profiles = service.GetByAuthorId(author.Id);
            profiles.Should().HaveCount(2);
            profiles.Single(profile => profile.Id == ebook.Id).Should().BeEquivalentTo(ebook);
            profiles.Single(profile => profile.Id == audio.Id).Should().BeEquivalentTo(audio);
            Db.All<Book>().Single().Monitored.Should().Be(changeMonitoring);
            books.Verify(service => service.SetMonitored(It.Is<IEnumerable<int>>(ids => ids.SequenceEqual(new[] { book.Id })), true),
                changeMonitoring ? Times.Once() : Times.Never());
            authors.Verify(service => service.UpdateAuthor(It.Is<Author>(updated => updated.Id == author.Id && updated.Monitored)),
                changeMonitoring ? Times.Once() : Times.Never());
            Db.All<BookFile>().Select(file => (file.Id, file.EditionId, file.Path)).Should()
                .BeEquivalentTo(new[] { (audioFile.Id, audioFile.EditionId, audioFile.Path), (ebookFile.Id, ebookFile.EditionId, ebookFile.Path) });
        }

        private BookFile AddLinkedFile(Book book, string suffix, bool ebook, string path, Quality quality)
        {
            var edition = Db.Insert(new Edition
            {
                BookId = book.Id,
                ForeignEditionId = "fixture-" + suffix,
                Title = book.Title,
                TitleSlug = "fixture-" + suffix,
                Format = ebook ? "Ebook" : "Audiobook",
                IsEbook = ebook
            });
            return Db.Insert(new BookFile { EditionId = edition.Id, Path = path, Quality = new QualityModel(quality) });
        }
    }
}
