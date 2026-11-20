using System;
using System.Collections.Generic;
using System.Linq;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Parser;

namespace NzbDrone.Core.Books
{
    public interface IRefreshSeriesService
    {
        bool RefreshSeriesInfo(int authorMetadataId, List<Series> remoteBooks, Author remoteData, bool forceBookRefresh, bool forceUpdateFileTags, DateTime? lastUpdate);
    }

    public class RefreshSeriesService : RefreshEntityServiceBase<Series, SeriesBookLink>, IRefreshSeriesService
    {
        private readonly IBookService _bookService;
        private readonly ISeriesService _seriesService;
        private readonly ISeriesBookLinkService _linkService;
        private readonly IRefreshSeriesBookLinkService _refreshLinkService;
        private readonly Logger _logger;

        public RefreshSeriesService(IBookService bookService,
                                    ISeriesService seriesService,
                                    ISeriesBookLinkService linkService,
                                    IRefreshSeriesBookLinkService refreshLinkService,
                                    IAuthorMetadataService authorMetadataService,
                                    Logger logger)
        : base(logger, authorMetadataService)
        {
            _bookService = bookService;
            _seriesService = seriesService;
            _linkService = linkService;
            _refreshLinkService = refreshLinkService;
            _logger = logger;
        }

        protected override RemoteData GetRemoteData(Series local, List<Series> remote, Author data)
        {
            return new RemoteData
            {
                Entity = remote.SingleOrDefault(x => x.ForeignSeriesId == local.ForeignSeriesId)
            };
        }

        protected override bool IsMerge(Series local, Series remote)
        {
            return local.ForeignSeriesId != remote.ForeignSeriesId;
        }

        protected override bool ShouldDelete(Series local)
        {
            return false;
        }

        protected override UpdateResult UpdateEntity(Series local, Series remote)
        {
            var changed = !local.Equals(remote);
            if (changed || remote.PendingAliases.Count > 0 || remote.PendingSourceAssociations.Count > 0)
            {
                local.UseMetadataFrom(remote);
            }

            return changed ? UpdateResult.UpdateTags : UpdateResult.None;
        }

        protected override Series GetEntityByForeignId(Series local)
        {
            return _seriesService.FindById(local.ForeignSeriesId);
        }

        protected override void SaveEntity(Series local)
        {
            // Use UpdateMany to avoid firing the book edited event
            _seriesService.UpdateMany(new List<Series> { local });
        }

        protected override void DeleteEntity(Series local, bool deleteFiles)
        {
            _logger.Trace($"Removing links for series {local} author {local.ForeignAuthorId}");
            var children = GetLocalChildren(local, null);
            _linkService.DeleteMany(children);

            if (!_linkService.GetLinksBySeries(local.Id).Any())
            {
                _logger.Trace($"Series {local} has no links remaining, removing");
                _seriesService.Delete(local.Id);
            }
        }

        protected override List<SeriesBookLink> GetRemoteChildren(Series local, Series remote)
        {
            return remote.LinkItems;
        }

        protected override List<SeriesBookLink> GetLocalChildren(Series entity, List<SeriesBookLink> remoteChildren)
        {
            return _linkService.GetLinksBySeriesAndAuthor(entity.Id, entity.ForeignAuthorId);
        }

        protected override Tuple<SeriesBookLink, List<SeriesBookLink>> GetMatchingExistingChildren(List<SeriesBookLink> existingChildren, SeriesBookLink remote)
        {
            var existingChild = existingChildren.SingleOrDefault(x => x.BookId == remote.Book.Value.Id);
            var mergeChildren = new List<SeriesBookLink>();
            return Tuple.Create(existingChild, mergeChildren);
        }

        protected override void PrepareNewChild(SeriesBookLink child, Series entity)
        {
            child.Series = entity;
            child.SeriesId = entity.Id;
            child.BookId = child.Book.Value.Id;
        }

        protected override void PrepareExistingChild(SeriesBookLink local, SeriesBookLink remote, Series entity)
        {
            local.Series = entity;
            local.SeriesId = entity.Id;

            remote.Id = local.Id;
            remote.BookId = local.BookId;
            remote.SeriesId = entity.Id;
        }

        protected override void AddChildren(List<SeriesBookLink> children)
        {
            _linkService.InsertMany(children);
        }

        protected override bool RefreshChildren(SortedChildren localChildren, List<SeriesBookLink> remoteChildren, Author remoteData, bool forceChildRefresh, bool forceUpdateFileTags, DateTime? lastUpdate)
        {
            return _refreshLinkService.RefreshSeriesBookLinkInfo(localChildren.Added, localChildren.Updated, localChildren.Merged, localChildren.Deleted, localChildren.UpToDate, remoteChildren, forceUpdateFileTags);
        }

