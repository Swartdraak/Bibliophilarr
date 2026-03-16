using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using NLog;

namespace NzbDrone.Core.MetadataSource
{
    public interface IMetadataConflictTelemetryService
    {
        void RecordDecision(string operation, MetadataConflictResolutionDecision decision);

        MetadataConflictTelemetrySnapshot GetSnapshot();
    }

    public class MetadataConflictTelemetrySnapshot
    {
        public int TotalDecisions { get; set; }

        public Dictionary<string, int> DecisionsByReason { get; set; }

        public Dictionary<string, int> DecisionsByProvider { get; set; }

        public MetadataConflictTelemetrySnapshot()
        {
            DecisionsByReason = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            DecisionsByProvider = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        }
    }

    public class MetadataConflictTelemetryService : IMetadataConflictTelemetryService
    {
        private readonly ConcurrentDictionary<string, int> _decisionsByReason;
        private readonly ConcurrentDictionary<string, int> _decisionsByProvider;
        private readonly Logger _logger;

        public MetadataConflictTelemetryService(Logger logger)
        {
            _decisionsByReason = new ConcurrentDictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            _decisionsByProvider = new ConcurrentDictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            _logger = logger;
        }

        public void RecordDecision(string operation, MetadataConflictResolutionDecision decision)
        {
            if (decision == null)
            {
                return;
            }

            var reason = decision.ResolutionReason ?? "unknown";
            var provider = decision.SelectedProvider ?? "none";

            _decisionsByReason.AddOrUpdate(reason, 1, (_, current) => current + 1);
            _decisionsByProvider.AddOrUpdate(provider, 1, (_, current) => current + 1);

            _logger.Debug(
                "Metadata conflict telemetry: operation={0}, provider={1}, reason={2}, tieBreak={3}, candidateCount={4}",
                operation,
                provider,
                reason,
                decision.TieBreakReason ?? "none",
                decision.CandidateCount);
        }

        public MetadataConflictTelemetrySnapshot GetSnapshot()
        {
            var snapshot = new MetadataConflictTelemetrySnapshot
            {
                TotalDecisions = 0
            };

            foreach (var pair in _decisionsByReason)
            {
                snapshot.DecisionsByReason[pair.Key] = pair.Value;
                snapshot.TotalDecisions += pair.Value;
            }

            foreach (var pair in _decisionsByProvider)
            {
                snapshot.DecisionsByProvider[pair.Key] = pair.Value;
            }

            return snapshot;
        }
    }
}
