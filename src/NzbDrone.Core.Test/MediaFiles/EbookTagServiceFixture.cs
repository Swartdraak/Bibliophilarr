using System.Collections.Generic;
using System.IO.Abstractions;
using System.Linq;
using FizzWare.NBuilder;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.Test.Framework;
using VersOne.Epub.Schema;

namespace NzbDrone.Core.Test.MediaFiles.AudioTagServiceFixture
{
    [TestFixture]
    public class EbookTagServiceFixture : CoreTest<EBookTagService>
    {
        [Test]
        public void should_return_null_isbn_for_null_identifier_list()
        {
            Subject.GetIsbn(null).Should().BeNull();
        }

        [Test]
        public void should_return_null_isbn_for_empty_identifier_list()
        {
            Subject.GetIsbn(new List<EpubMetadataIdentifier>()).Should().BeNull();
        }

        [Test]
        public void should_prefer_isbn13()
        {
            var ids = Builder<EpubMetadataIdentifier>
                .CreateListOfSize(2)
                .TheFirst(1)
                .With(x => x.Identifier = "4087738574")
                .TheNext(1)
                .With(x => x.Identifier = "9781455546176")
                .Build()
                .ToList();

            Subject.GetIsbn(ids).Should().Be("9781455546176");
        }

        [Test]
        public void should_use_extension_quality_source_when_epub_is_malformed()
        {
            var fileInfo = new Mock<IFileInfo>();
            fileInfo.Setup(x => x.Extension).Returns(".epub");
            fileInfo.Setup(x => x.FullName).Returns("/nonexistent/corrupt_file.epub");

            var result = Subject.ReadTags(fileInfo.Object);

            result.Should().NotBeNull();
            result.Quality.Quality.Should().Be(Quality.EPUB);
            result.Quality.QualityDetectionSource.Should().Be(QualityDetectionSource.Extension);
        }

        [Test]
        public void should_use_extension_quality_source_when_azw3_is_malformed()
        {
            var fileInfo = new Mock<IFileInfo>();
            fileInfo.Setup(x => x.Extension).Returns(".azw3");
            fileInfo.Setup(x => x.FullName).Returns("/nonexistent/corrupt_file.azw3");

            var result = Subject.ReadTags(fileInfo.Object);

            result.Should().NotBeNull();
            result.Quality.Quality.Should().Be(Quality.AZW3);
            result.Quality.QualityDetectionSource.Should().Be(QualityDetectionSource.Extension);
        }

        [Test]
        public void should_use_extension_quality_source_when_mobi_is_malformed()
        {
            var fileInfo = new Mock<IFileInfo>();
            fileInfo.Setup(x => x.Extension).Returns(".mobi");
            fileInfo.Setup(x => x.FullName).Returns("/nonexistent/corrupt_file.mobi");

            var result = Subject.ReadTags(fileInfo.Object);

            result.Should().NotBeNull();
            result.Quality.Quality.Should().Be(Quality.MOBI);
            result.Quality.QualityDetectionSource.Should().Be(QualityDetectionSource.Extension);
        }

        [Test]
        public void should_use_extension_quality_source_when_pdf_is_malformed()
        {
            var fileInfo = new Mock<IFileInfo>();
            fileInfo.Setup(x => x.Extension).Returns(".pdf");
            fileInfo.Setup(x => x.FullName).Returns("/nonexistent/corrupt_file.pdf");

            var result = Subject.ReadTags(fileInfo.Object);

            result.Should().NotBeNull();
            result.Quality.Quality.Should().Be(Quality.PDF);
            result.Quality.QualityDetectionSource.Should().Be(QualityDetectionSource.Extension);
        }

        [Test]
        public void should_not_inject_false_positive_isbn_or_asin_for_unparseable_filename()
        {
            var fileInfo = new Mock<IFileInfo>();
            fileInfo.Setup(x => x.Extension).Returns(".epub");
            fileInfo.Setup(x => x.FullName).Returns("/tmp/abc123xyz.epub");

            var result = Subject.ReadTags(fileInfo.Object);

            result.Should().NotBeNull();
            result.Isbn.Should().BeNullOrEmpty("filename contains no valid ISBN pattern");
            result.Asin.Should().BeNullOrEmpty("filename contains no valid ASIN pattern");
        }

        [Test]
        public void should_extract_isbn_from_filename_during_fallback()
        {
            var fileInfo = new Mock<IFileInfo>();
            fileInfo.Setup(x => x.Extension).Returns(".pdf");
            fileInfo.Setup(x => x.FullName).Returns("/tmp/Example Book - Example Author [9781455546176].pdf");

            var result = Subject.ReadTags(fileInfo.Object);

            result.Should().NotBeNull();
            result.Isbn.Should().Be("9781455546176");
            result.IsbnConfidence.Should().BeGreaterThan(0.0);
        }

        [Test]
        public void should_extract_asin_from_filename_during_fallback()
        {
            var fileInfo = new Mock<IFileInfo>();
            fileInfo.Setup(x => x.Extension).Returns(".azw3");
            fileInfo.Setup(x => x.FullName).Returns("/tmp/Example Book - Example Author [B08N5WRWNW].azw3");

            var result = Subject.ReadTags(fileInfo.Object);

            result.Should().NotBeNull();
            result.Asin.Should().Be("B08N5WRWNW");
            result.AsinConfidence.Should().BeGreaterThan(0.0);
        }
    }
}