        public bool RefreshSeriesInfo(int authorMetadataId, List<Series> remoteSeries, Author remoteData, bool forceBookRefresh, bool forceUpdateFileTags, DateTime? lastUpdate)
        {
            var updated = false;

            var existingByAuthor = _seriesService.GetByAuthorMetadataId(authorMetadataId);
            var existingBySeries = _seriesService.FindById(remoteSeries.Select(x => x.ForeignSeriesId).ToList());
            var existing = existingByAuthor.Concat(existingBySeries).GroupBy(x => x.ForeignSeriesId).Select(x => x.First()).ToList();

            var books = _bookService.GetBooksByAuthorMetadataId(authorMetadataId);
            var bookDict = books.ToDictionary(x => x.ForeignBookId);

            // Build series with links for books we have locally.
            // Series are preserved even when no local books match so they
            // remain visible in the UI with accurate metadata.
            foreach (var s in remoteData.Series.Value)
            {
                var matchedLinks = new List<SeriesBookLink>();

                s.LinkItems.Value.ForEach(x => x.Series = s);
                foreach (var link in s.LinkItems.Value)
                {
                    if (bookDict.TryGetValue(link.Book.Value.ForeignBookId, out var dbBook))
                    {
                        // Replace stub/remote book with the real DB book so Id is correct
                        link.Book = dbBook;
                        matchedLinks.Add(link);
                    }
                }

                s.LinkItems = matchedLinks;
            }

            remoteSeries = ReconcileSeriesSources(remoteData.Series.Value, existing, books.Select(book => book.Id).ToHashSet());
            remoteData.Series = remoteSeries;

            var toAdd = remoteSeries.ExceptBy(x => x.ForeignSeriesId, existing, x => x.ForeignSeriesId, StringComparer.Ordinal).ToList();
            var all = toAdd.Union(existing).ToList();

            _seriesService.InsertMany(toAdd);

            foreach (var item in all)
            {
                item.ForeignAuthorId = remoteData.ForeignAuthorId;
                updated |= RefreshEntityInfo(item, remoteSeries, remoteData, true, forceUpdateFileTags, null);
            }

            return updated;
        }

        private List<Series> ReconcileSeriesSources(List<Series> incoming, List<Series> existing, HashSet<int> authorBookIds)
        {
            var reconciled = new Dictionary<string, Series>(StringComparer.Ordinal);
            var incomingIds = incoming.Select(series => series.ForeignSeriesId).ToHashSet(StringComparer.Ordinal);
            var existingIds = existing.Select(series => series.ForeignSeriesId).ToHashSet(StringComparer.Ordinal);
            foreach (var candidate in incoming.OrderByDescending(series => existingIds.Contains(series.ForeignSeriesId)))
            {
                var alias = candidate.ForeignSeriesId;
                if (!existingIds.Contains(alias))
                {
                    var canonical = _seriesService.FindById(alias);
                    if (canonical == null)
                    {
                        var equivalent = existing.Where(series => !incomingIds.Contains(series.ForeignSeriesId) &&
                                                                  SeriesIdentityMatches(series, candidate)).Take(2).ToList();
                        if (equivalent.Count == 1)
                        {
                            canonical = equivalent[0];
                        }
                    }

                    if (canonical != null)
                    {
                        if (reconciled.ContainsKey(canonical.ForeignSeriesId))
                        {
                            continue;
                        }

                        if (existingIds.Add(canonical.ForeignSeriesId))
                        {
                            existing.Add(canonical);
                        }

                        ReuseCatalogueSeries(candidate, canonical, authorBookIds);
                    }
                }

                reconciled.TryAdd(candidate.ForeignSeriesId, candidate);
            }

            return reconciled.Values.ToList();
        }

        private static void ReuseCatalogueSeries(Series candidate, Series canonical, HashSet<int> authorBookIds)
        {
            var alias = candidate.ForeignSeriesId;
            if (alias != canonical.ForeignSeriesId)
            {
                candidate.PendingAliases.Add(alias);
                var source = CatalogueIdentityRepository.ParseLegacySource(CatalogueEntityKind.Series, alias, "corroborated-series-membership");
                if (source != null)
                {
                    candidate.PendingSourceAssociations.Add(source);
                }
            }

            candidate.UseDbFieldsFrom(canonical);
            candidate.ForeignSeriesId = canonical.ForeignSeriesId;
            var retainedLinks = canonical.LinkItems.Value.Where(link => authorBookIds.Contains(link.BookId)).ToList();
            candidate.LinkItems = retainedLinks.Concat(candidate.LinkItems.Value)
                .GroupBy(link => link.Book.Value.Id)
                .Select(group => group.FirstOrDefault(link => link.Position.IsNotNullOrWhiteSpace()) ?? group.First())
                .ToList();
        }

        internal static bool SeriesIdentityMatches(Series existing, Series incoming)
        {
            var existingProvider = existing.ForeignSeriesId?.Split(':', 2)[0];
            var incomingProvider = incoming.ForeignSeriesId?.Split(':', 2)[0];
            var title = existing.Title.CleanAuthorName();
            if (existingProvider.IsNullOrWhiteSpace() || incomingProvider.IsNullOrWhiteSpace() ||
                existingProvider.Equals(incomingProvider, StringComparison.OrdinalIgnoreCase) ||
                title.Length == 0 || title != incoming.Title.CleanAuthorName())
            {
                return false;
            }

            var existingLinks = existing.LinkItems?.Value ?? new List<SeriesBookLink>();
            var incomingLinks = incoming.LinkItems?.Value ?? new List<SeriesBookLink>();
            var comparisons = existingLinks.Where(link => link.IsPrimary && link.BookId > 0)
                .Join(incomingLinks.Where(link => link.IsPrimary && link.Book?.Value?.Id > 0),
                    link => link.BookId,
                    link => link.Book.Value.Id,
                    (left, right) => (Id: left.BookId, Left: left.Position, Right: right.Position))
                .Where(match => match.Left.IsNotNullOrWhiteSpace() && match.Right.IsNotNullOrWhiteSpace())
                .ToList();

            return comparisons.Select(match => match.Id).Distinct().Count() >= 2 &&
                   comparisons.All(match => match.Left == match.Right);
        }
    }
}
