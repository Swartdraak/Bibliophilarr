using System;
using System.IO;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Core.MediaCover;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MediaCoverTests
{
    [TestFixture]
    public class CoverAlreadyExistsSpecificationFixture : CoreTest<CoverAlreadyExistsSpecification>
    {
        private void GivenFileExistsOnDisk(DateTime? givenDate)
        {
            Mocker.GetMock<IDiskProvider>().Setup(c => c.FileExists(It.IsAny<string>())).Returns(true);
            Mocker.GetMock<IDiskProvider>().Setup(c => c.FileGetLastWrite(It.IsAny<string>())).Returns(givenDate ?? DateTime.Now);
            Mocker.GetMock<IDiskProvider>().Setup(c => c.GetFileSize(It.IsAny<string>())).Returns(1000);
        }

        [Test]
        public void should_return_false_if_file_not_exists()
        {
            Mocker.GetMock<IDiskProvider>().Setup(c => c.FileExists(It.IsAny<string>())).Returns(false);

            Subject.AlreadyExists(DateTime.Now, 0, "c:\\file.exe").Should().BeFalse();
        }

        [Test]
        public void should_return_false_if_file_exists_but_different_date()
        {
            GivenFileExistsOnDisk(DateTime.Now);

            Subject.AlreadyExists(DateTime.Now.AddHours(-5), 0, "c:\\file.exe").Should().BeFalse();
        }

        [Test]
        public void should_return_true_if_file_exists_and_same_date_but_no_length_header()
        {
            var givenDate = DateTime.Now;

            GivenFileExistsOnDisk(givenDate);

            Subject.AlreadyExists(givenDate, null, "c:\\file.exe").Should().BeTrue();
        }

        [TestCase(false)]
        [TestCase(true)]
        public void should_reject_an_empty_or_disappearing_file_even_with_a_matching_timestamp(bool disappeared)
        {
            var givenDate = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            GivenFileExistsOnDisk(givenDate);
            var length = Mocker.GetMock<IDiskProvider>().Setup(provider => provider.GetFileSize(It.IsAny<string>()));
            if (disappeared)
            {
                length.Throws(new FileNotFoundException());
            }
            else
            {
                length.Returns(0);
            }

            Subject.AlreadyExists(givenDate, null, "c:\\file.exe").Should().BeFalse();
        }

        [Test]
        public void should_return_false_if_file_exists_and_same_date_but_length_header_different()
        {
            var givenDate = DateTime.Now;

            GivenFileExistsOnDisk(givenDate);

            Subject.AlreadyExists(givenDate, 999, "c:\\file.exe").Should().BeFalse();
        }

        [Test]
        public void should_return_true_if_file_exists_and_date_header_is_null_but_has_length_header()
        {
            GivenFileExistsOnDisk(DateTime.Now);

            Subject.AlreadyExists(null, 1000, "c:\\file.exe").Should().BeTrue();
        }

        [Test]
        public void should_return_true_if_file_exists_and_date_header_is_different_but_length_header_the_same()
        {
            GivenFileExistsOnDisk(DateTime.Now.AddDays(-1));

            Subject.AlreadyExists(DateTime.Now, 1000, "c:\\file.exe").Should().BeTrue();
        }
    }
}
