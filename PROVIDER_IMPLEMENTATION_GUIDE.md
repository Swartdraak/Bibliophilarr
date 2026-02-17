# Metadata Provider Implementation Guide

**Version**: 1.0  
**Last Updated**: February 16, 2026  
**Status**: Phase 2 - Infrastructure

---

## Overview

This guide provides comprehensive instructions for implementing metadata providers in Bibliophilarr. It covers the new V2 provider interface hierarchy, implementation patterns, testing strategies, and best practices.

## Table of Contents

1. [Provider Interface Hierarchy](#provider-interface-hierarchy)
2. [Core Interfaces](#core-interfaces)
3. [Implementing a Provider](#implementing-a-provider)
4. [Quality Scoring](#quality-scoring)
5. [Provider Registry](#provider-registry)
6. [Testing Your Provider](#testing-your-provider)
7. [Best Practices](#best-practices)
8. [Examples](#examples)

---

## Provider Interface Hierarchy

The V2 provider system is built on a modular interface hierarchy:

```
IMetadataProvider (base)
    ├── ISearchForNewBookV2
    ├── ISearchForNewAuthorV2
    ├── IProvideBookInfoV2
    └── IProvideAuthorInfoV2

Supporting Interfaces:
    ├── IMetadataQualityScorer
    ├── IMetadataAggregator
    └── IMetadataProviderRegistry
```

### Design Principles

1. **Modular**: Providers implement only the interfaces they support
2. **Async-First**: All I/O operations use async/await
3. **Backward Compatible**: Synchronous methods provided for legacy support
4. **Observable**: Health status and rate limiting are first-class concerns
5. **Testable**: Designed for easy mocking and testing

---

## Core Interfaces

### IMetadataProvider

Base interface that all providers must implement.

```csharp
public interface IMetadataProvider
{
    // Identity
    string ProviderName { get; }        // e.g., "OpenLibrary"
    int Priority { get; }                // Lower = higher priority
    bool IsEnabled { get; }
    
    // Capabilities
    bool SupportsBookSearch { get; }
    bool SupportsAuthorSearch { get; }
    bool SupportsISBNLookup { get; }
    bool SupportsASINLookup { get; }
    bool SupportsSeriesInfo { get; }
    bool SupportsListInfo { get; }
    bool SupportsCoverImages { get; }
    bool SupportsBookInfo { get; }
    bool SupportsAuthorInfo { get; }
    
    // Observability
    ProviderRateLimitInfo GetRateLimitInfo();
    ProviderHealthStatus GetHealthStatus();
}
```

**Key Points:**
- `ProviderName` must be unique across all providers
- `Priority` determines order of provider selection (1 = highest)
- Capability flags enable runtime provider filtering
- Health and rate limit info support monitoring and throttling

### ISearchForNewBookV2

Enhanced interface for searching books.

```csharp
public interface ISearchForNewBookV2 : IMetadataProvider
{
    // Async methods (preferred)
    Task<List<Book>> SearchForNewBookAsync(string title, string author = null, BookSearchOptions options = null);
    Task<List<Book>> SearchByISBNAsync(string isbn, BookSearchOptions options = null);
    Task<List<Book>> SearchByASINAsync(string asin, BookSearchOptions options = null);
    Task<List<Book>> SearchByIdentifierAsync(string identifierType, string identifier, BookSearchOptions options = null);
    
    // Sync methods (backward compatibility)
    List<Book> SearchForNewBook(string title, string author = null, BookSearchOptions options = null);
    List<Book> SearchByISBN(string isbn, BookSearchOptions options = null);
    List<Book> SearchByASIN(string asin, BookSearchOptions options = null);
}
```

**BookSearchOptions:**
```csharp
public class BookSearchOptions
{
    public bool GetAllEditions { get; set; } = true;
    public int MaxResults { get; set; } = 20;
    public bool IncludeCoverImages { get; set; } = true;
    public string PreferredLanguage { get; set; }
    public bool UseCache { get; set; } = true;
}
```

### ISearchForNewAuthorV2

Enhanced interface for searching authors.

```csharp
public interface ISearchForNewAuthorV2 : IMetadataProvider
{
    Task<List<Author>> SearchForNewAuthorAsync(string name, AuthorSearchOptions options = null);
    Task<Author> SearchByIdentifierAsync(string identifierType, string identifier, AuthorSearchOptions options = null);
    List<Author> SearchForNewAuthor(string name, AuthorSearchOptions options = null);
}
```

### IProvideBookInfoV2

Enhanced interface for retrieving detailed book information.

```csharp
public interface IProvideBookInfoV2 : IMetadataProvider
{
    Task<Tuple<string, Book, List<AuthorMetadata>>> GetBookInfoAsync(string providerId, BookInfoOptions options = null);
    Task<Tuple<string, Book, List<AuthorMetadata>>> GetBookInfoByISBNAsync(string isbn, BookInfoOptions options = null);
    Task<Tuple<string, Book, List<AuthorMetadata>>> GetBookInfoByIdentifierAsync(string identifierType, string identifier, BookInfoOptions options = null);
    Tuple<string, Book, List<AuthorMetadata>> GetBookInfo(string providerId, BookInfoOptions options = null);
}
```

### IProvideAuthorInfoV2

Enhanced interface for retrieving detailed author information.

```csharp
public interface IProvideAuthorInfoV2 : IMetadataProvider
{
    Task<Author> GetAuthorInfoAsync(string providerId, AuthorInfoOptions options = null);
    Task<Author> GetAuthorInfoByIdentifierAsync(string identifierType, string identifier, AuthorInfoOptions options = null);
    Task<HashSet<string>> GetChangedAuthorsAsync(DateTime startTime);
    Author GetAuthorInfo(string providerId, AuthorInfoOptions options = null);
    HashSet<string> GetChangedAuthors(DateTime startTime);
}
```

---

## Implementing a Provider

### Step 1: Create Provider Class

```csharp
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NzbDrone.Core.Books;
using NzbDrone.Core.MetadataSource;

namespace NzbDrone.Core.MetadataSource.OpenLibrary
{
    public class OpenLibraryProvider : 
        ISearchForNewBookV2, 
        ISearchForNewAuthorV2, 
        IProvideBookInfoV2, 
        IProvideAuthorInfoV2
    {
        // Implement all required interfaces
    }
}
```

### Step 2: Implement IMetadataProvider Properties

```csharp
public string ProviderName => "OpenLibrary";
public int Priority => 1;  // Primary provider
public bool IsEnabled => _configService.EnableOpenLibrary;

// Capabilities
public bool SupportsBookSearch => true;
public bool SupportsAuthorSearch => true;
public bool SupportsISBNLookup => true;
public bool SupportsASINLookup => false;
public bool SupportsSeriesInfo => false;
public bool SupportsListInfo => false;
public bool SupportsCoverImages => true;
public bool SupportsBookInfo => true;
public bool SupportsAuthorInfo => true;
```

### Step 3: Implement Rate Limiting

```csharp
public ProviderRateLimitInfo GetRateLimitInfo()
{
    return new ProviderRateLimitInfo
    {
        MaxRequests = 100,
        TimeWindow = TimeSpan.FromMinutes(5),
        RequiresApiKey = false,
        SupportsAuthentication = true,
        AuthenticatedMaxRequests = 500
    };
}
```

### Step 4: Implement Health Monitoring

```csharp
private readonly ProviderHealthStatus _healthStatus = new ProviderHealthStatus();

public ProviderHealthStatus GetHealthStatus()
{
    return _healthStatus;
}

private void UpdateHealthAfterRequest(bool success, double responseTimeMs, string errorMessage = null)
{
    _healthStatus.LastChecked = DateTime.UtcNow;
    _healthStatus.AverageResponseTimeMs = (_healthStatus.AverageResponseTimeMs + responseTimeMs) / 2;
    
    if (success)
    {
        _healthStatus.LastSuccess = DateTime.UtcNow;
        _healthStatus.ConsecutiveFailures = 0;
        _healthStatus.SuccessRate = Math.Min(1.0, _healthStatus.SuccessRate + 0.01);
        _healthStatus.Health = _healthStatus.AverageResponseTimeMs < 2000 
            ? ProviderHealth.Healthy 
            : ProviderHealth.Degraded;
    }
    else
    {
        _healthStatus.LastFailure = DateTime.UtcNow;
        _healthStatus.LastErrorMessage = errorMessage;
        _healthStatus.ConsecutiveFailures++;
        _healthStatus.SuccessRate = Math.Max(0.0, _healthStatus.SuccessRate - 0.05);
        _healthStatus.Health = _healthStatus.ConsecutiveFailures > 5 
            ? ProviderHealth.Unhealthy 
            : ProviderHealth.Degraded;
    }
}
```

### Step 5: Implement Search Methods

```csharp
public async Task<List<Book>> SearchForNewBookAsync(string title, string author = null, BookSearchOptions options = null)
{
    options ??= new BookSearchOptions();
    var stopwatch = System.Diagnostics.Stopwatch.StartNew();
    
    try
    {
        // Rate limiting
        await _rateLimiter.WaitAsync();
        
        // Build request
        var url = $"{_baseUrl}/search.json?q={Uri.EscapeDataString(title)}";
        if (!string.IsNullOrWhiteSpace(author))
        {
            url += $"&author={Uri.EscapeDataString(author)}";
        }
        
        // Make request (IHttpClient expects an HttpRequest instance)
        var request = new HttpRequest(url);
        var response = await _httpClient.GetAsync<SearchResponse>(request);
        
        // Map to Book objects
        var books = MapSearchResponseToBooks(response, options);
        
        // Update health
        stopwatch.Stop();
        UpdateHealthAfterRequest(true, stopwatch.ElapsedMilliseconds);
        
        return books;
    }
    catch (Exception ex)
    {
        stopwatch.Stop();
        UpdateHealthAfterRequest(false, stopwatch.ElapsedMilliseconds, ex.Message);
        _logger.Error(ex, $"Error searching for book: {title}");
        throw;
    }
}

// Sync wrapper for backward compatibility
public List<Book> SearchForNewBook(string title, string author = null, BookSearchOptions options = null)
{
    return SearchForNewBookAsync(title, author, options).GetAwaiter().GetResult();
}
```

### Step 6: Implement ISBN Lookup

```csharp
public async Task<List<Book>> SearchByIsbnAsync(string isbn, BookSearchOptions options = null)
{
    options ??= new BookSearchOptions();
    var stopwatch = System.Diagnostics.Stopwatch.StartNew();
    
    try
    {
        await _rateLimiter.WaitAsync();
        
        // Clean ISBN (remove dashes, spaces)
        isbn = isbn.Replace("-", "").Replace(" ", "");
        
        var url = $"{_baseUrl}/isbn/{isbn}.json";
        var request = new HttpRequest(url);
        var response = await _httpClient.GetAsync<EditionResponse>(request);
        
        var book = MapEditionResponseToBook(response, options);
        
        stopwatch.Stop();
        UpdateHealthAfterRequest(true, stopwatch.ElapsedMilliseconds);
        
        return new List<Book> { book };
    }
    catch (HttpException ex) when (ex.Response?.StatusCode == HttpStatusCode.NotFound)
    {
        // ISBN not found is not an error
        stopwatch.Stop();
        UpdateHealthAfterRequest(true, stopwatch.ElapsedMilliseconds);
        return new List<Book>();
    }
    catch (Exception ex)
    {
        stopwatch.Stop();
        UpdateHealthAfterRequest(false, stopwatch.ElapsedMilliseconds, ex.Message);
        _logger.Error(ex, $"Error looking up ISBN: {isbn}");
        throw;
    }
}
```

---

## Quality Scoring

Use the `MetadataQualityScorer` to assess metadata quality:

```csharp
public class MetadataQualityScorer : IMetadataQualityScorer
{
    public int CalculateBookScore(Book book)
    {
        int score = 0;
        
        // Essential fields (60 points)
        if (!string.IsNullOrWhiteSpace(book.Title)) score += 20;
        if (book.AuthorMetadata?.Value != null) score += 20;
        if (!string.IsNullOrWhiteSpace(book.ForeignBookId)) score += 20;
        
        // Important fields (25 points)
        if (book.ReleaseDate.HasValue) score += 5;
        if (!string.IsNullOrWhiteSpace(book.ForeignEditionId)) score += 5;
        if (book.Editions?.Value?.Any() == true) score += 10;
        if (book.Ratings != null && book.Ratings.Votes > 0) score += 5;
        
        // Nice to have (15 points)
        if (book.Genres?.Any() == true) score += 3;
        if (book.Links?.Any() == true) score += 2;
        if (book.SeriesLinks?.Value?.Any() == true) score += 5;
        if (book.RelatedBooks?.Any() == true) score += 2;
        if (book.Editions?.Value?.Any(e => e.Images?.Any() == true) == true) score += 3;
        
        return score;
    }
    
    public bool IsQualityAcceptable(int score)
    {
        return score >= 50;  // Minimum 50% completeness
    }
}
```

---

## Provider Registry

Register your provider with the registry:

```csharp
// In your module or service registration (using the actual DI container)
// Note: This project uses DryIoc. Actual registration patterns should follow
// existing examples in the codebase (e.g., NzbDrone.Core/Datastore/Extensions/CompositionExtensions.cs)

// Example registration (adapt to actual DI setup):
services.AddSingleton<ISearchForNewBookV2, OpenLibraryProvider>();
services.AddSingleton<ISearchForNewAuthorV2, OpenLibraryProvider>();
services.AddSingleton<IProvideBookInfoV2, OpenLibraryProvider>();
services.AddSingleton<IProvideAuthorInfoV2, OpenLibraryProvider>();
```

The registry will automatically discover and manage providers based on their interface implementations.

---

## Testing Your Provider

### Unit Tests

Create a test class for your provider:

```csharp
[TestFixture]
public class OpenLibraryProviderTests
{
    private OpenLibraryProvider _provider;
    private Mock<IHttpClient> _mockHttpClient;
    
    [SetUp]
    public void Setup()
    {
        _mockHttpClient = new Mock<IHttpClient>();
        _provider = new OpenLibraryProvider(_mockHttpClient.Object);
    }
    
    [Test]
    public async Task SearchForNewBookAsync_WithValidTitle_ReturnsResults()
    {
        // Arrange
        var mockResponse = new SearchResponse
        {
            NumFound = 1,
            Docs = new List<SearchDoc>
            {
                new SearchDoc
                {
                    Key = "/works/OL45804W",
                    Title = "Foundation",
                    AuthorName = new List<string> { "Isaac Asimov" }
                }
            }
        };
        
        _mockHttpClient
            .Setup(x => x.GetAsync<SearchResponse>(It.IsAny<string>()))
            .ReturnsAsync(mockResponse);
        
        // Act
        var results = await _provider.SearchForNewBookAsync("Foundation");
        
        // Assert
        Assert.That(results, Is.Not.Empty);
        Assert.That(results[0].Title, Is.EqualTo("Foundation"));
    }
    
    [Test]
    public async Task SearchByISBNAsync_WithValidISBN_ReturnsBook()
    {
        // Test ISBN lookup
    }
    
    [Test]
    public void GetHealthStatus_ReturnsValidStatus()
    {
        // Test health monitoring
    }
}
```

### Integration Tests

Test against real APIs with caching:

```csharp
[TestFixture]
[Category("Integration")]
public class OpenLibraryProviderIntegrationTests
{
    private OpenLibraryProvider _provider;
    
    [Test]
    public async Task RealAPI_SearchForBook_ReturnsResults()
    {
        // Arrange
        _provider = new OpenLibraryProvider(new RealHttpClient());
        
        // Act
        var results = await _provider.SearchForNewBookAsync("The Hobbit", "J.R.R. Tolkien");
        
        // Assert
        Assert.That(results, Is.Not.Empty);
        Assert.That(results.Any(b => b.Title.Contains("Hobbit")), Is.True);
    }
}
```

---

## Best Practices

### 1. Rate Limiting

Always implement proper rate limiting:

```csharp
private readonly SemaphoreSlim _rateLimiter = new SemaphoreSlim(1);
private readonly Queue<DateTime> _requestTimestamps = new Queue<DateTime>();

private async Task WaitForRateLimitAsync()
{
    await _rateLimiter.WaitAsync();
    
    try
    {
        var now = DateTime.UtcNow;
        var rateLimitInfo = GetRateLimitInfo();
        
        // Remove old timestamps outside the window
        while (_requestTimestamps.Any() && 
               now - _requestTimestamps.Peek() > rateLimitInfo.TimeWindow)
        {
            _requestTimestamps.Dequeue();
        }
        
        // Wait if at limit
        if (_requestTimestamps.Count >= rateLimitInfo.MaxRequests)
        {
            var oldestRequest = _requestTimestamps.Peek();
            var waitTime = rateLimitInfo.TimeWindow - (now - oldestRequest);
            
            if (waitTime > TimeSpan.Zero)
            {
                await Task.Delay(waitTime);
            }
        }
        
        _requestTimestamps.Enqueue(now);
    }
    finally
    {
        _rateLimiter.Release();
    }
}
```

### 2. Error Handling

Handle errors gracefully and update health status:

```csharp
using System.Net;
using NzbDrone.Common.Http;

try
{
    // Make API call
}
catch (HttpException ex) when (ex.Response?.StatusCode == HttpStatusCode.NotFound)
{
    // Not found is not an error
    return new List<Book>();
}
catch (HttpException ex) when (ex.Response?.StatusCode == HttpStatusCode.TooManyRequests)
{
    // Rate limited - wait and retry
    _logger.Warn("Rate limited by provider, waiting...");
    await Task.Delay(TimeSpan.FromMinutes(1));
    throw; // Re-throw for retry logic or transient handling upstream
}
catch (Exception ex)
{
    // Unexpected error
    UpdateHealthAfterRequest(false, 0, ex.Message);
    throw;
}
```

### 3. Caching

Implement caching to reduce API calls:

```csharp
private readonly ICache<Book> _bookCache;

public async Task<Book> GetBookInfoAsync(string providerId, BookInfoOptions options = null)
{
    options ??= new BookInfoOptions();
    
    if (options.UseCache)
    {
        var cacheKey = $"book:{providerId}";
        var cached = _bookCache.Get(cacheKey);
        if (cached != null)
        {
            return cached;
        }
    }
    
    var book = await FetchBookFromAPIAsync(providerId);
    
    _bookCache.Set($"book:{providerId}", book, TimeSpan.FromDays(7));
    
    return book;
}
```

### 4. Logging

Use structured logging:

```csharp
_logger.Info("Searching for book: {Title} by {Author}", title, author);
_logger.Debug("Provider {Provider} returned {Count} results", ProviderName, results.Count);
_logger.Error(ex, "Error fetching book {BookId} from {Provider}", bookId, ProviderName);
```

### 5. Mapping

Create separate mapper classes for clean code:

```csharp
public class OpenLibraryMapper
{
    public Book MapSearchDocToBook(SearchDoc doc, BookSearchOptions options)
    {
        var book = new Book
        {
            ForeignBookId = ExtractOLID(doc.Key),
            Title = doc.Title,
            ReleaseDate = doc.FirstPublishYear.HasValue 
                ? new DateTime(doc.FirstPublishYear.Value, 1, 1) 
                : (DateTime?)null,
            Genres = doc.Subject ?? new List<string>()
        };
        
        if (options.IncludeCoverImages && doc.CoverId.HasValue)
        {
            book.Editions = new LazyLoaded<List<Edition>>(new List<Edition>
            {
                new Edition
                {
                    Images = new List<MediaCover>
                    {
                        new MediaCover
                        {
                            Url = $"https://covers.openlibrary.org/b/id/{doc.CoverId}-L.jpg",
                            CoverType = MediaCoverTypes.Cover
                        }
                    }
                }
            });
        }
        
        return book;
    }
}
```

---

## Examples

### Complete Minimal Provider

```csharp
public class MinimalProvider : ISearchForNewBookV2
{
    private readonly IHttpClient _httpClient;
    private readonly Logger _logger;
    
    public string ProviderName => "Minimal";
    public int Priority => 100;
    public bool IsEnabled => true;
    
    public bool SupportsBookSearch => true;
    public bool SupportsAuthorSearch => false;
    public bool SupportsISBNLookup => true;
    // ... other capabilities = false
    
    public ProviderRateLimitInfo GetRateLimitInfo() => new ProviderRateLimitInfo();
    public ProviderHealthStatus GetHealthStatus() => new ProviderHealthStatus { Health = ProviderHealth.Healthy };
    
    public async Task<List<Book>> SearchForNewBookAsync(string title, string author = null, BookSearchOptions options = null)
    {
        var url = $"https://api.example.com/search?q={Uri.EscapeDataString(title)}";
        var response = await _httpClient.GetAsync<SearchResponse>(url);
        return MapResponse(response);
    }
    
    public List<Book> SearchForNewBook(string title, string author = null, BookSearchOptions options = null)
    {
        return SearchForNewBookAsync(title, author, options).GetAwaiter().GetResult();
    }
    
    public async Task<List<Book>> SearchByISBNAsync(string isbn, BookSearchOptions options = null)
    {
        var url = $"https://api.example.com/isbn/{isbn}";
        var response = await _httpClient.GetAsync<BookResponse>(url);
        return new List<Book> { MapBook(response) };
    }
    
    public List<Book> SearchByISBN(string isbn, BookSearchOptions options = null)
    {
        return SearchByISBNAsync(isbn, options).GetAwaiter().GetResult();
    }
    
    // Not implemented - return empty or throw NotImplementedException
    public Task<List<Book>> SearchByASINAsync(string asin, BookSearchOptions options = null) 
        => Task.FromResult(new List<Book>());
    public List<Book> SearchByASIN(string asin, BookSearchOptions options = null) 
        => new List<Book>();
    public Task<List<Book>> SearchByIdentifierAsync(string identifierType, string identifier, BookSearchOptions options = null) 
        => Task.FromResult(new List<Book>());
        
    private List<Book> MapResponse(SearchResponse response) { /* ... */ }
    private Book MapBook(BookResponse response) { /* ... */ }
}
```

---

## Next Steps

1. Review existing provider implementations (BookInfoProxy, GoodreadsProxy)
2. Start with Open Library provider implementation
3. Write comprehensive tests
4. Document API quirks and edge cases
5. Monitor performance and health metrics

---

## Additional Resources

- [Open Library API Documentation](https://openlibrary.org/developers/api)
- [Inventaire API Documentation](https://api.inventaire.io/)
- [MIGRATION_PLAN.md](MIGRATION_PLAN.md) - Complete migration plan
- [CONTRIBUTING.md](CONTRIBUTING.md) - Contribution guidelines

---

**Questions or Issues?**

- Open a discussion: https://github.com/Swartdraak/Bibliophilarr/discussions
- Report bugs: https://github.com/Swartdraak/Bibliophilarr/issues
