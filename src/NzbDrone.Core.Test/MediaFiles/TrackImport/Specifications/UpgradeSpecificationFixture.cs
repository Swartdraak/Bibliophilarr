using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.BookImport.Specifications;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Profiles.Qualities;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MediaFiles.BookImport.Specifications
{
    [TestFixture]
    public class UpgradeSpecificationFixture : CoreTest<UpgradeSpecification>
    {
        [Test]
        public void should_accept_a_first_copy_without_a_quality_profile()
        {
            Subject.IsSatisfiedBy(new LocalBook { Book = new Book() }, null).Accepted.Should().BeTrue();
        }

        [TestCase(true, true)]
        [TestCase(false, false)]
        public void should_compare_only_matching_formats_when_dual_tracking_is_enabled(bool dualTracking, bool accepted)
        {
            Mocker.GetMock<IConfigService>().SetupGet(config => config.EnableDualFormatTracking).Returns(dualTracking);
            var item = Candidate(new QualityModel(Quality.MP3), new QualityModel(Quality.EPUB));

            Subject.IsSatisfiedBy(item, null).Accepted.Should().Be(accepted);
        }

        [TestCase(false, false)]
        [TestCase(true, true)]
        public void should_use_the_configured_format_profile_to_compare_quality(bool hasFormatProfile, bool accepted)
        {
            Mocker.GetMock<IConfigService>().SetupGet(config => config.EnableDualFormatTracking).Returns(true);
            var item = Candidate(new QualityModel(Quality.MP3), new QualityModel(Quality.FLAC));
            if (hasFormatProfile)
            {
                Mocker.GetMock<IAuthorFormatProfileService>()
                    .Setup(profiles => profiles.GetByAuthorIdAndFormat(item.Author.Id, FormatType.Audiobook))
                    .Returns(new AuthorFormatProfile { QualityProfileId = 17 });
                Mocker.GetMock<IQualityProfileService>()
                    .Setup(profiles => profiles.Get(17))
                    .Returns(Profile(Quality.FLAC, Quality.MP3));
            }

            var result = Subject.IsSatisfiedBy(item, null);

            result.Accepted.Should().Be(accepted);
            if (!accepted)
            {
                result.Reason.Should().Be("Not an upgrade for existing book file(s)");
            }
        }

        [TestCase(ProperDownloadTypes.PreferAndUpgrade, 2, 1, false)]
        [TestCase(ProperDownloadTypes.PreferAndUpgrade, 1, 2, true)]
        [TestCase(ProperDownloadTypes.DoNotPrefer, 2, 1, true)]
        public void should_consider_revisions_when_propers_are_preferred(
            ProperDownloadTypes preference, int currentVersion, int incomingVersion, bool accepted)
        {
            Mocker.GetMock<IConfigService>().SetupGet(config => config.DownloadPropersAndRepacks).Returns(preference);
            var item = Candidate(
                new QualityModel(Quality.MP3, new Revision(version: incomingVersion)),
                new QualityModel(Quality.MP3, new Revision(version: currentVersion)));

            Subject.IsSatisfiedBy(item, null).Accepted.Should().Be(accepted);
        }

        private static LocalBook Candidate(QualityModel incoming, QualityModel current)
        {
            return new LocalBook
            {
                Author = new Author { Id = 7, QualityProfile = Profile(Quality.MP3, Quality.FLAC, Quality.EPUB) },
                Book = new Book { BookFiles = new List<BookFile> { new BookFile { Quality = current } } },
                Quality = incoming
            };
        }

        private static QualityProfile Profile(params Quality[] qualities)
        {
            return new QualityProfile
            {
                Items = qualities.Select(quality => new QualityProfileQualityItem { Quality = quality, Allowed = true }).ToList()
            };
        }
    }
}
