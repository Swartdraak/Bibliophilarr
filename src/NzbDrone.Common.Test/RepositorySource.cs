using System;
using System.IO;

namespace NzbDrone.Common.Test
{
    internal static class RepositorySource
    {
        public static string Root { get; } = FindRoot();

        private static string FindRoot()
        {
            for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory != null; directory = directory.Parent)
            {
                if (File.Exists(Path.Combine(directory.FullName, "src", "Bibliophilarr.sln")))
                {
                    return directory.FullName;
                }
            }

            throw new DirectoryNotFoundException("Bibliophilarr source is required for the repository configuration tests.");
        }
    }
}
