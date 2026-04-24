using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Qualities
{
    [TestFixture]
    public class QualityFormatTypeFixture : CoreTest
    {
        [Test]
        public void should_classify_pdf_as_ebook()
        {
            Quality.GetFormatType(Quality.PDF).Should().Be(FormatType.Ebook);
        }

        [Test]
        public void should_classify_mobi_as_ebook()
        {
            Quality.GetFormatType(Quality.MOBI).Should().Be(FormatType.Ebook);
        }

        [Test]
        public void should_classify_epub_as_ebook()
        {
            Quality.GetFormatType(Quality.EPUB).Should().Be(FormatType.Ebook);
        }

        [Test]
        public void should_classify_azw3_as_ebook()
        {
            Quality.GetFormatType(Quality.AZW3).Should().Be(FormatType.Ebook);
        }

        [Test]
        public void should_classify_mp3_as_audiobook()
        {
            Quality.GetFormatType(Quality.MP3).Should().Be(FormatType.Audiobook);
        }

        [Test]
        public void should_classify_m4b_as_audiobook()
        {
            Quality.GetFormatType(Quality.M4B).Should().Be(FormatType.Audiobook);
        }

        [Test]
        public void should_classify_flac_as_audiobook()
        {
            Quality.GetFormatType(Quality.FLAC).Should().Be(FormatType.Audiobook);
        }

        [Test]
        public void should_classify_unknown_audio_as_audiobook()
        {
            Quality.GetFormatType(Quality.UnknownAudio).Should().Be(FormatType.Audiobook);
        }

        [Test]
        public void should_classify_unknown_as_ebook()
        {
            Quality.GetFormatType(Quality.Unknown).Should().Be(FormatType.Ebook);
        }

        [Test]
        public void should_classify_null_as_ebook()
        {
            Quality.GetFormatType(null).Should().Be(FormatType.Ebook);
        }
    }
}
