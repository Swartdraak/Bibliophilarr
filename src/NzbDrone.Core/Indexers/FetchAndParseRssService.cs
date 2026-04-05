using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Core.Parser.Model;
namespace NzbDrone.Core.Indexers
{
    public interface IFetchAndParseRss
    {
        Task<List<ReleaseInfo>> Fetch();
    }

    public class FetchAndParseRssService : IFetchAndParseRss
    {
        private static readonly TimeSpan NoIndexerWarningInterval = TimeSpan.FromHours(1);
        private static DateTime _nextNoIndexerWarningUtc = DateTime.MinValue;

        private readonly IIndexerFactory _indexerFactory;
        private readonly Logger _logger;

        public FetchAndParseRssService(IIndexerFactory indexerFactory, Logger logger)
        {
            _indexerFactory = indexerFactory;
            _logger = logger;
        }

        public async Task<List<ReleaseInfo>> Fetch()
        {
            var indexers = _indexerFactory.RssEnabled();

            if (!indexers.Any())
            {
                var now = DateTime.UtcNow;

                if (now >= _nextNoIndexerWarningUtc)
                {
                    _nextNoIndexerWarningUtc = now.Add(NoIndexerWarningInterval);
                    _logger.Warn("No available indexers. Configure at least one RSS-enabled indexer in Settings > Indexers, or ignore this warning if indexers are intentionally disabled.");
                }
                else
                {
                    _logger.Debug("No available indexers. Suppressing repeated warning until {0:o}", _nextNoIndexerWarningUtc);
                }

                return new List<ReleaseInfo>();
            }

            _logger.Debug("Available indexers {0}", indexers.Count);

            var tasks = indexers.Select(FetchIndexer);

            var batch = await Task.WhenAll(tasks);

            var result = batch.SelectMany(x => x).ToList();

            _logger.Debug("Found {0} reports", result.Count);

            return result;
        }

        private async Task<IList<ReleaseInfo>> FetchIndexer(IIndexer indexer)
        {
            try
            {
                return await indexer.FetchRecent();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error during RSS Sync");
            }

            return Array.Empty<ReleaseInfo>();
        }
    }
}
