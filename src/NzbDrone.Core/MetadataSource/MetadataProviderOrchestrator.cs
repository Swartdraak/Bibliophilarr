using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Books;
using NzbDrone.Core.Exceptions;
using NzbDrone.Core.MediaCover;
using NzbDrone.Core.MetadataSource.Hardcover;
using NzbDrone.Core.MetadataSource.OpenLibrary;

namespace NzbDrone.Core.MetadataSource
{
    /// <summary>
    /// Dispatches metadata operations to the appropriate provider via the registry.
    /// Uses an ID-scope compatibility check to route requests for provider-scoped IDs
    /// (e.g. "hardcover:author:12345") to the correct provider, and falls back through
    /// enabled providers in priority order when the primary fails or returns null.
    /// </summary>
    public class MetadataProviderOrchestrator : IMetadataProviderOrchestrator, ISearchForNewBook, ISearchForNewAuthor
    {
        private const string AuthorInfoOperation = "get-author-info";
        private const string BookSearchOperation = "search-for-new-book";
        private const int MaxPortraitIsbnLookups = 2;
        private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(250);
        private static readonly TimeSpan RetryInterval = TimeSpan.FromSeconds(30);

        // Matches bare Open Library identifiers like OL123A (author), OL456W (work), OL789M (edition).
        private static readonly Regex BareOpenLibraryTokenRegex = new Regex("^OL\\d+[AWM]$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private readonly ConcurrentDictionary<string, long> _nextRetry = new ConcurrentDictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        private readonly IMetadataProviderRegistry _registry;
        private readonly IMetadataProviderTelemetryService _telemetry;
        private readonly Logger _logger;

        public MetadataProviderOrchestrator(IMetadataProviderRegistry registry, IMetadataProviderTelemetryService telemetry, Logger logger)
        {
            _registry = registry;
            _telemetry = telemetry;
            _logger = logger;
        }

        public List<Book> SearchForNewBook(string title, string author, bool getAllEditions = true)
        {
            const string isbnPrefix = "isbn:";
            var term = title?.TrimStart();
            if (author.IsNullOrWhiteSpace() && term?.StartsWith(isbnPrefix, StringComparison.OrdinalIgnoreCase) == true)
            {
                var isbn = Isbn.AsIsbn13(term[isbnPrefix.Length..]);
                return isbn == null
                    ? new List<Book>()
                    : ExecuteFirstNonEmpty<ISearchForNewBook, List<Book>>(
                        provider => GetExplicitIsbnResults(provider, isbn),
                        "search-by-isbn",
                        provider => provider.SupportsIsbnLookup);
            }

            return ExecuteFirstNonEmpty<ISearchForNewBook, List<Book>>(
                p => p.SearchForNewBook(title, author, getAllEditions),
                BookSearchOperation,
                p => p.SupportsBookSearch);
        }

        public void SearchForLibraryImport(string title, string author, Func<List<Book>, bool> stopAfter)
        {
            ExecuteFirst<ISearchForNewBook, List<Book>>(
                provider => provider.SearchForNewBook(title, author, true),
                BookSearchOperation,
                provider => provider.SupportsBookSearch,
                treatEmptyListAsNoData: true,
                acceptResult: stopAfter);
        }

        private static List<Book> GetExplicitIsbnResults(ISearchForNewBook provider, string isbn)
        {
            var books = provider.SearchByIsbn(isbn);
            if (books == null)
            {
                return null;
            }

            return GetExactIsbnMatches(books, isbn)
                .Where(match => IsSourceEdition(match.Edition.ForeignEditionId))
                .GroupBy(match => match.Book, (IEqualityComparer<Book>)ReferenceEqualityComparer.Instance)
                .Select(matches =>
                {
                    var source = matches.Key;
                    var book = new Book
                    {
                        Author = source.Author,
                        AuthorMetadata = source.AuthorMetadata,
                        BookFiles = source.BookFiles,
                        SeriesLinks = source.SeriesLinks,
                        Editions = matches.Select(match => match.Edition).ToList()
                    };
                    book.UseMetadataFrom(source);
                    book.UseDbFieldsFrom(source);
                    return book;
                })
                .ToList();
        }

        private static bool IsSourceEdition(string id)
        {
            // Hardcover's legacy hardcover:edition: ID identifies a work projection.
            // Its source-edition namespace identifies an actual edition.
            var tokenStart = (id?.LastIndexOf(':') ?? -1) + 1;
            return id != null && !string.IsNullOrWhiteSpace(id[tokenStart..]) &&
                   (id.StartsWith("openlibrary:edition:", StringComparison.Ordinal) ||
                    id.StartsWith("googlebooks:edition:", StringComparison.Ordinal) ||
                    id.StartsWith(HardcoverFallbackSearchProvider.ProviderEditionIdPrefix, StringComparison.Ordinal));
        }

        public List<Book> SearchByIsbn(string isbn)
        {
            return ExecuteFirstNonEmpty<ISearchForNewBook, List<Book>>(
                p => p.SearchByIsbn(isbn),
                "search-by-isbn",
                p => p.SupportsIsbnLookup);
        }

        public List<Book> SearchByAsin(string asin)
        {
            return ExecuteFirstNonEmpty<ISearchForNewBook, List<Book>>(
                p => p.SearchByAsin(asin),
                "search-by-asin",
                p => p.SupportsBookSearch);
        }

        public List<Book> SearchByExternalId(string idType, string id)
        {
            return ExecuteFirstNonEmpty<ISearchForNewBook, List<Book>>(
                p => p.SearchByExternalId(idType, id),
                "search-by-external-id",
                p => p.SupportsBookSearch || p.SupportsIsbnLookup);
        }

        public List<Author> SearchForNewAuthor(string title)
        {
            return ExecuteFirstNonEmpty<ISearchForNewAuthor, List<Author>>(
                p => p.SearchForNewAuthor(title),
                "search-for-new-author",
                p => p.SupportsAuthorSearch);
        }

        public List<object> SearchForNewEntity(string title)
        {
            return ExecuteFirstNonEmpty<ISearchForNewEntity, List<object>>(
                p => p.SearchForNewEntity(title),
                "search-for-new-entity",
                p => p.SupportsAuthorSearch || p.SupportsBookSearch);
        }

        public Tuple<string, Book, List<AuthorMetadata>> GetBookInfo(string id)
        {
            var result = ExecuteFirst<IProvideBookInfo, Tuple<string, Book, List<AuthorMetadata>>>(
                p => p.GetBookInfo(id),
                "get-book-info",
                p => p.SupportsBookSearch || p.SupportsIsbnLookup,
                p => IsProviderCompatibleWithIdScope(p, id),
                entityId: id,
                throwOnTransportFailure: true);

            if (result == null)
            {
                throw new BookNotFoundException(id);
            }

            TryEnrichEditionMetadata(result.Item2, InferProviderFromScopedId(result.Item2?.ForeignBookId) ?? InferProviderFromScopedId(id));

            return result;
        }

        public Author GetAuthorInfo(string id, bool useCache = true)
        {
            var result = ExecuteFirst<IProvideAuthorInfo, Author>(
                p => p.GetAuthorInfo(id, useCache),
                AuthorInfoOperation,
                p => p.SupportsAuthorSearch,
                p => IsProviderCompatibleWithIdScope(p, id),
                entityId: id,
                throwOnTransportFailure: true);

            if (result == null)
            {
                throw new AuthorNotFoundException(id);
            }

            TryEnrichAuthorMetadata(result);

            return result;
        }

        public HashSet<string> GetChangedAuthors(DateTime startTime)
        {
            return ExecuteFirst<IProvideAuthorInfo, HashSet<string>>(
                p => p.GetChangedAuthors(startTime),
                "get-changed-authors",
                p => p.SupportsAuthorSearch);
        }

        private T ExecuteFirst<TContract, T>(Func<TContract, T> operation,
                                             string operationName,
                                             Func<IMetadataProvider, bool> supports,
                                             Func<IMetadataProvider, bool> compatibility = null,
                                             string entityId = null,
                                             bool throwOnTransportFailure = false,
                                             bool treatEmptyListAsNoData = false,
                                             Func<T, bool> acceptResult = null)
            where TContract : class
            where T : class
        {
            var providers = _registry.GetProviders()
                .Where(p => p is TContract)
                .Where(supports)
                .ToList();

            if (compatibility != null)
            {
                var compatible = providers.Where(compatibility).ToList();

                // Keep legacy behavior if nothing matches compatibility so non-ID operations continue to work.
                if (compatible.Any())
                {
                    providers = compatible;
                }
                else if (providers.Any())
                {
                    // The id is provider-scoped (e.g. "hardcover:work:123" or "openlibrary:work:OL1W")
                    // but no enabled provider is compatible with it. The compatible provider may be
                    // disabled, unregistered, or mismatched by name. Log a clear diagnostic so
                    // operators understand the observed condition (issue #214 / #215).
                    _logger.Warn("No enabled metadata provider is compatible with the provider-scoped id for operation '{0}'. Verify the expected provider is enabled and registered, or re-resolve the entity via an enabled provider.", operationName);
                }
            }

            var trackTransport = operationName is AuthorInfoOperation or BookSearchOperation;
            var transportFailures = 0;
            var receivedResponse = false;
            Exception lastError = null;
            AuthorNotFoundException notFound = null;
            var allFailuresWereTransport = true;
            var anyFailure = false;

            for (var i = 0; i < providers.Count; i++)
            {
                var provider = providers[i];
                var stopwatch = Stopwatch.StartNew();

                try
                {
                    T result;
                    try
                    {
                        result = operation((TContract)provider);
                    }
                    catch (Exception ex) when (trackTransport && IsTransportError(ex) && TryReserveRetry(provider.ProviderName))
                    {
                        _logger.Debug("Retrying metadata provider '{0}' after a transport failure during {1}", provider.ProviderName, operationName);
                        Thread.Sleep(RetryDelay);
                        result = operation((TContract)provider);
                    }

                    receivedResponse |= result != null;
                    stopwatch.Stop();

                    var returnedNull = result == null;
                    var resultCount = CountResults(result);

                    // "No data" (continue to the next provider) is: a null result, or — for
                    // search/lookup fan-outs only — an empty list. An empty list is the
                    // HTTP-200-with-empty-body case (issue #277): the provider was reachable
                    // but returned zero items, so it must not mask a later provider that has
                    // data. Entity lookups (single-object results) only ever see null, so
                    // treatEmptyListAsNoData is false there and behavior is unchanged.
                    var noData = returnedNull || (treatEmptyListAsNoData && IsNoData(result)) ||
                        (acceptResult != null && !acceptResult(result));

                    // Issue #277: record the result count so an HTTP-200-with-empty-body
                    // (provider reachable, returned zero data) is distinguishable in telemetry
                    // from a genuine failure or a provider that is down. A known-good provider
                    // collapsing to an empty list must be diagnosable without an out-of-band
                    // network probe.
                    _telemetry.Record(
                        provider.ProviderName,
                        operationName,
                        stopwatch.ElapsedMilliseconds,
                        !returnedNull,
                        returnedNull,
                        i > 0 && !noData,
                        resultCount);

                    if (noData && resultCount == 0 && providers.Count > 1)
                    {
                        _logger.Warn(
                            "Metadata provider '{0}' returned an empty result for operation '{1}'. Trying the remaining providers. See provider telemetry for result counts and failures.",
                            provider.ProviderName,
                            operationName);
                    }

                    if (!noData)
                    {
                        if (trackTransport)
                        {
                            _telemetry.RecordTransportOutage(operationName, false);
                        }

                        return result;
                    }
                }
                catch (AuthorNotFoundException ex) when (operationName == AuthorInfoOperation)
                {
                    stopwatch.Stop();
                    _telemetry.Record(provider.ProviderName, operationName, stopwatch.ElapsedMilliseconds, false, true, false);
                    receivedResponse |= ex.IsConfirmedMissing;
                    if (ex.IsConfirmedMissing)
                    {
                        notFound ??= ex;
                    }
                }
                catch (Exception ex)
                {
                    stopwatch.Stop();
                    _telemetry.Record(provider.ProviderName, operationName, stopwatch.ElapsedMilliseconds, false, false, false);
                    _logger.Warn(ex, "Metadata provider '{0}' failed during {1}", provider.ProviderName, operationName);
                    var isTransport = IsTransportError(ex);
                    if (isTransport)
                    {
                        transportFailures++;
                    }

                    if (throwOnTransportFailure && ex is MetadataProviderUnavailableException && (!trackTransport || !isTransport))
                    {
                        // A provider that explicitly reports incomplete metadata has not reported a removal.
                        throw;
                    }

                    lastError = ex;

                    anyFailure = true;
                    if (!isTransport)
                    {
                        // A non-transport failure (e.g. a genuine provider error) means we
                        // cannot conclude the whole operation was blocked by the network.
                        // Once a non-transport failure occurs, allFailuresWereTransport
                        // must stay false for the remainder of the loop.
                        allFailuresWereTransport = false;
                    }
                }
            }

            if (lastError != null)
            {
                _logger.Warn(lastError, "All providers failed for operation {0}", operationName);
            }

            if (trackTransport)
            {
                if (providers.Count > 0 && transportFailures == providers.Count)
                {
                    _telemetry.RecordTransportOutage(operationName, true);
                }
                else if (receivedResponse)
                {
                    _telemetry.RecordTransportOutage(operationName, false);
                }
            }

            // Author refresh needs to distinguish an empty lookup from a failed request.
            // Keep existing transport handling for other ID-based metadata lookups.
            if ((operationName == AuthorInfoOperation && anyFailure) ||
                (throwOnTransportFailure && anyFailure && allFailuresWereTransport))
            {
                throw new MetadataProviderUnavailableException(operationName, entityId, lastError);
            }

            if (notFound != null)
            {
                throw notFound;
            }

            return null;
        }

        /// <summary>
        /// Determines whether an exception represents a transient transport/network failure
        /// (host unreachable, DNS failure, timeout, connection reset, TLS handshake failure)
        /// as opposed to a definitive provider-level error such as a 404.
        /// </summary>
        internal static bool IsTransportError(Exception ex)
        {
            for (var current = ex; current != null; current = current.InnerException)
            {
                var failedWebConnection = current is WebException webException &&
                    webException.Status is WebExceptionStatus.ConnectFailure or WebExceptionStatus.ConnectionClosed or
                        WebExceptionStatus.NameResolutionFailure or WebExceptionStatus.ProxyNameResolutionFailure or
                        WebExceptionStatus.ReceiveFailure or WebExceptionStatus.Timeout;
                if ((current is HttpRequestException httpException && httpException.StatusCode == null) ||
                    current is System.Net.Sockets.SocketException ||
                    failedWebConnection ||
                    current is TimeoutException ||
                    current is System.Security.Authentication.AuthenticationException)
                {
                    return true;
                }
            }

            return false;
        }

        private bool TryReserveRetry(string providerName)
        {
            // One extra attempt per provider across concurrent and bulk operations within this interval.
            var now = Environment.TickCount64;
            var after = now + (long)RetryInterval.TotalMilliseconds;
            return _nextRetry.TryAdd(providerName, after) ||
                   (_nextRetry.TryGetValue(providerName, out var nextRetry) && nextRetry <= now &&
                    _nextRetry.TryUpdate(providerName, after, nextRetry));
        }

        private static bool IsProviderCompatibleWithIdScope(IMetadataProvider provider, string providerScopedId)
        {
            var expectedProvider = InferProviderFromScopedId(providerScopedId);

            if (expectedProvider == null)
            {
                return true;
            }

            return string.Equals(provider.ProviderName, expectedProvider, StringComparison.OrdinalIgnoreCase);
        }

        private static string InferProviderFromScopedId(string providerScopedId)
        {
            if (providerScopedId.IsNullOrWhiteSpace())
            {
                return null;
            }

            var candidate = providerScopedId.Trim();

            if (candidate.StartsWith("openlibrary:", StringComparison.OrdinalIgnoreCase) ||
                candidate.StartsWith("/authors/OL", StringComparison.OrdinalIgnoreCase) ||
                candidate.StartsWith("/works/OL", StringComparison.OrdinalIgnoreCase) ||
                BareOpenLibraryTokenRegex.IsMatch(candidate))
            {
                return "OpenLibrary";
            }

            if (candidate.StartsWith("googlebooks:", StringComparison.OrdinalIgnoreCase))
            {
                return "GoogleBooks";
            }

            if (candidate.StartsWith("inventaire:", StringComparison.OrdinalIgnoreCase))
            {
                return "Inventaire";
            }

            if (candidate.StartsWith("hardcover:", StringComparison.OrdinalIgnoreCase))
            {
                return "Hardcover";
            }

            return null;
        }

        private T ExecuteFirstNonEmpty<TContract, T>(Func<TContract, T> operation, string operationName, Func<IMetadataProvider, bool> supports)
            where TContract : class
            where T : class
        {
            // Issue #277: a search/lookup fan-out must treat an EMPTY result as "no data" and
            // continue down the provider chain (first NON-EMPTY result wins), not stop on the
            // first non-null list. Before this change an empty list was indistinguishable from
            // a real result (both non-null), so a provider returning an HTTP-200-with-empty-body
            // masked every subsequent provider that actually had data — the root cause of the
            // 0-result lookups reported in the issue. The empty-vs-null distinction only
            // applies to search/lookup fan-out; entity lookups (GetBookInfo/GetAuthorInfo, where
            // a result is a single object and an empty "result" is a null) keep using
            // ExecuteFirst's stop-on-first-non-null.
            return ExecuteFirst(operation, operationName, supports, treatEmptyListAsNoData: true);
        }

        /// <summary>
        /// Returns true when a fan-out result should be treated as "no data" and the search
        /// should continue to the next provider: a null result, or an empty list (the
        /// HTTP-200-with-empty-body case from issue #277). A non-empty list is data.
        /// </summary>
        private static bool IsNoData(object result)
        {
            if (result == null)
            {
                return true;
            }

            return result is System.Collections.ICollection collection && collection.Count == 0;
        }

        /// <summary>
        /// Enriches missing edition identifiers (ASIN) by consulting supplementary providers
        /// when the primary provider returned incomplete data. Uses ISBN-based lookup
        /// against non-primary providers to fill gaps.
        /// </summary>
        private void TryEnrichEditionMetadata(Book book, string primaryProviderName)
        {
            if (book?.Editions?.Value == null)
            {
                return;
            }

            var editionsNeedingData = book.Editions.Value
                .Where(edition => edition?.Isbn13.IsNotNullOrWhiteSpace() == true && NeedsSupplementaryMetadata(book, edition))
                .ToList();

            if (editionsNeedingData.Count == 0)
            {
                return;
            }

            var supplementaryProviders = _registry.GetProviders()
                .Where(provider => provider is ISearchForNewBook && provider.SupportsIsbnLookup &&
                                   !string.Equals(provider.ProviderName, primaryProviderName, StringComparison.OrdinalIgnoreCase))
                .ToList();

            foreach (var edition in editionsNeedingData)
            {
                foreach (var provider in supplementaryProviders)
                {
                    if (!NeedsSupplementaryMetadata(book, edition))
                    {
                        break;
                    }

                    try
                    {
                        var results = ((ISearchForNewBook)provider).SearchByIsbn(edition.Isbn13);
                        var matches = GetExactIsbnMatches(results, edition.Isbn13).Take(2).ToList();
                        if (matches.Count != 1)
                        {
                            continue;
                        }

                        var (matchedBook, matchedEdition) = matches[0];

                        if (string.Equals(provider.ProviderName, "OpenLibrary", StringComparison.OrdinalIgnoreCase))
                        {
                            StageOpenLibraryWorkSource(book, matchedBook);
                        }

                        if (edition.Asin.IsNullOrWhiteSpace() && matchedEdition.Asin.IsNotNullOrWhiteSpace())
                        {
                            edition.Asin = matchedEdition.Asin;
                        }

                        if (edition.PageCount == 0 && matchedEdition.PageCount > 0)
                        {
                            edition.PageCount = matchedEdition.PageCount;
                        }

                        if (!book.ReleaseDate.HasValue && matchedBook.ReleaseDate.HasValue)
                        {
                            book.ReleaseDate = matchedBook.ReleaseDate;
                        }

                        edition.ReleaseDate ??= matchedEdition.ReleaseDate;
                        if (edition.Publisher.IsNullOrWhiteSpace() && matchedEdition.Publisher.IsNotNullOrWhiteSpace())
                        {
                            edition.Publisher = matchedEdition.Publisher;
                        }

                        if (edition.Language.IsNullOrWhiteSpace() && matchedEdition.Language.IsNotNullOrWhiteSpace())
                        {
                            edition.Language = matchedEdition.Language;
                        }

                        if (edition.Overview.IsNullOrWhiteSpace() && matchedEdition.Overview.IsNotNullOrWhiteSpace())
                        {
                            edition.Overview = matchedEdition.Overview;
                        }

                        if (provider.SupportsCoverImages && edition.Images?.Any(IsCover) != true)
                        {
                            var cover = matchedEdition.Images?.FirstOrDefault(IsCover);
                            if (cover != null)
                            {
                                edition.Images ??= new List<MediaCover.MediaCover>();
                                edition.Images.Add(CopyImage(cover, provider.ProviderName, matchedEdition.ForeignEditionId));
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.Debug(
                            ex,
                            "Metadata enrichment from '{0}' failed for ISBN '{1}'",
                            provider.ProviderName,
                            edition.Isbn13);
                    }
                }
            }
        }

        private void TryEnrichAuthorMetadata(Author author)
        {
            var metadata = author.Metadata?.Value;
            if (metadata?.Name.IsNullOrWhiteSpace() != false ||
                !string.Equals(InferProviderFromScopedId(metadata.ForeignAuthorId), "Hardcover", StringComparison.OrdinalIgnoreCase) ||
                !NeedsSupplementaryAuthorMetadata(metadata))
            {
                return;
            }

            var provider = _registry.GetProviders().FirstOrDefault(candidate =>
                candidate is ISearchForNewBook && candidate.SupportsIsbnLookup && candidate.SupportsCoverImages &&
                string.Equals(candidate.ProviderName, "OpenLibrary", StringComparison.OrdinalIgnoreCase)) as ISearchForNewBook;
            if (provider == null || author.Books?.Value == null)
            {
                return;
            }

            var isbns = author.Books.Value
                .Where(book => book?.Editions?.Value != null)
                .SelectMany(book => book.Editions.Value)
                .Select(edition => edition?.Isbn13)
                .Where(isbn => Isbn.AreEquivalent(isbn, isbn))
                .Distinct()
                .Take(MaxPortraitIsbnLookups);
            var knownAuthorId = OpenLibraryIdNormalizer.NormalizeAuthorId(metadata.OpenLibraryAuthorId);

            foreach (var isbn in isbns)
            {
                try
                {
                    var candidates = GetExactIsbnMatches(provider.SearchByIsbn(isbn), isbn)
                        .Select(match => match.Book.AuthorMetadata?.Value)
                        .Where(candidate => string.Equals(candidate?.Name?.Trim(), metadata.Name.Trim(), StringComparison.OrdinalIgnoreCase))
                        .Select(candidate => (Author: candidate, Id: OpenLibraryIdNormalizer.NormalizeAuthorId(candidate.OpenLibraryAuthorId)))
                        .Where(candidate => candidate.Id != null)
                        .ToList();
                    var candidateIds = candidates.Select(candidate => candidate.Id).Distinct().ToList();

                    if (candidateIds.Count != 1 || (knownAuthorId != null && candidateIds[0] != knownAuthorId))
                    {
                        continue;
                    }

                    // Once a match corroborates the identity, another ISBN must
                    // resolve to the same author before it may fill remaining fields.
                    knownAuthorId = candidateIds[0];
                    StageOpenLibrarySource(metadata.PendingSourceAssociations, knownAuthorId, "exact-isbn-and-author");
                    var source = candidates[0].Author;
                    if (metadata.Overview.IsNullOrWhiteSpace() && source.Overview.IsNotNullOrWhiteSpace())
                    {
                        metadata.Overview = source.Overview;
                    }

                    metadata.Born ??= source.Born;
                    metadata.Died ??= source.Died;

                    if (metadata.Images?.Any(IsPoster) != true)
                    {
                        var portrait = candidates.SelectMany(candidate => candidate.Author.Images ?? Enumerable.Empty<MediaCover.MediaCover>())
                            .FirstOrDefault(IsPoster);
                        if (portrait != null)
                        {
                            metadata.Images ??= new List<MediaCover.MediaCover>();
                            metadata.Images.Add(CopyImage(portrait, "OpenLibrary", knownAuthorId));
                        }
                    }

                    if (!NeedsSupplementaryAuthorMetadata(metadata))
                    {
                        return;
                    }
                }
                catch (Exception ex)
                {
                    _logger.Debug(ex, "Author metadata enrichment from Open Library failed for ISBN '{0}'", isbn);
                    return;
                }
            }
        }

        internal static IEnumerable<(Book Book, Edition Edition)> GetExactIsbnMatches(IEnumerable<Book> books, string isbn)
        {
            return (books ?? Enumerable.Empty<Book>())
                .Where(book => book?.Editions?.Value != null)
                .SelectMany(book => book.Editions.Value.Select(edition => (Book: book, Edition: edition)))
                .Where(match => Isbn.AreEquivalent(isbn, match.Edition?.Isbn13));
        }

        private static void StageOpenLibraryWorkSource(Book book, Book source)
        {
            var sourceId = BookWorkIdentity.GetOpenLibraryWorkId(source);
            var knownId = BookWorkIdentity.GetOpenLibraryWorkId(book);
            var bookAuthor = book.AuthorMetadata?.Value;
            var sourceAuthor = source.AuthorMetadata?.Value;
            var knownAuthorId = OpenLibraryIdNormalizer.NormalizeAuthorId(bookAuthor?.OpenLibraryAuthorId);
            var sourceAuthorId = OpenLibraryIdNormalizer.NormalizeAuthorId(sourceAuthor?.OpenLibraryAuthorId);

            if (sourceId != null && (knownId == null || sourceId == knownId) &&
                (knownAuthorId == null || knownAuthorId == sourceAuthorId) &&
                HaveEqualNonEmptyText(book.Title, source.Title) &&
                HaveEqualNonEmptyText(bookAuthor?.Name, sourceAuthor?.Name))
            {
                StageOpenLibrarySource(book.PendingSourceAssociations, sourceId, "exact-isbn-and-title-and-author");
            }
        }

        private static void StageOpenLibrarySource(List<CatalogueSourceAssociation> sources, string sourceId, string evidence)
        {
            if (!sources.Any(source => string.Equals(source.Provider, "OpenLibrary", StringComparison.OrdinalIgnoreCase) && source.ProviderId == sourceId))
            {
                sources.Add(new CatalogueSourceAssociation
                {
                    Provider = "OpenLibrary",
                    ProviderId = sourceId,
                    Evidence = evidence
                });
            }
        }

        private static bool HaveEqualNonEmptyText(string first, string second)
        {
            return first.IsNotNullOrWhiteSpace() && string.Equals(first.Trim(), second?.Trim(), StringComparison.OrdinalIgnoreCase);
        }

        private static bool NeedsSupplementaryMetadata(Book book, Edition edition)
        {
            return edition.Asin.IsNullOrWhiteSpace() ||
                   edition.PageCount == 0 ||
                   !book.ReleaseDate.HasValue ||
                   !edition.ReleaseDate.HasValue ||
                   edition.Publisher.IsNullOrWhiteSpace() ||
                   edition.Language.IsNullOrWhiteSpace() ||
                   edition.Overview.IsNullOrWhiteSpace() ||
                   edition.Images?.Any(IsCover) != true;
        }

        private static bool NeedsSupplementaryAuthorMetadata(AuthorMetadata metadata)
        {
            return metadata.Overview.IsNullOrWhiteSpace() || metadata.Images?.Any(IsPoster) != true;
        }

        private static MediaCover.MediaCover CopyImage(MediaCover.MediaCover image, string sourceProvider, string sourceId)
        {
            var hasSource = image.SourceProvider.IsNotNullOrWhiteSpace();
            return new MediaCover.MediaCover(image.CoverType, image.Url)
            {
                SourceProvider = hasSource ? image.SourceProvider : sourceProvider,
                SourceId = hasSource ? image.SourceId : sourceId
            };
        }

        private static bool IsCover(MediaCover.MediaCover image)
        {
            return image?.CoverType == MediaCoverTypes.Cover && image.Url.IsNotNullOrWhiteSpace();
        }

        private static bool IsPoster(MediaCover.MediaCover image)
        {
            return image?.CoverType == MediaCoverTypes.Poster && image.Url.IsNotNullOrWhiteSpace();
        }

        /// <summary>
        /// Counts the results a provider operation returned, for telemetry. The orchestrator's
        /// fan-out methods always return a non-null List when a provider was reached, so this
        /// distinguishes "provider returned zero items" (empty list) from "provider returned
        /// items" (non-empty list). A null result (provider threw / no provider) reports 0.
        /// </summary>
        private static int CountResults(object result)
        {
            if (result == null)
            {
                return 0;
            }

            if (result is System.Collections.ICollection collection)
            {
                return collection.Count;
            }

            return 1;
        }
    }
}
