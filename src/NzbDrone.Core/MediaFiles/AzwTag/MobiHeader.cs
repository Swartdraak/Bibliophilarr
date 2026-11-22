using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;

namespace NzbDrone.Core.MediaFiles.Azw
{
    public class MobiHeader
    {
        public MobiHeader(Stream stream, SectionInfo section)
        {
            const int palmDocHeaderLength = 16;
            const int requiredPrefixLength = 0x84;

            Span<byte> header = stackalloc byte[requiredPrefixLength];
            Util.ReadAt(stream, section.Start, header, section.End);
            if (!header.Slice(palmDocHeaderLength, 4).SequenceEqual("MOBI"u8))
            {
                throw new AzwTagException("Invalid MOBI record header");
            }

            var headerLength = BinaryPrimitives.ReadUInt32BigEndian(header[20..]);
            if (headerLength < requiredPrefixLength - palmDocHeaderLength)
            {
                throw new AzwTagException("Incomplete MOBI record header");
            }

            var exthStart = section.Start + palmDocHeaderLength + headerLength;
            Util.EnsureRange(section.Start, palmDocHeaderLength + (long)headerLength, section.End);

            Version = BinaryPrimitives.ReadUInt32BigEndian(header[36..]);
            MobiType = BinaryPrimitives.ReadUInt32BigEndian(header[24..]);
            var codePage = BinaryPrimitives.ReadUInt32BigEndian(header[28..]);
            var encoding = codePage > int.MaxValue ? null : CodePagesEncodingProvider.Instance.GetEncoding((int)codePage);
            if (codePage == 65001)
            {
                encoding = Encoding.UTF8;
            }

            if (encoding == null)
            {
                throw new AzwTagException("Unsupported MOBI text encoding");
            }

            var exthFlags = BinaryPrimitives.ReadUInt32BigEndian(header[0x80..]);
            if ((exthFlags & 0x40) == 0)
            {
                throw new AzwTagException("No EXTH header. Bibliophilarr cannot process this file.");
            }

            var titleStart = section.Start + BinaryPrimitives.ReadUInt32BigEndian(header[0x54..]);
            var titleLength = BinaryPrimitives.ReadUInt32BigEndian(header[0x58..]);
            Util.EnsureRange(titleStart, titleLength, section.End);
            Title = titleLength <= Util.MaxTextBytes
                ? Util.ReadText(stream, titleStart, (int)titleLength, section.End, encoding)
                : string.Empty;
            ExtMeta = new ExtMeta(stream, exthStart, section.End, encoding);
        }

        public string Title { get; }
        public uint Version { get; }
        public uint MobiType { get; }
        public ExtMeta ExtMeta { get; }
    }
}
