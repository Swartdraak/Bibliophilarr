using System;
using System.Collections.Generic;
using System.Linq;

namespace Bibliophilarr.Api.V1.Search
{
    public interface ISearchTelemetryService
    {
        void RecordUnsupportedEntityType(string term, Type entityType);
        SearchTelemetrySnapshot GetSnapshot();
        void Reset();
    }

    public class SearchTelemetrySnapshot
    {
        public int UnsupportedEntityCount { get; set; }
        public Dictionary<string, int> UnsupportedEntityTypes { get; set; } = new Dictionary<string, int>();
        public Dictionary<string, int> Terms { get; set; } = new Dictionary<string, int>();
    }

    public class SearchTelemetryService : ISearchTelemetryService
    {
        private readonly object _sync = new object();
        private readonly Dictionary<string, int> _unsupportedEntityTypes = new Dictionary<string, int>(StringComparer.Ordinal);
        private readonly Dictionary<string, int> _terms = new Dictionary<string, int>(StringComparer.Ordinal);
        private int _unsupportedEntityCount;

        public void RecordUnsupportedEntityType(string term, Type entityType)
        {
            var typeKey = entityType?.FullName ?? "<null>";
            var termKey = string.IsNullOrWhiteSpace(term) ? "<empty>" : term;

            lock (_sync)
            {
                _unsupportedEntityCount++;
                _unsupportedEntityTypes[typeKey] = _unsupportedEntityTypes.TryGetValue(typeKey, out var typeCount) ? typeCount + 1 : 1;
                _terms[termKey] = _terms.TryGetValue(termKey, out var termCount) ? termCount + 1 : 1;
            }
        }

        public SearchTelemetrySnapshot GetSnapshot()
        {
            lock (_sync)
            {
                return new SearchTelemetrySnapshot
                {
                    UnsupportedEntityCount = _unsupportedEntityCount,
                    UnsupportedEntityTypes = _unsupportedEntityTypes.ToDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal),
                    Terms = _terms.ToDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal)
                };
            }
        }

        public void Reset()
        {
            lock (_sync)
            {
                _unsupportedEntityCount = 0;
                _unsupportedEntityTypes.Clear();
                _terms.Clear();
            }
        }
    }
}
