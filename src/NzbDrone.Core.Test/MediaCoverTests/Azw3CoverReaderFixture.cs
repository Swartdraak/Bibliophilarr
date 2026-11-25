using System;
using System.Buffers.Binary;
using System.IO;
using System.Linq;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.MediaCover;

namespace NzbDrone.Core.Test.MediaCoverTests
{
    [TestFixture]
    public class Azw3CoverReaderFixture
    {
        private const int PalmHeader = 78;
        private const int RecordZero = PalmHeader + (4 * 8) + 2;
        private const int MobiLength = 264;
        private const int Exth = RecordZero + 16 + MobiLength;

        private static readonly byte[] JpegPayload = { 0xff, 0xd8, 0xff, 0xe0, 1, 2, 0xff, 0xd9 };
        private static readonly byte[] PngPayload = { 137, 80, 78, 71, 13, 10, 26, 10, 0, 0, 0, 13, 73, 72, 68, 82 };
        private static readonly byte[] IndexRecord = "INDX"u8.ToArray();

        [TestCase(true, 0u)]
        [TestCase(false, 1u)]
        public void should_return_only_the_explicit_standalone_kf8_cover(bool jpeg, uint imageOffset)
        {
            var bytes = Book(new[] { ExthField(201, imageOffset) }, JpegPayload, PngPayload);
            using var input = new MemoryStream(bytes);

            Azw3CoverReader.Read(input).Should().Equal(jpeg ? JpegPayload : PngPayload);
            input.CanRead.Should().BeTrue();
        }

        [Test]
        public void should_allow_the_no_hybrid_sentinel_in_legacy_boundary_metadata()
        {
            using var input = new MemoryStream(Book(new[] { ExthField(121, uint.MaxValue), ExthField(201, 0) }));

            Azw3CoverReader.Read(input).Should().Equal(JpegPayload);
        }

        [Test]
        public void should_never_guess_the_first_resource_or_use_the_thumbnail()
        {
            using var input = new MemoryStream(Book(new[] { ExthField(202, 0) }));

            Azw3CoverReader.Read(input).Should().BeNull();
        }

        [TestCase(0u)]
        [TestCase(1u)]
        public void should_decline_duplicate_cover_offsets_even_if_they_agree(uint second)
        {
            using var input = new MemoryStream(Book(new[] { ExthField(201, 0), ExthField(201, second) }));

            Azw3CoverReader.Read(input).Should().BeNull();
        }

        [Test]
        public void should_decline_a_joint_kf8_boundary()
        {
            using var input = new MemoryStream(Book(new[] { ExthField(121, 3), ExthField(201, 0) }));

            Azw3CoverReader.Read(input).Should().BeNull();
        }

        [TestCase(36, 6u)]
        [TestCase(104, 6u)]
        [TestCase(128, 0u)]
        [TestCase(168, 300u)]
        [TestCase(172, 1u)]
        [TestCase(176, 1u)]
        [TestCase(180, 1u)]
        [TestCase(252, uint.MaxValue)]
        [TestCase(252, 4u)]
        [TestCase(252, 2u)]
        public void should_require_an_unprotected_native_kf8_header_and_distinct_indices(int field, uint value)
        {
            var bytes = Book(new[] { ExthField(201, 0) });
            WriteUInt32(bytes, RecordZero + field, value);
            using var input = new MemoryStream(bytes);

            Azw3CoverReader.Read(input).Should().BeNull();
        }

        [TestCase(1)]
        [TestCase(2)]
        public void should_decline_palm_doc_encryption(int encryptionType)
        {
            var bytes = Book(new[] { ExthField(201, 0) });
            BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(RecordZero + 12), (ushort)encryptionType);
            using var input = new MemoryStream(bytes);

            Azw3CoverReader.Read(input).Should().BeNull();
        }

        [TestCase(2u)]
        [TestCase(uint.MaxValue)]
        public void should_decline_cover_offsets_outside_the_section_table(uint coverOffset)
        {
            using var input = new MemoryStream(Book(new[] { ExthField(201, coverOffset) }));

            Azw3CoverReader.Read(input).Should().BeNull();
        }

        [Test]
        public void should_decline_non_image_bytes_at_the_declared_offset()
        {
            using var input = new MemoryStream(Book(new[] { ExthField(201, 0) }, "GIF89a demonstration"u8.ToArray()));

            Azw3CoverReader.Read(input).Should().BeNull();
        }

