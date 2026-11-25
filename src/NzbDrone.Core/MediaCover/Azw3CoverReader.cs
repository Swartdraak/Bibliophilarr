using System;
using System.Buffers.Binary;
using System.IO;
using NzbDrone.Core.MediaFiles.Azw;

namespace NzbDrone.Core.MediaCover
{
    internal static class Azw3CoverReader
    {
        private const int PalmHeaderSize = 78;
        private const int PalmEntrySize = 8;
        private const int PalmDocHeaderSize = 16;
        private const int RequiredRecordZeroBytes = 264;
        private const int MaximumExthBytes = 1024 * 1024;
        private const int MaximumExthFields = 4096;
        private const int MaximumCoverBytes = 4 * 1024 * 1024;

        public static byte[] Read(Stream stream)
        {
            if (stream == null || !stream.CanRead || !stream.CanSeek)
            {
                return null;
            }

            try
            {
                return ReadStandaloneKf8(stream);
            }
            catch (Exception error) when (error is AzwTagException or IOException or NotSupportedException)
            {
                return null;
            }
        }

        private static byte[] ReadStandaloneKf8(Stream stream)
        {
            var fileLength = stream.Length;
            Span<byte> palm = stackalloc byte[PalmHeaderSize];
            Util.ReadAt(stream, 0, palm, fileLength);
            var entries = BinaryPrimitives.ReadUInt16BigEndian(palm[76..]);
            if (!palm.Slice(60, 8).SequenceEqual("BOOKMOBI"u8) || entries < 3)
            {
                return null;
            }

            var tableSize = entries * PalmEntrySize;
            var tableEnd = PalmHeaderSize + tableSize;
            Util.EnsureRange(PalmHeaderSize, tableSize, fileLength);
            var table = new byte[tableSize];
            Util.ReadAt(stream, PalmHeaderSize, table, fileLength);
            if (!HasOrderedRecords(table, tableEnd, fileLength, entries))
            {
                return null;
            }

            var recordStart = RecordOffset(table, 0);
            var recordEnd = RecordOffset(table, 1);
            Span<byte> header = stackalloc byte[RequiredRecordZeroBytes];
            Util.ReadAt(stream, recordStart, header, recordEnd);

            const uint kf8Version = 8;
            var skeleton = BinaryPrimitives.ReadUInt32BigEndian(header[252..]);
            if (!header.Slice(PalmDocHeaderSize, 4).SequenceEqual("MOBI"u8) ||
                BinaryPrimitives.ReadUInt32BigEndian(header[36..]) != kf8Version ||
                BinaryPrimitives.ReadUInt32BigEndian(header[104..]) != kf8Version ||
                skeleton == 0 || skeleton >= entries || IsProtected(header) ||
                (BinaryPrimitives.ReadUInt32BigEndian(header[128..]) & 0x40) == 0)
            {
                return null;
            }

            var mobiLength = BinaryPrimitives.ReadUInt32BigEndian(header[20..]);
            if (mobiLength < RequiredRecordZeroBytes - PalmDocHeaderSize)
            {
                return null;
            }

            var exthStart = recordStart + PalmDocHeaderSize + (long)mobiLength;
            var firstResource = BinaryPrimitives.ReadUInt32BigEndian(header[108..]);
            if (firstResource == 0 || firstResource >= entries ||
                !TryGetCoverOffset(stream, exthStart, recordEnd, out var coverOffset))
            {
                return null;
            }

            var index = (ulong)firstResource + coverOffset;
            if (index >= (ulong)entries || index == skeleton)
            {
                return null;
            }

            var coverStart = RecordOffset(table, (int)index);
            var coverEnd = index + 1 == (ulong)entries ? fileLength : RecordOffset(table, (int)index + 1);
            return ReadImage(stream, coverStart, coverEnd);
        }

        private static bool IsProtected(ReadOnlySpan<byte> header)
        {
            var encryption = BinaryPrimitives.ReadUInt16BigEndian(header[12..]);
            var drmOffset = BinaryPrimitives.ReadUInt32BigEndian(header[168..]);
            return encryption != 0 || (drmOffset != 0 && drmOffset != uint.MaxValue) ||
                BinaryPrimitives.ReadUInt32BigEndian(header[172..]) != 0 ||
                BinaryPrimitives.ReadUInt32BigEndian(header[176..]) != 0 ||
                BinaryPrimitives.ReadUInt32BigEndian(header[180..]) != 0;
        }

