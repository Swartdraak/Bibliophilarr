using System;
using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;
using System.Text;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.MediaCover;

namespace NzbDrone.Core.Test.MediaCoverTests
{
    [TestFixture]
    public class EpubCoverReaderFixture
    {
        private const string Container = """
            <container version="1.0" xmlns="urn:oasis:names:tc:opendocument:xmlns:container">
              <rootfiles>
                <rootfile full-path="OEBPS/content.opf" media-type="application/oebps-package+xml"/>
              </rootfiles>
            </container>
            """;

        private const string Epub2Cover = """<meta name="cover" content="art"/>""";
        private const string Epub3Cover = """<item id="art" media-type="image/jpeg" href="images/front.jpg" properties="cover-image"/>""";
        private const string UnmarkedCover = """<item id="art" media-type="image/jpeg" href="images/front.jpg"/>""";
        private const string OtherCover = """<item id="other" media-type="image/jpeg" href="images/other.jpg" properties="cover-image"/>""";
        private const string ImagePath = "OEBPS/images/front.jpg";
        private static readonly byte[] Cover = { 0xff, 0xd8, 0xff, 0x42 };

        [Test]
        public void should_read_the_explicit_epub3_cover()
        {
            using var input = Archive(Package("3.0", "", Epub3Cover));

            EpubCoverReader.Read(input).Should().Equal(Cover);
            input.CanRead.Should().BeTrue();
        }

        [Test]
        public void should_resolve_the_legacy_epub2_manifest_id()
        {
            using var input = Archive(Package("2.0", Epub2Cover, UnmarkedCover));

            EpubCoverReader.Read(input).Should().Equal(Cover);
        }

        [Test]
        public void should_accept_both_declarations_when_they_name_the_same_item()
        {
            using var input = Archive(Package("3.0", Epub2Cover, Epub3Cover));

            EpubCoverReader.Read(input).Should().Equal(Cover);
        }

        [Test]
        public void should_allow_local_dot_segments_and_escaped_spaces()
        {
            const string image = """<item id="art" media-type="image/png" href="./assets/../images/front%20cover.png" properties="nav cover-image"/>""";
            using var input = Archive(Package("3.0", "", image), imagePath: "OEBPS/images/front cover.png");

            EpubCoverReader.Read(input).Should().Equal(Cover);
        }

        [TestCase("", UnmarkedCover)]
        [TestCase(Epub2Cover, OtherCover)]
        [TestCase(Epub2Cover, UnmarkedCover + OtherCover)]
        [TestCase("", Epub3Cover + OtherCover)]
        [TestCase("", Epub3Cover + UnmarkedCover)]
        [TestCase(Epub2Cover + Epub2Cover, UnmarkedCover)]
        public void should_decline_absent_conflicting_or_duplicate_declarations(string metadata, string manifest)
        {
            using var input = Archive(Package("3.0", metadata, manifest));

            EpubCoverReader.Read(input).Should().BeNull();
        }

        [TestCase("https://books.invalid/front.jpg")]
        [TestCase("//books.invalid/front.jpg")]
        [TestCase("/OEBPS/images/front.jpg")]
        [TestCase("../../front.jpg")]
        [TestCase("images%2ffront.jpg")]
        [TestCase("images/%2e%2e/front.jpg")]
        [TestCase("images/front.jpg#fragment")]
        [TestCase("images/front.jpg?query")]
        public void should_decline_external_escaping_or_ambiguous_paths(string path)
        {
            var manifest = Epub3Cover.Replace("images/front.jpg", path, StringComparison.Ordinal);
            using var input = Archive(Package("3.0", "", manifest));

            EpubCoverReader.Read(input).Should().BeNull();
        }

        [Test]
        public void should_decline_a_duplicate_zip_name()
        {
            using var input = Archive(Package("3.0", "", Epub3Cover), extra: zip => Add(zip, ImagePath, Cover));

            EpubCoverReader.Read(input).Should().BeNull();
        }

