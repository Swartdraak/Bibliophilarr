using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml;
using System.Xml.Linq;

namespace NzbDrone.Core.MediaCover
{
    internal static class EpubCoverReader
    {
        private const uint EndSignature = 0x06054b50;
        private const uint EntrySignature = 0x02014b50;
        private const int EndHeaderSize = 22;
        private const int EntryHeaderSize = 46;
        private const int MaximumEntries = 12000;
        private const int MaximumDirectoryBytes = 1024 * 1024;
        private const int MaximumXmlBytes = 1024 * 1024;
        private const int MaximumCoverBytes = 4 * 1024 * 1024;
        private const int MaximumPathLength = 4096;

        private static readonly XNamespace ContainerNamespace = "urn:oasis:names:tc:opendocument:xmlns:container";
        private static readonly XNamespace OpfNamespace = "http://www.idpf.org/2007/opf";
        private static readonly char[] PropertyWhitespace = { ' ', '\t', '\r', '\n' };

        public static byte[] Read(Stream stream)
        {
            if (stream == null || !stream.CanRead || !stream.CanSeek)
            {
                return null;
            }

            try
            {
                if (!HasBoundedDirectory(stream, out var entryCount))
                {
                    return null;
                }

                using var zip = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
                var entries = new Dictionary<string, ZipArchiveEntry>(entryCount, StringComparer.Ordinal);
                foreach (var entry in zip.Entries)
                {
                    if (!entries.TryAdd(entry.FullName, entry))
                    {
                        return null;
                    }
                }

                if (entries.Count != entryCount)
                {
                    return null;
                }

                var container = ReadXml(entries.GetValueOrDefault("META-INF/container.xml"));
                var rootfiles = container?.Name == ContainerNamespace + "container"
                    ? OnlyElement(container.Elements(ContainerNamespace + "rootfiles"))
                    : null;
                var rootfile = rootfiles == null ? null : OnlyElement(rootfiles.Elements(ContainerNamespace + "rootfile"));
                if ((string)rootfile?.Attribute("media-type") != "application/oebps-package+xml" ||
                    !TryResolvePath(string.Empty, (string)rootfile?.Attribute("full-path"), out var packagePath))
                {
                    return null;
                }

                var package = ReadXml(entries.GetValueOrDefault(packagePath));
                var coverHref = SelectCoverHref(package);
                var lastSeparator = packagePath.LastIndexOf('/');
                var packageFolder = lastSeparator < 0 ? string.Empty : packagePath[..lastSeparator];
                return TryResolvePath(packageFolder, coverHref, out var coverPath)
                    ? ReadEntry(entries.GetValueOrDefault(coverPath), MaximumCoverBytes)
                    : null;
            }
            catch (Exception error) when (error is IOException or InvalidDataException or XmlException or
                                           ArgumentException or NotSupportedException or UriFormatException)
            {
                return null;
            }
        }

        private static string SelectCoverHref(XElement package)
        {
            if (package?.Name != OpfNamespace + "package")
            {
                return null;
            }

            var manifest = OnlyElement(package.Elements(OpfNamespace + "manifest"));
            var metadata = OnlyElement(package.Elements(OpfNamespace + "metadata"));
            if (manifest == null || metadata == null)
            {
                return null;
            }

            var items = new Dictionary<string, XElement>(StringComparer.Ordinal);
            XElement explicitCover = null;
            foreach (var item in manifest.Elements(OpfNamespace + "item"))
            {
                var id = (string)item.Attribute("id");
                if (string.IsNullOrEmpty(id) || !items.TryAdd(id, item))
                {
                    return null;
                }

                var properties = (string)item.Attribute("properties");
                if (properties?.Split(PropertyWhitespace, StringSplitOptions.RemoveEmptyEntries)
                               .Contains("cover-image", StringComparer.Ordinal) == true)
                {
                    if (explicitCover != null)
                    {
                        return null;
                    }

                    explicitCover = item;
                }
            }

            var legacy = metadata.Elements(OpfNamespace + "meta")
                .Where(item => (string)item.Attribute("name") == "cover").Take(2).ToArray();
            if (legacy.Length > 0)
            {
                var legacyId = (string)legacy[0].Attribute("content");
                if (legacy.Length != 1 || string.IsNullOrEmpty(legacyId) || !items.TryGetValue(legacyId, out var legacyCover) ||
                    (explicitCover != null && explicitCover != legacyCover))
                {
                    return null;
                }

                explicitCover = legacyCover;
            }

            return (string)explicitCover?.Attribute("media-type") is "image/jpeg" or "image/png"
                ? (string)explicitCover.Attribute("href")
                : null;
        }

        private static XElement OnlyElement(IEnumerable<XElement> elements)
        {
            using var value = elements.GetEnumerator();
            if (!value.MoveNext())
            {
                return null;
            }

            var element = value.Current;
            return value.MoveNext() ? null : element;
        }

        private static XElement ReadXml(ZipArchiveEntry entry)
        {
            var bytes = ReadEntry(entry, MaximumXmlBytes);
            if (bytes == null)
            {
                return null;
            }

            using var data = new MemoryStream(bytes, writable: false);
            using var reader = XmlReader.Create(data, new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                MaxCharactersInDocument = MaximumXmlBytes
            });

            var root = XDocument.Load(reader).Root;
            return root?.DescendantsAndSelf().Attributes(XNamespace.Xml + "base").Any() == false ? root : null;
        }

