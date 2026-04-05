using System.Linq;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Books;
using NzbDrone.Core.DecisionEngine;
using NzbDrone.Core.Download;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.RootFolders;

namespace NzbDrone.Core.MediaFiles.BookImport.Specifications
{
    public class AuthorPathInRootFolderSpecification : IImportDecisionEngineSpecification<LocalEdition>
    {
        private readonly IRootFolderService _rootFolderService;
        private readonly IAuthorService _authorService;
        private readonly Logger _logger;

        public AuthorPathInRootFolderSpecification(IRootFolderService rootFolderService,
                                                   IAuthorService authorService,
                                                   Logger logger)
        {
            _rootFolderService = rootFolderService;
            _authorService = authorService;
            _logger = logger;
        }

        public Decision IsSatisfiedBy(LocalEdition item, DownloadClientItem downloadClientItem)
        {
            // Prevent imports to authors that are no longer inside a root folder Bibliophilarr manages
            var author = item.Edition?.Book?.Value?.Author?.Value;

            if (author == null)
            {
                return Decision.Accept();
            }

            // a new author will have empty path, and will end up having path assinged based on file location
            string pathToCheck;
            if (author.Path.IsNotNullOrWhiteSpace())
            {
                // Author has a configured path (already in DB)
                pathToCheck = author.Path;
            }
            else
            {
                // Remote author stub — ForeignAuthorId is on Book.AuthorMetadata (not Author.Metadata which is empty for remote stubs)
                var authorForeignId = author.Metadata?.Value?.ForeignAuthorId;
                var bookForeignId = item.Edition?.Book?.Value?.AuthorMetadata?.Value?.ForeignAuthorId;
                var foreignId = authorForeignId.IsNotNullOrWhiteSpace() ? authorForeignId : bookForeignId;
                var localAuthor = foreignId.IsNotNullOrWhiteSpace() ? _authorService.FindById(foreignId) : null;

                // If numeric-ID lookup failed, the stub may have a name-based ForeignAuthorId (e.g. "hardcover:author:Charlaine%20Harris").
                // Fall back to name lookup so we can find the real local author path.
                if (localAuthor == null && foreignId.IsNotNullOrWhiteSpace())
                {
                    var authorName = author.Name.IsNotNullOrWhiteSpace() ? author.Name
                        : item.Edition?.Book?.Value?.AuthorMetadata?.Value?.Name;
                    if (authorName.IsNotNullOrWhiteSpace())
                    {
                        localAuthor = _authorService.FindByName(authorName);
                    }
                }

                if (localAuthor != null && localAuthor.Path.IsNotNullOrWhiteSpace())
                {
                    pathToCheck = localAuthor.Path;
                }
                else
                {
                    // Truly new author — path will be assigned based on file location
                    pathToCheck = item.LocalBooks.First().Path.GetParentPath();
                }
            }

            if (_rootFolderService.GetBestRootFolder(pathToCheck) == null)
            {
                _logger.Warn($"Destination folder {pathToCheck} not in a Root Folder, skipping import");
                return Decision.Reject($"Destination folder {pathToCheck} is not in a Root Folder");
            }

            return Decision.Accept();
        }
    }
}