        [Test]
        public void should_decline_multiple_rootfiles()
        {
            var rootfiles = """
                <rootfile full-path="alternate.opf" media-type="application/oebps-package+xml"/>
                </rootfiles>
                """;
            var multiple = Container.Replace("</rootfiles>", rootfiles, StringComparison.Ordinal);
            using var input = Archive(Package("3.0", "", Epub3Cover), container: multiple);

            EpubCoverReader.Read(input).Should().BeNull();
        }

        [Test]
        public void should_decline_a_dtd_without_resolving_its_system_identifier()
        {
            var pack = """<!DOCTYPE package SYSTEM "https://books.invalid/opf.dtd">""" + Package("3.0", "", Epub3Cover);
            using var input = Archive(pack);

            EpubCoverReader.Read(input).Should().BeNull();
        }

        [Test]
        public void should_decline_an_existing_member_without_supported_raster_mime()
        {
            var manifest = Epub3Cover.Replace("image/jpeg", "image/svg+xml", StringComparison.Ordinal);
            using var input = Archive(Package("3.0", "", manifest));

            EpubCoverReader.Read(input).Should().BeNull();
        }

        [Test]
        public void should_decline_a_missing_or_mismatched_case_zip_member()
        {
            using var input = Archive(Package("3.0", "", Epub3Cover), imagePath: "OEBPS/images/Front.jpg");

            EpubCoverReader.Read(input).Should().BeNull();
        }

        [Test]
        public void should_decline_an_oversized_expanded_cover_even_when_its_compressed_size_is_small()
        {
            using var input = Archive(Package("3.0", "", Epub3Cover), cover: new byte[(4 * 1024 * 1024) + 1]);

            input.Length.Should().BeLessThan(10000);
            EpubCoverReader.Read(input).Should().BeNull();
        }

        [Test]
        public void should_decline_oversized_opf_metadata()
        {
            using var input = Archive(Package("3.0", new string(' ', 1024 * 1024), Epub3Cover));

            EpubCoverReader.Read(input).Should().BeNull();
        }

        [TestCase(0u)]
        [TestCase(45u)]
        [TestCase(1048577u)]
        [TestCase(uint.MaxValue)]
        public void should_decline_an_invalid_directory_span(uint length)
        {
            using var valid = Archive(Package("3.0", "", Epub3Cover));
            var bytes = valid.ToArray();
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(bytes.Length - 22 + 12), length);
            using var input = new MemoryStream(bytes);

            EpubCoverReader.Read(input).Should().BeNull();
        }

