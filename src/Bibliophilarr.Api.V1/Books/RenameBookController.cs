using System.Collections.Generic;
using Bibliophilarr.Http;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.Commands;
using NzbDrone.Core.Messaging.Commands;

namespace Bibliophilarr.Api.V1.Books
{
    [V1ApiController("rename")]
    public class RenameBookController : Controller
    {
        private readonly IRenameBookFileService _renameBookFileService;
        private readonly IManageCommandQueue _commandQueueManager;

        public RenameBookController(IRenameBookFileService renameBookFileService, IManageCommandQueue commandQueueManager)
        {
            _renameBookFileService = renameBookFileService;
            _commandQueueManager = commandQueueManager;
        }

        [HttpGet]
        public List<RenameBookResource> GetBookFiles(int authorId, int? bookId)
        {
            if (bookId.HasValue)
            {
                return _renameBookFileService.GetRenamePreviews(authorId, bookId.Value).ToResource();
            }

            return _renameBookFileService.GetRenamePreviews(authorId).ToResource();
        }

        [HttpPost]
        public IActionResult RenameFiles(int authorId, [FromBody] List<int> files)
        {
            if (files == null || files.Count == 0)
            {
                return BadRequest("No files specified for renaming");
            }

            var command = new RenameFilesCommand(authorId, files);
            command.Trigger = CommandTrigger.Manual;
            command.SendUpdatesToClient = true;

            var trackedCommand = _commandQueueManager.Push(command, CommandPriority.Normal, CommandTrigger.Manual);

            return Accepted();
        }

        [HttpPost("author")]
        public IActionResult RenameAuthor([FromBody] List<int> authorIds)
        {
            if (authorIds == null || authorIds.Count == 0)
            {
                return BadRequest("No authors specified for renaming");
            }

            var command = new RenameAuthorCommand(authorIds);
            command.Trigger = CommandTrigger.Manual;
            command.SendUpdatesToClient = true;

            var trackedCommand = _commandQueueManager.Push(command, CommandPriority.Normal, CommandTrigger.Manual);

            return Accepted();
        }
    }
}
