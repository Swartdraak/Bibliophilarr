using System;
using System.IO;
using System.Text;

namespace NzbDrone.Core.MediaFiles.Azw
{
    internal static class Util
    {
        public const int MaxTextBytes = 1024 * 1024;

        public static void EnsureRange(long start, long length, long end)
        {
            if (start < 0 || length < 0 || start > end || length > end - start)
            {
                throw new AzwTagException("MOBI metadata lies outside its record");
            }
        }

        public static void ReadAt(Stream stream, long offset, Span<byte> buffer, long end)
        {
            EnsureRange(offset, buffer.Length, end);
            stream.Position = offset;
            stream.ReadExactly(buffer);
        }

        public static string ReadText(Stream stream, long offset, int length, long end, Encoding encoding)
        {
            EnsureRange(offset, length, end);
            if (length == 0)
            {
                return string.Empty;
            }

            var bytes = new byte[length];
            ReadAt(stream, offset, bytes, end);
            return encoding.GetString(bytes);
        }
    }
}