        private static byte[] ReadEntry(ZipArchiveEntry entry, int limit)
        {
            const uint unixFileType = 0xf000;
            const uint unixSymbolicLink = 0xa000;
            if (entry == null || entry.Length <= 0 || entry.Length > limit ||
                entry.CompressedLength <= 0 || entry.CompressedLength > limit ||
                (((uint)entry.ExternalAttributes >> 16) & unixFileType) == unixSymbolicLink)
            {
                return null;
            }

            using var input = entry.Open();
            var bytes = new byte[(int)entry.Length];
            input.ReadExactly(bytes);
            return input.ReadByte() == -1 ? bytes : null;
        }

        private static bool TryResolvePath(string folder, string path, out string resolved)
        {
            resolved = null;
            if (string.IsNullOrEmpty(path) || path.Length > MaximumPathLength || path[0] == '/')
            {
                return false;
            }

            var segments = folder.Length == 0 ? new List<string>() : new List<string>(folder.Split('/'));
            foreach (var raw in path.Split('/'))
            {
                if (raw == ".")
                {
                    continue;
                }

                if (raw == "..")
                {
                    if (segments.Count == 0)
                    {
                        return false;
                    }

                    segments.RemoveAt(segments.Count - 1);
                    continue;
                }

                var segment = Uri.UnescapeDataString(raw);
                if (segment.Length == 0 || segment == "." || segment == ".." ||
                    segment.Any(value => char.IsControl(value) || value is '/' or '\\' or '%' or ':' or '?' or '#'))
                {
                    return false;
                }

                segments.Add(segment);
            }

            if (segments.Count == 0 || path.EndsWith("/.", StringComparison.Ordinal) || path.EndsWith("/..", StringComparison.Ordinal) ||
                path is "." or "..")
            {
                return false;
            }

            resolved = string.Join('/', segments);
            return true;
        }

        private static bool HasBoundedDirectory(Stream stream, out int entryCount)
        {
            entryCount = 0;
            var length = stream.Length;
            if (length < EndHeaderSize)
            {
                return false;
            }

            Span<byte> finalHeader = stackalloc byte[EndHeaderSize];
            var endPosition = length - EndHeaderSize;
            ReadAt(stream, endPosition, finalHeader);
            if (!IsSignature(finalHeader, EndSignature) || BinaryPrimitives.ReadUInt16LittleEndian(finalHeader[20..]) != 0)
            {
                // Only a bounded ZIP comment can move the end header away from EOF.
                var tailLength = (int)Math.Min(length, EndHeaderSize + ushort.MaxValue);
                var tail = new byte[tailLength];
                var tailStart = length - tailLength;
                ReadAt(stream, tailStart, tail);
                var index = tail.AsSpan().LastIndexOf("PK\u0005\u0006"u8);
                if (index < 0 || tailLength - index < EndHeaderSize)
                {
                    return false;
                }

                tail.AsSpan(index, EndHeaderSize).CopyTo(finalHeader);
                endPosition = tailStart + index;
                if (length - endPosition - EndHeaderSize != BinaryPrimitives.ReadUInt16LittleEndian(finalHeader[20..]))
                {
                    return false;
                }
            }

            var count = BinaryPrimitives.ReadUInt16LittleEndian(finalHeader[10..]);
            var directorySize = BinaryPrimitives.ReadUInt32LittleEndian(finalHeader[12..]);
            var directoryStart = BinaryPrimitives.ReadUInt32LittleEndian(finalHeader[16..]);

            // Initial support is single disk ordinary ZIP. Check size and exact extent
            // before ZipArchive can allocate or traverse a claimed central directory.
            if (BinaryPrimitives.ReadUInt32LittleEndian(finalHeader[4..]) != 0 ||
                BinaryPrimitives.ReadUInt16LittleEndian(finalHeader[8..]) != count ||
                count == 0 || count > MaximumEntries ||
                directorySize > MaximumDirectoryBytes || directoryStart == uint.MaxValue ||
                (long)directoryStart + directorySize != endPosition ||
                !ValidateDirectoryEntries(stream, directoryStart, endPosition, count))
            {
                return false;
            }

            entryCount = count;
            return true;
        }

        private static bool ValidateDirectoryEntries(Stream stream, long start, long end, int count)
        {
            Span<byte> header = stackalloc byte[EntryHeaderSize];
            var offset = start;
            for (var index = 0; index < count; index++)
            {
                if (end - offset < EntryHeaderSize)
                {
                    return false;
                }

                ReadAt(stream, offset, header);
                if (!IsSignature(header, EntrySignature) ||
                    (BinaryPrimitives.ReadUInt16LittleEndian(header[8..]) & 1) != 0 ||
                    BinaryPrimitives.ReadUInt16LittleEndian(header[34..]) != 0 ||
                    BinaryPrimitives.ReadUInt32LittleEndian(header[20..]) == uint.MaxValue ||
                    BinaryPrimitives.ReadUInt32LittleEndian(header[24..]) == uint.MaxValue ||
                    BinaryPrimitives.ReadUInt32LittleEndian(header[42..]) >= start)
                {
                    return false;
                }

                var nameLength = BinaryPrimitives.ReadUInt16LittleEndian(header[28..]);
                var extraLength = BinaryPrimitives.ReadUInt16LittleEndian(header[30..]);
                var commentLength = BinaryPrimitives.ReadUInt16LittleEndian(header[32..]);
                if (nameLength == 0 || nameLength > MaximumPathLength)
                {
                    return false;
                }

                offset += EntryHeaderSize + nameLength + extraLength + commentLength;
            }

            return offset == end;
        }

        private static bool IsSignature(ReadOnlySpan<byte> header, uint signature)
        {
            return BinaryPrimitives.ReadUInt32LittleEndian(header) == signature;
        }

        private static void ReadAt(Stream stream, long offset, Span<byte> buffer)
        {
            stream.Position = offset;
            stream.ReadExactly(buffer);
        }
    }
}
