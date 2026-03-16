using System;
using System.Collections.Generic;
using System.Linq;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Books;

namespace NzbDrone.Core.MetadataSource
{
    public class MetadataProviderBookCandidate
    {
        public string ProviderName { get; set; }
        public Book Book { get; set; }
        public int QualityScore { get; set; }

        public bool HasCover
        {
            get
            {
                return Book?.Editions?.Value?.Any(e => e.Images != null && e.Images.Any()) ?? false;
            }
        }
    }

    public class MetadataConflictResolutionDecision
    {
        public Book SelectedBook { get; set; }
        public string SelectedProvider { get; set; }
        public string ResolutionReason { get; set; }
        public string TieBreakReason { get; set; }
        public bool SelectedHasCover { get; set; }
        public bool UsedProviderPrecedence { get; set; }
        public DateTime ResolvedAtUtc { get; set; }
        public int CandidateCount { get; set; }
        public List<string> EvaluatedProviders { get; set; }
        public Dictionary<string, int> ProviderScores { get; set; }

        public MetadataConflictResolutionDecision()
        {
            EvaluatedProviders = new List<string>();
            ProviderScores = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            ResolvedAtUtc = DateTime.UtcNow;
        }
    }

    public interface IMetadataConflictResolutionPolicy
    {
        MetadataConflictResolutionDecision ResolveBookConflict(IEnumerable<MetadataProviderBookCandidate> candidates, string preferredProvider = null);
    }

    public class MetadataConflictResolutionPolicy : IMetadataConflictResolutionPolicy
    {
        private readonly IMetadataConflictTelemetryService _telemetryService;
        private readonly Logger _logger;

        public MetadataConflictResolutionPolicy(IMetadataConflictTelemetryService telemetryService, Logger logger)
        {
            _telemetryService = telemetryService;
            _logger = logger;
        }

        private static readonly Dictionary<string, int> ProviderPrecedence = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["OpenLibrary"] = 10,
            ["Inventaire"] = 20,
            ["GoogleBooks"] = 30,
            ["Hardcover"] = 40,
            ["Goodreads"] = 90
        };

        public MetadataConflictResolutionDecision ResolveBookConflict(IEnumerable<MetadataProviderBookCandidate> candidates, string preferredProvider = null)
        {
            var decision = new MetadataConflictResolutionDecision();
            var normalized = (candidates ?? new List<MetadataProviderBookCandidate>())
                .Where(c => c != null && c.Book != null && c.ProviderName.IsNotNullOrWhiteSpace())
                .ToList();

            if (!normalized.Any())
            {
                decision.ResolutionReason = "no-candidates";
                decision.CandidateCount = 0;
                EmitDecisionTelemetry(decision, "resolve-book-conflict");
                return decision;
            }

            decision.CandidateCount = normalized.Count;
            decision.EvaluatedProviders = normalized.Select(c => c.ProviderName).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            foreach (var candidate in normalized)
            {
                decision.ProviderScores[candidate.ProviderName] = candidate.QualityScore;
            }

            if (preferredProvider.IsNotNullOrWhiteSpace())
            {
                var preferred = normalized
                    .Where(c => c.ProviderName.Equals(preferredProvider, StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(c => c.QualityScore)
                    .ThenBy(c => GetProviderPrecedence(c.ProviderName))
                    .FirstOrDefault();

                if (preferred != null)
                {
                    return FinalizeAndEmit(decision, preferred, "preferred-provider", null, false);
                }
            }

            var highestScore = normalized.Max(c => c.QualityScore);
            var topCandidates = normalized.Where(c => c.QualityScore == highestScore).ToList();

            if (topCandidates.Count == 1)
            {
                return FinalizeAndEmit(decision, topCandidates[0], "quality-score", null, false);
            }

            var withCover = topCandidates.Where(c => c.HasCover).ToList();
            if (withCover.Any())
            {
                var selectedWithCover = withCover
                    .OrderBy(c => GetProviderPrecedence(c.ProviderName))
                    .ThenBy(c => c.ProviderName, StringComparer.OrdinalIgnoreCase)
                    .First();

                return FinalizeAndEmit(decision, selectedWithCover, "tie-break", "cover-availability-then-provider-precedence", true);
            }

            var selected = topCandidates
                .OrderBy(c => GetProviderPrecedence(c.ProviderName))
                .ThenBy(c => c.ProviderName, StringComparer.OrdinalIgnoreCase)
                .First();

            return FinalizeAndEmit(decision, selected, "tie-break", "provider-precedence", true);
        }

        private MetadataConflictResolutionDecision FinalizeAndEmit(MetadataConflictResolutionDecision decision,
                                                                   MetadataProviderBookCandidate selected,
                                                                   string resolutionReason,
                                                                   string tieBreakReason,
                                                                   bool usedProviderPrecedence)
        {
            var finalized = FinalizeDecision(decision, selected, resolutionReason, tieBreakReason, usedProviderPrecedence);
            EmitDecisionTelemetry(finalized, "resolve-book-conflict");
            return finalized;
        }

        private void EmitDecisionTelemetry(MetadataConflictResolutionDecision decision, string operation)
        {
            _telemetryService.RecordDecision(operation, decision);

            var scoreSummary = decision.ProviderScores == null || decision.ProviderScores.Count == 0
                ? "none"
                : string.Join(",", decision.ProviderScores.OrderBy(x => x.Key).Select(x => $"{x.Key}:{x.Value}"));

            _logger.Info(
                "Metadata conflict decision: operation={0}, selectedProvider={1}, reason={2}, tieBreak={3}, candidateCount={4}, scores={5}",
                operation,
                decision.SelectedProvider ?? "none",
                decision.ResolutionReason ?? "none",
                decision.TieBreakReason ?? "none",
                decision.CandidateCount,
                scoreSummary);
        }

        private static MetadataConflictResolutionDecision FinalizeDecision(MetadataConflictResolutionDecision decision,
                                                                           MetadataProviderBookCandidate selected,
                                                                           string resolutionReason,
                                                                           string tieBreakReason,
                                                                           bool usedProviderPrecedence)
        {
            decision.SelectedBook = selected.Book;
            decision.SelectedProvider = selected.ProviderName;
            decision.SelectedHasCover = selected.HasCover;
            decision.ResolutionReason = resolutionReason;
            decision.TieBreakReason = tieBreakReason;
            decision.UsedProviderPrecedence = usedProviderPrecedence;
            decision.ResolvedAtUtc = DateTime.UtcNow;
            return decision;
        }

        private static int GetProviderPrecedence(string providerName)
        {
            return ProviderPrecedence.TryGetValue(providerName ?? string.Empty, out var value)
                ? value
                : 100;
        }
    }
}
