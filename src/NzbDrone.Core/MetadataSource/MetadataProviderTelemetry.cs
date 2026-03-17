using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace NzbDrone.Core.MetadataSource
{
    public class MetadataProviderTelemetrySnapshot
    {
        public string ProviderName { get; set; }
        public long Calls { get; set; }
        public long Successes { get; set; }
        public long Failures { get; set; }
        public long NullResults { get; set; }
        public long FallbackHits { get; set; }
        public long TotalLatencyMs { get; set; }
        public double AverageLatencyMs => Calls == 0 ? 0 : (double)TotalLatencyMs / Calls;
        public double HitRate => Calls == 0 ? 0 : (double)Successes / Calls;
    }

    public interface IMetadataProviderTelemetryService
    {
        void Record(string providerName, long latencyMs, bool success, bool returnedNull, bool fallbackHit);
        IReadOnlyList<MetadataProviderTelemetrySnapshot> GetSnapshots();
    }

    public class MetadataProviderTelemetryService : IMetadataProviderTelemetryService
    {
        private class ProviderStats
        {
            public string ProviderName;
            public long Calls;
            public long Successes;
            public long Failures;
            public long NullResults;
            public long FallbackHits;
            public long TotalLatencyMs;
        }

        private readonly ConcurrentDictionary<string, ProviderStats> _stats = new ConcurrentDictionary<string, ProviderStats>(StringComparer.OrdinalIgnoreCase);

        public void Record(string providerName, long latencyMs, bool success, bool returnedNull, bool fallbackHit)
        {
            var stat = _stats.GetOrAdd(providerName ?? "unknown", name => new ProviderStats { ProviderName = name });

            System.Threading.Interlocked.Increment(ref stat.Calls);
            System.Threading.Interlocked.Add(ref stat.TotalLatencyMs, latencyMs);

            if (success)
            {
                System.Threading.Interlocked.Increment(ref stat.Successes);
            }

            if (returnedNull)
            {
                System.Threading.Interlocked.Increment(ref stat.NullResults);
            }

            if (fallbackHit)
            {
                System.Threading.Interlocked.Increment(ref stat.FallbackHits);
            }

            if (!success && !returnedNull)
            {
                System.Threading.Interlocked.Increment(ref stat.Failures);
            }
        }

        public IReadOnlyList<MetadataProviderTelemetrySnapshot> GetSnapshots()
        {
            return _stats.Values
                .Select(x => new MetadataProviderTelemetrySnapshot
                {
                    ProviderName = x.ProviderName,
                    Calls = x.Calls,
                    Successes = x.Successes,
                    Failures = x.Failures,
                    NullResults = x.NullResults,
                    FallbackHits = x.FallbackHits,
                    TotalLatencyMs = x.TotalLatencyMs
                })
                .OrderBy(x => x.ProviderName)
                .ToList();
        }
    }
}
