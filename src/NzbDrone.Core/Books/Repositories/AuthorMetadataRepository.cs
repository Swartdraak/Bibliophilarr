using System;
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
                if (existing == null)
                {
                    var linkedOwner = CatalogueIdentityRepository.FindByLegacyProviderId(_database, CatalogueEntityKind.Author, meta.ForeignAuthorId);
                    if (linkedOwner.HasValue)
                    {
                        existing = Find(linkedOwner.Value);
                        if (existing != null)
                        {
                            // An accepted alias hydrates the same author without changing their primary ID or route.
                            meta.ForeignAuthorId = existing.ForeignAuthorId;
                            meta.TitleSlug = existing.TitleSlug;
                        }
                    }
                }

                if (existing == null)
                {
                    existing = FindCorroboratedLegacyHardcoverAuthor(meta);
                }

                if (existing == null)
                {
                    if (meta.Id == 0)
                    {
                        addMetadataList.Add(meta);
                    }

                    continue;
                }

                meta.UseDbFieldsFrom(existing);
                PreserveExistingAuthorMetadata(meta, existing);
                if (meta.PendingSourceAssociations.Count > 0 || !meta.Equals(existing))
                {
                    updateMetadataList.Add(meta);
                }
                else
                {
                    upToDateMetadataCount++;
                }
            }

            AssignUniqueSlugs(addMetadataList, updateMetadataList.Select(meta => meta.TitleSlug));

            UpdateMany(updateMetadataList);
            InsertMany(addMetadataList);

            _logger.Debug($"{upToDateMetadataCount} author metadata up to date; Updating {updateMetadataList.Count}, Adding {addMetadataList.Count} author metadata entries.");

            return updateMetadataList.Count > 0 || addMetadataList.Count > 0;
        }

        private void AssignUniqueSlugs(List<AuthorMetadata> incoming, IEnumerable<string> pendingSlugs)
        {
            var used = new HashSet<string>(pendingSlugs, StringComparer.Ordinal);
            foreach (var meta in incoming)
            {
                var baseSlug = meta.TitleSlug ?? meta.ForeignAuthorId?.ToUrlSlug() ?? meta.Name?.ToUrlSlug();
                if (baseSlug.IsNullOrWhiteSpace())
                {
                    baseSlug = "unknown";
                }

                var slug = baseSlug;
                var suffix = 2;
                while (used.Contains(slug) || Query(current => current.TitleSlug == slug).Any() ||
                       CatalogueIdentityRepository.FindByRoute(_database, CatalogueEntityKind.Author, slug).HasValue)
                {
                    slug = $"{baseSlug}-{suffix}";
                    suffix++;
                }

                meta.TitleSlug = slug;
                used.Add(slug);
            }
        }

        private AuthorMetadata FindCorroboratedLegacyHardcoverAuthor(AuthorMetadata incoming)
        {
            var source = CatalogueIdentityRepository.ParseLegacySource(CatalogueEntityKind.Author, incoming.ForeignAuthorId, null);
            if (source?.Provider != "Hardcover" || incoming.Name.IsNullOrWhiteSpace())
            {
                return null;
            }

            var nameBasedId = $"hardcover:author:{Uri.EscapeDataString(incoming.Name.Trim())}";
            var previous = Query(author => author.ForeignAuthorId == nameBasedId).SingleOrDefault();
            if (previous == null ||
                !string.Equals(previous.Name, incoming.Name, StringComparison.OrdinalIgnoreCase) ||
                CatalogueIdentityRepository.ParseLegacySource(CatalogueEntityKind.Author, previous.ForeignAuthorId, null) != null)
            {
                return null;
            }

            var previousOpenLibrary = CatalogueIdentityRepository.NormalizeSource(CatalogueEntityKind.Author, "OpenLibrary", previous.OpenLibraryAuthorId, null);
            var incomingOpenLibrary = CatalogueIdentityRepository.NormalizeSource(CatalogueEntityKind.Author, "OpenLibrary", incoming.OpenLibraryAuthorId, null);
            if (previousOpenLibrary != null && incomingOpenLibrary != null)
            {
                return previousOpenLibrary.ProviderId == incomingOpenLibrary.ProviderId ? previous : null;
            }

            var born = incoming.Born?.Date;
            return born.HasValue && (born.Value.Month != 1 || born.Value.Day != 1) && born == previous.Born?.Date
                ? previous
                : null;
        }

        private static void PreserveExistingAuthorMetadata(AuthorMetadata incoming, AuthorMetadata existing)
        {
            incoming.Overview = incoming.Overview.IsNullOrWhiteSpace() ? existing.Overview : incoming.Overview;
            incoming.OpenLibraryAuthorId ??= existing.OpenLibraryAuthorId;
            incoming.Born ??= existing.Born;
            incoming.Died ??= existing.Died;
            if (incoming.Images?.Any() != true)
            {
                incoming.Images = existing.Images;
            }
        }
    }
}
