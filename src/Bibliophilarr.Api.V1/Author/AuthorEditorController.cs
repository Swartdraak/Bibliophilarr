using System.Collections.Generic;
using System.Linq;
using Bibliophilarr.Http;
using Microsoft.AspNetCore.Mvc;
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

        public AuthorEditorController(IAuthorService authorService, IAuthorFormatProfileService formatProfileService, IManageCommandQueue commandQueueManager)
        {
            _authorService = authorService;
            _formatProfileService = formatProfileService;
            _commandQueueManager = commandQueueManager;
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

            if (hasFormatChanges)
            {
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
                            }

                            _formatProfileService.Update(ebookProfile);
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
                            }

                            _formatProfileService.Update(audiobookProfile);
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
