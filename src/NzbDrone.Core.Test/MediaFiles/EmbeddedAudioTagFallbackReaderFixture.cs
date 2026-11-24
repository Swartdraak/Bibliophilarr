using System.ComponentModel;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Processes;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MediaFiles
{
    [TestFixture]
    public class EmbeddedAudioTagFallbackReaderFixture : CoreTest<EmbeddedAudioTagFallbackReader>
    {
        [Test]
        public void should_report_no_fallback_tags_when_ffprobe_is_missing()
        {
            Mocker.GetMock<IProcessProvider>()
                .Setup(provider => provider.StartAndCapture("ffprobe", It.IsAny<string>(), null))
                .Throws(new Win32Exception(2));

            Subject.ReadTags("disposable.wav").Should().BeNull();
        }
    }
}
