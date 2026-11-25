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
    public class M4bCoverReaderFixture
    {
        private const uint Jpeg = 13;
        private const uint Png = 14;
        private const int MaximumCoverSize = 4 * 1024 * 1024;
        private static readonly byte[] Payload = { 0x11, 0x22, 0x33 };

        [TestCase(Jpeg)]
        [TestCase(Png)]
        public void should_return_the_exact_payload_of_a_single_supported_typed_cover(uint type)
        {
            using var input = new MemoryStream(Container(Cover(Data(type, Payload))));
            M4bCoverReader.Read(input).Should().Equal(Payload);
        }

        [Test]
        public void should_skip_a_large_extended_size_media_box_without_reading_its_payload()
        {
            const long mediaSize = 5L * 1024 * 1024 * 1024;
            var header = new byte[16];
            BinaryPrimitives.WriteUInt32BigEndian(header, 1);
            "mdat"u8.CopyTo(header.AsSpan(4));
            BinaryPrimitives.WriteUInt64BigEndian(header.AsSpan(8), (ulong)mediaSize);
            var ftyp = FileType();
            using var input = new HeaderAndTailStream(
                ftyp.Concat(header).ToArray(),
                ftyp.Length + mediaSize,
                Container(Cover(Data(Jpeg, Payload)), false));

            M4bCoverReader.Read(input).Should().Equal(Payload);
            input.BytesRead.Should().BeLessThan(256);
        }

        [Test]
        public void should_ignore_an_image_atom_outside_the_supported_cover_path()
        {
            using var input = new MemoryStream(FileType().Concat(Box("moov"u8, Cover(Data(Jpeg, Payload)))).ToArray());

            M4bCoverReader.Read(input).Should().BeNull();
        }

        [Test]
        public void should_return_null_when_cover_metadata_is_absent()
        {
            using var input = new MemoryStream(Container(Box("free"u8)));

            M4bCoverReader.Read(input).Should().BeNull();
        }

        [Test]
        public void should_return_null_when_a_cover_has_two_data_boxes()
        {
            using var input = new MemoryStream(Container(Cover(Data(Jpeg, Payload), Data(Png, Payload))));

            M4bCoverReader.Read(input).Should().BeNull();
        }

        [Test]
        public void should_return_null_with_two_cover_atoms_even_if_the_bytes_match()
        {
            using var input = new MemoryStream(Container(Cover(Data(Jpeg, Payload)), true, Cover(Data(Jpeg, Payload))));

            M4bCoverReader.Read(input).Should().BeNull();
        }

        [TestCase(0u)]
        [TestCase(27u)]
        [TestCase(0x0100000du)]
        public void should_reject_other_data_types_and_nonzero_data_versions(uint type)
        {
            using var input = new MemoryStream(Container(Cover(Data(type, Payload))));

            M4bCoverReader.Read(input).Should().BeNull();
        }

        [Test]
        public void should_reject_a_declared_empty_image()
        {
            using var input = new MemoryStream(Container(Cover(Data(Png, Array.Empty<byte>()))));

            M4bCoverReader.Read(input).Should().BeNull();
        }

        [Test]
        public void should_reject_an_image_larger_than_the_limit_before_reading_any_payload()
        {
            var file = Container(Cover(Data(Jpeg, new byte[MaximumCoverSize + 1])));
            using var input = new HeaderAndTailStream(file[..^(MaximumCoverSize + 1)], file.LongLength, Array.Empty<byte>());

            M4bCoverReader.Read(input).Should().BeNull();
            input.BytesRead.Should().BeLessThan(256);
        }

        [Test]
        public void should_accept_an_image_at_the_exact_byte_limit()
        {
            var payload = new byte[MaximumCoverSize];
            payload[0] = 7;
            payload[^1] = 9;
            using var input = new MemoryStream(Container(Cover(Data(Png, payload))));

            M4bCoverReader.Read(input).Should().Equal(payload);
        }

        [TestCase(4u)]
        [TestCase(uint.MaxValue)]
        public void should_return_null_for_a_child_with_an_invalid_size(uint size)
        {
            var malformed = Data(Jpeg, Payload);
            BinaryPrimitives.WriteUInt32BigEndian(malformed, size);
            using var input = new MemoryStream(Container(Cover(malformed)));

            M4bCoverReader.Read(input).Should().BeNull();
        }

        [Test]
        public void should_return_null_on_an_unrepresentable_extended_box_size()
        {
            var malformed = Box("mdat"u8, new byte[8]);
            BinaryPrimitives.WriteUInt32BigEndian(malformed, 1);
            BinaryPrimitives.WriteUInt64BigEndian(malformed.AsSpan(8), ulong.MaxValue);
            using var input = new MemoryStream(FileType().Concat(malformed).ToArray());

            M4bCoverReader.Read(input).Should().BeNull();
        }

        [Test]
        public void should_reject_a_truncated_file()
        {
            using var input = new MemoryStream(Container(Cover(Data(Jpeg, Payload)))[..^1]);

            M4bCoverReader.Read(input).Should().BeNull();
        }

        [Test]
        public void should_bound_box_traversal_before_reading_a_cover_after_excessive_metadata()
        {
            var metadata = Enumerable.Repeat(Box("free"u8), 5000).SelectMany(x => x);
            using var input = new MemoryStream(FileType().Concat(metadata).Concat(Container(Cover(Data(Jpeg, Payload)), false)).ToArray());

            M4bCoverReader.Read(input).Should().BeNull();
            input.Position.Should().BeLessThan(35000);
        }

        private static byte[] FileType()
        {
            return Box("ftyp"u8, "M4B \0\0\0\0M4B "u8.ToArray());
        }

        private static byte[] Container(byte[] item, bool includeFileType = true, params byte[][] otherItems)
        {
            var items = new[] { item }.Concat(otherItems).ToArray();
            var metadata = Box("meta"u8, new byte[4], Box("ilst"u8, items));
            var movie = Box("moov"u8, Box("udta"u8, metadata));
            return includeFileType ? FileType().Concat(movie).ToArray() : movie;
        }

        private static byte[] Cover(params byte[][] data)
        {
            return Box("covr"u8, data);
        }

        private static byte[] Data(uint type, byte[] cover)
        {
            var flagsAndLocale = new byte[8];
            BinaryPrimitives.WriteUInt32BigEndian(flagsAndLocale, type);
            return Box("data"u8, flagsAndLocale, cover);
        }

        private static byte[] Box(ReadOnlySpan<byte> type, params byte[][] contents)
        {
            var box = new byte[8 + contents.Sum(item => item.Length)];
            BinaryPrimitives.WriteUInt32BigEndian(box, (uint)box.Length);
            type.CopyTo(box.AsSpan(4));
            var offset = 8;
            foreach (var item in contents)
            {
                item.CopyTo(box, offset);
                offset += item.Length;
            }

            return box;
        }

        private sealed class HeaderAndTailStream : Stream
        {
            private readonly byte[] _header;
            private readonly long _tailOffset;
            private readonly byte[] _tail;

            public HeaderAndTailStream(byte[] header, long tailOffset, byte[] tail)
            {
                _header = header;
                _tailOffset = tailOffset;
                _tail = tail;
            }

            public int BytesRead { get; private set; }
            public override bool CanRead => true;
            public override bool CanSeek => true;
            public override bool CanWrite => false;
            public override long Length => _tailOffset + _tail.Length;
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
                if (Position < _header.Length)
                {
                    source = _header.AsSpan((int)Position);
                }
                else if (Position >= _tailOffset)
                {
                    source = _tail.AsSpan((int)(Position - _tailOffset));
                }
                else
                {
                    throw new InvalidOperationException("The reader accessed bytes outside the expected metadata.");
                }

                var available = Math.Min(source.Length, buffer.Length);
                source[..available].CopyTo(buffer);
                Position += available;
                BytesRead += available;
                return available;
            }

            public override void Flush()
            {
                throw new NotSupportedException();
            }

            public override long Seek(long offset, SeekOrigin origin)
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
