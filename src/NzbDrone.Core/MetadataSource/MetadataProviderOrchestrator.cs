using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using NLog;
using NzbDrone.Core.Books;
using NzbDrone.Core.Exceptions;

namespace NzbDrone.Core.MetadataSource
{
    public class MetadataProviderOrchestrator : IMetadataProviderOrchestrator
    {
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
                p => p.SupportsBookSearch || p.SupportsIsbnLookup);

            if (result == null)
            {
                throw new BookNotFoundException(id);
            }

            return result;
        }

        public Author GetAuthorInfo(string id, bool useCache = true)
        {
            var result = ExecuteFirst<IProvideAuthorInfo, Author>(
                p => p.GetAuthorInfo(id, useCache),
                "get-author-info",
                p => p.SupportsAuthorSearch);

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

        private T ExecuteFirst<TContract, T>(Func<TContract, T> operation, string operationName, Func<IMetadataProvider, bool> supports)
            where TContract : class
            where T : class
        {
            var providers = _registry.GetProviders()
                .Where(p => p is TContract)
                .Where(supports)
                .ToList();

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
                    _telemetry.Record(provider.ProviderName, stopwatch.ElapsedMilliseconds, !returnedNull, returnedNull, i > 0 && !returnedNull);

                    if (!returnedNull)
                    {
                        return result;
                    }
                }
                catch (Exception ex)
                {
                    stopwatch.Stop();
                    _telemetry.Record(provider.ProviderName, stopwatch.ElapsedMilliseconds, false, false, false);
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

        private T ExecuteFirstNonEmpty<TContract, T>(Func<TContract, T> operation, string operationName, Func<IMetadataProvider, bool> supports)
            where TContract : class
            where T : class
        {
            return ExecuteFirst(operation, operationName, supports);
        }
    }
}
