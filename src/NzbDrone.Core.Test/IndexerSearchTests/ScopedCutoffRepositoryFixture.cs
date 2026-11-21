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
        [TestCase(FormatType.Ebook)]
        [TestCase(FormatType.Audiobook)]
        public void should_read_dual_cutoff_from_the_current_format_profile_and_best_linked_file(FormatType format)
        {
            var ebook = format == FormatType.Ebook;
            var below = ebook ? Quality.EPUB : Quality.MP3;
            var cutoff = ebook ? Quality.AZW3 : Quality.M4B;
            var other = ebook ? Quality.M4B : Quality.AZW3;
            var belowExtension = ebook ? ".epub" : ".mp3";
            var cutoffExtension = ebook ? ".azw3" : ".m4b";
            var otherExtension = ebook ? ".m4b" : ".azw3";
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
                FormatType = format,
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
                Format = ebook ? "Audiobook" : "Ebook",
                IsEbook = !ebook,
                Monitored = true
            });

            Mocker.SetConstant<IBookRepository>(Mocker.Resolve<BookRepository>());
            Mocker.GetMock<IConfigService>().SetupGet(config => config.EnableDualFormatTracking).Returns(true);
            var profiles = new List<QualityProfile>
            {
                CreateProfile(900, below, Quality.Unknown, below, other, cutoff),
                CreateProfile(901, cutoff, Quality.Unknown, below, other, cutoff)
            };
            Mocker.GetMock<IQualityProfileService>().Setup(service => service.All()).Returns(profiles);
            var service = Mocker.Resolve<BookCutoffService>();

            PagingSpec<Book> Page(bool monitored)
            {
                return service.BooksWhereCutoffUnmet(new PagingSpec<Book> { Page = 1, PageSize = 10, SortKey = "Id" }, format, monitored);
            }

            Db.Insert(new BookFile { EditionId = edition.Id, Path = "/library/other" + otherExtension, Quality = new QualityModel(other) });
            Db.Insert(new BookFile { EditionId = edition.Id, Path = "/library/legacy-mismatch" + otherExtension.ToUpperInvariant(), Quality = new QualityModel(below) });
            Db.Insert(new BookFile { EditionId = edition.Id, Path = "/library/unknown", Quality = new QualityModel(Quality.Unknown) });
            Page(true).Records.Should().BeEmpty("the other format, contradictory path and generic unknown do not establish a below-cutoff file");
            Db.Insert(new BookFile { EditionId = edition.Id, Path = "/library/work" + belowExtension, Quality = new QualityModel(below) });
            Db.Insert(new BookFile { EditionId = edition.Id, Path = "/library/work-copy" + belowExtension, Quality = new QualityModel(below) });
            var matched = Page(true);
            matched.Records.Select(item => item.Id).Should().Equal(book.Id);
            matched.TotalRecords.Should().Be(1);

            var repository = Mocker.Resolve<BookRepository>();
            repository.SetFormatMonitored(new[] { book.Id }, format, false);
            Page(true).Records.Should().BeEmpty();
            Page(false).Records.Select(item => item.Id).Should().Equal(book.Id);

            repository.SetFormatMonitored(new[] { book.Id }, format, true);
            var formatProfiles = Mocker.Resolve<AuthorFormatProfileRepository>();
            profile.QualityProfileId = 900;
            formatProfiles.Update(profile);
            Page(true).Records.Should().BeEmpty("the current format profile now places the existing file at cutoff");

            profile.QualityProfileId = 901;
            profile.Monitored = false;
            formatProfiles.Update(profile);
            Page(true).Records.Should().BeEmpty("a disabled format profile does not appear as monitored");
            Page(false).Records.Select(item => item.Id).Should().Equal(book.Id);
            profile.Monitored = true;
            formatProfiles.Update(profile);

            Db.Insert(new BookFile { EditionId = edition.Id, Path = "/library/work-target" + cutoffExtension, Quality = new QualityModel(cutoff) });
            Page(true).Records.Should().BeEmpty("a better copy of the same format already meets the cutoff");
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
