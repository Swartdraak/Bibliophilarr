using System.Collections.Generic;
using System.Linq;
using Bibliophilarr.Http;
using Microsoft.AspNetCore.Mvc;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Books;
using NzbDrone.Core.Books.Commands;
using NzbDrone.Core.Messaging.Commands;

namespace Bibliophilarr.Api.V1.Author
{
    [V1ApiController("author/editor")]
    public class AuthorEditorController : Controller
    {
        private readonly IAuthorService _authorService;
        private readonly IAuthorFormatProfileService _formatProfileService;
        private readonly IManageCommandQueue _commandQueueManager;
        private readonly Logger _logger;

        public AuthorEditorController(IAuthorService authorService, IAuthorFormatProfileService formatProfileService, IManageCommandQueue commandQueueManager, Logger logger)
        {
            _authorService = authorService;
            _formatProfileService = formatProfileService;
            _commandQueueManager = commandQueueManager;
            _logger = logger;
        }

        [HttpPut]
        public IActionResult SaveAll([FromBody] AuthorEditorResource resource)
        {
            var authorsToUpdate = _authorService.GetAuthors(resource.AuthorIds);
            var authorsToMove = new List<BulkMoveAuthor>();

            foreach (var author in authorsToUpdate)
            {
                if (resource.Monitored.HasValue)
                {
                    author.Monitored = resource.Monitored.Value;
                }

                if (resource.MonitorNewItems.HasValue)
                {
                    author.MonitorNewItems = resource.MonitorNewItems.Value;
                }

                if (resource.QualityProfileId.HasValue)
                {
                    author.QualityProfileId = resource.QualityProfileId.Value;
                }

                if (resource.MetadataProfileId.HasValue)
                {
                    author.MetadataProfileId = resource.MetadataProfileId.Value;
                }

                if (resource.RootFolderPath.IsNotNullOrWhiteSpace())
                {
                    author.RootFolderPath = resource.RootFolderPath;
                    authorsToMove.Add(new BulkMoveAuthor
                    {
                        AuthorId = author.Id,
                        SourcePath = author.Path
                    });
                }

                if (resource.Tags != null)
                {
                    var newTags = resource.Tags;
                    var applyTags = resource.ApplyTags;

                    switch (applyTags)
                    {
                        case ApplyTags.Add:
                            newTags.ForEach(t => author.Tags.Add(t));
                            break;
                        case ApplyTags.Remove:
                            newTags.ForEach(t => author.Tags.Remove(t));
                            break;
                        case ApplyTags.Replace:
                            author.Tags = new HashSet<int>(newTags);
                            break;
                    }
                }
            }

            if (resource.MoveFiles && authorsToMove.Any())
            {
                _commandQueueManager.Push(new BulkMoveAuthorCommand
                {
                    DestinationRootFolder = resource.RootFolderPath,
                    Author = authorsToMove
                });
            }

            // Update per-format profiles (ebook/audiobook QP and root folders)
            var hasFormatChanges = resource.EbookQualityProfileId.HasValue ||
                                   resource.AudiobookQualityProfileId.HasValue ||
                                   resource.EbookRootFolderPath.IsNotNullOrWhiteSpace() ||
                                   resource.AudiobookRootFolderPath.IsNotNullOrWhiteSpace();

            if (resource.MoveFiles && !authorsToMove.Any() && hasFormatChanges)
            {
                _logger.Warn("Move files requested for format-specific root folder change but file moves are not yet supported for per-format paths. Files will be relocated during next import or manual rename.");
            }

            if (hasFormatChanges)
            {
                _logger.Info("Applying format profile changes to {0} author(s): EbookQP={1}, AudiobookQP={2}, EbookRF={3}, AudiobookRF={4}",
                    authorsToUpdate.Count,
                    resource.EbookQualityProfileId,
                    resource.AudiobookQualityProfileId,
                    resource.EbookRootFolderPath,
                    resource.AudiobookRootFolderPath);

                foreach (var author in authorsToUpdate)
                {
                    if (resource.EbookQualityProfileId.HasValue || resource.EbookRootFolderPath.IsNotNullOrWhiteSpace())
                    {
                        var ebookProfile = _formatProfileService.GetByAuthorIdAndFormat(author.Id, FormatType.Ebook);
                        if (ebookProfile != null)
                        {
                            if (resource.EbookQualityProfileId.HasValue)
                            {
                                ebookProfile.QualityProfileId = resource.EbookQualityProfileId.Value;
                            }

                            if (resource.EbookRootFolderPath.IsNotNullOrWhiteSpace())
                            {
                                ebookProfile.RootFolderPath = resource.EbookRootFolderPath;
                                ebookProfile.Path = global::System.IO.Path.Combine(resource.EbookRootFolderPath, author.CleanName ?? author.Name);
                            }

                            _formatProfileService.Update(ebookProfile);
                            _logger.Debug(
                                "Updated ebook format profile for author '{0}' (id: {1}): QP={2}, RootFolder={3}, Path={4}",
                                author.Name,
                                author.Id,
                                ebookProfile.QualityProfileId,
                                ebookProfile.RootFolderPath,
                                ebookProfile.Path);
                        }
                    }

                    if (resource.AudiobookQualityProfileId.HasValue || resource.AudiobookRootFolderPath.IsNotNullOrWhiteSpace())
                    {
                        var audiobookProfile = _formatProfileService.GetByAuthorIdAndFormat(author.Id, FormatType.Audiobook);
                        if (audiobookProfile != null)
                        {
                            if (resource.AudiobookQualityProfileId.HasValue)
                            {
                                audiobookProfile.QualityProfileId = resource.AudiobookQualityProfileId.Value;
                            }

                            if (resource.AudiobookRootFolderPath.IsNotNullOrWhiteSpace())
                            {
                                audiobookProfile.RootFolderPath = resource.AudiobookRootFolderPath;
                                audiobookProfile.Path = global::System.IO.Path.Combine(resource.AudiobookRootFolderPath, author.CleanName ?? author.Name);
                            }

                            _formatProfileService.Update(audiobookProfile);
                            _logger.Debug(
                                "Updated audiobook format profile for author '{0}' (id: {1}): QP={2}, RootFolder={3}, Path={4}",
                                author.Name,
                                author.Id,
                                audiobookProfile.QualityProfileId,
                                audiobookProfile.RootFolderPath,
                                audiobookProfile.Path);
                        }
                    }
                }
            }

            var authorResources = _authorService.UpdateAuthors(authorsToUpdate, !resource.MoveFiles).ToResource();

            // Include updated format profiles in the response so the frontend store stays in sync
            foreach (var authorResource in authorResources)
            {
                authorResource.FormatProfiles = _formatProfileService.GetByAuthorId(authorResource.Id).ToResource();
            }

            return Accepted(authorResources);
        }

        [HttpDelete]
        public object DeleteAuthor([FromBody] AuthorEditorResource resource)
        {
            foreach (var authorId in resource.AuthorIds)
            {
                _authorService.DeleteAuthor(authorId, false);
            }

            return new { };
        }
    }
}
