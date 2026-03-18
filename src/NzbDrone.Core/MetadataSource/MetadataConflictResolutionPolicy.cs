using System;
using System.Collections.Generic;
using System.Linq;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Books;
using NzbDrone.Core.Configuration;

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
        public Dictionary<string, string> FieldSelections { get; set; }

        public MetadataConflictResolutionDecision()
        {
            EvaluatedProviders = new List<string>();
            ProviderScores = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            FieldSelections = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            ResolvedAtUtc = DateTime.UtcNow;
        }
    }

    public enum MetadataConflictField
    {
        Title,
        Subtitle,
        AuthorIdentity,
        Identifiers,
        PublicationDate,
        Language,
        CoverLinks
    }

    public class MetadataFieldPrecedenceMatrix
    {
        public Dictionary<MetadataConflictField, IReadOnlyList<string>> ProviderOrderByField { get; }

        public MetadataFieldPrecedenceMatrix(Dictionary<MetadataConflictField, IReadOnlyList<string>> providerOrderByField)
        {
            ProviderOrderByField = providerOrderByField ?? new Dictionary<MetadataConflictField, IReadOnlyList<string>>();
        }

        public IReadOnlyList<string> GetProviderOrder(MetadataConflictField field)
        {
            return ProviderOrderByField.TryGetValue(field, out var order)
                ? order
                : Array.Empty<string>();
        }

        public static MetadataFieldPrecedenceMatrix CreateDefault()
        {
            return new MetadataFieldPrecedenceMatrix(new Dictionary<MetadataConflictField, IReadOnlyList<string>>
            {
                [MetadataConflictField.Title] = new[] { "Inventaire", "OpenLibrary", "GoogleBooks", "Hardcover", "OpenLibrary" },
                [MetadataConflictField.Subtitle] = new[] { "OpenLibrary", "Inventaire", "GoogleBooks", "Hardcover", "OpenLibrary" },
                [MetadataConflictField.AuthorIdentity] = new[] { "Inventaire", "OpenLibrary", "GoogleBooks", "Hardcover", "OpenLibrary" },
                [MetadataConflictField.Identifiers] = new[] { "OpenLibrary", "Inventaire", "GoogleBooks", "Hardcover", "OpenLibrary" },
                [MetadataConflictField.PublicationDate] = new[] { "OpenLibrary", "Inventaire", "GoogleBooks", "Hardcover", "OpenLibrary" },
                [MetadataConflictField.Language] = new[] { "OpenLibrary", "Inventaire", "GoogleBooks", "Hardcover", "OpenLibrary" },
                [MetadataConflictField.CoverLinks] = new[] { "Inventaire", "OpenLibrary", "GoogleBooks", "Hardcover", "OpenLibrary" }
            });
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
        private readonly IConfigService _configService;

        public MetadataConflictResolutionPolicy(IMetadataConflictTelemetryService telemetryService, Logger logger, IConfigService configService)
        {
            _telemetryService = telemetryService;
            _logger = logger;
            _configService = configService;
        }

        private static readonly Dictionary<string, int> ProviderPrecedence = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["OpenLibrary"] = 10,
            ["Inventaire"] = 20,
            ["GoogleBooks"] = 30,
            ["Hardcover"] = 40,
            ["OpenLibrary"] = 90
        };

        private static readonly MetadataFieldPrecedenceMatrix FieldPrecedenceMatrix = MetadataFieldPrecedenceMatrix.CreateDefault();

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
                    return FinalizeAndEmit(decision, normalized, preferred, "preferred-provider", null, false);
                }
            }

            var highestScore = normalized.Max(c => c.QualityScore);
            var topCandidates = normalized.Where(c => c.QualityScore == highestScore).ToList();

            if (topCandidates.Count == 1)
            {
                return FinalizeAndEmit(decision, normalized, topCandidates[0], "quality-score", null, false);
            }

            if (_configService.EnableMetadataConflictStrategyVariants)
            {
                var selectedByVariant = topCandidates
                    .OrderBy(c => GetProviderPrecedence(c.ProviderName))
                    .ThenBy(c => c.ProviderName, StringComparer.OrdinalIgnoreCase)
                    .First();

                return FinalizeAndEmit(decision, normalized, selectedByVariant, "tie-break", "experimental-provider-precedence-only", true);
            }

            var withCover = topCandidates.Where(c => c.HasCover).ToList();
            if (withCover.Any())
            {
                var selectedWithCover = withCover
                    .OrderBy(c => GetProviderPrecedence(c.ProviderName))
                    .ThenBy(c => c.ProviderName, StringComparer.OrdinalIgnoreCase)
                    .First();

                return FinalizeAndEmit(decision, normalized, selectedWithCover, "tie-break", "cover-availability-then-provider-precedence", true);
            }

            var selected = topCandidates
                .OrderBy(c => GetProviderPrecedence(c.ProviderName))
                .ThenBy(c => c.ProviderName, StringComparer.OrdinalIgnoreCase)
                .First();

            return FinalizeAndEmit(decision, normalized, selected, "tie-break", "provider-precedence", true);
        }

        private MetadataConflictResolutionDecision FinalizeAndEmit(MetadataConflictResolutionDecision decision,
                                                                   List<MetadataProviderBookCandidate> allCandidates,
                                                                   MetadataProviderBookCandidate selected,
                                                                   string resolutionReason,
                                                                   string tieBreakReason,
                                                                   bool usedProviderPrecedence)
        {
            var finalized = FinalizeDecision(decision, selected, resolutionReason, tieBreakReason, usedProviderPrecedence);

            if (selected != null)
            {
                finalized.FieldSelections = ResolveFieldSelections(allCandidates, selected);
            }

            EmitDecisionTelemetry(finalized, "resolve-book-conflict");
            return finalized;
        }

        private static Dictionary<string, string> ResolveFieldSelections(List<MetadataProviderBookCandidate> allCandidates,
                                                                          MetadataProviderBookCandidate selected)
        {
            var selections = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var candidates = (allCandidates ?? new List<MetadataProviderBookCandidate>())
                .Where(c => c != null && c.Book != null && c.ProviderName.IsNotNullOrWhiteSpace())
                .ToList();

            if (!candidates.Any() || selected == null)
            {
                return selections;
            }

            selections[GetFieldKey(MetadataConflictField.Title)] = SelectProviderForField(candidates, selected, MetadataConflictField.Title, c => c.Book?.Title.IsNotNullOrWhiteSpace() == true);
            selections[GetFieldKey(MetadataConflictField.Subtitle)] = SelectProviderForField(candidates, selected, MetadataConflictField.Subtitle, c => GetPrimaryEdition(c.Book)?.Disambiguation.IsNotNullOrWhiteSpace() == true);
            selections[GetFieldKey(MetadataConflictField.AuthorIdentity)] = SelectProviderForField(candidates, selected, MetadataConflictField.AuthorIdentity, c => c.Book?.AuthorMetadata?.Value?.ForeignAuthorId.IsNotNullOrWhiteSpace() == true || c.Book?.AuthorMetadata?.Value?.Name.IsNotNullOrWhiteSpace() == true);
            selections[GetFieldKey(MetadataConflictField.Identifiers)] = SelectProviderForField(candidates, selected, MetadataConflictField.Identifiers, c => HasIdentifiers(c.Book));
            selections[GetFieldKey(MetadataConflictField.PublicationDate)] = SelectProviderForField(candidates, selected, MetadataConflictField.PublicationDate, c => HasPublicationDate(c.Book));
            selections[GetFieldKey(MetadataConflictField.Language)] = SelectProviderForField(candidates, selected, MetadataConflictField.Language, c => GetPrimaryEdition(c.Book)?.Language.IsNotNullOrWhiteSpace() == true);
            selections[GetFieldKey(MetadataConflictField.CoverLinks)] = SelectProviderForField(candidates, selected, MetadataConflictField.CoverLinks, c => c.HasCover);

            return selections;
        }

        private static string SelectProviderForField(List<MetadataProviderBookCandidate> candidates,
                                                     MetadataProviderBookCandidate selected,
                                                     MetadataConflictField field,
                                                     Func<MetadataProviderBookCandidate, bool> hasFieldValue)
        {
            var withValue = candidates.Where(hasFieldValue).ToList();
            if (!withValue.Any())
            {
                return selected.ProviderName;
            }

            var preferredOrder = FieldPrecedenceMatrix.GetProviderOrder(field);
            if (preferredOrder.Any())
            {
                foreach (var providerName in preferredOrder)
                {
                    var exact = withValue.FirstOrDefault(c => c.ProviderName.Equals(providerName, StringComparison.OrdinalIgnoreCase));
                    if (exact != null)
                    {
                        return exact.ProviderName;
                    }
                }
            }

            return withValue
                .OrderBy(c => GetProviderPrecedence(c.ProviderName))
                .ThenBy(c => c.ProviderName, StringComparer.OrdinalIgnoreCase)
                .First()
                .ProviderName;
        }

        private static Edition GetPrimaryEdition(Book book)
        {
            return book?.Editions?.Value?.FirstOrDefault();
        }

        private static bool HasIdentifiers(Book book)
        {
            var edition = GetPrimaryEdition(book);
            return book?.ForeignBookId.IsNotNullOrWhiteSpace() == true ||
                   edition?.Isbn13.IsNotNullOrWhiteSpace() == true ||
                   edition?.Asin.IsNotNullOrWhiteSpace() == true;
        }

        private static bool HasPublicationDate(Book book)
        {
            return book?.ReleaseDate != null || GetPrimaryEdition(book)?.ReleaseDate != null;
        }

        private static string GetFieldKey(MetadataConflictField field)
        {
            return field switch
            {
                MetadataConflictField.Title => "title",
                MetadataConflictField.Subtitle => "subtitle",
                MetadataConflictField.AuthorIdentity => "author-identity",
                MetadataConflictField.Identifiers => "identifiers",
                MetadataConflictField.PublicationDate => "publication-date",
                MetadataConflictField.Language => "language",
                MetadataConflictField.CoverLinks => "cover-links",
                _ => "unknown"
            };
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
