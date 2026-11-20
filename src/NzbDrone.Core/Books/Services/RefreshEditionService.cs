using System;
using System.Collections.Generic;
using System.Linq;
using NLog;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.BookImport;

namespace NzbDrone.Core.Books
{
    public interface IRefreshEditionService
    {
        bool RefreshEditionInfo(List<Edition> add, List<Edition> update, List<Tuple<Edition, Edition>> merge, List<Edition> delete, List<Edition> upToDate, List<Edition> remoteEditions, bool forceUpdateFileTags);
    }

    public class RefreshEditionService : IRefreshEditionService
    {
        private readonly IEditionService _editionService;
        private readonly IMediaFileService _mediaFileService;
        private readonly IMetadataTagService _metadataTagService;
        private readonly Logger _logger;

        public RefreshEditionService(IEditionService editionService,
            IMediaFileService mediaFileService,
            IMetadataTagService metadataTagService,
            Logger logger)
        {
            _editionService = editionService;
            _mediaFileService = mediaFileService;
            _metadataTagService = metadataTagService;
            _logger = logger;
        }

        public bool RefreshEditionInfo(List<Edition> add, List<Edition> update, List<Tuple<Edition, Edition>> merge, List<Edition> delete, List<Edition> upToDate, List<Edition> remoteEditions, bool forceUpdateFileTags)
        {
            var updateList = new List<Edition>();

            // for editions that need updating, just grab the remote edition and set db ids
            foreach (var edition in update)
            {
                var remoteEdition = remoteEditions.SingleOrDefault(e => e.ForeignEditionId == edition.ForeignEditionId);
                if (remoteEdition != null)
                {
                    if (remoteEdition.BookId > 0)
                    {
                        edition.BookId = remoteEdition.BookId;
                        edition.Book = remoteEdition.Book;
                    }

                    if (HasLinkedFileFormatConflict(edition, remoteEdition, _mediaFileService))
                    {
                        _logger.Warn("Retaining stored metadata for edition {0}: provider format {1} conflicts with its linked files.", edition.Id, remoteEdition.Format);
                    }
                    else
                    {
                        edition.UseMetadataFrom(remoteEdition);
                    }
                }

                // make sure title is not null
                edition.Title = edition.Title ?? "Unknown";
                updateList.Add(edition);
            }

            _editionService.DeleteMany(delete.Concat(merge.Select(x => x.Item1)).ToList());
            _editionService.UpdateMany(updateList);

            var tagsToUpdate = updateList;
            if (forceUpdateFileTags)
            {
                _logger.Debug("Forcing tag update due to Author/Book/Edition updates");
                tagsToUpdate = updateList.Concat(upToDate).ToList();
            }

            _metadataTagService.SyncTags(tagsToUpdate);

            return add.Any() || delete.Any() || updateList.Any() || merge.Any();
        }

        internal static bool HasLinkedFileFormatConflict(Edition edition, Edition remoteEdition, IMediaFileService mediaFileService)
        {
            return (edition.Format != remoteEdition.Format || edition.IsEbook != remoteEdition.IsEbook) &&
                   mediaFileService.GetFilesByEdition(edition.Id).Any(file => !ImportFormat.CanLink(file.Path, remoteEdition));
        }
    }
}