        [Test]
        public void should_decline_a_directory_with_more_entries_than_the_end_record()
        {
            using var valid = Archive(Package("3.0", "", Epub3Cover));
            var bytes = valid.ToArray();
            BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(bytes.Length - 22 + 8), 1);
            BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(bytes.Length - 22 + 10), 1);
            using var input = new MemoryStream(bytes);

            EpubCoverReader.Read(input).Should().BeNull();
        }

        [Test]
        public void should_seek_over_a_large_sparse_non_metadata_region()
        {
            using var valid = Archive(Package("3.0", "", Epub3Cover));
            var bytes = valid.ToArray();
            var directoryOffset = (int)BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(bytes.Length - 22 + 16));
            var tail = bytes[directoryOffset..];
            const long sparseDirectoryOffset = 1024L * 1024 * 1024;
            BinaryPrimitives.WriteUInt32LittleEndian(tail.AsSpan(tail.Length - 22 + 16), (uint)sparseDirectoryOffset);
            using var input = new SparseZipStream(bytes[..directoryOffset], sparseDirectoryOffset, tail);

            EpubCoverReader.Read(input).Should().Equal(Cover);
            input.BytesRead.Should().BeLessThan(8192);
        }

        private static string Package(string version, string metadata, string manifest)
        {
            return $"""
                <package xmlns="http://www.idpf.org/2007/opf" version="{version}" unique-identifier="pub">
                  <metadata xmlns:dc="http://purl.org/dc/elements/1.1/">
                    <dc:identifier id="pub">urn:uuid:56c26811-0695-4ab9-b66e-bcd136544e6f</dc:identifier>
                    <dc:title>Synthetic Cover Test</dc:title>
                    <dc:language>en</dc:language>
                    {metadata}
                  </metadata>
                  <manifest>
                    <item id="chapter" href="chapter.xhtml" media-type="application/xhtml+xml"/>
                    {manifest}
                  </manifest>
                  <spine><itemref idref="chapter"/></spine>
                </package>
                """;
        }

        private static MemoryStream Archive(string package,
                                            string container = Container,
                                            string imagePath = ImagePath,
                                            byte[] cover = null,
                                            Action<ZipArchive> extra = null)
        {
            var output = new MemoryStream();
            using (var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
            {
                Add(zip, "mimetype", "application/epub+zip"u8, CompressionLevel.NoCompression);
                Add(zip, "META-INF/container.xml", Encoding.UTF8.GetBytes(container));
                Add(zip, "OEBPS/content.opf", Encoding.UTF8.GetBytes(package));
                Add(zip, "OEBPS/chapter.xhtml", """<html xmlns="http://www.w3.org/1999/xhtml"><head><title>Chapter</title></head><body>Text</body></html>"""u8);
                Add(zip, imagePath, cover ?? Cover);
                extra?.Invoke(zip);
            }

            output.Position = 0;
            return output;
        }

        private static void Add(ZipArchive zip, string name, ReadOnlySpan<byte> data, CompressionLevel compression = CompressionLevel.Optimal)
        {
            using var entry = zip.CreateEntry(name, compression).Open();
            entry.Write(data);
        }

        private sealed class SparseZipStream : Stream
        {
            private readonly byte[] _prefix;
            private readonly long _directoryOffset;
            private readonly byte[] _directory;

            public SparseZipStream(byte[] prefix, long directoryOffset, byte[] directory)
            {
                _prefix = prefix;
                _directoryOffset = directoryOffset;
                _directory = directory;
            }

            public int BytesRead { get; private set; }
            public override bool CanRead => true;
            public override bool CanSeek => true;
            public override bool CanWrite => false;
            public override long Length => _directoryOffset + _directory.Length;
            public override long Position { get; set; }

            public override int Read(byte[] buffer, int offset, int count)
            {
                return Read(buffer.AsSpan(offset, count));
            }

            public override int Read(Span<byte> buffer)
            {
                if (Position >= Length)
                {
                    return 0;
                }

                ReadOnlySpan<byte> source;
                if (Position < _prefix.Length)
                {
                    source = _prefix.AsSpan((int)Position);
                }
                else if (Position >= _directoryOffset)
                {
                    source = _directory.AsSpan((int)(Position - _directoryOffset));
                }
                else
                {
                    var holeLength = (int)Math.Min(buffer.Length, _directoryOffset - Position);
                    buffer[..holeLength].Clear();
                    Position += holeLength;
                    BytesRead += holeLength;
                    return holeLength;
                }

                var length = Math.Min(buffer.Length, source.Length);
                source[..length].CopyTo(buffer);
                Position += length;
                BytesRead += length;
                return length;
            }

            public override long Seek(long offset, SeekOrigin origin)
            {
                Position = origin switch
                {
                    SeekOrigin.Begin => offset,
                    SeekOrigin.Current => Position + offset,
                    SeekOrigin.End => Length + offset,
                    _ => throw new ArgumentOutOfRangeException(nameof(origin))
                };
                return Position;
            }

            public override void Flush()
            {
                throw new NotSupportedException();
            }

            public override void SetLength(long value)
            {
                throw new NotSupportedException();
            }

            public override void Write(byte[] buffer, int offset, int count)
            {
                throw new NotSupportedException();
            }
        }
    }
}
