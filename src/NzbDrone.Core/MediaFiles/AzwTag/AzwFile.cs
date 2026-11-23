using System;
using System.Buffers.Binary;
using System.IO;

namespace NzbDrone.Core.MediaFiles.Azw
{
    public class AzwFile
    {
        protected static SectionInfo ReadFirstSection(Stream stream)
        {
            const int palmHeaderLength = 78;
            const int tableEntryLength = 8;

            var fileLength = stream.Length;
            Span<byte> header = stackalloc byte[palmHeaderLength];
            Util.ReadAt(stream, 0, header, fileLength);
            var recordCount = BinaryPrimitives.ReadUInt16BigEndian(header[76..]);
            if (!header.Slice(60, 8).SequenceEqual("BOOKMOBI"u8) || recordCount == 0)
            {
                throw new AzwTagException("Invalid MOBI Palm database header");
            }

            var tableEnd = palmHeaderLength + (recordCount * tableEntryLength);
            Util.EnsureRange(0, tableEnd, fileLength);

            Span<byte> entries = stackalloc byte[tableEntryLength * 2];
            var relevantEntries = recordCount == 1 ? entries[..tableEntryLength] : entries;
            Util.ReadAt(stream, palmHeaderLength, relevantEntries, fileLength);

            var start = BinaryPrimitives.ReadUInt32BigEndian(relevantEntries);
            var end = recordCount == 1 ? fileLength : BinaryPrimitives.ReadUInt32BigEndian(relevantEntries[tableEntryLength..]);
            if (start < tableEnd || end <= start || end > fileLength)
            {
                throw new AzwTagException("Invalid MOBI first record bounds");
            }

            return new SectionInfo(start, end);
        }
    }
}
