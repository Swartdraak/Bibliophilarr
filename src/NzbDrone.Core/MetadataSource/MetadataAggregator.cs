using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Common.Http;
using NzbDrone.Core.Books;

namespace NzbDrone.Core.MetadataSource
{
    public class MetadataAggregator : IMetadataAggregator
    {
        private readonly IMetadataProviderRegistry _providerRegistry;
        private readonly IMetadataQualityScorer _qualityScorer;
        private readonly IMetadataConflictResolutionPolicy _conflictPolicy;
        private readonly IProviderTelemetryService _providerTelemetryService;
        private readonly Logger _logger;

        public MetadataAggregator(IMetadataProviderRegistry providerRegistry,
                                  IMetadataQualityScorer qualityScorer,
                                  IMetadataConflictResolutionPolicy conflictPolicy,
                                  IProviderTelemetryService providerTelemetryService,
                                  Logger logger)
        {
            _providerRegistry = providerRegistry;
            _qualityScorer = qualityScorer;
            _conflictPolicy = conflictPolicy;
            _providerTelemetryService = providerTelemetryService;
            _logger = logger;
        }

        public async Task<AggregatedResult<Book>> GetBookMetadataAsync(string identifier, string identifierType, AggregationOptions options = null)
        {
            options ??= new AggregationOptions();
            var result = new AggregatedResult<Book>();
            var candidates = new List<MetadataProviderBookCandidate>();

            foreach (var provider in _providerRegistry.GetBookSearchProviders().Take(Math.Max(1, options.MaxProviders)))
            {
                result.QueriedProviders.Add(provider.ProviderName);
                var watch = Stopwatch.StartNew();

                try
                {
                    var books = await SearchByIdentifierAsync(provider, identifierType, identifier, BuildBookSearchOptions(options));
                    watch.Stop();

                    var providerBooks = books ?? new List<Book>();
                    _providerTelemetryService.RecordSuccess(provider.ProviderName, "aggregator-book-metadata", watch.Elapsed.TotalMilliseconds, providerBooks.Count);

                    var best = providerBooks
                        .OrderByDescending(_qualityScorer.CalculateBookScore)
                        .FirstOrDefault();

                    if (best != null)
                    {
                        candidates.Add(new MetadataProviderBookCandidate
                        {
                            ProviderName = provider.ProviderName,
                            Book = best,
                            QualityScore = _qualityScorer.CalculateBookScore(best)
                        });

                        if (options.StopOnFirstSuccess && options.Strategy == AggregationStrategy.FirstAcceptable)
                        {
                            break;
                        }
                    }
                }
                catch (HttpException ex) when (IsTransientProviderStatus(ex.Response?.StatusCode))
                {
                    watch.Stop();
                    result.FailedProviders[provider.ProviderName] = ex.Message;
                    RecordTransientTelemetry(provider.ProviderName, "aggregator-book-metadata", ex);
                }
                catch (HttpException ex) when (IsNotFoundStatus(ex.Response?.StatusCode))
                {
                    watch.Stop();
                    _logger.Debug("Provider {0} returned not-found for book identifier during aggregator-book-metadata", provider.ProviderName);
                }
                catch (HttpException ex) when (IsAuthFailureStatus(ex.Response?.StatusCode))
                {
                    watch.Stop();
                    result.FailedProviders[provider.ProviderName] = ex.Message;
                    _logger.Warn("Provider {0} returned auth failure ({1}) during aggregator-book-metadata — check API key configuration", provider.ProviderName, ex.Response.StatusCode);
                    _providerTelemetryService.RecordFailure(provider.ProviderName, "aggregator-book-metadata", ex);
                }
                catch (Exception ex)
                {
                    result.FailedProviders[provider.ProviderName] = ex.Message;
                    _providerTelemetryService.RecordFailure(provider.ProviderName, "aggregator-book-metadata", ex);
                }
            }

            var decision = _conflictPolicy.ResolveBookConflict(candidates);
            result.Result = decision.SelectedBook;
            result.ProviderName = decision.SelectedProvider;
            result.QualityScore = decision.SelectedBook != null ? _qualityScorer.CalculateBookScore(decision.SelectedBook) : 0;
            result.IsMerged = false;
            result.MergedFromProviders = decision.EvaluatedProviders ?? new List<string>();

            return result;
        }

