using System;
using System.IO;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Organizer;
using NzbDrone.Core.RootFolders;

namespace NzbDrone.Core.Books
{
    public interface IBuildAuthorPaths
    {
        string BuildPath(Author author, bool useExistingRelativeFolder);
        string BuildFormatPath(Author author, FormatType formatType);
    }

    public class AuthorPathBuilder : IBuildAuthorPaths
    {
        private readonly IBuildFileNames _fileNameBuilder;
        private readonly IRootFolderService _rootFolderService;
        private readonly IAuthorFormatProfileService _formatProfileService;

        public AuthorPathBuilder(IBuildFileNames fileNameBuilder,
                                 IRootFolderService rootFolderService,
                                 IAuthorFormatProfileService formatProfileService)
        {
            _fileNameBuilder = fileNameBuilder;
            _rootFolderService = rootFolderService;
            _formatProfileService = formatProfileService;
        }

        public string BuildPath(Author author, bool useExistingRelativeFolder)
        {
            if (author.RootFolderPath.IsNullOrWhiteSpace())
            {
                throw new ArgumentException("Root folder was not provided", nameof(author));
            }

            if (useExistingRelativeFolder && author.Path.IsNotNullOrWhiteSpace())
            {
                var relativePath = GetExistingRelativePath(author);
                return Path.Combine(author.RootFolderPath, relativePath);
            }

            return Path.Combine(author.RootFolderPath, _fileNameBuilder.GetAuthorFolder(author));
        }

        public string BuildFormatPath(Author author, FormatType formatType)
        {
            if (author.Id <= 0)
            {
                return author.Path;
            }

            var profile = _formatProfileService.GetByAuthorIdAndFormat(author.Id, formatType);

            if (profile?.RootFolderPath.IsNotNullOrWhiteSpace() != true)
            {
                return author.Path;
            }

            if (author.Path.IsNotNullOrWhiteSpace())
            {
                var relativePath = GetExistingRelativePath(author);
                return Path.Combine(profile.RootFolderPath, relativePath);
            }

            return Path.Combine(profile.RootFolderPath, _fileNameBuilder.GetAuthorFolder(author));
        }

        private string GetExistingRelativePath(Author author)
        {
            var rootFolderPath = _rootFolderService.GetBestRootFolderPath(author.Path);

            return rootFolderPath.GetRelativePath(author.Path);
        }
    }
}
