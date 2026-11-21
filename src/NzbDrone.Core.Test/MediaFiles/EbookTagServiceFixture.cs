using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text;
using FizzWare.NBuilder;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;
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

            // File-read failure and missing-filename-metadata both log at Warn; allow them.
            ExceptionVerification.IgnoreWarns();

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

            ExceptionVerification.IgnoreWarns();

            result.Should().NotBeNull();
            result.Quality.Quality.Should().Be(Quality.AZW3);
            result.Quality.QualityDetectionSource.Should().Be(QualityDetectionSource.Extension);
        }

        [TestCase(".azw3", 8, "9781442482890", "9781442482890", 0.97)]
        [TestCase(".mobi", 6, "1-4424-8289-3", "9781442482890", 0.97)]
        [TestCase(".azw3", 8, "4006381333931", "9781455546176", 0.55)]
        public void should_use_valid_exth_isbn_before_the_filename_isbn(string extension, int version, string embeddedIsbn, string expectedIsbn, double confidence)
        {
            var directory = Directory.CreateTempSubdirectory();
            try
            {
                const int sectionStart = 96;
                const int mobiHeaderLength = 132;
                const int exthStart = sectionStart + 16 + mobiHeaderLength;
                var exthLength = 12 + 8 + embeddedIsbn.Length;
                var fileContent = new byte[exthStart + exthLength];

                "BOOKMOBI"u8.CopyTo(fileContent.AsSpan(60));
                BinaryPrimitives.WriteUInt16BigEndian(fileContent.AsSpan(76), 1);
                WriteNumber(78, sectionStart);
                WriteNumber(86, fileContent.Length);
                "MOBI"u8.CopyTo(fileContent.AsSpan(sectionStart + 16));
                WriteNumber(sectionStart + 20, mobiHeaderLength);
                WriteNumber(sectionStart + 28, 65001);
                WriteNumber(sectionStart + 36, version);
                WriteNumber(sectionStart + 128, 64);
                "EXTH"u8.CopyTo(fileContent.AsSpan(exthStart));
                WriteNumber(exthStart + 4, exthLength);
                WriteNumber(exthStart + 8, 1);
                WriteNumber(exthStart + 12, 104);
                WriteNumber(exthStart + 16, 8 + embeddedIsbn.Length);
                Encoding.ASCII.GetBytes(embeddedIsbn).CopyTo(fileContent, exthStart + 20);

                var path = Path.Combine(directory.FullName, "Traffick - Ellen Hopkins [9781455546176]" + extension);
                File.WriteAllBytes(path, fileContent);

                var result = Subject.ReadTags(new FileInfoWrapper(new FileSystem(), new FileInfo(path)));

                result.Isbn.Should().Be(expectedIsbn);
                result.IsbnConfidence.Should().Be(confidence);

                void WriteNumber(int offset, int value) => BinaryPrimitives.WriteInt32BigEndian(fileContent.AsSpan(offset), value);
            }
            finally
            {
                directory.Delete(true);
            }
        }

        [Test]
        public void should_use_extension_quality_source_when_mobi_is_malformed()
        {
            var fileInfo = new Mock<IFileInfo>();
            fileInfo.Setup(x => x.Extension).Returns(".mobi");
            fileInfo.Setup(x => x.FullName).Returns("/nonexistent/corrupt_file.mobi");

            var result = Subject.ReadTags(fileInfo.Object);

            ExceptionVerification.IgnoreWarns();

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

            ExceptionVerification.IgnoreWarns();

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

            ExceptionVerification.IgnoreWarns();

            result.Should().NotBeNull();
            result.Isbn.Should().BeNullOrEmpty("filename contains no valid ISBN pattern");
            result.Asin.Should().BeNullOrEmpty("filename contains no valid ASIN pattern");
        }

        [TestCase("/tmp/integrated/Example Book - Jean Smith.epub")]
        [TestCase("/tmp/B08N5WRWNW/Example Book - Jean Smith.epub")]
        [TestCase("/tmp/9781455546176/Example Book - Jean Smith.epub")]
        [TestCase("/tmp/Example Collection - Jean Smith.epub")]
        [TestCase("/tmp/Example Book - Jean Smith [4087738574].epub")]
        public void should_not_use_directory_or_unlabelled_text_as_an_asin(string path)
        {
            var file = new Mock<IFileInfo>();
            file.Setup(x => x.Extension).Returns(".epub");
            file.Setup(x => x.FullName).Returns(path);

            var result = Subject.ReadTags(file.Object);

            ExceptionVerification.IgnoreWarns();
            result.Asin.Should().BeNull();
            if (path.Contains("/9781455546176/"))
            {
                result.Isbn.Should().BeNull();
            }
        }

        [Test]
        public void should_read_a_numeric_asin_when_the_filename_labels_it()
        {
            var file = new Mock<IFileInfo>();
            file.Setup(x => x.Extension).Returns(".epub");
            file.Setup(x => x.FullName).Returns("/tmp/Example Book - Jean Smith [ASIN 4087738574].epub");

            var result = Subject.ReadTags(file.Object);

            ExceptionVerification.IgnoreWarns();
            result.Asin.Should().Be("4087738574");
        }

        [Test]
        public void should_prefer_namespaced_epub_asin_over_a_different_filename_asin()
        {
            var directory = Directory.CreateTempSubdirectory("integrated-epub-");
            try
            {
                var path = Path.Combine(directory.FullName, "Example Book - Jean Smith [B08N5WRWNW].epub");
                using (var archive = ZipFile.Open(path, ZipArchiveMode.Create))
                {
                    using (var container = new StreamWriter(archive.CreateEntry("META-INF/container.xml").Open()))
                    {
                        container.Write("""
                            <container xmlns="urn:oasis:names:tc:opendocument:xmlns:container">
                              <rootfiles><rootfile full-path="metadata.opf" /></rootfiles>
                            </container>
                            """);
                    }

                    using (var package = new StreamWriter(archive.CreateEntry("metadata.opf").Open()))
                    {
                        package.Write("""
                            <package xmlns="http://www.idpf.org/2007/opf" xmlns:opf="http://www.idpf.org/2007/opf"
                                     xmlns:dc="http://purl.org/dc/elements/1.1/" version="2.0">
                              <metadata>
                                <dc:title>Example Book</dc:title>
                                <dc:creator>Jean Smith</dc:creator>
                                <dc:identifier opf:scheme="MOBI-ASIN">B07N5WRWNW</dc:identifier>
                              </metadata>
                            </package>
                            """);
                    }
                }

                var file = new Mock<IFileInfo>();
                file.Setup(x => x.Extension).Returns(".epub");
                file.Setup(x => x.FullName).Returns(path);
                var result = Subject.ReadTags(file.Object);

                result.Asin.Should().Be("B07N5WRWNW");
                result.AsinConfidence.Should().Be(0.9);
            }
            finally
            {
                directory.Delete(true);
            }
        }

        [Test]
        public void should_extract_isbn_from_filename_during_fallback()
        {
            var fileInfo = new Mock<IFileInfo>();
            fileInfo.Setup(x => x.Extension).Returns(".pdf");
            fileInfo.Setup(x => x.FullName).Returns("/tmp/Example Book - Example Author [9781455546176].pdf");

            var result = Subject.ReadTags(fileInfo.Object);

            ExceptionVerification.IgnoreWarns();

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

            ExceptionVerification.IgnoreWarns();

            result.Should().NotBeNull();
            result.Asin.Should().Be("B08N5WRWNW");
            result.AsinConfidence.Should().BeGreaterThan(0.0);
        }

        [Test]
        public void should_replace_suspicious_high_confidence_embedded_title_and_author_with_filename_identity()
        {
            var applyFallback = typeof(EBookTagService).GetMethod("ApplyFilenameFallback", BindingFlags.NonPublic | BindingFlags.Instance);
            applyFallback.Should().NotBeNull();

            var parsed = new ParsedTrackInfo
            {
                Authors = new List<string> { "medi" },
                AuthorConfidence = 0.95,
                BookTitle = "audio retail 2023",
                BookTitleConfidence = 0.95
            };

            applyFallback.Invoke(Subject, new object[] { "/tmp/Frank Herbert - Dune (2023).epub", parsed, 0.5 });

            parsed.Authors.Should().Contain("Frank Herbert");
            parsed.BookTitle.Should().Be("Dune");
            parsed.AuthorConfidence.Should().Be(0.5);
            parsed.BookTitleConfidence.Should().Be(0.5);
        }

        [Test]
        public void should_swap_filename_identity_when_reversed_order_scores_better()
        {
            var fileInfo = new Mock<IFileInfo>();
            fileInfo.Setup(x => x.Extension).Returns(".epub");
            fileInfo.Setup(x => x.FullName).Returns("/tmp/Dune - Frank Herbert.epub");

            var result = Subject.ReadTags(fileInfo.Object);

            ExceptionVerification.IgnoreWarns();

            result.Should().NotBeNull();
            result.Authors.Should().ContainSingle().Which.Should().Be("Frank Herbert");
            result.BookTitle.Should().Be("Dune");
        }
    }
}
