using FizzWare.NBuilder;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.DecisionEngine.Specifications;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Profiles.Qualities;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.DecisionEngineTests
{
    [TestFixture]
    public class QualityAllowedByProfileFormatFixture : CoreTest<QualityAllowedByProfileSpecification>
    {
        private RemoteBook _remoteBook;
        private QualityProfile _ebookProfile;
        private QualityProfile _audiobookProfile;

        [SetUp]
        public void Setup()
        {
            var fakeAuthor = Builder<Author>.CreateNew()
                .With(c => c.Id = 1)
                .With(c => c.QualityProfile = new QualityProfile
                {
                    Id = 1,
                    Name = "Default",
                    Cutoff = Quality.EPUB.Id,
                    Items = Qualities.QualityFixture.GetDefaultQualities(Quality.EPUB, Quality.MOBI, Quality.PDF)
                })
                .Build();

            _remoteBook = new RemoteBook
            {
                Author = fakeAuthor,
                ParsedBookInfo = new ParsedBookInfo { Quality = new QualityModel(Quality.EPUB) },
            };

            _ebookProfile = new QualityProfile
            {
                Id = 10,
                Name = "eBook",
                Cutoff = Quality.EPUB.Id,
                Items = Qualities.QualityFixture.GetDefaultQualities(Quality.EPUB, Quality.MOBI, Quality.PDF)
            };

            _audiobookProfile = new QualityProfile
            {
                Id = 20,
                Name = "Spoken",
                Cutoff = Quality.FLAC.Id,
                Items = Qualities.QualityFixture.GetDefaultQualities(Quality.MP3, Quality.FLAC, Quality.M4B)
            };
        }

        [Test]
        public void should_accept_epub_with_ebook_format_profile()
        {
            Mocker.GetMock<IConfigService>()
                .Setup(s => s.EnableDualFormatTracking).Returns(true);

            Mocker.GetMock<IAuthorFormatProfileService>()
                .Setup(s => s.GetByAuthorIdAndFormat(1, FormatType.Ebook))
                .Returns(new AuthorFormatProfile { QualityProfileId = 10 });

            Mocker.GetMock<IQualityProfileService>()
                .Setup(s => s.Get(10))
                .Returns(_ebookProfile);

            _remoteBook.ParsedBookInfo.Quality = new QualityModel(Quality.EPUB);

            var result = Subject.IsSatisfiedBy(_remoteBook, null);

            result.Accepted.Should().BeTrue();
            _remoteBook.ResolvedFormatType.Should().Be(FormatType.Ebook);
            _remoteBook.ResolvedQualityProfile.Should().BeSameAs(_ebookProfile);
        }

        [Test]
        public void should_accept_m4b_with_audiobook_format_profile()
        {
            Mocker.GetMock<IConfigService>()
                .Setup(s => s.EnableDualFormatTracking).Returns(true);

            Mocker.GetMock<IAuthorFormatProfileService>()
                .Setup(s => s.GetByAuthorIdAndFormat(1, FormatType.Audiobook))
                .Returns(new AuthorFormatProfile { QualityProfileId = 20 });

            Mocker.GetMock<IQualityProfileService>()
                .Setup(s => s.Get(20))
                .Returns(_audiobookProfile);

            _remoteBook.ParsedBookInfo.Quality = new QualityModel(Quality.M4B);

            var result = Subject.IsSatisfiedBy(_remoteBook, null);

            result.Accepted.Should().BeTrue();
            _remoteBook.ResolvedFormatType.Should().Be(FormatType.Audiobook);
        }

        [Test]
        public void should_reject_epub_when_only_audiobook_profile_configured()
        {
            Mocker.GetMock<IConfigService>()
                .Setup(s => s.EnableDualFormatTracking).Returns(true);

            // No ebook format profile — returns null
            Mocker.GetMock<IAuthorFormatProfileService>()
                .Setup(s => s.GetByAuthorIdAndFormat(1, FormatType.Ebook))
                .Returns((AuthorFormatProfile)null);

            // EPUB is ebook quality; author's default profile only allows audio
            _remoteBook.Author.QualityProfile.Value.Items =
                Qualities.QualityFixture.GetDefaultQualities(Quality.MP3, Quality.FLAC, Quality.M4B);

            _remoteBook.ParsedBookInfo.Quality = new QualityModel(Quality.EPUB);

            var result = Subject.IsSatisfiedBy(_remoteBook, null);

            result.Accepted.Should().BeFalse();
        }

        [Test]
        public void should_use_default_profile_when_flag_off()
        {
            Mocker.GetMock<IConfigService>()
                .Setup(s => s.EnableDualFormatTracking).Returns(false);

            _remoteBook.ParsedBookInfo.Quality = new QualityModel(Quality.EPUB);

            var result = Subject.IsSatisfiedBy(_remoteBook, null);

            result.Accepted.Should().BeTrue();
            _remoteBook.ResolvedFormatType.Should().BeNull();
            _remoteBook.ResolvedQualityProfile.Should().BeNull();
        }
    }
}
