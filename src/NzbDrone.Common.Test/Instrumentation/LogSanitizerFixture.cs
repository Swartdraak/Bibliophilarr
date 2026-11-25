using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Common.Instrumentation;

namespace NzbDrone.Common.Test.Instrumentation
{
    [TestFixture]
    public class LogSanitizerFixture
    {
        [TestCase("First line\r\nSecond line", "First lineSecond line")]
        [TestCase("First\rSecond\nThird", "FirstSecondThird")]
        [TestCase("Section\tDetail", "Section Detail")]
        [TestCase("Ordinary text", "Ordinary text")]
        [TestCase(null, null)]
        public void should_format_diagnostic_text_as_one_line(string input, string expected)
        {
            LogSanitizer.Sanitize(input).Should().Be(expected);
        }
    }
}