        public async Task<List<Book>> SearchBooksAsync(string title, string author = null, AggregationOptions options = null)
        {
            options ??= new AggregationOptions();
            var providerResults = new List<List<Book>>();
            var topCandidates = new List<MetadataProviderBookCandidate>();

            foreach (var provider in _providerRegistry.GetBookSearchProviders().Take(Math.Max(1, options.MaxProviders)))
            {
                var watch = Stopwatch.StartNew();

                try
                {
                    var results = await provider.SearchForNewBookAsync(title, author, BuildBookSearchOptions(options));
                    watch.Stop();

                    var books = results ?? new List<Book>();
                    _providerTelemetryService.RecordSuccess(provider.ProviderName, "aggregator-search-books", watch.Elapsed.TotalMilliseconds, books.Count);
                    providerResults.Add(books);

                    var best = books
                        .OrderByDescending(_qualityScorer.CalculateBookScore)
                        .FirstOrDefault();

                    if (best != null)
                    {
                        topCandidates.Add(new MetadataProviderBookCandidate
                        {
                            ProviderName = provider.ProviderName,
                            Book = best,
                            QualityScore = _qualityScorer.CalculateBookScore(best)
                        });
                    }

                    if (books.Any() && options.StopOnFirstSuccess && options.Strategy == AggregationStrategy.FirstAcceptable)
                    {
                        break;
                    }
                }
                catch (HttpException ex) when (IsTransientProviderStatus(ex.Response?.StatusCode))
                {
                    watch.Stop();
                    RecordTransientTelemetry(provider.ProviderName, "aggregator-search-books", ex);
                }
                catch (HttpException ex) when (IsNotFoundStatus(ex.Response?.StatusCode))
                {
                    watch.Stop();
                    _logger.Debug("Provider {0} returned not-found during aggregator-search-books", provider.ProviderName);
                }
                catch (HttpException ex) when (IsAuthFailureStatus(ex.Response?.StatusCode))
                {
                    watch.Stop();
                    _logger.Warn("Provider {0} returned auth failure ({1}) during aggregator-search-books — check API key configuration", provider.ProviderName, ex.Response.StatusCode);
                    _providerTelemetryService.RecordFailure(provider.ProviderName, "aggregator-search-books", ex);
                }
                catch (Exception ex)
                {
                    _providerTelemetryService.RecordFailure(provider.ProviderName, "aggregator-search-books", ex);
                }
            }

            var merged = MergeSearchResults(providerResults);
            if (!topCandidates.Any())
            {
                return merged;
            }

            var decision = _conflictPolicy.ResolveBookConflict(topCandidates);
            if (decision.SelectedBook == null)
            {
                return merged;
            }

            var ordered = merged
                .Where(b => b != null)
                .OrderByDescending(b => b.ForeignBookId == decision.SelectedBook.ForeignBookId)
                .ToList();

            return ordered;
        }

        public async Task<AggregatedResult<Author>> GetAuthorMetadataAsync(string identifier, string identifierType, AggregationOptions options = null)
        {
            options ??= new AggregationOptions();
            var result = new AggregatedResult<Author>();

            foreach (var provider in _providerRegistry.GetAuthorInfoProviders().Take(Math.Max(1, options.MaxProviders)))
            {
                result.QueriedProviders.Add(provider.ProviderName);
                var watch = Stopwatch.StartNew();

                try
                {
                    var author = await provider.GetAuthorInfoByIdentifierAsync(identifierType, identifier, new AuthorInfoOptions { UseCache = true });
                    watch.Stop();

                    var score = _qualityScorer.CalculateAuthorScore(author);
                    _providerTelemetryService.RecordSuccess(provider.ProviderName, "aggregator-author-metadata", watch.Elapsed.TotalMilliseconds, author == null ? 0 : 1);

                    if (author != null && _qualityScorer.IsQualityAcceptable(score))
                    {
                        result.Result = author;
                        result.ProviderName = provider.ProviderName;
                        result.QualityScore = score;
                        return result;
                    }
                }
                catch (HttpException ex) when (IsTransientProviderStatus(ex.Response?.StatusCode))
                {
                    watch.Stop();
                    result.FailedProviders[provider.ProviderName] = ex.Message;
                    RecordTransientTelemetry(provider.ProviderName, "aggregator-author-metadata", ex);
                }
                catch (HttpException ex) when (IsNotFoundStatus(ex.Response?.StatusCode))
                {
                    watch.Stop();
                    _logger.Debug("Provider {0} returned not-found during aggregator-author-metadata", provider.ProviderName);
                }
                catch (HttpException ex) when (IsAuthFailureStatus(ex.Response?.StatusCode))
                {
                    watch.Stop();
                    result.FailedProviders[provider.ProviderName] = ex.Message;
                    _logger.Warn("Provider {0} returned auth failure ({1}) during aggregator-author-metadata — check API key configuration", provider.ProviderName, ex.Response.StatusCode);
                    _providerTelemetryService.RecordFailure(provider.ProviderName, "aggregator-author-metadata", ex);
                }
                catch (Exception ex)
                {
                    result.FailedProviders[provider.ProviderName] = ex.Message;
                    _providerTelemetryService.RecordFailure(provider.ProviderName, "aggregator-author-metadata", ex);
                }
            }

            return result;
        }

