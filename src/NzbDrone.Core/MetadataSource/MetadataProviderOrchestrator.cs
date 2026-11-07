using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Books;
using NzbDrone.Core.Exceptions;

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
        // Matches bare Open Library identifiers like OL123A (author), OL456W (work), OL789M (edition).
        private static readonly Regex BareOpenLibraryTokenRegex = new Regex("^OL\\d+[AWM]$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

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
            return ExecuteFirstNonEmpty<ISearchForNewBook, List<Book>>(
                p => p.SearchForNewBook(title, author, getAllEditions),
                "search-for-new-book",
                p => p.SupportsBookSearch);
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

            TryEnrichEditionIdentifiers(result.Item2, InferProviderFromScopedId(id));
            TryEnrichEditionMetadata(result.Item2, InferProviderFromScopedId(id));

            return result;
        }

        public Author GetAuthorInfo(string id, bool useCache = true)
        {
            var result = ExecuteFirst<IProvideAuthorInfo, Author>(
                p => p.GetAuthorInfo(id, useCache),
                "get-author-info",
                p => p.SupportsAuthorSearch,
                p => IsProviderCompatibleWithIdScope(p, id),
                entityId: id,
                throwOnTransportFailure: true);

            if (result == null)
            {
                throw new AuthorNotFoundException(id);
            }

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
                                             bool treatEmptyListAsNoData = false)
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
                    _logger.Warn("No enabled metadata provider is compatible with the provider-scoped id for operation '{0}'. The lookup will fail with a not-found. Verify the expected provider is enabled and registered, or re-resolve the entity via an enabled provider.", operationName);
                }
            }

            Exception lastError = null;
            var allFailuresWereTransport = true;
            var anyFailure = false;

            for (var i = 0; i < providers.Count; i++)
            {
                var provider = providers[i];
                var stopwatch = Stopwatch.StartNew();

                try
                {
                    var result = operation((TContract)provider);
                    stopwatch.Stop();

                    var returnedNull = result == null;
                    var resultCount = CountResults(result);

                    // "No data" (continue to the next provider) is: a null result, or — for
                    // search/lookup fan-outs only — an empty list. An empty list is the
                    // HTTP-200-with-empty-body case (issue #277): the provider was reachable
                    // but returned zero items, so it must not mask a later provider that has
                    // data. Entity lookups (single-object results) only ever see null, so
                    // treatEmptyListAsNoData is false there and behavior is unchanged.
                    var noData = returnedNull || (treatEmptyListAsNoData && IsNoData(result));

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
                            "Metadata provider '{0}' returned an EMPTY result set for {1} (operation '{2}') while at least one other enabled provider is configured. The lookup is continuing down the provider chain; if every provider returns empty, the endpoint will report 200 with zero results even though the providers are reachable. Inspect provider telemetry (provider, operation, resultCount, error) before treating this as a genuine no-data.",
                            provider.ProviderName,
                            entityId ?? "<search>",
                            operationName);
                    }

                    if (!noData)
                    {
                        return result;
                    }
                }
                catch (Exception ex)
                {
                    stopwatch.Stop();
                    _telemetry.Record(provider.ProviderName, operationName, stopwatch.ElapsedMilliseconds, false, false, false);
                    _logger.Warn(ex, "Metadata provider '{0}' failed during {1}", provider.ProviderName, operationName);
                    lastError = ex;

                    anyFailure = true;
                    if (!IsTransportError(ex))
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

            if (throwOnTransportFailure && anyFailure && allFailuresWereTransport)
            {
                // Every provider failed with a transport/network error. This is a transient
                // outage, NOT a "provider removed this entity" signal. Surface it distinctly
                // so callers never interpret it as a genuine not-found (which would lead to
                // destructive deletion of local data — see issue #204 / #209).
                throw new MetadataProviderUnavailableException(operationName, entityId, lastError);
            }

            return null;
        }

        /// <summary>
        /// Determines whether an exception represents a transient transport/network failure
        /// (host unreachable, DNS failure, timeout, connection reset, TLS handshake failure)
        /// as opposed to a definitive provider-level error such as a 404.
        /// </summary>
        private static bool IsTransportError(Exception ex)
        {
            for (var current = ex; current != null; current = current.InnerException)
            {
                if (current is HttpRequestException ||
                    current is System.Net.Sockets.SocketException ||
                    current is WebException ||
                    current is TimeoutException ||
                    current is System.Security.Authentication.AuthenticationException)
                {
                    return true;
                }
            }

            return false;
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
        private void TryEnrichEditionIdentifiers(Book book, string primaryProviderName)
        {
            if (book?.Editions?.Value == null)
            {
                return;
            }

            var editionsNeedingAsin = book.Editions.Value
                .Where(e => e.Asin.IsNullOrWhiteSpace() && e.Isbn13.IsNotNullOrWhiteSpace())
                .ToList();

            if (!editionsNeedingAsin.Any())
            {
                return;
            }

            var supplementaryProviders = _registry.GetProviders()
                .Where(p => p is ISearchForNewBook)
                .Where(p => p.SupportsIsbnLookup)
                .Where(p => primaryProviderName == null ||
                            !string.Equals(p.ProviderName, primaryProviderName, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (!supplementaryProviders.Any())
            {
                return;
            }

            foreach (var edition in editionsNeedingAsin)
            {
                foreach (var provider in supplementaryProviders)
                {
                    try
                    {
                        var results = ((ISearchForNewBook)provider).SearchByIsbn(edition.Isbn13);
                        var matchedAsin = results?
                            .SelectMany(b => b.Editions?.Value ?? new List<Edition>())
                            .Select(e => e.Asin)
                            .FirstOrDefault(a => a.IsNotNullOrWhiteSpace());

                        if (matchedAsin != null)
                        {
                            edition.Asin = matchedAsin;
                            _logger.Debug(
                                "Enriched ASIN '{0}' for edition '{1}' from provider '{2}'",
                                edition.Asin,
                                edition.ForeignEditionId,
                                provider.ProviderName);
                            break;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.Debug(
                            ex,
                            "ASIN enrichment from '{0}' failed for ISBN '{1}'",
                            provider.ProviderName,
                            edition.Isbn13);
                    }
                }
            }
        }

        /// <summary>
        /// Enriches missing edition metadata (page count, release date, language) by
        /// consulting supplementary providers when the primary provider returned
        /// incomplete data. Uses ISBN-based lookup against non-primary providers.
        /// </summary>
        private void TryEnrichEditionMetadata(Book book, string primaryProviderName)
        {
            if (book?.Editions?.Value == null)
            {
                return;
            }

            var editionsNeedingData = book.Editions.Value
                .Where(e => e.Isbn13.IsNotNullOrWhiteSpace() &&
                            (e.PageCount == 0 || !book.ReleaseDate.HasValue || e.Language.IsNullOrWhiteSpace()))
                .ToList();

            if (!editionsNeedingData.Any())
            {
                return;
            }

            var supplementaryProviders = _registry.GetProviders()
                .Where(p => p is ISearchForNewBook)
                .Where(p => p.SupportsIsbnLookup)
                .Where(p => primaryProviderName == null ||
                            !string.Equals(p.ProviderName, primaryProviderName, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (!supplementaryProviders.Any())
            {
                return;
            }

            foreach (var edition in editionsNeedingData)
            {
                foreach (var provider in supplementaryProviders)
                {
                    try
                    {
                        var results = ((ISearchForNewBook)provider).SearchByIsbn(edition.Isbn13);
                        var matchedBook = results?.FirstOrDefault();
                        var matchedEdition = matchedBook?.Editions?.Value?.FirstOrDefault();

                        if (matchedEdition == null)
                        {
                            continue;
                        }

                        var enriched = false;

                        if (edition.PageCount == 0 && matchedEdition.PageCount > 0)
                        {
                            edition.PageCount = matchedEdition.PageCount;
                            enriched = true;
                        }

                        if (!book.ReleaseDate.HasValue && matchedBook.ReleaseDate.HasValue)
                        {
                            book.ReleaseDate = matchedBook.ReleaseDate;
                            enriched = true;
                        }

                        if (edition.Language.IsNullOrWhiteSpace() && matchedEdition.Language.IsNotNullOrWhiteSpace())
                        {
                            edition.Language = matchedEdition.Language;
                            enriched = true;
                        }

                        if (edition.Overview.IsNullOrWhiteSpace() && matchedEdition.Overview.IsNotNullOrWhiteSpace())
                        {
                            edition.Overview = matchedEdition.Overview;
                            enriched = true;
                        }

                        if (enriched)
                        {
                            _logger.Debug(
                                "Enriched metadata for edition '{0}' (ISBN {1}) from provider '{2}'",
                                edition.ForeignEditionId,
                                edition.Isbn13,
                                provider.ProviderName);
                            break;
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

        // ── ISearchForNewBook / ISearchForNewAuthor (V1 fan-out surface) ────────────
        // Issue #277: the /api/v1/book/lookup and /api/v1/author/lookup endpoints inject
        // these raw provider contracts directly, which DryIoc (auto-concrete-type
        // resolution, first concrete wins) binds to a single provider and bypasses this
        // orchestrator entirely. As a result those endpoints got neither priority-ordered
        // fan-out (Hardcover -> OpenLibrary fallback) nor the per-provider telemetry that
        // /api/v1/search (via IMetadataProviderOrchestrator) records. Declaring the
        // orchestrator as a concrete implementer of the V1 contracts means DryIoc resolves
        // them to the orchestrator, so every lookup path goes through ExecuteFirst:
        // enabled providers are tried in priority order, the first non-empty result wins,
        // and every provider call is recorded (provider, operation, latency, success,
        // null, fallback, result count) so an empty fan-out is diagnosable in telemetry.
        //
        // No new method bodies are needed: SearchForNewBook / SearchForNewAuthor /
        // SearchByIsbn / SearchByAsin / SearchByExternalId are already implemented for
        // IMetadataProviderOrchestrator (above), and those signatures satisfy the V1
        // ISearchForNewBook / ISearchForNewAuthor contracts exactly. Declaring the V1
        // interfaces on the class is therefore sufficient to make DryIoc resolve them to
        // this orchestrator.

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
