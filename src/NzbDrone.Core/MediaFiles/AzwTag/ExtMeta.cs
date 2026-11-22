using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace NzbDrone.Core.MediaFiles.Azw
{
    public class ExtMeta
    {
        private readonly Dictionary<uint, List<string>> _strings = new Dictionary<uint, List<string>>();

        public ExtMeta(Stream stream, long start, long sectionEnd, Encoding encoding)
        {
            const int headerLength = 12;
            const int recordHeaderLength = 8;
            const int maxRecords = 4096;

            Span<byte> header = stackalloc byte[headerLength];
            Util.ReadAt(stream, start, header, sectionEnd);
            if (!header[..4].SequenceEqual("EXTH"u8))
            {
                throw new AzwTagException("Invalid EXTH header");
            }

            var length = BinaryPrimitives.ReadUInt32BigEndian(header[4..]);
            var count = BinaryPrimitives.ReadUInt32BigEndian(header[8..]);
            if (length < headerLength || count > (length - headerLength) / recordHeaderLength || count > maxRecords)
            {
                throw new AzwTagException("Invalid or oversized EXTH table");
            }

            Util.EnsureRange(start, length, sectionEnd);
            var end = start + length;
            var offset = start + headerLength;
            var remainingAuthorBytes = Util.MaxTextBytes;
            var remainingSupplementalBytes = 2 * Util.MaxTextBytes;
            Span<byte> record = stackalloc byte[recordHeaderLength];

            for (var i = 0; i < count; i++)
            {
                Util.ReadAt(stream, offset, record, end);
                var id = BinaryPrimitives.ReadUInt32BigEndian(record);
                var size = BinaryPrimitives.ReadUInt32BigEndian(record[4..]);
                if (size < recordHeaderLength)
                {
                    throw new AzwTagException("Invalid EXTH record length");
                }

                Util.EnsureRange(offset, size, end);
                var payloadSize = size - recordHeaderLength;

                // Keep author and identifier limits independent of optional text. Only
                // contributors are exposed as a list, so later scalar duplicates can be skipped.
                var isAuthor = id == 100;
                var isIdentifier = id is 104 or 113;
                var remainingBytes = id switch
                {
                    100 => remainingAuthorBytes,
                    104 or 113 => Util.MaxTextBytes,
                    _ => remainingSupplementalBytes
                };
                if ((id is 100 or 101 or 102 or 103 or 104 or 106 or 112 or 113 or 524) &&
                    payloadSize <= Math.Min(Util.MaxTextBytes, remainingBytes) &&
                    (isAuthor || !_strings.ContainsKey(id)))
                {
                    var text = Util.ReadText(stream, offset + recordHeaderLength, (int)payloadSize, end, encoding);
                    if (!_strings.TryGetValue(id, out var values))
                    {
                        values = new List<string>();
                        _strings.Add(id, values);
                    }

                    values.Add(text);

                    if (isAuthor)
                    {
                        remainingAuthorBytes -= (int)payloadSize;
                    }
                    else if (!isIdentifier)
                    {
                        remainingSupplementalBytes -= (int)payloadSize;
                    }
                }

                offset += size;
            }
        }

        public string StringOrNull(uint key)
        {
            return _strings.TryGetValue(key, out var value) ? value.FirstOrDefault() : null;
        }

        public List<string> StringList(uint key)
        {
            return _strings.TryGetValue(key, out var value) ? value : new List<string>();
        }
    }
}
