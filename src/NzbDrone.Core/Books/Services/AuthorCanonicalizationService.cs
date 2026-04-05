using System;
using System.Collections.Generic;
using System.Linq;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Books.Commands;
using NzbDrone.Core.History;
using NzbDrone.Core.ImportLists.Exclusions;
using NzbDrone.Core.Messaging.Commands;

namespace NzbDrone.Core.Books
{
    public interface IAuthorCanonicalizationService
    {
        Author TryFindCanonicalMatch(Author incomingAuthor, out double confidence, out string reason, double minConfidence = 0.92);

        CanonicalizationSummary CanonicalizeDuplicates(bool dryRun, double minConfidence, int maxMerges);
    }

    public class CanonicalizationSummary
    {
        public int DuplicateGroupsEvaluated { get; set; }
        public int CandidatesConsidered { get; set; }
        public int MergesPerformed { get; set; }
        public int MergesSkipped { get; set; }
    }

    public class AuthorCanonicalizationService : IAuthorCanonicalizationService, IExecute<CanonicalizeAuthorsCommand>
    {
        private readonly IAuthorService _authorService;
        private readonly IBookService _bookService;
        private readonly IHistoryService _historyService;
        private readonly IImportListExclusionService _importListExclusionService;
        private readonly Logger _logger;

        public AuthorCanonicalizationService(IAuthorService authorService,
                                             IBookService bookService,
                                             IHistoryService historyService,
                                             IImportListExclusionService importListExclusionService,
                                             Logger logger)
        {
            _authorService = authorService;
            _bookService = bookService;
            _historyService = historyService;
            _importListExclusionService = importListExclusionService;
            _logger = logger;
        }

        public Author TryFindCanonicalMatch(Author incomingAuthor, out double confidence, out string reason, double minConfidence = 0.92)
        {
            confidence = 0;
            reason = null;

            if (incomingAuthor?.Metadata?.Value == null)
            {
                return null;
            }

            var metadata = incomingAuthor.Metadata.Value;

            if (metadata.ForeignAuthorId.IsNotNullOrWhiteSpace())
            {
                var direct = _authorService.FindById(metadata.ForeignAuthorId);
                if (direct != null)
                {
                    confidence = 1.0;
                    reason = "foreign-author-id match";
                    return direct;
                }
            }

            var authors = _authorService.GetAllAuthors();
            Author best = null;
            var bestScore = 0d;
            var bestReason = string.Empty;

            foreach (var candidate in authors)
            {
                var score = ComputeConfidence(metadata, candidate?.Metadata?.Value, out var scoreReason);

                if (score > bestScore)
                {
                    bestScore = score;
                    best = candidate;
                    bestReason = scoreReason;
                }
            }

            if (best != null && bestScore >= minConfidence)
            {
                confidence = bestScore;
                reason = bestReason;
                return best;
            }

            return null;
        }

        public CanonicalizationSummary CanonicalizeDuplicates(bool dryRun, double minConfidence, int maxMerges)
        {
            var summary = new CanonicalizationSummary();
            var authors = _authorService.GetAllAuthors();

            var grouped = authors
                .Where(a => a?.Metadata?.Value != null)
                .GroupBy(a => NormalizeName(a.Metadata.Value.Name), StringComparer.Ordinal)
                .Where(g => g.Key.IsNotNullOrWhiteSpace() && g.Count() > 1)
                .ToList();

            summary.DuplicateGroupsEvaluated = grouped.Count;

            foreach (var group in grouped)
            {
                var primary = SelectCanonical(group.ToList());
                if (primary == null)
                {
                    continue;
                }

                foreach (var duplicate in group.Where(a => a.Id != primary.Id))
                {
                    summary.CandidatesConsidered++;

                    var score = ComputeConfidence(primary.Metadata.Value, duplicate.Metadata.Value, out var reason);
                    if (score < minConfidence)
                    {
                        summary.MergesSkipped++;
                        continue;
                    }

                    if (summary.MergesPerformed >= maxMerges)
                    {
                        _logger.Warn("Author canonicalization reached max merge cap ({0})", maxMerges);
                        return summary;
                    }

                    if (dryRun)
                    {
                        summary.MergesPerformed++;
                        _logger.Info("[DRY-RUN] Candidate author merge: keep {0}, merge {1}, confidence={2:0.000}, reason={3}", primary, duplicate, score, reason);
                        continue;
                    }

                    MergeIntoPrimary(primary, duplicate);
                    summary.MergesPerformed++;
                    _logger.Info("Merged duplicate author {0} into canonical {1} (confidence={2:0.000}, reason={3})", duplicate, primary, score, reason);
                }
            }

            return summary;
        }

        public void Execute(CanonicalizeAuthorsCommand message)
        {
            var summary = CanonicalizeDuplicates(
                message?.DryRun ?? true,
                Math.Max(0, message?.MinConfidence ?? 0.95),
                Math.Max(1, message?.MaxMerges ?? 200));

            _logger.Info("Author canonicalization summary: groups={0}, candidates={1}, merges={2}, skipped={3}",
                summary.DuplicateGroupsEvaluated,
                summary.CandidatesConsidered,
                summary.MergesPerformed,
                summary.MergesSkipped);
        }

