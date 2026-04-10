using System.Collections.Generic;
using System.IO;
using System.Linq;
using Bibliophilarr.Http;
using Microsoft.AspNetCore.Mvc;
using NLog;
using NzbDrone.Core.Books;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.BookImport.Manual;
using NzbDrone.Core.Qualities;

namespace Bibliophilarr.Api.V1.ManualImport
{
    [V1ApiController]
    public class ManualImportController : Controller
    {
        private readonly IAuthorService _authorService;
        private readonly IBookService _bookService;
        private readonly IEditionService _editionService;
        private readonly IManualImportService _manualImportService;
        private readonly IAuthorFormatProfileService _formatProfileService;
        private readonly IConfigService _configService;
        private readonly Logger _logger;

        public ManualImportController(IManualImportService manualImportService,
                                  IAuthorService authorService,
                                  IEditionService editionService,
                                  IBookService bookService,
                                  IAuthorFormatProfileService formatProfileService,
                                  IConfigService configService,
                                  Logger logger)
        {
            _authorService = authorService;
            _bookService = bookService;
            _editionService = editionService;
            _manualImportService = manualImportService;
            _formatProfileService = formatProfileService;
            _configService = configService;
            _logger = logger;
        }

        [HttpPost]
        public IActionResult UpdateItems([FromBody] List<ManualImportUpdateResource> resource)
        {
            return Accepted(UpdateImportItems(resource));
        }

        [HttpGet]
        public List<ManualImportResource> GetMediaFiles(string folder, string downloadId, int? authorId, bool filterExistingFiles = true, bool replaceExistingFiles = true)
        {
            NzbDrone.Core.Books.Author author = null;

            if (authorId > 0)
            {
                author = _authorService.GetAuthor(authorId.Value);

                if (string.IsNullOrWhiteSpace(folder) && string.IsNullOrWhiteSpace(downloadId) && author?.Path != null)
                {
                    // When dual-format tracking is enabled, scan all format profile paths
                    // so the modal shows files from both ebook and audiobook root folders.
                    if (_configService.EnableDualFormatTracking)
                    {
                        var formatProfiles = _formatProfileService.GetByAuthorId(author.Id);
                        var authorFolderName = new DirectoryInfo(author.Path).Name;
                        var paths = formatProfiles
                            .Where(fp => fp.Monitored && !string.IsNullOrWhiteSpace(fp.RootFolderPath))
                            .Select(fp => !string.IsNullOrWhiteSpace(fp.Path) ? fp.Path : Path.Combine(fp.RootFolderPath, authorFolderName))
                            .Distinct()
                            .ToList();

                        if (paths.Count > 1)
                        {
                            var multiFilter = filterExistingFiles ? FilterFilesType.Matched : FilterFilesType.None;
                            var allResults = new List<ManualImportResource>();

                            foreach (var formatPath in paths)
                            {
                                _logger.Debug("Manual import scanning format path: {0}", formatPath);
                                var items = _manualImportService.GetMediaFiles(formatPath, downloadId, author, multiFilter, replaceExistingFiles)
                                    .ToResource()
                                    .Select(AddQualityWeight);
                                allResults.AddRange(items);
                            }

                            return allResults.ToList();
                        }

                        folder = paths.FirstOrDefault() ?? author.Path;
                    }
                    else
                    {
                        folder = author.Path;
                    }
                }
            }

            var filter = filterExistingFiles ? FilterFilesType.Matched : FilterFilesType.None;

            return _manualImportService.GetMediaFiles(folder, downloadId, author, filter, replaceExistingFiles).ToResource().Select(AddQualityWeight).ToList();
        }

        private ManualImportResource AddQualityWeight(ManualImportResource item)
        {
            if (item.Quality != null)
            {
                item.QualityWeight = Quality.DefaultQualityDefinitions.Single(q => q.Quality == item.Quality.Quality).Weight;
                item.QualityWeight += item.Quality.Revision.Real * 10;
                item.QualityWeight += item.Quality.Revision.Version;
            }

            return item;
        }

        private List<ManualImportResource> UpdateImportItems(List<ManualImportUpdateResource> resources)
        {
            var items = new List<ManualImportItem>();
            foreach (var resource in resources)
            {
                items.Add(new ManualImportItem
                {
                    Id = resource.Id,
                    Path = resource.Path,
                    Name = resource.Name,
                    Author = resource.AuthorId.HasValue ? _authorService.GetAuthor(resource.AuthorId.Value) : null,
                    Book = resource.BookId.HasValue ? _bookService.GetBook(resource.BookId.Value) : null,
                    Edition = resource.ForeignEditionId == null ? null : _editionService.GetEditionByForeignEditionId(resource.ForeignEditionId),
                    Quality = resource.Quality,
                    ReleaseGroup = resource.ReleaseGroup,
                    IndexerFlags = resource.IndexerFlags,
                    DownloadId = resource.DownloadId,
                    AdditionalFile = resource.AdditionalFile,
                    ReplaceExistingFiles = resource.ReplaceExistingFiles,
                    DisableReleaseSwitching = resource.DisableReleaseSwitching
                });
            }

            return _manualImportService.UpdateItems(items).Select(x => x.ToResource()).ToList();
        }
    }
}
