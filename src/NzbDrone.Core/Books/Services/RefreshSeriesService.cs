using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using NLog;
using NzbDrone.Common.Extensions;

namespace NzbDrone.Core.Books
{
    public interface IRefreshSeriesService
    {
        bool RefreshSeriesInfo(int authorMetadataId, List<Series> remoteBooks, Author remoteData, bool forceBookRefresh, bool forceUpdateFileTags, DateTime? lastUpdate);
    }

    public class RefreshSeriesService : RefreshEntityServiceBase<Series, SeriesBookLink>, IRefreshSeriesService
    {
        private static readonly Dictionary<string, RefreshGate> RefreshGates = new Dictionary<string, RefreshGate>(StringComparer.Ordinal);

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

        protected override UpdateResult UpdateEntity(Series local, Series remote)
        {
            if (local.Equals(remote))
            {
                return UpdateResult.None;
            }

            local.UseMetadataFrom(remote);

            return UpdateResult.UpdateTags;
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
            // Stabilize the author's link set while identifying shared series that this refresh might write.
            return WithRefreshGates(new[] { $"author:{authorMetadataId}" }, () =>
            {
                var existingByAuthor = _seriesService.GetByAuthorMetadataId(authorMetadataId);
                var foreignIds = remoteSeries.Concat(remoteData.Series.Value).Concat(existingByAuthor)
                    .Select(series => series.ForeignSeriesId).Distinct(StringComparer.Ordinal).ToList();

                // All refreshes acquire the author gate first and shared series gates in the same order.
                return WithRefreshGates(foreignIds.Select(id => $"series:{id}"),
                    () => ReconcileSeries(authorMetadataId, foreignIds, remoteData, forceUpdateFileTags));
            });
        }

        private static bool WithRefreshGates(IEnumerable<string> keys, Func<bool> action)
        {
            var orderedKeys = keys.Distinct(StringComparer.Ordinal).OrderBy(key => key, StringComparer.Ordinal).ToList();
            var reservations = new List<(string Key, RefreshGate Gate)>(orderedKeys.Count);
            lock (RefreshGates)
            {
                foreach (var key in orderedKeys)
                {
                    if (!RefreshGates.TryGetValue(key, out var gate))
                    {
                        gate = new RefreshGate();
                        RefreshGates.Add(key, gate);
                    }

                    gate.Users++;
                    reservations.Add((key, gate));
                }
            }

            var entered = 0;
            try
            {
                for (; entered < reservations.Count; entered++)
                {
                    Monitor.Enter(reservations[entered].Gate);
                }

                return action();
            }
            finally
            {
                for (var index = entered - 1; index >= 0; index--)
                {
                    Monitor.Exit(reservations[index].Gate);
                }

                lock (RefreshGates)
                {
                    foreach (var (key, gate) in reservations)
                    {
                        // A waiting refresh reserves the same gate before sleeping.
                        if (--gate.Users == 0)
                        {
                            RefreshGates.Remove(key);
                        }
                    }
                }
            }
        }

        private sealed class RefreshGate
        {
            public int Users { get; set; }
        }

        private bool ReconcileSeries(int authorMetadataId, List<string> foreignIds, Author remoteData, bool forceUpdateFileTags)
        {
            var updated = false;

            // Discovery above identifies keys only. Reread the rows after all their gates are owned.
            var existing = _seriesService.FindById(foreignIds);

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

            var remoteSeries = remoteData.Series.Value;

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
    }
}
