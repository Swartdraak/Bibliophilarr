using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Http;
using NzbDrone.Core.Books.Commands;
using NzbDrone.Core.Exceptions;
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
        private readonly ICatalogueIdentityRepository _catalogueIdentity;
        private readonly IMetadataProviderOrchestrator _metadata;
        private readonly Logger _logger;

        public AuthorCanonicalizationService(IAuthorService authorService,
                                             ICatalogueIdentityRepository catalogueIdentity,
                                             IMetadataProviderOrchestrator metadata,
                                             Logger logger)
        {
            _authorService = authorService;
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

            var soleCandidate = candidates.Count == 1;
            if (!soleCandidate)
            {
                bibliography ??= LoadBibliography(incoming);
            }

            var matches = candidates.Where(candidate => soleCandidate
                ? SharesVerifiedBibliographyOnAdd(incoming, bibliography, candidate.Author, candidate.Bibliography)
                : SharesVerifiedBibliography(incoming, bibliography, candidate.Author, candidate.Bibliography ?? LoadBibliography(candidate.Author)))
                .Take(2).ToList();
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
            if (!dryRun)
            {
                throw new CommandFailedException("Batch author canonicalization only previews candidates. Review a specific pair with ReconcileCatalogueAuthors.");
            }

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
                        _logger.Warn("Author canonicalization preview reached max candidate cap ({0})", maxMerges);
                        return summary;
                    }

                    summary.MergesPerformed++;
                    _logger.Info("[DRY-RUN] Candidate author merge: keep {0}, merge {1}, confidence={2:0.000}, reason={3}", primary, duplicate, score, reason);
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

            _logger.Info("Author canonicalization preview: groups={0}, candidates={1}, matches={2}, skipped={3}",
                summary.DuplicateGroupsEvaluated,
                summary.CandidatesConsidered,
                summary.MergesPerformed,
                summary.MergesSkipped);
        }

        private IReadOnlyList<Book> LoadBibliography(Author authority)
        {
            var remote = _metadata.GetAuthorInfo(authority.ForeignAuthorId);
            var expected = GetAuthority(authority);
            var actual = GetAuthority(remote);
            return actual?.Provider == expected.Provider && actual.ProviderId == expected.ProviderId ? remote.Books?.Value : null;
        }

        private bool SharesVerifiedBibliographyOnAdd(Author incoming,
            IReadOnlyList<Book> incomingBooks,
            Author incumbent,
            IReadOnlyList<Book> incumbentBooks)
        {
            var incomingIsOpenLibrary = GetAuthority(incoming)?.Provider == "OpenLibrary";
            var openLibrary = incomingIsOpenLibrary ? incoming : incumbent;
            var hardcover = incomingIsOpenLibrary ? incumbent : incoming;
            var openLibraryAuthority = GetAuthority(openLibrary);

            try
            {
                incomingBooks ??= LoadBibliography(incoming);
                incumbentBooks ??= LoadBibliography(incumbent);
            }
            catch (MetadataProviderUnavailableException ex) when (
                ex.AuthoritativeBibliographyEndedEarly &&
                ex.Operation == MetadataProviderOrchestrator.AuthorInfoOperation &&
                GetAuthority(hardcover)?.Provider == "Hardcover" &&
                openLibraryAuthority?.Provider == "OpenLibrary" &&
                ex.EntityId == $"openlibrary:author:{openLibraryAuthority.ProviderId}")
            {
                var hardcoverBooks = incomingIsOpenLibrary ? incumbentBooks : incomingBooks;
                if (VerifyWorksWithoutOpenLibraryBibliography(hardcover, hardcoverBooks, openLibrary, incomingIsOpenLibrary && incumbent.Id > 0))
                {
                    return true;
                }

                // Failure to recover positive evidence still means the authority is unknown.
                throw;
            }

            return SharesVerifiedBibliography(incoming, incomingBooks, incumbent, incumbentBooks);
        }

        private IReadOnlyList<Book> LoadHardcoverWitnessCandidates(Author hardcover, bool canUseStoredWorks)
        {
            try
            {
                return LoadBibliography(hardcover);
            }
            catch (MetadataProviderUnavailableException ex) when (canUseStoredWorks &&
                ex.Operation == MetadataProviderOrchestrator.AuthorInfoOperation &&
                ex.EntityId == hardcover.ForeignAuthorId && ex.InnerException is not TooManyRequestsException)
            {
                // Stored works suggest ISBNs. Both providers must still verify each author's credit.
                return hardcover.Books?.Value;
            }
        }

        private bool VerifyWorksWithoutOpenLibraryBibliography(Author hardcover, IReadOnlyList<Book> books, Author openLibrary, bool canUseStoredWorks)
        {
            var verifiedHardcover = new HashSet<string>(StringComparer.Ordinal);
            var verifiedOpenLibrary = new HashSet<string>(StringComparer.Ordinal);
            var verifiedTitles = new HashSet<string>(StringComparer.Ordinal);
            try
            {
                var witnesses = GetWitnessSuggestions(books ?? LoadHardcoverWitnessCandidates(hardcover, canUseStoredWorks), "Hardcover")
                    .OrderBy(item => item.Isbn, StringComparer.Ordinal)
                    .GroupBy(item => item.Title)
                    .SelectMany(group => group.Select((witness, rank) => (Witness: witness, Rank: rank)))
                    .OrderBy(item => item.Rank).ThenBy(item => item.Witness.Isbn, StringComparer.Ordinal)
                    .Take(MaxWitnessPairs).Select(item => item.Witness);

                foreach (var witness in witnesses)
                {
                    if (verifiedHardcover.Contains(witness.WorkId) || verifiedTitles.Contains(witness.Title))
                    {
                        continue;
                    }

                    var leftTitle = _metadata.GetVerifiedAuthorWorkTitle(hardcover.ForeignAuthorId, witness.WorkId, witness.Isbn);
                    if (NormalizeTitle(leftTitle) != witness.Title)
                    {
                        return false;
                    }

                    var right = _metadata.GetVerifiedOpenLibraryAuthorWorkByIsbn(openLibrary.ForeignAuthorId, witness.Isbn);
                    if (right == null || NormalizeTitle(right.Value.Title) != witness.Title)
                    {
                        return false;
                    }

                    if (verifiedOpenLibrary.Add(right.Value.WorkId))
                    {
                        verifiedHardcover.Add(witness.WorkId);
                        verifiedTitles.Add(witness.Title);
                        if (verifiedTitles.Count == 2)
                        {
                            return true;
                        }
                    }
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.Warn(ex, "Could not corroborate author identity using exact ISBN witnesses after an incomplete Open Library bibliography");
            }

            return false;
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