        private void MergeIntoPrimary(Author primary, Author duplicate)
        {
            if (primary == null || duplicate == null || primary.Id == duplicate.Id)
            {
                return;
            }

            var books = _bookService.GetBooksByAuthor(duplicate.Id);
            if (books.Any())
            {
                books.ForEach(x => x.AuthorMetadataId = primary.AuthorMetadataId);
                _bookService.UpdateMany(books);
            }

            var historyItems = _historyService.GetByAuthor(duplicate.Id, null);
            if (historyItems.Any())
            {
                historyItems.ForEach(x => x.AuthorId = primary.Id);
                _historyService.UpdateMany(historyItems);
            }

            var duplicateForeignId = duplicate.Metadata?.Value?.ForeignAuthorId;
            if (duplicateForeignId.IsNotNullOrWhiteSpace())
            {
                var duplicateExclusion = _importListExclusionService.FindByForeignId(duplicateForeignId);
                if (duplicateExclusion != null)
                {
                    var primaryForeignId = primary.Metadata?.Value?.ForeignAuthorId;
                    var primaryExclusion = primaryForeignId.IsNotNullOrWhiteSpace()
                        ? _importListExclusionService.FindByForeignId(primaryForeignId)
                        : null;

                    if (primaryExclusion == null && primaryForeignId.IsNotNullOrWhiteSpace())
                    {
                        duplicateExclusion.ForeignId = primaryForeignId;
                        duplicateExclusion.Name = primary.Metadata?.Value?.Name ?? duplicateExclusion.Name;
                        _importListExclusionService.Update(duplicateExclusion);
                    }
                    else
                    {
                        _importListExclusionService.Delete(duplicateExclusion.Id);
                    }
                }
            }

            _authorService.DeleteAuthor(duplicate.Id, false, false);
        }

        private static Author SelectCanonical(List<Author> candidates)
        {
            return candidates
                .OrderByDescending(a => a.Books?.Value?.Count ?? 0)
                .ThenBy(a => a.Added)
                .ThenBy(a => a.Id)
                .FirstOrDefault();
        }

        private static double ComputeConfidence(AuthorMetadata incoming, AuthorMetadata existing, out string reason)
        {
            reason = "none";

            if (incoming == null || existing == null)
            {
                return 0;
            }

            if (incoming.ForeignAuthorId.IsNotNullOrWhiteSpace() &&
                existing.ForeignAuthorId.IsNotNullOrWhiteSpace() &&
                incoming.ForeignAuthorId.Equals(existing.ForeignAuthorId, StringComparison.OrdinalIgnoreCase))
            {
                reason = "foreign-author-id";
                return 1.0;
            }

            var incomingName = NormalizeName(incoming.Name);
            var existingName = NormalizeName(existing.Name);

            if (incomingName.IsNullOrWhiteSpace() || existingName.IsNullOrWhiteSpace())
            {
                return 0;
            }

            var score = 0d;

            if (incomingName == existingName)
            {
                score += 0.72;
                reason = "normalized-name-equal";
            }

            var fuzzy = incomingName.FuzzyMatch(existingName);
            if (fuzzy >= 0.98)
            {
                score += 0.20;
                reason += "+fuzzy-0.98";
            }
            else if (fuzzy >= 0.94)
            {
                score += 0.12;
                reason += "+fuzzy-0.94";
            }

            var incomingAliases = NormalizeAliasSet(incoming);
            var existingAliases = NormalizeAliasSet(existing);

            if (incomingAliases.Overlaps(existingAliases))
            {
                score += 0.12;
                reason += "+alias-overlap";
            }

            if (incoming.Disambiguation.IsNotNullOrWhiteSpace() &&
                existing.Disambiguation.IsNotNullOrWhiteSpace() &&
                incoming.Disambiguation.Equals(existing.Disambiguation, StringComparison.OrdinalIgnoreCase))
            {
                score += 0.06;
                reason += "+disambiguation";
            }

            return Math.Min(1.0, score);
        }

        private static HashSet<string> NormalizeAliasSet(AuthorMetadata metadata)
        {
            var aliases = new HashSet<string>(StringComparer.Ordinal);

            if (metadata == null)
            {
                return aliases;
            }

            aliases.Add(NormalizeName(metadata.Name));

            foreach (var alias in metadata.Aliases ?? new List<string>())
            {
                aliases.Add(NormalizeName(alias));
            }

            return new HashSet<string>(aliases.Where(a => a.IsNotNullOrWhiteSpace()), StringComparer.Ordinal);
        }

        private static string NormalizeName(string value)
        {
            if (value.IsNullOrWhiteSpace())
            {
                return null;
            }

            var chars = value.Trim().ToLowerInvariant().Where(c => char.IsLetterOrDigit(c) || char.IsWhiteSpace(c)).ToArray();
            return string.Join(" ", new string(chars).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries));
        }
    }
}