        public async Task<List<Author>> SearchAuthorsAsync(string name, AggregationOptions options = null)
        {
            options ??= new AggregationOptions();
            var results = new List<Author>();

            foreach (var provider in _providerRegistry.GetAuthorSearchProviders().Take(Math.Max(1, options.MaxProviders)))
            {
                var watch = Stopwatch.StartNew();

                try
                {
                    var providerResults = await provider.SearchForNewAuthorAsync(name, new AuthorSearchOptions { UseCache = true });
                    watch.Stop();

                    var authors = providerResults ?? new List<Author>();
                    _providerTelemetryService.RecordSuccess(provider.ProviderName, "aggregator-search-authors", watch.Elapsed.TotalMilliseconds, authors.Count);

                    results.AddRange(authors);

                    if (authors.Any() && options.StopOnFirstSuccess && options.Strategy == AggregationStrategy.FirstAcceptable)
                    {
                        break;
                    }
                }
                catch (HttpException ex) when (IsTransientProviderStatus(ex.Response?.StatusCode))
                {
                    watch.Stop();
                    RecordTransientTelemetry(provider.ProviderName, "aggregator-search-authors", ex);
                }
                catch (HttpException ex) when (IsNotFoundStatus(ex.Response?.StatusCode))
                {
                    watch.Stop();
                    _logger.Debug("Provider {0} returned not-found during aggregator-search-authors", provider.ProviderName);
                }
                catch (HttpException ex) when (IsAuthFailureStatus(ex.Response?.StatusCode))
                {
                    watch.Stop();
                    _logger.Warn("Provider {0} returned auth failure ({1}) during aggregator-search-authors — check API key configuration", provider.ProviderName, ex.Response.StatusCode);
                    _providerTelemetryService.RecordFailure(provider.ProviderName, "aggregator-search-authors", ex);
                }
                catch (Exception ex)
                {
                    _providerTelemetryService.RecordFailure(provider.ProviderName, "aggregator-search-authors", ex);
                }
            }

            return results
                .Where(a => a != null)
                .GroupBy(a => a.ForeignAuthorId ?? a.Name)
                .Select(g => g.First())
                .ToList();
        }

        public Book MergeBookMetadata(List<Book> books)
        {
            return books?
                .Where(b => b != null)
                .OrderByDescending(_qualityScorer.CalculateBookScore)
                .FirstOrDefault();
        }

        public Author MergeAuthorMetadata(List<Author> authors)
        {
            return authors?
                .Where(a => a != null)
                .OrderByDescending(_qualityScorer.CalculateAuthorScore)
                .FirstOrDefault();
        }

        public List<Book> MergeSearchResults(List<List<Book>> providerResults)
        {
            return (providerResults ?? new List<List<Book>>())
                .Where(x => x != null)
                .SelectMany(x => x)
                .Where(x => x != null)
                .GroupBy(x => x.ForeignBookId ?? x.Title)
                .Select(g => g.OrderByDescending(_qualityScorer.CalculateBookScore).First())
                .ToList();
        }

        private static BookSearchOptions BuildBookSearchOptions(AggregationOptions options)
        {
            return new BookSearchOptions
            {
                GetAllEditions = true,
                UseCache = true,
                IncludeCoverImages = true,
                MaxResults = 20
            };
        }

        private static async Task<List<Book>> SearchByIdentifierAsync(ISearchForNewBookV2 provider, string identifierType, string identifier, BookSearchOptions options)
        {
            var type = (identifierType ?? string.Empty).ToLowerInvariant().Trim();

            if (type == "isbn")
            {
                return await provider.SearchByIsbnAsync(identifier, options);
            }

            if (type == "asin")
            {
                return await provider.SearchByAsinAsync(identifier, options);
            }

            return await provider.SearchByIdentifierAsync(type, identifier, options);
        }

        private void RecordTransientTelemetry(string providerName, string operation, HttpException exception)
        {
            if (exception?.Response?.StatusCode == HttpStatusCode.RequestTimeout)
            {
                _providerTelemetryService.RecordTimeout(providerName, operation);
                return;
            }

            if (exception?.Response?.StatusCode == HttpStatusCode.TooManyRequests)
            {
                _logger.Warn("Provider {0} rate-limited during {1}", providerName, operation);
            }

            _providerTelemetryService.RecordFailure(providerName, operation, exception);
        }

        private static bool IsTransientProviderStatus(HttpStatusCode? statusCode)
        {
            if (!statusCode.HasValue)
            {
                return false;
            }

            return statusCode == HttpStatusCode.RequestTimeout ||
                   statusCode == HttpStatusCode.TooManyRequests ||
                   statusCode == HttpStatusCode.ServiceUnavailable;
        }

        private static bool IsNotFoundStatus(HttpStatusCode? statusCode)
        {
            return statusCode == HttpStatusCode.NotFound ||
                   statusCode == HttpStatusCode.Gone;
        }

        private static bool IsAuthFailureStatus(HttpStatusCode? statusCode)
        {
            return statusCode == HttpStatusCode.Unauthorized ||
                   statusCode == HttpStatusCode.Forbidden;
        }
    }
}
