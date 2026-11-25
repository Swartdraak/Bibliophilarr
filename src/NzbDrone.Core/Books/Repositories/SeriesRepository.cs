using System;
using System.Collections.Generic;
using System.Linq;
using Dapper;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.Books
{
    public interface ISeriesRepository : IBasicRepository<Series>
    {
        Series FindById(string foreignSeriesId);
        List<Series> FindById(List<string> foreignSeriesId);
        List<Series> GetByAuthorMetadataId(int authorMetadataId);
        List<Series> GetByAuthorId(int authorId);
        bool TryFillHardcoverDescription(Author author, Series series, string description);
    }

    public class SeriesRepository : BasicRepository<Series>, ISeriesRepository
    {
        public SeriesRepository(IMainDatabase database, IEventAggregator eventAggregator)
            : base(database, eventAggregator)
        {
        }

        public Series FindById(string foreignSeriesId)
        {
            var direct = Query(x => x.ForeignSeriesId == foreignSeriesId).SingleOrDefault();
            if (direct != null)
            {
                return direct;
            }

            const CatalogueEntityKind kind = CatalogueEntityKind.Series;
            var owner = CatalogueIdentityRepository.FindByRoute(_database, kind, foreignSeriesId) ??
                        CatalogueIdentityRepository.FindByLegacyProviderId(_database, kind, foreignSeriesId);
            return owner.HasValue ? Find(owner.Value) : null;
        }

        public List<Series> FindById(List<string> foreignSeriesId)
        {
            return Query(x => foreignSeriesId.Contains(x.ForeignSeriesId));
        }

        public List<Series> GetByAuthorMetadataId(int authorMetadataId)
        {
            return QueryDistinct(Builder().Join<Series, SeriesBookLink>((l, r) => l.Id == r.SeriesId)
                                 .Join<SeriesBookLink, Book>((l, r) => l.BookId == r.Id)
                                 .Where<Book>(x => x.AuthorMetadataId == authorMetadataId));
        }

        public List<Series> GetByAuthorId(int authorId)
        {
            return QueryDistinct(Builder().Join<Series, SeriesBookLink>((l, r) => l.Id == r.SeriesId)
                                 .Join<SeriesBookLink, Book>((l, r) => l.BookId == r.Id)
                                 .Join<Book, Author>((l, r) => l.AuthorMetadataId == r.AuthorMetadataId)
                                 .Where<Author>(x => x.Id == authorId));
        }

        public bool TryFillHardcoverDescription(Author author, Series series, string description)
        {
            var metadata = author?.Metadata?.Value;
            if (author is not { Id: > 0, AuthorMetadataId: > 0 } || metadata?.Id != author.AuthorMetadataId ||
                series is not { Id: > 0 } || !string.IsNullOrWhiteSpace(series.Description) || string.IsNullOrWhiteSpace(description) ||
                !Guid.TryParseExact(metadata.CanonicalId, "N", out var authorCanonical) || authorCanonical == Guid.Empty ||
                !Guid.TryParseExact(series.CanonicalId, "N", out var seriesCanonical) || seriesCanonical == Guid.Empty)
            {
                return false;
            }

            var authorSource = CatalogueIdentityRepository.ParseLegacySource(CatalogueEntityKind.Author, metadata.ForeignAuthorId, null);
            var seriesSource = CatalogueIdentityRepository.ParseLegacySource(CatalogueEntityKind.Series, series.ForeignSeriesId, null);
            if (authorSource?.Provider != "Hardcover" || metadata.ForeignAuthorId != $"hardcover:author:{authorSource.ProviderId}" ||
                seriesSource?.Provider != "Hardcover" || series.ForeignSeriesId != $"hardcover:series:{seriesSource.ProviderId}")
            {
                return false;
            }

            using var connection = _database.OpenConnection();
            return connection.Execute(
                """
                UPDATE "Series" SET "Description" = @description
                 WHERE "Id" = @Id AND "CanonicalId" = @CanonicalId AND "ForeignSeriesId" = @ForeignSeriesId
                   AND ("Description" = @previous OR ("Description" IS NULL AND @previous IS NULL))
                   AND EXISTS (
                       SELECT 1 FROM "SeriesSourceAssociations" s
                        WHERE s."SeriesId" = @Id AND s."Provider" = 'Hardcover' AND s."ProviderId" = @seriesSourceId
                          AND s."Evidence" IN ('primary-provider-id', 'legacy-provider-id')
                   )
                   AND EXISTS (
                       SELECT 1 FROM "SeriesBookLink" l
                         JOIN "Books" b ON b."Id" = l."BookId"
                         JOIN "AuthorMetadata" m ON m."Id" = b."AuthorMetadataId"
                         JOIN "Authors" a ON a."AuthorMetadataId" = m."Id"
                         JOIN "AuthorSourceAssociations" s ON s."AuthorMetadataId" = m."Id"
                        WHERE l."SeriesId" = @Id AND a."Id" = @authorId AND m."Id" = @metadataId
                          AND m."CanonicalId" = @authorCanonicalId AND m."ForeignAuthorId" = @authorForeignId
                          AND s."Provider" = 'Hardcover' AND s."ProviderId" = @authorSourceId
                          AND s."Evidence" IN ('primary-provider-id', 'legacy-provider-id')
                   )
                """,
                new
                {
                    series.Id,
                    series.CanonicalId,
                    series.ForeignSeriesId,
                    description = description.Trim(),
                    previous = series.Description,
                    seriesSourceId = seriesSource.ProviderId,
                    authorId = author.Id,
                    metadataId = metadata.Id,
                    authorCanonicalId = metadata.CanonicalId,
                    authorForeignId = metadata.ForeignAuthorId,
                    authorSourceId = authorSource.ProviderId
                }) == 1;
        }
    }
}