        private static bool HasOrderedRecords(ReadOnlySpan<byte> table, long tableEnd, long fileLength, int entries)
        {
            var previous = tableEnd - 1;
            for (var i = 0; i < entries; i++)
            {
                var offset = RecordOffset(table, i);
                if (offset <= previous || offset >= fileLength)
                {
                    return false;
                }

                previous = offset;
            }

            return true;
        }

        private static long RecordOffset(ReadOnlySpan<byte> table, int index)
        {
            return BinaryPrimitives.ReadUInt32BigEndian(table.Slice(index * PalmEntrySize));
        }

        private static bool TryGetCoverOffset(Stream stream, long start, long recordEnd, out uint coverOffset)
        {
            const uint coverField = 201;
            const uint hybridBoundaryField = 121;
            const int exthHeaderSize = 12;
            const int fieldHeaderSize = 8;
            coverOffset = uint.MaxValue;

            Span<byte> header = stackalloc byte[exthHeaderSize];
            Util.ReadAt(stream, start, header, recordEnd);
            var length = BinaryPrimitives.ReadUInt32BigEndian(header[4..]);
            var count = BinaryPrimitives.ReadUInt32BigEndian(header[8..]);
            if (!header[..4].SequenceEqual("EXTH"u8) || length < exthHeaderSize ||
                length > MaximumExthBytes || count > MaximumExthFields ||
                count > (length - exthHeaderSize) / fieldHeaderSize)
            {
                return false;
            }

            Util.EnsureRange(start, length, recordEnd);
            var end = start + length;
            var position = start + exthHeaderSize;
            var sawCover = false;
            Span<byte> field = stackalloc byte[fieldHeaderSize];
            for (var i = 0; i < count; i++)
            {
                Util.ReadAt(stream, position, field, end);
                var type = BinaryPrimitives.ReadUInt32BigEndian(field);
                var fieldLength = BinaryPrimitives.ReadUInt32BigEndian(field[4..]);
                if (fieldLength < fieldHeaderSize || fieldLength > end - position)
                {
                    return false;
                }

                if (type is coverField or hybridBoundaryField)
                {
                    if (fieldLength != fieldHeaderSize + sizeof(uint))
                    {
                        return false;
                    }

                    Util.ReadAt(stream, position + fieldHeaderSize, field[..sizeof(uint)], end);
                    var value = BinaryPrimitives.ReadUInt32BigEndian(field);
                    if (type == coverField && !sawCover)
                    {
                        sawCover = true;
                        coverOffset = value;
                    }
                    else if (type != hybridBoundaryField || value != uint.MaxValue)
                    {
                        return false;
                    }
                }

                position += fieldLength;
            }

            // An EXTH length may include up to three alignment bytes.
            if (end - position > 3)
            {
                return false;
            }

            return sawCover && coverOffset != uint.MaxValue;
        }

        private static byte[] ReadImage(Stream stream, long start, long end)
        {
            var length = end - start;
            if (length < 8 || length > MaximumCoverBytes)
            {
                return null;
            }

            Span<byte> prefix = stackalloc byte[16];
            var prefixLength = (int)Math.Min(prefix.Length, length);
            var header = prefix[..prefixLength];
            Util.ReadAt(stream, start, header, end);
            if (!LooksLikeJpeg(header) && !LooksLikePng(header))
            {
                return null;
            }

            var result = new byte[(int)length];
            header.CopyTo(result);
            Util.ReadAt(stream, start + header.Length, result.AsSpan(header.Length), end);
            return result;
        }

        private static bool LooksLikeJpeg(ReadOnlySpan<byte> header)
        {
            return header[0] == 0xff && header[1] == 0xd8 && header[2] == 0xff;
        }

        private static bool LooksLikePng(ReadOnlySpan<byte> header)
        {
            ReadOnlySpan<byte> signature = new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 };
            return header.Length >= 16 && header[..8].SequenceEqual(signature) &&
                BinaryPrimitives.ReadUInt32BigEndian(header[8..]) == 13 && header[12..16].SequenceEqual("IHDR"u8);
        }
    }
}
