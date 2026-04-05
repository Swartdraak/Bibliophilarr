using System.IO;
using System.IO.Compression;
using System.Linq;
using VersOne.Epub.Internal;

namespace VersOne.Epub
{
    public static class EpubReader
    {
        /// <summary>
        /// Opens the book synchronously without reading its whole content. Holds the handle to the EPUB file.
        /// </summary>
        /// <param name="filePath">path to the EPUB file</param>
        /// <returns></returns>
        public static EpubBookRef OpenBook(string filePath)
        {
            if (!File.Exists(filePath))
            {
                if (!filePath.StartsWith(@"\\?\"))
                {
                    filePath = @"\\?\" + filePath;
                }

                if (!File.Exists(filePath))
                {
                    throw new FileNotFoundException("Specified epub file not found.", filePath);
                }
            }

            var zipArchive = GetZipArchive(filePath);
            EpubBookRef result = null;
            try
            {
                result = new EpubBookRef(zipArchive);
                result.FilePath = filePath;
                result.Schema = SchemaReader.ReadSchema(zipArchive);
                result.Title = result.Schema.Package.Metadata.Titles.FirstOrDefault() ?? string.Empty;
                result.AuthorList = result.Schema.Package.Metadata.Creators.Select(creator => creator.Creator).ToList();
                result.Author = string.Join(", ", result.AuthorList);
                return result;
            }
            catch
            {
                result?.Dispose();
                throw;
            }
        }

        private static ZipArchive GetZipArchive(string filePath)
        {
            return ZipFile.OpenRead(filePath);
        }
    }
}
