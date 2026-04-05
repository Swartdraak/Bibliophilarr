using System.IO.Compression;
using System.Threading.Tasks;

namespace VersOne.Epub.Internal
{
    public static class SchemaReader
    {
        public static async Task<EpubSchema> ReadSchemaAsync(ZipArchive epubArchive)
        {
            var result = new EpubSchema();
            var rootFilePath = await RootFilePathReader.GetRootFilePathAsync(epubArchive).ConfigureAwait(false);
            var contentDirectoryPath = ZipPathUtils.GetDirectoryPath(rootFilePath);
            result.ContentDirectoryPath = contentDirectoryPath;
            var package = await PackageReader.ReadPackageAsync(epubArchive, rootFilePath).ConfigureAwait(false);
            result.Package = package;
            return result;
        }

        public static EpubSchema ReadSchema(ZipArchive epubArchive)
        {
            var result = new EpubSchema();
            var rootFilePath = RootFilePathReader.GetRootFilePath(epubArchive);
            var contentDirectoryPath = ZipPathUtils.GetDirectoryPath(rootFilePath);
            result.ContentDirectoryPath = contentDirectoryPath;
            var package = PackageReader.ReadPackage(epubArchive, rootFilePath);
            result.Package = package;
            return result;
        }
    }
}
