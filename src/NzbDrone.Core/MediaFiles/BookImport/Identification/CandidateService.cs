using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Books;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.MetadataSource.OpenLibrary;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.MediaFiles.BookImport.Identification
{
    public interface ICandidateService
    {
        List<CandidateEdition> GetDbCandidatesFromTags(LocalEdition localEdition, IdentificationOverrides idOverrides, bool includeExisting);
        IEnumerable<CandidateEdition> GetRemoteCandidates(LocalEdition localEdition, IdentificationOverrides idOverrides);
    }

    public class CandidateService : ICandidateService
    {
        private const int IsbnFallbackCacheMaxSize = 200;
        private static readonly Regex Isbn13Regex = new Regex(@"(?<!\d)(97[89][\d-]{10,16})(?!\d)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex Isbn10Regex = new Regex(@"(?<![\dXx])([\d-]{9}[\dXx])(?![\dXx])", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex AsinRegex = new Regex(@"\b(B0[0-9A-Z]{8})\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Dictionary<string, int> FallbackProviderPriorities = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["Inventaire"] = 10,
            ["GoogleBooks"] = 20,
            ["Hardcover"] = 30
        };

        private readonly IMetadataProviderOrchestrator _metadataOrchestrator;
        private readonly IAuthorService _authorService;
        private readonly IBookService _bookService;
        private readonly IEditionService _editionService;
        private readonly IMediaFileService _mediaFileService;
        private readonly IMetadataQueryNormalizationService _queryNormalizationService;
        private readonly IEnumerable<IBookSearchFallbackProvider> _fallbackProviders;
        private readonly IBookSearchFallbackExecutionService _fallbackExecutionService;
        private readonly IConfigService _configService;
        private readonly Logger _logger;

        private readonly ConcurrentDictionary<string, IReadOnlyList<Book>> _isbnFallbackCache =
            new ConcurrentDictionary<string, IReadOnlyList<Book>>(StringComparer.OrdinalIgnoreCase);

        public CandidateService(IMetadataProviderOrchestrator metadataOrchestrator,
                                IAuthorService authorService,
                                IBookService bookService,
                                IEditionService editionService,
                                IMediaFileService mediaFileService,
                                IMetadataQueryNormalizationService queryNormalizationService,
                                IEnumerable<IBookSearchFallbackProvider> fallbackProviders,
                                IBookSearchFallbackExecutionService fallbackExecutionService,
                                IConfigService configService,
                                Logger logger)
        {
            _metadataOrchestrator = metadataOrchestrator;
            _authorService = authorService;
            _bookService = bookService;
            _editionService = editionService;
            _mediaFileService = mediaFileService;
            _queryNormalizationService = queryNormalizationService;
            _fallbackProviders = (fallbackProviders ?? new List<IBookSearchFallbackProvider>())
                .OrderBy(GetFallbackProviderPriority)
                .ThenBy(x => x.ProviderName, StringComparer.OrdinalIgnoreCase)
                .ToList();
            _fallbackExecutionService = fallbackExecutionService;
            _configService = configService;
            _logger = logger;
        }

        public List<CandidateEdition> GetDbCandidatesFromTags(LocalEdition localEdition, IdentificationOverrides idOverrides, bool includeExisting)
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();

            // Generally author, book and release are null.  But if they're not then limit candidates appropriately.
            // We've tried to make sure that tracks are all for a single release.
            List<CandidateEdition> candidateReleases;

            // if we have a Book ID, use that
            Book tagMbidRelease = null;
            List<CandidateEdition> tagCandidate = null;

            // NOTE: select by ISBN?
            // var releaseIds = localEdition.LocalTracks.Select(x => x.FileTrackInfo.ReleaseMBId).Distinct().ToList();
            // if (releaseIds.Count == 1 && releaseIds[0].IsNotNullOrWhiteSpace())
            // {
            //     _logger.Debug("Selecting release from consensus ForeignReleaseId [{0}]", releaseIds[0]);
            //     tagMbidRelease = _releaseService.GetReleaseByForeignReleaseId(releaseIds[0], true);

            //     if (tagMbidRelease != null)
            //     {
            //         tagCandidate = GetDbCandidatesByRelease(new List<BookRelease> { tagMbidRelease }, includeExisting);
            //     }
            // }
            if (idOverrides?.Edition != null)
            {
                var release = idOverrides.Edition;
                _logger.Debug("Edition {0} was forced", release);
                candidateReleases = GetDbCandidatesByEdition(new List<Edition> { release }, includeExisting);
            }
            else if (idOverrides?.Book != null)
            {
                // use the release from file tags if it exists and agrees with the specified book
                if (tagMbidRelease?.Id == idOverrides.Book.Id)
                {
                    candidateReleases = tagCandidate;
                }
                else
                {
                    candidateReleases = GetDbCandidatesByBook(idOverrides.Book, includeExisting);
                }
            }
            else if (idOverrides?.Author != null)
            {
                // use the release from file tags if it exists and agrees with the specified book
                if (tagMbidRelease?.AuthorMetadataId == idOverrides.Author.AuthorMetadataId)
                {
                    candidateReleases = tagCandidate;
                }
                else
                {
                    candidateReleases = GetDbCandidatesByAuthor(localEdition, idOverrides.Author, includeExisting);
                }
            }
            else
            {
                if (tagMbidRelease != null)
                {
                    candidateReleases = tagCandidate;
                }
                else
                {
                    candidateReleases = GetDbCandidates(localEdition, includeExisting);
                }
            }

            watch.Stop();
            _logger.Debug($"Getting {candidateReleases.Count} candidates from tags for {localEdition.LocalBooks.Count} tracks took {watch.ElapsedMilliseconds}ms");

            return candidateReleases;
        }

        private List<CandidateEdition> GetDbCandidatesByEdition(List<Edition> editions, bool includeExisting)
        {
            // get the local tracks on disk for each book
            var bookFiles = editions.Select(x => x.BookId)
                .Distinct()
                .ToDictionary(id => id, id => includeExisting ? _mediaFileService.GetFilesByBook(id) : new List<BookFile>());

            return editions.Select(x => new CandidateEdition
            {
                Edition = x,
                ExistingFiles = bookFiles[x.BookId]
            }).ToList();
        }

        private List<CandidateEdition> GetDbCandidatesByBook(Book book, bool includeExisting)
        {
            // Sort by most voted so less likely to swap to a random release
            return GetDbCandidatesByEdition(_editionService.GetEditionsByBook(book.Id)
                                            .OrderByDescending(x => x.Ratings.Popularity)
                                            .ToList(), includeExisting);
        }

        private List<CandidateEdition> GetDbCandidatesByAuthor(LocalEdition localEdition, Author author, bool includeExisting)
        {
            _logger.Trace("Getting candidates for {0}", author);
            var candidateReleases = new List<CandidateEdition>();

            var bookTag = localEdition.LocalBooks.MostCommon(x => x.FileTrackInfo.BookTitle) ?? "";
            if (bookTag.IsNotNullOrWhiteSpace())
            {
                var possibleBooks = _bookService.GetCandidates(author.AuthorMetadataId, bookTag);
                foreach (var book in possibleBooks)
                {
                    candidateReleases.AddRange(GetDbCandidatesByBook(book, includeExisting));
                }

                var possibleEditions = _editionService.GetCandidates(author.AuthorMetadataId, bookTag);
                candidateReleases.AddRange(GetDbCandidatesByEdition(possibleEditions, includeExisting));
            }

            return candidateReleases;
        }

        private List<CandidateEdition> GetDbCandidates(LocalEdition localEdition, bool includeExisting)
        {
            // most general version, nothing has been specified.
            // get all plausible authors, then all plausible books, then get releases for each of these.
            var candidateReleases = new List<CandidateEdition>();

            // check if it looks like VA.
            if (TrackGroupingService.IsVariousAuthors(localEdition.LocalBooks))
            {
                var va = _authorService.FindById(DistanceCalculator.VariousAuthorIds[0]);
                if (va != null)
                {
                    candidateReleases.AddRange(GetDbCandidatesByAuthor(localEdition, va, includeExisting));
                }
            }

            var authorTags = localEdition.LocalBooks.MostCommon(x => x.FileTrackInfo.Authors) ?? new List<string>();
            if (authorTags.Any())
            {
                var variants = DistanceCalculator.GetAuthorVariants(authorTags.Where(x => x.IsNotNullOrWhiteSpace()).ToList());

                foreach (var authorTag in variants)
                {
                    if (authorTag.IsNotNullOrWhiteSpace())
                    {
                        var possibleAuthors = _authorService.GetCandidates(authorTag);
                        foreach (var author in possibleAuthors)
                        {
                            candidateReleases.AddRange(GetDbCandidatesByAuthor(localEdition, author, includeExisting));
                        }
                    }
                }
            }

            return candidateReleases;
        }

        public IEnumerable<CandidateEdition> GetRemoteCandidates(LocalEdition localEdition, IdentificationOverrides idOverrides)
        {
            // NOTE handle edition override

            // Gets candidate book releases from the metadata server.
            // Will eventually need adding locally if we find a match
            List<Book> remoteBooks;
            var seenCandidates = new HashSet<string>();
            var contextualFallbackFoundCandidates = false;

            var isbns = localEdition.LocalBooks.Select(x => x.FileTrackInfo.Isbn).Distinct().ToList();
            var asins = localEdition.LocalBooks.Select(x => x.FileTrackInfo.Asin).Distinct().ToList();
            var openlibrary = localEdition.LocalBooks.Select(x => x.FileTrackInfo.OpenLibraryId).Distinct().ToList();
            var isbn = SelectSingleIsbn(isbns, localEdition);
            var asin = SelectSingleAsin(asins, localEdition);

            // grab possibilities for all the IDs present
            if (isbn.IsNotNullOrWhiteSpace())
            {
                _logger.Trace($"Searching by isbn {isbn}");

                try
                {
                    remoteBooks = _metadataOrchestrator.SearchByIsbn(isbn);
                }
                catch (OpenLibraryException e)
                {
                    _logger.Info(e, "Skipping ISBN search due to OpenLibrary Error");
                    remoteBooks = new List<Book>();
                }

                foreach (var candidate in ToCandidates(remoteBooks, seenCandidates, idOverrides))
                {
                    yield return candidate;
                }

                // If ISBN lookup misses, attempt a few author+title searches before other ID-based lookups.
                if (!seenCandidates.Any())
                {
                    var preContextCount = seenCandidates.Count;

                    foreach (var candidate in TryIsbnContextFallback(localEdition, seenCandidates, idOverrides))
                    {
                        yield return candidate;
                    }

                    contextualFallbackFoundCandidates = seenCandidates.Count > preContextCount;
                }
            }

            if (asin.IsNotNullOrWhiteSpace() && asin.Length == 10)
            {
                _logger.Trace($"Searching by asin {asin}");

                try
                {
                    remoteBooks = _metadataOrchestrator.SearchByAsin(asin);
                }
                catch (OpenLibraryException e)
                {
                    _logger.Info(e, "Skipping ASIN search due to OpenLibrary Error");
                    remoteBooks = new List<Book>();
                }

                foreach (var candidate in ToCandidates(remoteBooks, seenCandidates, idOverrides))
                {
                    yield return candidate;
                }
            }

            if (openlibrary.Count == 1 &&
                openlibrary[0].IsNotNullOrWhiteSpace())
            {
                _logger.Trace($"Searching by openlibrary id {openlibrary[0]}");

                try
                {
                    remoteBooks = _metadataOrchestrator.SearchByExternalId("openlibrary", openlibrary[0]);
                }
                catch (OpenLibraryException e)
                {
                    _logger.Info(e, "Skipping OpenLibrary ID search due to OpenLibrary Error");
                    remoteBooks = new List<Book>();
                }

                foreach (var candidate in ToCandidates(remoteBooks, seenCandidates, idOverrides))
                {
                    yield return candidate;
                }
            }

            // If we got an id result, or any overrides are set, stop
            if (contextualFallbackFoundCandidates ||
                idOverrides?.Edition != null ||
                idOverrides?.Book != null ||
                idOverrides?.Author != null)
            {
                yield break;
            }

            // Always attempt author / book name search even when ID-based results
            // were found — embedded ISBNs/ASINs can be incorrect, and the
            // author+title search may find the correct book. seenCandidates
            // prevents duplicate editions from being yielded.
            var authorTags = new List<string>();

            if (TrackGroupingService.IsVariousAuthors(localEdition.LocalBooks))
            {
                authorTags.Add("Various Authors");
            }
            else
            {
                // the most common list of authors reported by a file
                var authors = localEdition.LocalBooks.Select(x => x.FileTrackInfo.Authors.Where(a => a.IsNotNullOrWhiteSpace()).ToList())
                    .GroupBy(x => x.ConcatToString())
                    .OrderByDescending(x => x.Count())
                    .First()
                    .First();
                authorTags.AddRange(authors);
            }

            var bookTag = localEdition.LocalBooks.MostCommon(x => x.FileTrackInfo.BookTitle) ?? "";
            var authorVariants = BuildRobustAuthorVariants(authorTags);
            var titleVariants = BuildRobustTitleVariants(bookTag);

            // If neither author nor title tags are available, stop.
            if (!authorVariants.Any() && !titleVariants.Any())
            {
                yield break;
            }

            // Search by author+book
            if (titleVariants.Any() && authorVariants.Any())
            {
                var authorTitleQueries = new List<(string Title, string Author, string Mode)>();

                foreach (var titleVariant in titleVariants)
                {
                    foreach (var authorTag in authorVariants)
                    {
                        authorTitleQueries.Add((titleVariant, authorTag, "author/title search"));
                    }
                }

                remoteBooks = SearchPrimaryWithFanOut(authorTitleQueries, _configService.RemoteCandidateSearchWorkerCount);

                foreach (var candidate in ToCandidates(remoteBooks, seenCandidates, idOverrides))
                {
                    yield return candidate;
                }
            }

            // If we got an author/book search result, stop
            if (seenCandidates.Any())
            {
                yield break;
            }

            foreach (var swappedPair in BuildSwappedAuthorTitleFallbackPairs(bookTag, authorTags))
            {
                remoteBooks = SearchPrimary(swappedPair.Title, swappedPair.Author, "swapped author/title search");

                foreach (var candidate in ToCandidates(remoteBooks, seenCandidates, idOverrides))
                {
                    yield return candidate;
                }

                if (seenCandidates.Any())
                {
                    yield break;
                }
            }

            // Search by just book title
            if (titleVariants.Any())
            {
                var titleOnlyQueries = titleVariants.Select(titleVariant => (titleVariant, (string)null, "book title search")).ToList();
                remoteBooks = SearchPrimaryWithFanOut(titleOnlyQueries, _configService.RemoteCandidateSearchWorkerCount);

                foreach (var candidate in ToCandidates(remoteBooks, seenCandidates, idOverrides))
                {
                    yield return candidate;
                }
            }

            // Search by just author
            if (authorVariants.Any())
            {
                var authorOnlyQueries = authorVariants.Select(author => (author, (string)null, "author search")).ToList();
                remoteBooks = SearchPrimaryWithFanOut(authorOnlyQueries, _configService.RemoteCandidateSearchWorkerCount);

                foreach (var candidate in ToCandidates(remoteBooks, seenCandidates, idOverrides))
                {
                    yield return candidate;
                }
            }

            if (seenCandidates.Any())
            {
                yield break;
            }

            foreach (var provider in _fallbackProviders)
            {
                _logger.Debug("Trying tertiary fallback provider {0}", provider.ProviderName);

                if (titleVariants.Any() && authorVariants.Any())
                {
                    foreach (var titleVariant in titleVariants)
                    {
                        foreach (var authorVariant in authorVariants)
                        {
                            foreach (var candidate in ToCandidates(SearchFallback(provider, titleVariant, authorVariant), seenCandidates, idOverrides))
                            {
                                yield return candidate;
                            }
                        }
                    }
                }

                if (!seenCandidates.Any() && titleVariants.Any())
                {
                    foreach (var titleVariant in titleVariants)
                    {
                        foreach (var candidate in ToCandidates(SearchFallback(provider, titleVariant, null), seenCandidates, idOverrides))
                        {
                            yield return candidate;
                        }
                    }
                }

                if (!seenCandidates.Any())
                {
                    foreach (var authorVariant in authorVariants)
                    {
                        foreach (var candidate in ToCandidates(SearchFallback(provider, null, authorVariant), seenCandidates, idOverrides))
                        {
                            yield return candidate;
                        }
                    }
                }

                if (seenCandidates.Any())
                {
                    _logger.Debug("Provider {0} returned fallback candidates", provider.ProviderName);
                    yield break;
                }
            }

            LogCandidateRejectionTelemetry(localEdition, "all-search-paths-exhausted", 0, 0, seenCandidates.Count);
        }

        private IEnumerable<CandidateEdition> TryIsbnContextFallback(LocalEdition localEdition, HashSet<string> seenCandidates, IdentificationOverrides idOverrides)
        {
            var attemptLimit = _configService.IsbnContextFallbackLimit;
            var attempts = 0;
            var cacheHits = 0;

            var bookTag = localEdition.LocalBooks.MostCommon(x => x.FileTrackInfo.BookTitle) ?? string.Empty;
            var authorTags = localEdition.LocalBooks.MostCommon(x => x.FileTrackInfo.Authors) ?? new List<string>();

            var titleVariants = BuildRobustTitleVariants(bookTag);
            var authorVariants = BuildRobustAuthorVariants(authorTags);

            if (!titleVariants.Any())
            {
                yield break;
            }

            if (!authorVariants.Any())
            {
                authorVariants.Add(null);
            }

            foreach (var title in titleVariants)
            {
                foreach (var author in authorVariants)
                {
                    var cacheKey = $"{title}|{author ?? string.Empty}".ToLowerInvariant();

                    if (_isbnFallbackCache.TryGetValue(cacheKey, out var cached))
                    {
                        cacheHits++;
                        _logger.Debug("ISBN contextual fallback cache hit for title='{0}', author='{1}'", title, author ?? "<none>");
                        foreach (var candidate in ToCandidates(cached, seenCandidates, idOverrides))
                        {
                            yield return candidate;
                        }

                        if (seenCandidates.Any())
                        {
                            _logger.Info("ISBN contextual fallback resolved via cache after {0} live attempt(s) and {1} cache hit(s)", attempts, cacheHits);
                            yield break;
                        }

                        continue;
                    }

                    if (attempts >= attemptLimit)
                    {
                        yield break;
                    }

                    attempts++;
                    _logger.Debug(
                        "ISBN lookup miss; trying contextual fallback ({0}/{1}): title='{2}', author='{3}'",
                        attempts,
                        attemptLimit,
                        title,
                        author ?? "<none>");

                    List<Book> remoteBooks;
                    try
                    {
                        remoteBooks = _metadataOrchestrator.SearchForNewBook(title, author, true);
                    }
                    catch (OpenLibraryException e)
                    {
                        _logger.Info(e, "Skipping ISBN contextual fallback search due to OpenLibrary Error");
                        LogCandidateRejectionTelemetry(localEdition, "isbn-context-fallback-exhausted", attempts, cacheHits, seenCandidates.Count);
                        remoteBooks = new List<Book>();
                    }

                    if (_isbnFallbackCache.Count >= IsbnFallbackCacheMaxSize)
                    {
                        _isbnFallbackCache.Clear();
                    }

                    _isbnFallbackCache[cacheKey] = remoteBooks.AsReadOnly();

                    foreach (var candidate in ToCandidates(remoteBooks, seenCandidates, idOverrides))
                    {
                        yield return candidate;
                    }

                    if (seenCandidates.Any())
                    {
                        _logger.Info(
                            "ISBN contextual fallback hit after {0} attempt(s) and {1} cache hit(s); candidates found",
                            attempts,
                            cacheHits);
                        yield break;
                    }
                }
            }

            if (attempts > 0 || cacheHits > 0)
            {
                _logger.Info(
                    "ISBN contextual fallback exhausted: {0} live attempt(s), {1} cache hit(s), limit={2}, no candidates found",
                    attempts,
                    cacheHits,
                    attemptLimit);
            }
        }

        private List<Book> SearchPrimary(string title, string author, string mode)
        {
            try
            {
                var results = _metadataOrchestrator.SearchForNewBook(title, author);

                if (!results.Any())
                {
                    _logger.Debug("Primary identification search returned no candidates: mode={0}, title='{1}', author='{2}'", mode, title ?? "<none>", author ?? "<none>");
                }

                return results;
            }
            catch (OpenLibraryException e)
            {
                _logger.Info(e, "Skipping {0} due to OpenLibrary Error", mode);
                return new List<Book>();
            }
        }

        private List<Book> SearchPrimaryWithFanOut(List<(string Title, string Author, string Mode)> queries, int configuredWorkers)
        {
            if (queries == null || queries.Count == 0)
            {
                return new List<Book>();
            }

            var maxWorkers = Math.Max(1, Math.Min(configuredWorkers, queries.Count));

            _logger.Debug(
                "Running {0} primary candidate search query(ies) with up to {1} worker(s)",
                queries.Count,
                maxWorkers);

            if (maxWorkers == 1)
            {
                var sequentialResults = new List<Book>();

                foreach (var query in queries)
                {
                    sequentialResults.AddRange(SearchPrimary(query.Title, query.Author, query.Mode));
                }

                return sequentialResults;
            }

            var allResults = new ConcurrentBag<Book>();
            var options = new ParallelOptions { MaxDegreeOfParallelism = maxWorkers };

            Parallel.ForEach(queries, options, query =>
            {
                var results = SearchPrimary(query.Title, query.Author, query.Mode);

                foreach (var result in results)
                {
                    allResults.Add(result);
                }
            });

            return allResults.ToList();
        }

        private List<Book> SearchFallback(IBookSearchFallbackProvider provider, string title, string author)
        {
            var results = _fallbackExecutionService.Search(provider, title, author);

            if (!results.Any())
            {
                _logger.Debug("Fallback identification search returned no candidates: provider={0}, title='{1}', author='{2}'", provider?.ProviderName ?? "<null>", title ?? "<none>", author ?? "<none>");
            }

            return results;
        }

        private List<string> BuildRobustTitleVariants(string rawTitle)
        {
            var variants = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var variant in _queryNormalizationService.BuildTitleVariants(rawTitle) ?? new List<string>())
            {
                if (variant.IsNotNullOrWhiteSpace())
                {
                    variants.Add(variant.Trim());
                }
            }

            if (rawTitle.IsNotNullOrWhiteSpace())
            {
                var sanitized = Regex.Replace(rawTitle, "\\[[^\\]]+\\]|\\([^\\)]+\\)", " ");
                sanitized = sanitized.Replace('_', ' ').Replace('-', ' ').CleanSpaces();

                if (sanitized.IsNotNullOrWhiteSpace())
                {
                    variants.Add(sanitized);
                }

                if (sanitized.Contains(':'))
                {
                    variants.Add(sanitized.Split(':')[0].CleanSpaces());
                }
            }

            return variants.Where(v => v.IsNotNullOrWhiteSpace()).Distinct().ToList();
        }

        private List<string> BuildRobustAuthorVariants(IEnumerable<string> authorTags)
        {
            var variants = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var variant in _queryNormalizationService.ExpandAuthorAliases(authorTags) ?? new List<string>())
            {
                if (variant.IsNotNullOrWhiteSpace())
                {
                    variants.Add(variant.Trim());
                }
            }

            foreach (var author in authorTags ?? new List<string>())
            {
                if (author.IsNullOrWhiteSpace())
                {
                    continue;
                }

                var parts = author.Split(new[] { '&', ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.Trim())
                    .Where(x => x.IsNotNullOrWhiteSpace());

                foreach (var part in parts)
                {
                    variants.Add(part);
                }
            }

            return variants.Where(v => v.IsNotNullOrWhiteSpace()).Distinct().ToList();
        }

        private IEnumerable<(string Title, string Author)> BuildSwappedAuthorTitleFallbackPairs(string bookTag, IEnumerable<string> authorTags)
        {
            var normalizedTitle = (bookTag ?? string.Empty).Trim();
            var normalizedAuthors = NormalizeAuthorCandidates(authorTags);

            if (normalizedTitle.IsNullOrWhiteSpace() || normalizedAuthors.Count != 1)
            {
                yield break;
            }

            var swappedTitle = normalizedAuthors[0];
            var swappedAuthor = normalizedTitle;

            if (string.Equals(swappedTitle, swappedAuthor, StringComparison.OrdinalIgnoreCase))
            {
                yield break;
            }

            _logger.Debug("Trying swapped filename fallback search: title='{0}', author='{1}'", swappedTitle, swappedAuthor);
            yield return (swappedTitle, swappedAuthor);
        }

        private static List<string> NormalizeAuthorCandidates(IEnumerable<string> authorTags)
        {
            return (authorTags ?? new List<string>())
                .Where(x => x.IsNotNullOrWhiteSpace())
                .Select(x => x.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private void LogCandidateRejectionTelemetry(LocalEdition localEdition, string reason, int attempts, int cacheHits, int seenCandidates)
        {
            var topTitle = localEdition?.LocalBooks?.MostCommon(x => x.FileTrackInfo.BookTitle) ?? "<unknown>";
            var topAuthors = (localEdition?.LocalBooks?.MostCommon(x => x.FileTrackInfo.Authors) ?? new List<string>()).ConcatToString();

            _logger.Info("Identification fallback telemetry: reason={0}, attempts={1}, cacheHits={2}, candidates={3}, title='{4}', authors='{5}'",
                reason,
                attempts,
                cacheHits,
                seenCandidates,
                topTitle,
                topAuthors);
        }

        private string SelectSingleIsbn(List<string> parsedIsbns, LocalEdition localEdition)
        {
            var normalizedParsed = parsedIsbns
                .Where(x => x.IsNotNullOrWhiteSpace())
                .Select(NormalizeIsbn)
                .Where(x => x.IsNotNullOrWhiteSpace())
                .Distinct()
                .ToList();

            if (normalizedParsed.Count == 1)
            {
                return normalizedParsed[0];
            }

            var extracted = localEdition.LocalBooks
                .Select(x => x.FileTrackInfo.BookTitle)
                .Where(x => x.IsNotNullOrWhiteSpace())
                .SelectMany(ExtractIsbns)
                .Distinct()
                .ToList();

            return extracted.Count == 1 ? extracted[0] : null;
        }

        private string SelectSingleAsin(List<string> parsedAsins, LocalEdition localEdition)
        {
            var normalizedParsed = parsedAsins
                .Where(x => x.IsNotNullOrWhiteSpace())
                .Select(x => x.Trim().ToUpperInvariant())
                .Where(x => x.Length == 10)
                .Distinct()
                .ToList();

            if (normalizedParsed.Count == 1)
            {
                return normalizedParsed[0];
            }

            var extracted = localEdition.LocalBooks
                .Select(x => x.FileTrackInfo.BookTitle)
                .Where(x => x.IsNotNullOrWhiteSpace())
                .SelectMany(ExtractAsins)
                .Distinct()
                .ToList();

            return extracted.Count == 1 ? extracted[0] : null;
        }

        private static IEnumerable<string> ExtractIsbns(string value)
        {
            var isbn13 = Isbn13Regex.Match(value);
            if (isbn13.Success)
            {
                var normalized13 = NormalizeIsbn(isbn13.Value);
                if (normalized13.IsNotNullOrWhiteSpace())
                {
                    yield return normalized13;
                }
            }

            var isbn10 = Isbn10Regex.Match(value);
            if (isbn10.Success)
            {
                var normalized10 = NormalizeIsbn(isbn10.Value);
                if (normalized10.IsNotNullOrWhiteSpace())
                {
                    yield return normalized10;
                }
            }
        }

        private static IEnumerable<string> ExtractAsins(string value)
        {
            var asin = AsinRegex.Match(value);
            if (asin.Success)
            {
                yield return asin.Value.ToUpperInvariant();
            }
        }

        private static string NormalizeIsbn(string value)
        {
            var normalized = new string(value.Where(c => char.IsDigit(c) || c == 'X' || c == 'x').ToArray()).ToUpperInvariant();

            if (normalized.Length == 13 && normalized.All(char.IsDigit))
            {
                return normalized;
            }

            if (normalized.Length == 10)
            {
                return normalized;
            }

            return null;
        }

        private List<CandidateEdition> ToCandidates(IEnumerable<Book> books, HashSet<string> seenCandidates, IdentificationOverrides idOverrides)
        {
            var candidates = new List<CandidateEdition>();

            foreach (var book in books)
            {
                // We have to make sure various bits and pieces are populated that are normally handled
                // by a database lazy load
                foreach (var edition in book.Editions.Value)
                {
                    edition.Book = book;

                    if (!seenCandidates.Contains(edition.ForeignEditionId) && SatisfiesOverride(edition, idOverrides))
                    {
                        seenCandidates.Add(edition.ForeignEditionId);
                        candidates.Add(new CandidateEdition
                        {
                            Edition = edition,
                            ExistingFiles = new List<BookFile>()
                        });
                    }
                }
            }

            return candidates;
        }

        private static int GetFallbackProviderPriority(IBookSearchFallbackProvider provider)
        {
            if (provider == null || provider.ProviderName.IsNullOrWhiteSpace())
            {
                return int.MaxValue;
            }

            return FallbackProviderPriorities.TryGetValue(provider.ProviderName, out var priority)
                ? priority
                : 100;
        }

        private bool SatisfiesOverride(Edition edition, IdentificationOverrides idOverride)
        {
            if (idOverride?.Edition != null)
            {
                return edition.ForeignEditionId == idOverride.Edition.ForeignEditionId;
            }

            if (idOverride?.Book != null)
            {
                return edition.Book.Value.ForeignBookId == idOverride.Book.ForeignBookId;
            }

            if (idOverride?.Author != null)
            {
                return edition.Book.Value.Author.Value.ForeignAuthorId == idOverride.Author.ForeignAuthorId;
            }

            return true;
        }
    }
}
