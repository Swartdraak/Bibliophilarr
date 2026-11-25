using System;
using System.Buffers.Binary;
using System.IO;

namespace NzbDrone.Core.MediaCover
{
    internal static class M4bCoverReader
    {
        private const int BoxHeaderSize = 8;
        private const int MaximumBoxes = 4096;
        private const int MaximumCoverSize = 4 * 1024 * 1024;
        private const uint JpegDataType = 13;
        private const uint PngDataType = 14;

        private enum BoxType : uint
        {
            Root,
            Ftyp = 0x66747970,
            Moov = 0x6d6f6f76,
            Udta = 0x75647461,
            Meta = 0x6d657461,
            Ilst = 0x696c7374,
            Covr = 0x636f7672,
            Data = 0x64617461
        }

        public static byte[] Read(Stream stream)
        {
            if (stream == null || !stream.CanRead || !stream.CanSeek)
            {
                return null;
            }

            try
            {
                return new CoverSearch(stream).Read();
            }
            catch (IOException)
            {
                return null;
            }
            catch (NotSupportedException)
            {
                return null;
            }
        }

        private sealed class CoverSearch
        {
            private readonly Stream _stream;
            private int _remainingBoxes = MaximumBoxes;
            private bool _foundCoverBox;
            private long _payloadOffset;
            private int _payloadLength;

            public CoverSearch(Stream stream)
            {
                _stream = stream;
            }

            public byte[] Read()
            {
                // Resolve the entire structural path before allocating or reading a payload.
                // The box limit bounds header I/O independently of file size and seek distance.
                if (!ScanBoxes(0, _stream.Length, BoxType.Root) || _payloadLength == 0)
                {
                    return null;
                }

                var result = new byte[_payloadLength];
                ReadAt(_payloadOffset, result);
                return result;
            }

            private bool ScanBoxes(long start, long end, BoxType parent)
            {
                Span<byte> header = stackalloc byte[BoxHeaderSize];
                for (var offset = start; offset < end;)
                {
                    if (_remainingBoxes-- <= 0 || end - offset < BoxHeaderSize)
                    {
                        return false;
                    }

                    ReadAt(offset, header);
                    var type = (BoxType)BinaryPrimitives.ReadUInt32BigEndian(header[4..]);
                    var headerSize = BoxHeaderSize;
                    ulong length = BinaryPrimitives.ReadUInt32BigEndian(header);
                    if (length == 1)
                    {
                        headerSize += sizeof(ulong);
                        if (end - offset < headerSize)
                        {
                            return false;
                        }

                        ReadAt(offset + BoxHeaderSize, header);
                        length = BinaryPrimitives.ReadUInt64BigEndian(header);
                    }
                    else if (length == 0 && parent == BoxType.Root)
                    {
                        length = (ulong)(end - offset);
                    }

                    if (length < (ulong)headerSize || length > (ulong)(end - offset))
                    {
                        return false;
                    }

                    // Checking length against the remaining parent bounds keeps both additions safe.
                    var body = offset + headerSize;
                    var boxEnd = offset + (long)length;
                    if (offset == 0 && (type != BoxType.Ftyp || boxEnd - body < 8))
                    {
                        return false;
                    }

                    if (!ScanBox(parent, type, body, boxEnd))
                    {
                        return false;
                    }

                    offset = boxEnd;
                }

                return true;
            }

            private bool ScanBox(BoxType parent, BoxType type, long body, long end)
            {
                switch (parent, type)
                {
                    case (BoxType.Root, BoxType.Moov):
                    case (BoxType.Moov, BoxType.Udta):
                    case (BoxType.Meta, BoxType.Ilst):
                        return ScanBoxes(body, end, type);
                    case (BoxType.Udta, BoxType.Meta):
                        return end - body >= sizeof(uint) && ScanBoxes(body + sizeof(uint), end, type);
                    case (BoxType.Ilst, BoxType.Covr):
                        if (_foundCoverBox)
                        {
                            return false;
                        }

                        _foundCoverBox = true;
                        return ScanBoxes(body, end, type);
                    case (BoxType.Covr, BoxType.Data):
                        return SetCoverPayload(body, end);
                    default:
                        // Only descend into the cover metadata path. In particular, skip mdat.
                        return true;
                }
            }

            private bool SetCoverPayload(long start, long end)
            {
                const int dataHeaderSize = 8; // Flags (type) and locale.
                var length = end - start - dataHeaderSize;
                if (_payloadLength != 0 || length <= 0 || length > MaximumCoverSize)
                {
                    return false;
                }

                Span<byte> type = stackalloc byte[sizeof(uint)];
                ReadAt(start, type);
                var dataType = BinaryPrimitives.ReadUInt32BigEndian(type);
                if (dataType != JpegDataType && dataType != PngDataType)
                {
                    return false;
                }

                _payloadOffset = start + dataHeaderSize;
                _payloadLength = (int)length;
                return true;
            }

            private void ReadAt(long offset, Span<byte> target)
            {
                _stream.Position = offset;
                _stream.ReadExactly(target);
            }
        }
    }
}
