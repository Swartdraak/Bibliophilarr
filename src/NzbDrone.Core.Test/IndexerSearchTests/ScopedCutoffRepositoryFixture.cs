using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Profiles.Qualities;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.IndexerSearchTests
{
    [TestFixture]
    public class ScopedCutoffRepositoryFixture : DbTest
    {
        [Test]
        public void should_read_dual_cutoff_from_the_current_format_profile_and_actual_linked_file()
        {
            var metadata = Db.Insert(new AuthorMetadata
            {
                ForeignAuthorId = "hardcover:author:123",
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
                Monitored = true,
                QualityProfileId = 900
            });
            var profile = Db.Insert(new AuthorFormatProfile
            {
                AuthorId = author.Id,
                FormatType = FormatType.Ebook,
                QualityProfileId = 901,
                RootFolderPath = "/library/ebook",
                Monitored = true
            });
            var book = Db.Insert(new Book
            {
                AuthorMetadataId = metadata.Id,
                ForeignBookId = "hardcover:work:456",
                Title = "Fixture Work",
                TitleSlug = "fixture-work",
                CleanTitle = "fixturework",
                Monitored = true
            });
            var edition = Db.Insert(new Edition
            {
                BookId = book.Id,
                ForeignEditionId = "fixture-edition",
                Title = book.Title,
                TitleSlug = "fixture-edition",
                Format = "Audiobook",
                IsEbook = false,
                Monitored = true
            });

            Mocker.SetConstant<IBookRepository>(Mocker.Resolve<BookRepository>());
            Mocker.GetMock<IConfigService>().SetupGet(config => config.EnableDualFormatTracking).Returns(true);
            var profiles = new List<QualityProfile>
            {
                CreateProfile(900, Quality.EPUB, Quality.EPUB, Quality.AZW3, Quality.M4B),
                CreateProfile(901, Quality.AZW3, Quality.EPUB, Quality.M4B, Quality.AZW3)
            };
            Mocker.GetMock<IQualityProfileService>().Setup(service => service.All()).Returns(profiles);
            var service = Mocker.Resolve<BookCutoffService>();

            PagingSpec<Book> Page(bool monitored)
            {
                return service.BooksWhereCutoffUnmet(new PagingSpec<Book> { Page = 1, PageSize = 10, SortKey = "Id" }, FormatType.Ebook, monitored);
            }

            Db.Insert(new BookFile { EditionId = edition.Id, Path = "/library/work.m4b", Quality = new QualityModel(Quality.M4B) });
            Page(true).Records.Should().BeEmpty("another format does not satisfy ebook presence");
            Db.Insert(new BookFile { EditionId = edition.Id, Path = "/library/work.epub", Quality = new QualityModel(Quality.EPUB) });
            Db.Insert(new BookFile { EditionId = edition.Id, Path = "/library/work-copy.epub", Quality = new QualityModel(Quality.EPUB) });
            var matched = Page(true);
            matched.Records.Select(item => item.Id).Should().Equal(book.Id);
            matched.TotalRecords.Should().Be(1);

            book.EbookMonitoringOverride = false;
            Mocker.Resolve<BookRepository>().Update(book);
            Page(true).Records.Should().BeEmpty();
            Page(false).Records.Select(item => item.Id).Should().Equal(book.Id);

            book.EbookMonitoringOverride = true;
            Mocker.Resolve<BookRepository>().Update(book);
            profile.QualityProfileId = 900;
            Mocker.Resolve<AuthorFormatProfileRepository>().Update(profile);
            Page(true).Records.Should().BeEmpty("the current format profile now places EPUB at cutoff");
        }

        private static QualityProfile CreateProfile(int id, Quality cutoff, params Quality[] qualities)
        {
            return new QualityProfile
            {
                Id = id,
                UpgradeAllowed = true,
                Cutoff = cutoff.Id,
                Items = qualities.Select(quality => new QualityProfileQualityItem { Quality = quality, Allowed = true }).ToList()
            };
        }
    }
}
