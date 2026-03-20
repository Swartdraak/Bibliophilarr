using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace NzbDrone.Core.MetadataSource
{
    public class MetadataProviderOperationTelemetrySnapshot
    {
        public string ProviderName { get; set; }
        public string OperationName { get; set; }
        public long Calls { get; set; }
        public long Successes { get; set; }
        public long Failures { get; set; }
        public long NullResults { get; set; }
        public long FallbackHits { get; set; }
        public long TotalLatencyMs { get; set; }
        public double AverageLatencyMs => Calls == 0 ? 0 : (double)TotalLatencyMs / Calls;
        public double HitRate => Calls == 0 ? 0 : (double)Successes / Calls;
    }

    public class MetadataProviderTelemetrySnapshot
    {
        public string ProviderName { get; set; }
        public long Calls { get; set; }
        public long Successes { get; set; }
        public long Failures { get; set; }
        public long NullResults { get; set; }
        public long FallbackHits { get; set; }
        public long TotalLatencyMs { get; set; }
        public IReadOnlyList<MetadataProviderOperationTelemetrySnapshot> Operations { get; set; }
        public double AverageLatencyMs => Calls == 0 ? 0 : (double)TotalLatencyMs / Calls;
        public double HitRate => Calls == 0 ? 0 : (double)Successes / Calls;
    }

    public interface IMetadataProviderTelemetryService
    {
        void Record(string providerName, string operationName, long latencyMs, bool success, bool returnedNull, bool fallbackHit);
        IReadOnlyList<MetadataProviderTelemetrySnapshot> GetSnapshots();
        IReadOnlyList<MetadataProviderOperationTelemetrySnapshot> GetOperationSnapshots();
    }

    public class MetadataProviderTelemetryService : IMetadataProviderTelemetryService
    {
        private class ProviderStats
        {
            public string ProviderName;
            public string OperationName;
            public long Calls;
            public long Successes;
            public long Failures;
            public long NullResults;
            public long FallbackHits;
            public long TotalLatencyMs;
        }

        private readonly ConcurrentDictionary<string, ProviderStats> _stats = new ConcurrentDictionary<string, ProviderStats>(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, ProviderStats> _operationStats = new ConcurrentDictionary<string, ProviderStats>(StringComparer.OrdinalIgnoreCase);

        public void Record(string providerName, string operationName, long latencyMs, bool success, bool returnedNull, bool fallbackHit)
        {
            var normalizedProviderName = providerName ?? "unknown";
            var normalizedOperationName = operationName ?? "unknown";

            var aggregate = _stats.GetOrAdd(normalizedProviderName, name => new ProviderStats { ProviderName = name });
            RecordStats(aggregate, latencyMs, success, returnedNull, fallbackHit);

            var operationKey = normalizedProviderName + "\u001f" + normalizedOperationName;
            var operation = _operationStats.GetOrAdd(operationKey, _ => new ProviderStats
            {
                ProviderName = normalizedProviderName,
                OperationName = normalizedOperationName
            });
            RecordStats(operation, latencyMs, success, returnedNull, fallbackHit);
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
                    TotalLatencyMs = x.TotalLatencyMs,
                    Operations = _operationStats.Values
                        .Where(y => string.Equals(y.ProviderName, x.ProviderName, StringComparison.OrdinalIgnoreCase))
                        .OrderBy(y => y.OperationName)
                        .Select(y => new MetadataProviderOperationTelemetrySnapshot
                        {
                            ProviderName = y.ProviderName,
                            OperationName = y.OperationName,
                            Calls = y.Calls,
                            Successes = y.Successes,
                            Failures = y.Failures,
                            NullResults = y.NullResults,
                            FallbackHits = y.FallbackHits,
                            TotalLatencyMs = y.TotalLatencyMs
                        })
                        .ToList()
                })
                .OrderBy(x => x.ProviderName)
                .ToList();
        }

        public IReadOnlyList<MetadataProviderOperationTelemetrySnapshot> GetOperationSnapshots()
        {
            return _operationStats.Values
                .Select(x => new MetadataProviderOperationTelemetrySnapshot
                {
                    ProviderName = x.ProviderName,
                    OperationName = x.OperationName,
                    Calls = x.Calls,
                    Successes = x.Successes,
                    Failures = x.Failures,
                    NullResults = x.NullResults,
                    FallbackHits = x.FallbackHits,
                    TotalLatencyMs = x.TotalLatencyMs
                })
                .OrderBy(x => x.ProviderName)
                .ThenBy(x => x.OperationName)
                .ToList();
        }

        private static void RecordStats(ProviderStats stat, long latencyMs, bool success, bool returnedNull, bool fallbackHit)
        {
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
    }
}