        [Test]
        public void should_decline_a_claimed_png_without_a_valid_first_chunk_header()
        {
            var falsePng = PngPayload.ToArray();
            falsePng[^1] = 0;
            using var input = new MemoryStream(Book(new[] { ExthField(201, 0) }, falsePng));

            Azw3CoverReader.Read(input).Should().BeNull();
        }

        [TestCase(8u)]
        [TestCase(13u)]
        [TestCase(uint.MaxValue)]
        public void should_decline_ambiguous_or_out_of_bounds_cover_record_length(uint length)
        {
            var bytes = Book(new[] { ExthField(201, 0) });
            WriteUInt32(bytes, Exth + 16, length);
            using var input = new MemoryStream(bytes);

            Azw3CoverReader.Read(input).Should().BeNull();
        }

        [Test]
        public void should_decline_truncated_or_disordered_palm_sections()
        {
            var bytes = Book(new[] { ExthField(201, 0) });
            using var truncated = new MemoryStream(bytes[..RecordZero]);
            Azw3CoverReader.Read(truncated).Should().BeNull();

            WriteUInt32(bytes, PalmHeader + 8, RecordZero);
            using var unordered = new MemoryStream(bytes);
            Azw3CoverReader.Read(unordered).Should().BeNull();
        }

        [Test]
        public void should_decline_an_oversized_image_before_reading_that_section()
        {
            var bytes = Book(new[] { ExthField(201, 0) });
            var coverStart = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(PalmHeader + 16));
            var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            using var file = new FileStream(directory, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 4096, FileOptions.DeleteOnClose);
            file.Write(bytes.AsSpan(0, (int)coverStart));
            file.Write(JpegPayload);

            const uint oversized = (4 * 1024 * 1024) + 1;
            file.SetLength(coverStart + oversized + 32);
            file.Position = PalmHeader + 24;
            Span<byte> offset = stackalloc byte[4];
            BinaryPrimitives.WriteUInt32BigEndian(offset, coverStart + oversized);
            file.Write(offset);

            Azw3CoverReader.Read(file).Should().BeNull();
            file.Position.Should().BeLessThan(coverStart);
        }

        private static byte[] Book(byte[][] fields, byte[] firstImage = null, byte[] secondImage = null)
        {
            // A complete metadata record, a standalone index record, and two resources.
            var metadata = fields.SelectMany(field => field).ToArray();
            var record = new byte[16 + MobiLength + 12 + metadata.Length];
            "MOBI"u8.CopyTo(record.AsSpan(16));
            WriteUInt32(record, 20, MobiLength);
            WriteUInt32(record, 36, 8);
            WriteUInt32(record, 104, 8);
            WriteUInt32(record, 108, 2);
            WriteUInt32(record, 128, 0x40);
            WriteUInt32(record, 168, uint.MaxValue);
            WriteUInt32(record, 252, 1);
            var start = 16 + MobiLength;
            "EXTH"u8.CopyTo(record.AsSpan(start));
            WriteUInt32(record, start + 4, (uint)(12 + metadata.Length));
            WriteUInt32(record, start + 8, (uint)fields.Length);
            metadata.CopyTo(record, start + 12);

            var records = new[] { record, IndexRecord, firstImage ?? JpegPayload, secondImage ?? PngPayload };
            var header = new byte[RecordZero];
            "BOOKMOBI"u8.CopyTo(header.AsSpan(60));
            BinaryPrimitives.WriteUInt16BigEndian(header.AsSpan(76), (ushort)records.Length);
            var offset = RecordZero;
            for (var i = 0; i < records.Length; i++)
            {
                WriteUInt32(header, PalmHeader + (8 * i), (uint)offset);
                offset += records[i].Length;
            }

            return header.Concat(records.SelectMany(bytes => bytes)).ToArray();
        }

        private static byte[] ExthField(uint type, uint value)
        {
            var field = new byte[12];
            WriteUInt32(field, 0, type);
            WriteUInt32(field, 4, 12);
            WriteUInt32(field, 8, value);
            return field;
        }

        private static void WriteUInt32(byte[] bytes, int offset, uint value)
        {
            BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(offset), value);
        }
    }
}
