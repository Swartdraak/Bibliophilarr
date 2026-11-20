using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Books.Commands;
using NzbDrone.Core.History;
using NzbDrone.Core.ImportLists.Exclusions;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.Parser;

namespace NzbDrone.Core.Books
{
    public interface IAuthorCanonicalizationService
    {
        Author TryFindCanonicalMatch(Author incomingAuthor, out double confidence, out string reason, double minConfidence = 0.92);

        Author TryReuseAuthorOnAdd(Author incoming, IReadOnlyList<Book> bibliography, IReadOnlyList<(Author Author, IReadOnlyList<Book> Bibliography)> pending);

        bool VerifyDistinctAuthorities(Author first, Author second);

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
        internal const string BibliographyEvidence = "two-isbn-title-author-work-witnesses";
        private const int MaxAuthorCandidates = 3;
        private const int MaxWitnessPairs = 6;
        private readonly IAuthorService _authorService;
        private readonly IBookService _bookService;
        private readonly IHistoryService _historyService;
        private readonly IImportListExclusionService _importListExclusionService;
        private readonly ICatalogueIdentityRepository _catalogueIdentity;
        private readonly IMetadataProviderOrchestrator _metadata;
        private readonly Logger _logger;

        public AuthorCanonicalizationService(IAuthorService authorService,
                                             IBookService bookService,
                                             IHistoryService historyService,
                                             IImportListExclusionService importListExclusionService,
                                             ICatalogueIdentityRepository catalogueIdentity,
                                             IMetadataProviderOrchestrator metadata,
                                             Logger logger)
        {
            _authorService = authorService;
            _bookService = bookService;
            _historyService = historyService;
            _importListExclusionService = importListExclusionService;
            _catalogueIdentity = catalogueIdentity;
            _metadata = metadata;
            _logger = logger;
        }

