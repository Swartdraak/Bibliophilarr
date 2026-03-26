using System.Collections.Generic;
using System.Linq;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.Books
{
    public interface IAuthorMetadataRepository : IBasicRepository<AuthorMetadata>
    {
        List<AuthorMetadata> FindById(List<string> foreignIds);
        bool UpsertMany(List<AuthorMetadata> data);
    }

    public class AuthorMetadataRepository : BasicRepository<AuthorMetadata>, IAuthorMetadataRepository
    {
        private readonly Logger _logger;

        public AuthorMetadataRepository(IMainDatabase database, IEventAggregator eventAggregator, Logger logger)
            : base(database, eventAggregator)
        {
            _logger = logger;
        }

        public List<AuthorMetadata> FindById(List<string> foreignIds)
        {
            return Query(x => Enumerable.Contains(foreignIds, x.ForeignAuthorId));
        }

        public bool UpsertMany(List<AuthorMetadata> data)
        {
            var existingMetadata = FindById(data.Select(x => x.ForeignAuthorId).ToList());
            var updateMetadataList = new List<AuthorMetadata>();
            var addMetadataList = new List<AuthorMetadata>();
            var upToDateMetadataCount = 0;

            foreach (var meta in data)
            {
                var existing = existingMetadata.SingleOrDefault(x => x.ForeignAuthorId == meta.ForeignAuthorId);
                if (existing != null)
                {
                    // populate Id in remote data
                    meta.UseDbFieldsFrom(existing);

                    // responses vary, so try adding remote to what we have
                    if (!meta.Equals(existing))
                    {
                        updateMetadataList.Add(meta);
                    }
                    else
                    {
                        upToDateMetadataCount++;
                    }
                }
                else
                {
                    addMetadataList.Add(meta);
                }
            }

            // Safety net: ensure TitleSlug is never null before database insert
            foreach (var meta in addMetadataList)
            {
                if (meta.TitleSlug.IsNullOrWhiteSpace())
                {
                    meta.TitleSlug = meta.ForeignAuthorId.IsNotNullOrWhiteSpace()
                        ? meta.ForeignAuthorId.ToUrlSlug()
                        : meta.Name?.ToUrlSlug() ?? "unknown";
                    _logger.Warn("Auto-generated TitleSlug for author '{0}' (ForeignId: {1})", meta.Name, meta.ForeignAuthorId);
                }
            }

            // Resolve TitleSlug UNIQUE collisions against existing DB records and within the batch
            var slugsToInsert = addMetadataList.Select(x => x.TitleSlug).Distinct().ToList();
            var existingBySlug = slugsToInsert.Any()
                ? Query(x => Enumerable.Contains(slugsToInsert, x.TitleSlug))
                : new List<AuthorMetadata>();
            var usedSlugs = new HashSet<string>(existingBySlug.Select(x => x.TitleSlug));

            // Also include slugs from update list to avoid intra-batch collisions
            foreach (var meta in updateMetadataList)
            {
                usedSlugs.Add(meta.TitleSlug);
            }

            foreach (var meta in addMetadataList)
            {
                if (usedSlugs.Contains(meta.TitleSlug))
                {
                    // Check if this is actually the same author (already in DB with same slug)
                    var existingWithSlug = existingBySlug.FirstOrDefault(x => x.TitleSlug == meta.TitleSlug);
                    if (existingWithSlug != null)
                    {
                        // Reuse the existing record instead of inserting a duplicate
                        _logger.Info(
                            "Author '{0}' (ForeignId: {1}) maps to existing slug '{2}' (ForeignId: {3}); reusing existing metadata record",
                            meta.Name,
                            meta.ForeignAuthorId,
                            meta.TitleSlug,
                            existingWithSlug.ForeignAuthorId);
                        meta.UseDbFieldsFrom(existingWithSlug);
                        continue;
                    }

                    // Intra-batch collision: disambiguate with numeric suffix
                    var baseSlug = meta.TitleSlug;
                    var counter = 2;
                    while (usedSlugs.Contains(meta.TitleSlug))
                    {
                        meta.TitleSlug = $"{baseSlug}-{counter}";
                        counter++;
                    }

                    _logger.Warn("Disambiguated TitleSlug for author '{0}' from '{1}' to '{2}'", meta.Name, baseSlug, meta.TitleSlug);
                }

                usedSlugs.Add(meta.TitleSlug);
            }

            // Remove entries that were remapped to existing records (they now have an Id)
            addMetadataList.RemoveAll(x => x.Id > 0);

            UpdateMany(updateMetadataList);
            InsertMany(addMetadataList);

            _logger.Debug($"{upToDateMetadataCount} author metadata up to date; Updating {updateMetadataList.Count}, Adding {addMetadataList.Count} author metadata entries.");

            return updateMetadataList.Count > 0 || addMetadataList.Count > 0;
        }
    }
}
