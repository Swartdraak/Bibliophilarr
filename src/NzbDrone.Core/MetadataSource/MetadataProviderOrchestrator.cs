using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
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
    public class MetadataProviderOrchestrator : IMetadataProviderOrchestrator
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
                p => IsProviderCompatibleWithIdScope(p, id));

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
                p => IsProviderCompatibleWithIdScope(p, id));

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
                                             Func<IMetadataProvider, bool> compatibility = null)
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
            }

            Exception lastError = null;

            for (var i = 0; i < providers.Count; i++)
            {
                var provider = providers[i];
                var stopwatch = Stopwatch.StartNew();

                try
                {
                    var result = operation((TContract)provider);
                    stopwatch.Stop();

                    var returnedNull = result == null;
                    _telemetry.Record(provider.ProviderName, operationName, stopwatch.ElapsedMilliseconds, !returnedNull, returnedNull, i > 0 && !returnedNull);

                    if (!returnedNull)
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
                }
            }

            if (lastError != null)
            {
                _logger.Warn(lastError, "All providers failed for operation {0}", operationName);
            }

            return null;
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
            return ExecuteFirst(operation, operationName, supports);
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
    }
}
