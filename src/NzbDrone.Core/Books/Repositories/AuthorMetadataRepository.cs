using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Dapper;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Datastore.Converters;
using NzbDrone.Core.MediaCover;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.Books
{
    public interface IAuthorMetadataRepository : IBasicRepository<AuthorMetadata>
    {
        List<AuthorMetadata> FindById(List<string> foreignIds);
        bool UpsertMany(List<AuthorMetadata> data);
        bool TryFillOpenLibraryProfile(Author author, string sourceId, string overview, MediaCover.MediaCover portrait);
    }

    public class AuthorMetadataRepository : BasicRepository<AuthorMetadata>, IAuthorMetadataRepository
    {
        private static readonly EmbeddedDocumentConverter<List<MediaCover.MediaCover>> ImageConverter = new EmbeddedDocumentConverter<List<MediaCover.MediaCover>>();
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

        public bool TryFillOpenLibraryProfile(Author author, string sourceId, string overview, MediaCover.MediaCover portrait)
        {
            var metadata = author?.Metadata?.Value;
            if (author is not { Id: > 0, AuthorMetadataId: > 0 } || metadata?.Id != author.AuthorMetadataId ||
                !Guid.TryParseExact(metadata.CanonicalId, "N", out var canonicalId) || canonicalId == Guid.Empty ||
                CatalogueIdentityRepository.ParseLegacySource(CatalogueEntityKind.Author, metadata.ForeignAuthorId, null)?.Provider != "Hardcover" ||
                sourceId == null || OpenLibraryMissingAuthorPortrait.NormalizeSourceId(sourceId) != sourceId ||
                (metadata.OpenLibraryAuthorId.IsNotNullOrWhiteSpace() && OpenLibraryMissingAuthorPortrait.NormalizeSourceId(metadata.OpenLibraryAuthorId) != sourceId))
            {
                return false;
            }

            using var connection = _database.OpenConnection();
            var existing = connection.QuerySingleOrDefault<(string Overview, string Images)>(
                """SELECT "Overview", "Images" FROM "AuthorMetadata" WHERE "Id" = @Id""", new { metadata.Id });
            var fillOverview = existing.Overview.IsNullOrWhiteSpace() && overview.IsNotNullOrWhiteSpace();
            var images = portrait == null ? null : ReadImagesForPortrait(existing.Images, metadata.Id);
            var fillPortrait = images != null && !images.Any(image => image?.CoverType == MediaCoverTypes.Poster && image.Url.IsNotNullOrWhiteSpace());
            if (!fillOverview && !fillPortrait)
            {
                return false;
            }

            if (fillPortrait)
            {
                images.Add(portrait);
            }

            // Compare only fields we mean to fill. An author refresh may have finished while the profile was in flight.
            return connection.Execute(
                """
                UPDATE "AuthorMetadata"
                   SET "Overview" = CASE WHEN @fillOverview AND "Overview" IS NOT DISTINCT FROM @previousOverview THEN @overview ELSE "Overview" END,
                       "Images" = CASE WHEN @fillPortrait AND "Images" IS NOT DISTINCT FROM @previousImages THEN @images ELSE "Images" END
                 WHERE "Id" = @Id AND "CanonicalId" = @CanonicalId AND "ForeignAuthorId" = @ForeignAuthorId
                   AND (@fillOverview AND "Overview" IS NOT DISTINCT FROM @previousOverview OR @fillPortrait AND "Images" IS NOT DISTINCT FROM @previousImages)
                   AND ("OpenLibraryAuthorId" IS NULL OR "OpenLibraryAuthorId" = @previousOpenLibraryId)
                   AND EXISTS (
                       SELECT 1 FROM "Authors" a WHERE a."Id" = @authorId AND a."AuthorMetadataId" = @Id
                   )
                   AND EXISTS (
                       SELECT 1 FROM "AuthorSourceAssociations" s
                        WHERE s."AuthorMetadataId" = @Id AND s."Provider" = 'OpenLibrary' AND s."ProviderId" = @sourceId
                          AND s."Evidence" = @evidence
                   )
                   AND NOT EXISTS (
                       SELECT 1 FROM "AuthorSourceAssociations" s
                        WHERE s."AuthorMetadataId" = @Id AND s."Provider" = 'OpenLibrary' AND s."ProviderId" <> @sourceId
                   )
                """,
                new
                {
                    metadata.Id,
                    metadata.CanonicalId,
                    metadata.ForeignAuthorId,
                    authorId = author.Id,
                    sourceId,
                    evidence = AuthorCanonicalizationService.BibliographyEvidence,
                    previousOpenLibraryId = metadata.OpenLibraryAuthorId,
                    fillOverview,
                    fillPortrait,
                    overview = overview?.Trim(),
                    images = images ?? new List<MediaCover.MediaCover>(),
                    previousOverview = existing.Overview,
                    previousImages = existing.Images
                }) == 1;
        }

        private List<MediaCover.MediaCover> ReadImagesForPortrait(string storedImages, int metadataId)
        {
            if (storedImages.IsNullOrWhiteSpace())
            {
                return new List<MediaCover.MediaCover>();
            }

            try
            {
                return ImageConverter.Parse(storedImages) ?? new List<MediaCover.MediaCover>();
            }
            catch (JsonException)
            {
                _logger.Debug("Skipping portrait enrichment for author metadata {0} because its existing image data could not be read.", metadataId);
                return null;
            }
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