        public Author TryReuseAuthorOnAdd(Author incoming, IReadOnlyList<Book> bibliography, IReadOnlyList<(Author Author, IReadOnlyList<Book> Bibliography)> pending)
        {
            var existing = _authorService.FindById(incoming.ForeignAuthorId);
            if (existing != null)
            {
                return existing;
            }

            var source = GetAuthority(incoming);
            var staged = pending.FirstOrDefault(candidate =>
                candidate.Author.ForeignAuthorId == incoming.ForeignAuthorId ||
                (source != null && candidate.Author.Metadata.Value.PendingSourceAssociations.Any(alias =>
                    alias.Provider == source.Provider && alias.ProviderId == source.ProviderId)));
            if (staged.Author != null || source == null)
            {
                return staged.Author;
            }

            // A name is only a bounded lookup hint. It contributes no identity evidence.
            var name = NormalizeName(incoming.Name);
            if (name == null)
            {
                return null;
            }

            var candidates = _authorService.GetAuthorsByCleanName(incoming.Name.CleanAuthorName())
                .Select(author => (Author: author, Bibliography: (IReadOnlyList<Book>)null))
                .Concat(pending)
                .Where(candidate => NormalizeName(candidate.Author.Name) == name &&
                    GetAuthority(candidate.Author) is { } authority && authority.Provider != source.Provider)
                .Take(MaxAuthorCandidates + 1)
                .ToList();
            if (candidates.Count == 0 || candidates.Count > MaxAuthorCandidates)
            {
                return null;
            }

            bibliography ??= LoadBibliography(incoming);
            var matches = candidates.Where(candidate => SharesVerifiedBibliography(
                incoming, bibliography, candidate.Author, candidate.Bibliography ?? LoadBibliography(candidate.Author))).Take(2).ToList();
            if (matches.Count > 1)
            {
                throw new InvalidOperationException("More than one catalogue author has corroborated work identity.");
            }

            var incumbent = matches.FirstOrDefault().Author;
            if (incumbent == null)
            {
                return null;
            }

            if (incumbent.Id == 0)
            {
                source.Evidence = BibliographyEvidence;
                incumbent.Metadata.Value.PendingSourceAssociations.Add(source);
                return incumbent;
            }

            if (_catalogueIdentity.TryAddSource(CatalogueEntityKind.Author, incumbent.AuthorMetadataId, source.Provider, source.ProviderId, BibliographyEvidence) ||
                _catalogueIdentity.FindByProviderId(CatalogueEntityKind.Author, source.Provider, source.ProviderId) == incumbent.AuthorMetadataId)
            {
                _logger.Info("Reusing catalogue author {0} after corroborating two independently credited works", incumbent.Id);
                return incumbent;
            }

            throw new InvalidOperationException("The corroborated author source has a different catalogue owner.");
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
                else if (score == bestScore)
                {
                    best = null;
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

        public bool VerifyDistinctAuthorities(Author first, Author second)
        {
            var left = GetAuthority(first);
            var right = GetAuthority(second);
            return left != null && right != null && left.Provider != right.Provider &&
                   SharesVerifiedBibliography(first, LoadBibliography(first), second, LoadBibliography(second));
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
                    if (score <= 0 || score < minConfidence)
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

        private IReadOnlyList<Book> LoadBibliography(Author authority)
        {
            var remote = _metadata.GetAuthorInfo(authority.ForeignAuthorId);
            var expected = GetAuthority(authority);
            var actual = GetAuthority(remote);
            return actual?.Provider == expected.Provider && actual.ProviderId == expected.ProviderId ? remote.Books?.Value : null;
        }

        private bool SharesVerifiedBibliography(Author first, IReadOnlyList<Book> firstBooks, Author second, IReadOnlyList<Book> secondBooks)
        {
            var counter = GetWitnessSuggestions(secondBooks, GetAuthority(second).Provider);
            var verifiedFirst = new HashSet<string>(StringComparer.Ordinal);
            var verifiedSecond = new HashSet<string>(StringComparer.Ordinal);
            var verifiedTitles = new HashSet<string>(StringComparer.Ordinal);
            var pairs = GetWitnessSuggestions(firstBooks, GetAuthority(first).Provider)
                .Join(counter, left => (left.Isbn, left.Title), right => (right.Isbn, right.Title), (left, right) => (First: left, Second: right))
                .OrderBy(pair => pair.First.Isbn, StringComparer.Ordinal)
                .GroupBy(pair => pair.First.Title)
                .SelectMany(group => group.Select((pair, index) => (Pair: pair, Rank: index)))
                .OrderBy(item => item.Rank).ThenBy(item => item.Pair.First.Isbn, StringComparer.Ordinal)
                .Take(MaxWitnessPairs).Select(item => item.Pair);
            foreach (var pair in pairs)
            {
                if (verifiedFirst.Contains(pair.First.WorkId) || verifiedSecond.Contains(pair.Second.WorkId) || verifiedTitles.Contains(pair.First.Title))
                {
                    continue;
                }

                var leftTitle = _metadata.GetVerifiedAuthorWorkTitle(first.ForeignAuthorId, pair.First.WorkId, pair.First.Isbn);
                if (NormalizeTitle(leftTitle) != pair.First.Title)
                {
                    continue;
                }

                var rightTitle = _metadata.GetVerifiedAuthorWorkTitle(second.ForeignAuthorId, pair.Second.WorkId, pair.Second.Isbn);
                if (NormalizeTitle(rightTitle) != pair.Second.Title)
                {
                    continue;
                }

                verifiedFirst.Add(pair.First.WorkId);
                verifiedSecond.Add(pair.Second.WorkId);
                verifiedTitles.Add(pair.First.Title);
                if (verifiedTitles.Count == 2)
                {
                    return true;
                }
            }

            return false;
        }

        private static IEnumerable<(string WorkId, string Isbn, string Title)> GetWitnessSuggestions(IEnumerable<Book> books, string authority)
        {
            return (books ?? Enumerable.Empty<Book>())
                .Select(book => (Book: book, Work: CatalogueIdentityRepository.ParseLegacySource(CatalogueEntityKind.Work, book?.ForeignBookId, null)))
                .Where(item => item.Work?.Provider == authority && !string.IsNullOrWhiteSpace(item.Book.Title) && item.Book.Editions?.Value != null)
                .SelectMany(item => item.Book.Editions.Value.Select(edition =>
                    (WorkId: $"{item.Work.Provider.ToLowerInvariant()}:work:{item.Work.ProviderId}", Isbn: Isbn.AsIsbn13(edition?.Isbn13), Title: NormalizeTitle(item.Book.Title))))
                .Where(item => item.Isbn != null)
                .Distinct()
                .GroupBy(item => item.Isbn)
                .Where(group => group.Count() == 1)
                .Select(group => group.Single());
        }

        private static string NormalizeTitle(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? null : string.Join(" ", value.Normalize(NormalizationForm.FormKC)
                .Split((char[])null, StringSplitOptions.RemoveEmptyEntries)).ToUpperInvariant();
        }

        private static CatalogueSourceAssociation GetAuthority(Author author)
        {
            return CatalogueIdentityRepository.ParseLegacySource(CatalogueEntityKind.Author, author?.Metadata?.Value?.ForeignAuthorId, null);
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

            const CatalogueEntityKind kind = CatalogueEntityKind.Author;
            var incomingSource = CatalogueIdentityRepository.ParseLegacySource(kind, incoming.ForeignAuthorId, null);
            var existingSource = CatalogueIdentityRepository.ParseLegacySource(kind, existing.ForeignAuthorId, null);
            if (incomingSource != null && incomingSource.Provider == existingSource?.Provider)
            {
                if (incomingSource.ProviderId == existingSource.ProviderId)
                {
                    reason = "provider-author-id";
                    return 1.0;
                }

                return 0;
            }

            var incomingLink = GetOpenLibraryIdentity(incoming, incomingSource);
            var existingLink = GetOpenLibraryIdentity(existing, existingSource);
            if (incomingLink != null && incomingLink == existingLink && NormalizeName(incoming.Name) == NormalizeName(existing.Name))
            {
                reason = "shared-openlibrary-author-id";
                return 1.0;
            }

            return 0;
        }

        private static string GetOpenLibraryIdentity(AuthorMetadata metadata, CatalogueSourceAssociation source)
        {
            return source?.Provider == "OpenLibrary"
                ? source.ProviderId
                : CatalogueIdentityRepository.NormalizeSource(CatalogueEntityKind.Author, "OpenLibrary", metadata.OpenLibraryAuthorId, null)?.ProviderId;
        }

        internal static string NormalizeName(string value)
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
