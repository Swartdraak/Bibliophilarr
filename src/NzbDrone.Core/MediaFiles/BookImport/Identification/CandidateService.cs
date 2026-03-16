using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Books;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.MetadataSource.Goodreads;
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
        private static readonly Regex Isbn13Regex = new Regex(@"(?<!\d)(97[89][\d-]{10,16})(?!\d)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex Isbn10Regex = new Regex(@"(?<![\dXx])([\d-]{9}[\dXx])(?![\dXx])", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex AsinRegex = new Regex(@"\b(B0[0-9A-Z]{8})\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Dictionary<string, int> FallbackProviderPriorities = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["Inventaire"] = 10,
            ["GoogleBooks"] = 20,
            ["Hardcover"] = 30
        };

        private readonly ISearchForNewBook _bookSearchService;
        private readonly IAuthorService _authorService;
        private readonly IBookService _bookService;
        private readonly IEditionService _editionService;
        private readonly IMediaFileService _mediaFileService;
        private readonly IMetadataQueryNormalizationService _queryNormalizationService;
        private readonly IEnumerable<IBookSearchFallbackProvider> _fallbackProviders;
        private readonly IBookSearchFallbackExecutionService _fallbackExecutionService;
        private readonly Logger _logger;

        public CandidateService(ISearchForNewBook bookSearchService,
                                IAuthorService authorService,
                                IBookService bookService,
                                IEditionService editionService,
                                IMediaFileService mediaFileService,
                                IMetadataQueryNormalizationService queryNormalizationService,
                                IEnumerable<IBookSearchFallbackProvider> fallbackProviders,
                                IBookSearchFallbackExecutionService fallbackExecutionService,
                                Logger logger)
        {
            _bookSearchService = bookSearchService;
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

            // TODO: select by ISBN?
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
            // TODO handle edition override

            // Gets candidate book releases from the metadata server.
            // Will eventually need adding locally if we find a match
            List<Book> remoteBooks;
            var seenCandidates = new HashSet<string>();

            var isbns = localEdition.LocalBooks.Select(x => x.FileTrackInfo.Isbn).Distinct().ToList();
            var asins = localEdition.LocalBooks.Select(x => x.FileTrackInfo.Asin).Distinct().ToList();
            var goodreads = localEdition.LocalBooks.Select(x => x.FileTrackInfo.GoodreadsId).Distinct().ToList();
            var isbn = SelectSingleIsbn(isbns, localEdition);
            var asin = SelectSingleAsin(asins, localEdition);

            // grab possibilities for all the IDs present
            if (isbn.IsNotNullOrWhiteSpace())
            {
                _logger.Trace($"Searching by isbn {isbn}");

                try
                {
                    remoteBooks = _bookSearchService.SearchByIsbn(isbn);
                }
                catch (GoodreadsException e)
                {
                    _logger.Info(e, "Skipping ISBN search due to Goodreads Error");
                    remoteBooks = new List<Book>();
                }

                foreach (var candidate in ToCandidates(remoteBooks, seenCandidates, idOverrides))
                {
                    yield return candidate;
                }
            }

            if (asin.IsNotNullOrWhiteSpace() && asin.Length == 10)
            {
                _logger.Trace($"Searching by asin {asin}");

                try
                {
                    remoteBooks = _bookSearchService.SearchByAsin(asin);
                }
                catch (GoodreadsException e)
                {
                    _logger.Info(e, "Skipping ASIN search due to Goodreads Error");
                    remoteBooks = new List<Book>();
                }

                foreach (var candidate in ToCandidates(remoteBooks, seenCandidates, idOverrides))
                {
                    yield return candidate;
                }
            }

            if (goodreads.Count == 1 &&
                goodreads[0].IsNotNullOrWhiteSpace())
            {
                if (int.TryParse(goodreads[0], out var id))
                {
                    _logger.Trace($"Searching by goodreads id {id}");

                    try
                    {
                        remoteBooks = _bookSearchService.SearchByGoodreadsBookId(id, true);
                    }
                    catch (GoodreadsException e)
                    {
                        _logger.Info(e, "Skipping Goodreads ID search due to Goodreads Error");
                        remoteBooks = new List<Book>();
                    }

                    foreach (var candidate in ToCandidates(remoteBooks, seenCandidates, idOverrides))
                    {
                        yield return candidate;
                    }
                }
            }

            // If we got an id result, or any overrides are set, stop
            if (seenCandidates.Any() ||
                idOverrides?.Edition != null ||
                idOverrides?.Book != null ||
                idOverrides?.Author != null)
            {
                yield break;
            }

            // fall back to author / book name search
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
            var authorVariants = (_queryNormalizationService.ExpandAuthorAliases(authorTags) ?? new List<string>())
                .Where(a => a.IsNotNullOrWhiteSpace())
                .Distinct()
                .ToList();
            var titleVariants = (_queryNormalizationService.BuildTitleVariants(bookTag) ?? new List<string>())
                .Where(t => t.IsNotNullOrWhiteSpace())
                .Distinct()
                .ToList();

            // If neither author nor title tags are available, stop.
            if (!authorVariants.Any() && !titleVariants.Any())
            {
                yield break;
            }

            // Search by author+book
            if (titleVariants.Any() && authorVariants.Any())
            {
                foreach (var titleVariant in titleVariants)
                {
                    foreach (var authorTag in authorVariants)
                    {
                        remoteBooks = SearchPrimary(titleVariant, authorTag, "author/title search");

                        foreach (var candidate in ToCandidates(remoteBooks, seenCandidates, idOverrides))
                        {
                            yield return candidate;
                        }
                    }
                }
            }

            // If we got an author/book search result, stop
            if (seenCandidates.Any())
            {
                yield break;
            }

            // Search by just book title
            if (titleVariants.Any())
            {
                foreach (var titleVariant in titleVariants)
                {
                    remoteBooks = SearchPrimary(titleVariant, null, "book title search");

                    foreach (var candidate in ToCandidates(remoteBooks, seenCandidates, idOverrides))
                    {
                        yield return candidate;
                    }
                }
            }

            // Search by just author
            foreach (var a in authorVariants)
            {
                remoteBooks = SearchPrimary(a, null, "author search");

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
        }

        private List<Book> SearchPrimary(string title, string author, string mode)
        {
            try
            {
                return _bookSearchService.SearchForNewBook(title, author);
            }
            catch (GoodreadsException e)
            {
                _logger.Info(e, "Skipping {0} due to Goodreads Error", mode);
                return new List<Book>();
            }
        }

        private List<Book> SearchFallback(IBookSearchFallbackProvider provider, string title, string author)
        {
            return _fallbackExecutionService.Search(provider, title, author);
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
