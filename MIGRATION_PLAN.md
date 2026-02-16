# Bibliophilarr Metadata Migration Plan

## Executive Summary

This document outlines the comprehensive technical plan for migrating Bibliophilarr (formerly Readarr) from proprietary Goodreads metadata to Free and Open Source Software (FOSS) metadata providers. The goal is to create a sustainable, reliable, and community-maintainable book and audiobook collection manager.

## Table of Contents
- [Current State](#current-state)
- [Goals](#goals)
- [FOSS Metadata Provider Options](#foss-metadata-provider-options)
- [Architecture Design](#architecture-design)
- [Implementation Phases](#implementation-phases)
- [Technical Specifications](#technical-specifications)
- [Testing Strategy](#testing-strategy)
- [Migration Tools](#migration-tools)
- [Risks and Mitigations](#risks-and-mitigations)
- [Timeline and Milestones](#timeline-and-milestones)

---

## Current State

### Existing Architecture
Bibliophilarr currently uses a two-tier metadata system:

1. **BookInfoProxy** (Primary Provider)
   - Implements: `IProvideAuthorInfo`, `IProvideBookInfo`, `ISearchForNewBook`, `ISearchForNewAuthor`, `ISearchForNewEntity`
   - JSON-based API
   - Comprehensive book and author metadata
   - Advanced search with special syntax (edition:, author:, work:, isbn:, asin:)

2. **GoodreadsProxy** (Legacy Provider)
   - Implements: `IProvideSeriesInfo`, `IProvideListInfo`
   - XML-based Goodreads API
   - Series and list metadata only
   - Deprecated but still in use

### Problems with Current System
- **Goodreads API**: Deprecated and unreliable
- **Proprietary Dependency**: Not community maintainable
- **Single Point of Failure**: No fallback options
- **Legal Concerns**: Terms of service restrictions
- **Data Quality**: Inconsistent metadata, missing books
- **Rate Limiting**: Restrictive API quotas

### Foreign ID System
Currently uses Goodreads IDs as `ForeignAuthorId` and `ForeignBookId` throughout the codebase:
- Database schema uses these IDs
- User libraries are tagged with Goodreads IDs
- Import/export relies on Goodreads identification

---

## Goals

### Primary Goals
1. **Complete FOSS Migration**: Replace all Goodreads dependencies with FOSS providers
2. **Multi-Provider Support**: Implement fallback and aggregation strategies
3. **Data Preservation**: Maintain existing user libraries without data loss
4. **Backward Compatibility**: Support legacy Goodreads IDs during transition
5. **Improved Reliability**: Multiple sources prevent single point of failure

### Secondary Goals
1. **Better Metadata Quality**: Aggregate data from multiple sources
2. **Community Contribution**: Enable users to improve metadata
3. **Extensibility**: Easy to add new providers
4. **Performance**: Efficient caching and request handling
5. **Transparency**: Show metadata sources to users

---

## FOSS Metadata Provider Options

### Primary Provider: Open Library
**URL**: https://openlibrary.org/

**Pros:**
- ✅ Fully open source (AGPL)
- ✅ Comprehensive coverage (20M+ books)
- ✅ Active development by Internet Archive
- ✅ ISBN, LCCN, OCLC, and other identifier support
- ✅ Author information and works
- ✅ Cover images available
- ✅ REST API and bulk data dumps
- ✅ No API key required
- ✅ Supports multiple editions per work

**Cons:**
- ⚠️ Rate limiting (100 req/5min for unregistered, more with account)
- ⚠️ Variable metadata quality
- ⚠️ Some books may be missing
- ⚠️ API can be slow at times

**API Endpoints:**
```
Search: /search.json?q={query}&author={author}
Work: /works/{OLID}.json
Edition: /books/{OLID}.json
Author: /authors/{OLID}.json
ISBN: /isbn/{ISBN}.json
Covers: https://covers.openlibrary.org/b/id/{ID}-{SIZE}.jpg
```

### Secondary Provider: Inventaire
**URL**: https://inventaire.io/

**Pros:**
- ✅ Fully open source (AGPL)
- ✅ Built on Wikidata
- ✅ Active community
- ✅ Good for non-English books
- ✅ No API key required
- ✅ GraphQL API

**Cons:**
- ⚠️ Smaller catalog than Open Library
- ⚠️ Less mature API
- ⚠️ May lack some popular titles

**API Endpoints:**
```
Search: /api/search?types=works&search={query}
Entity: /api/entities?action=by-uris&uris={uri}
ISBN: /api/entities?action=by-isbn&isbns={isbn}
```

### Tertiary Provider: Google Books API
**URL**: https://developers.google.com/books

**Pros:**
- ✅ Comprehensive coverage
- ✅ High quality metadata
- ✅ Good search capabilities
- ✅ Reliable uptime
- ✅ Cover images

**Cons:**
- ⚠️ Not open source
- ⚠️ Requires API key
- ⚠️ Rate limiting (1000 req/day free tier)
- ⚠️ Terms of service restrictions
- ⚠️ Not fully free for commercial use

**Usage**: Fallback only for critical missing data

### Additional Data Sources

#### MusicBrainz BookBrainz (Future Consideration)
- Still in development
- Community-driven book database
- Would be ideal when mature

#### ISBN Database Services
- ISBN.org (official ISBN registry)
- ISBNdb.com (freemium, requires key)
- Use for ISBN → metadata resolution

---

## Architecture Design

### Multi-Provider Architecture

```
┌─────────────────────────────────────────────────────────────┐
│                     Service Layer                           │
│  (RefreshBookService, AddBookService, SearchService)        │
└───────────────────────────┬─────────────────────────────────┘
                            │
┌───────────────────────────▼─────────────────────────────────┐
│              Metadata Abstraction Layer                      │
│  - Provider Selection Logic                                  │
│  - Fallback/Aggregation Strategy                            │
│  - Caching Layer                                             │
│  - Quality Scoring                                           │
└───────────────────────────┬─────────────────────────────────┘
                            │
        ┌───────────────────┼───────────────────┐
        │                   │                   │
┌───────▼────────┐  ┌──────▼──────┐  ┌────────▼─────────┐
│  Open Library  │  │  Inventaire │  │  Google Books    │
│    Provider    │  │   Provider  │  │    Provider      │
│   (Primary)    │  │ (Secondary) │  │   (Fallback)     │
└────────────────┘  └─────────────┘  └──────────────────┘
```

### Provider Interface Design

```csharp
// Core Provider Interface
public interface IMetadataProvider
{
    string ProviderName { get; }
    int Priority { get; }
    bool IsEnabled { get; }
    
    // Capability flags
    bool SupportsAuthorSearch { get; }
    bool SupportsBookSearch { get; }
    bool SupportsISBNLookup { get; }
    bool SupportsSeriesInfo { get; }
    bool SupportsCoverImages { get; }
    
    // Rate limiting
    RateLimitInfo GetRateLimits();
}

// Enhanced search interface
public interface ISearchForNewBookV2 : IMetadataProvider
{
    Task<List<Book>> SearchForNewBook(string title, string author, SearchOptions options);
    Task<List<Book>> SearchByISBN(string isbn);
    Task<List<Book>> SearchByIdentifier(string identifierType, string identifier);
}

// Enhanced author interface  
public interface IProvideAuthorInfoV2 : IMetadataProvider
{
    Task<Author> GetAuthorInfo(string providerId);
    Task<Author> GetAuthorByName(string name);
}

// Metadata quality scoring
public interface IMetadataQualityScorer
{
    int CalculateScore(Book book);
    int CalculateScore(Author author);
}

// Provider aggregation
public interface IMetadataAggregator
{
    Task<Book> GetBestBookMetadata(string identifier, IdentifierType type);
    Task<List<Book>> MergeSearchResults(List<List<Book>> providerResults);
}
```

### ID Mapping Strategy

To handle the transition from Goodreads IDs to multiple provider IDs:

```csharp
public class BookIdentifiers
{
    public string PrimaryId { get; set; }  // Internal Bibliophilarr ID
    public string GoodreadsId { get; set; }  // Legacy support
    public string OpenLibraryWorkId { get; set; }  // OLID
    public string OpenLibraryEditionId { get; set; }
    public string InventaireId { get; set; }
    public string ISBN { get; set; }  // Primary external identifier
    public string ISBN13 { get; set; }
    public string ASIN { get; set; }
    public string LCCN { get; set; }
    public string OCLC { get; set; }
    
    // Source tracking
    public string PreferredProvider { get; set; }
    public Dictionary<string, string> ProviderSpecificIds { get; set; }
}

public class AuthorIdentifiers
{
    public string PrimaryId { get; set; }
    public string GoodreadsId { get; set; }
    public string OpenLibraryAuthorId { get; set; }
    public string InventaireId { get; set; }
    public string ISNICode { get; set; }  // International Standard Name Identifier
    public string VIAFId { get; set; }  // Virtual International Authority File
    
    public Dictionary<string, string> ProviderSpecificIds { get; set; }
}
```

### Caching Strategy

```csharp
public class MetadataCacheManager
{
    // Multi-level caching
    // L1: In-memory cache (fast, volatile)
    // L2: SQLite cache (persistent, local)
    // L3: Optional shared cache (Redis for multi-instance)
    
    public class CachePolicy
    {
        public TimeSpan AuthorCacheDuration { get; set; } = TimeSpan.FromDays(7);
        public TimeSpan BookCacheDuration { get; set; } = TimeSpan.FromDays(7);
        public TimeSpan SearchCacheDuration { get; set; } = TimeSpan.FromHours(24);
        public TimeSpan CoverImageCacheDuration { get; set; } = TimeSpan.FromDays(30);
    }
}
```

---

## Implementation Phases

### Phase 1: Foundation & Documentation ✓
**Status**: Current Phase

**Tasks:**
- [x] Document current architecture
- [x] Research FOSS alternatives
- [x] Create migration plan
- [ ] Update README and contributing guides
- [ ] Set up project roadmap

**Deliverables:**
- MIGRATION_PLAN.md (this document)
- Updated README.md
- Contributor guidelines for metadata work

### Phase 2: Infrastructure Setup

**Tasks:**
1. Create new provider interfaces
2. Implement provider registry system
3. Build metadata quality scorer
4. Create provider testing framework
5. Set up monitoring/logging for providers

**Deliverables:**
- `IMetadataProviderV2` interface hierarchy
- `MetadataProviderRegistry` service
- `MetadataQualityScorer` implementation
- Unit test framework for providers

### Phase 3: Open Library Provider Implementation

**Tasks:**
1. Implement Open Library API client
2. Map Open Library data to Bibliophilarr models
3. Implement search functionality
4. Add ISBN/identifier lookup
5. Implement author information retrieval
6. Handle cover image fetching
7. Add rate limiting and retry logic
8. Comprehensive testing

**API Mapping:**
```
Open Library Work → Bibliophilarr Book
Open Library Edition → Bibliophilarr Edition
Open Library Author → Bibliophilarr Author
```

**Implementation Files:**
```
src/NzbDrone.Core/MetadataSource/OpenLibrary/
  ├── OpenLibraryProvider.cs
  ├── OpenLibraryClient.cs
  ├── OpenLibraryMapper.cs
  ├── Resources/
  │   ├── WorkResource.cs
  │   ├── EditionResource.cs
  │   ├── AuthorResource.cs
  │   └── SearchResultResource.cs
  └── OpenLibraryException.cs
```

### Phase 4: Inventaire Provider Implementation

**Tasks:**
1. Implement Inventaire API client
2. Map Inventaire/Wikidata entities
3. Implement search functionality
4. Add entity resolution
5. Testing and validation

**Implementation Files:**
```
src/NzbDrone.Core/MetadataSource/Inventaire/
  ├── InventaireProvider.cs
  ├── InventaireClient.cs
  ├── InventaireMapper.cs
  └── Resources/
```

### Phase 5: Provider Aggregation Layer

**Tasks:**
1. Implement metadata aggregator
2. Create provider selection strategy
3. Build fallback logic
4. Implement metadata merging
5. Add quality scoring
6. Create provider health monitoring

**Key Components:**
```csharp
public class MetadataAggregator
{
    // Try providers in priority order
    public async Task<Book> GetBookMetadata(string isbn)
    {
        foreach (var provider in GetSortedProviders())
        {
            try
            {
                var result = await provider.SearchByISBN(isbn);
                if (IsQualityAcceptable(result))
                    return result;
            }
            catch (Exception ex)
            {
                // Log and continue to next provider
            }
        }
        throw new MetadataNotFoundException();
    }
    
    // Merge results from multiple providers
    public async Task<Book> GetBestBookMetadata(string isbn)
    {
        var results = await Task.WhenAll(
            providers.Select(p => p.SearchByISBN(isbn))
        );
        
        return MergeBookMetadata(results);
    }
}
```

### Phase 6: Database Migration

**Tasks:**
1. Add new identifier columns to database
2. Create ID mapping tables
3. Implement migration scripts
4. Add backward compatibility layer
5. Create rollback procedures

**Schema Changes:**
```sql
-- Add new identifier columns
ALTER TABLE Books ADD COLUMN OpenLibraryWorkId TEXT;
ALTER TABLE Books ADD COLUMN OpenLibraryEditionId TEXT;
ALTER TABLE Books ADD COLUMN ISBN TEXT;
ALTER TABLE Books ADD COLUMN ISBN13 TEXT;

ALTER TABLE Authors ADD COLUMN OpenLibraryAuthorId TEXT;
ALTER TABLE Authors ADD COLUMN ISNI TEXT;
ALTER TABLE Authors ADD COLUMN VIAF TEXT;

-- Create mapping table for migration
CREATE TABLE IdentifierMappings (
    Id INTEGER PRIMARY KEY,
    GoodreadsId TEXT NOT NULL,
    ISBN TEXT,
    OpenLibraryWorkId TEXT,
    MappingSource TEXT,
    Confidence REAL,
    CreatedAt DATETIME,
    UNIQUE(GoodreadsId)
);

-- Indexes for performance
CREATE INDEX IX_Books_ISBN ON Books(ISBN);
CREATE INDEX IX_Books_OpenLibraryWorkId ON Books(OpenLibraryWorkId);
CREATE INDEX IX_Authors_OpenLibraryAuthorId ON Authors(OpenLibraryAuthorId);
```

### Phase 7: Migration Tools

**Tasks:**
1. Create Goodreads → ISBN mapper
2. Build bulk metadata updater
3. Implement conflict resolver
4. Create migration progress tracker
5. Build rollback tool

**Migration Tool:**
```csharp
public class LibraryMigrationService
{
    public async Task<MigrationReport> MigrateLibrary(MigrationOptions options)
    {
        var report = new MigrationReport();
        
        // Step 1: Export existing data
        var books = await ExportExistingBooks();
        
        // Step 2: Map Goodreads IDs to ISBNs
        foreach (var book in books)
        {
            if (!string.IsNullOrEmpty(book.GoodreadsId))
            {
                var isbn = await MapGoodreadsToISBN(book.GoodreadsId);
                book.ISBN = isbn;
            }
        }
        
        // Step 3: Fetch new metadata
        foreach (var book in books)
        {
            try
            {
                var newMetadata = await FetchFromNewProviders(book);
                await UpdateBookMetadata(book, newMetadata);
                report.SuccessCount++;
            }
            catch (Exception ex)
            {
                report.FailedBooks.Add(book);
                report.FailureCount++;
            }
        }
        
        return report;
    }
}
```

### Phase 8: UI/UX Updates

**Tasks:**
1. Add provider selection in settings
2. Display metadata source attribution
3. Show provider status/health
4. Add manual provider override per book
5. Improve search result display
6. Add migration progress UI

**Settings UI:**
```
Settings → Metadata
  ├── Primary Provider: [Open Library ▼]
  ├── Secondary Provider: [Inventaire ▼]
  ├── Fallback Provider: [Google Books ▼]
  ├── Enable Metadata Aggregation: [✓]
  ├── Preferred Identifier: [ISBN-13 ▼]
  └── Provider Health Status:
      ├── Open Library: ● Healthy (Response: 450ms)
      ├── Inventaire: ● Healthy (Response: 320ms)
      └── Google Books: ● Healthy (Response: 180ms)
```

### Phase 9: Testing & Quality Assurance

**Tasks:**
1. Unit tests for each provider
2. Integration tests with real APIs
3. Performance benchmarking
4. Load testing with large libraries
5. Edge case testing
6. User acceptance testing

**Test Coverage:**
- Provider implementations: >90%
- Aggregation logic: 100%
- Migration tools: >85%
- UI components: >80%

### Phase 10: Documentation & Release

**Tasks:**
1. User migration guide
2. API documentation
3. Provider comparison docs
4. Troubleshooting guide
5. Update wiki
6. Release notes
7. Migration FAQ

---

## Technical Specifications

### Open Library Implementation Details

#### Search Endpoint
```
GET /search.json?q={query}&author={author}&title={title}

Response:
{
  "numFound": 123,
  "docs": [
    {
      "key": "/works/OL45804W",
      "title": "Foundation",
      "author_name": ["Isaac Asimov"],
      "isbn": ["9780553293357"],
      "cover_i": 6694312,
      "first_publish_year": 1951,
      "edition_count": 217
    }
  ]
}
```

#### Work Endpoint
```
GET /works/{OLID}.json

Response:
{
  "key": "/works/OL45804W",
  "title": "Foundation",
  "authors": [{"author": {"key": "/authors/OL34221A"}}],
  "description": "...",
  "covers": [6694312],
  "subjects": ["Science fiction", "Space colonies"],
  "first_publish_date": "1951"
}
```

#### ISBN Lookup
```
GET /isbn/{ISBN}.json

Response:
{
  "key": "/books/OL7353617M",
  "isbn_13": ["9780553293357"],
  "title": "Foundation",
  "authors": [{"key": "/authors/OL34221A"}],
  "works": [{"key": "/works/OL45804W"}],
  "covers": [6694312]
}
```

### Rate Limiting Implementation

```csharp
public class RateLimitedHttpClient
{
    private readonly SemaphoreSlim _semaphore;
    private readonly Queue<DateTime> _requestTimestamps;
    private readonly int _maxRequestsPerWindow;
    private readonly TimeSpan _windowDuration;
    
    public async Task<HttpResponse> GetAsync(string url)
    {
        await WaitForRateLimit();
        
        try
        {
            return await _httpClient.GetAsync(url);
        }
        finally
        {
            RecordRequest();
        }
    }
    
    private async Task WaitForRateLimit()
    {
        await _semaphore.WaitAsync();
        
        try
        {
            CleanOldTimestamps();
            
            if (_requestTimestamps.Count >= _maxRequestsPerWindow)
            {
                var oldestRequest = _requestTimestamps.Peek();
                var waitTime = _windowDuration - (DateTime.UtcNow - oldestRequest);
                
                if (waitTime > TimeSpan.Zero)
                {
                    await Task.Delay(waitTime);
                }
            }
        }
        finally
        {
            _semaphore.Release();
        }
    }
}
```

### Metadata Quality Scoring

```csharp
public class MetadataQualityScorer
{
    public int CalculateBookScore(Book book)
    {
        int score = 0;
        
        // Essential fields
        if (!string.IsNullOrEmpty(book.Title)) score += 20;
        if (book.Authors?.Any() == true) score += 20;
        if (!string.IsNullOrEmpty(book.ISBN)) score += 15;
        
        // Important fields
        if (!string.IsNullOrEmpty(book.Description)) score += 10;
        if (book.PublishDate != default) score += 5;
        if (!string.IsNullOrEmpty(book.Publisher)) score += 5;
        
        // Nice to have
        if (book.Covers?.Any() == true) score += 10;
        if (book.Genres?.Any() == true) score += 5;
        if (book.PageCount > 0) score += 5;
        if (!string.IsNullOrEmpty(book.Language)) score += 5;
        
        return score;  // Max: 100
    }
}
```

---

## Testing Strategy

### Unit Testing
- Test each provider independently with mocked HTTP responses
- Test data mapping/transformation logic
- Test rate limiting logic
- Test error handling

### Integration Testing
- Test against real provider APIs (with caching to avoid rate limits)
- Test provider fallback scenarios
- Test metadata aggregation
- Test database migrations

### Performance Testing
- Benchmark search performance
- Test with libraries of varying sizes (100, 1000, 10000+ books)
- Measure cache effectiveness
- Test concurrent request handling

### User Acceptance Testing
- Beta release to community
- Migration of real user libraries
- Feedback collection
- Bug reporting and fixes

---

## Migration Tools

### Goodreads ID Mapper

For existing libraries with Goodreads IDs, we need to map them to ISBNs and new provider IDs:

```csharp
public class GoodreadsIdMapper
{
    // Strategy 1: Use existing book files (metadata in ebook files)
    public async Task<string> ExtractISBNFromFile(string bookPath)
    {
        // Read ISBN from ebook metadata (EPUB, MOBI, PDF)
    }
    
    // Strategy 2: Use ISBN database lookup with Goodreads ID
    public async Task<string> MapGoodreadsIdToISBN(string goodreadsId)
    {
        // Try cached mapping first
        // Try third-party mapping services
        // Manual user input as last resort
    }
    
    // Strategy 3: Title/Author matching with new providers
    public async Task<BookIdentifiers> MapBookByTitleAuthor(string title, string author)
    {
        // Search in new providers
        // Score and rank matches
        // Return best match with confidence score
    }
}
```

### Migration Report

```csharp
public class MigrationReport
{
    public int TotalBooks { get; set; }
    public int SuccessfulMigrations { get; set; }
    public int FailedMigrations { get; set; }
    public int PartialMigrations { get; set; }  // Found but with less complete metadata
    public int SkippedBooks { get; set; }
    
    public List<MigrationError> Errors { get; set; }
    public List<MigrationWarning> Warnings { get; set; }
    public Dictionary<string, int> SuccessByProvider { get; set; }
    
    public TimeSpan Duration { get; set; }
    public DateTime CompletedAt { get; set; }
}
```

---

## Risks and Mitigations

### Risk 1: Open Library Rate Limiting
**Impact**: High  
**Probability**: Medium

**Mitigation:**
- Implement aggressive caching (7-day default)
- Use batch API calls where possible
- Implement exponential backoff
- Register for API key (5x higher limits)
- Consider hosting a local Open Library mirror for large instances

### Risk 2: Incomplete Metadata Coverage
**Impact**: Medium  
**Probability**: Medium

**Mitigation:**
- Multiple provider fallback
- Allow manual metadata entry
- Preserve Goodreads data as read-only reference
- Community contribution tools
- Gradual migration with user validation

### Risk 3: ISBN Mapping Failures
**Impact**: High  
**Probability**: Medium

**Mitigation:**
- Extract ISBNs from ebook file metadata
- Use title/author fuzzy matching
- Manual user mapping tools
- Community-contributed mapping database
- Keep Goodreads IDs as legacy reference

### Risk 4: Performance Degradation
**Impact**: Medium  
**Probability**: Low

**Mitigation:**
- Comprehensive performance testing
- Efficient caching strategy
- Background metadata updates
- Async/await throughout
- Connection pooling and keep-alive

### Risk 5: Provider API Changes
**Impact**: Medium  
**Probability**: Low

**Mitigation:**
- Version provider implementations
- Comprehensive integration tests
- Monitor provider announcements
- Quick rollback capability
- Multiple provider redundancy

### Risk 6: Data Quality Issues
**Impact**: Medium  
**Probability**: High

**Mitigation:**
- Metadata quality scoring
- Multiple source verification
- User reporting tools
- Fallback to alternative providers
- Community metadata improvement

---

## Timeline and Milestones

### Milestone 1: Foundation (Current - Week 4)
- ✅ Repository analysis
- ✅ Migration plan creation
- 🔄 Documentation updates
- 🔄 Community engagement

### Milestone 2: Infrastructure (Week 5-8)
- Provider interfaces
- Testing framework
- Quality scoring system
- Monitoring/logging

### Milestone 3: Open Library Provider (Week 9-14)
- Complete implementation
- Comprehensive testing
- Performance optimization
- Documentation

### Milestone 4: Multi-Provider Support (Week 15-18)
- Inventaire implementation
- Aggregation layer
- Fallback logic
- Provider management UI

### Milestone 5: Migration Tools (Week 19-22)
- Database migration
- ID mapping tools
- Bulk updater
- User migration guide

### Milestone 6: Beta Release (Week 23-26)
- Community testing
- Bug fixes
- Performance tuning
- Documentation updates

### Milestone 7: Stable Release (Week 27-30)
- Final testing
- Production deployment
- Goodreads deprecation
- Celebration! 🎉

---

## Contributing

We welcome contributions to this migration effort! Priority areas:

1. **Provider Implementation**: Help build Open Library and Inventaire providers
2. **Testing**: Write tests, report bugs, test with real libraries
3. **Documentation**: Improve guides, add examples, translate
4. **UI/UX**: Design provider settings, improve metadata display
5. **Migration Tools**: Build tools to help users migrate

See [CONTRIBUTING.md](CONTRIBUTING.md) for detailed guidelines.

---

## References

### FOSS Metadata Resources
- [Open Library API](https://openlibrary.org/developers/api)
- [Inventaire API](https://api.inventaire.io/)
- [Open Library Data Dumps](https://openlibrary.org/developers/dumps)
- [BookBrainz](https://bookbrainz.org/) (future consideration)

### Technical Resources
- [ISBN Standards](https://www.isbn-international.org/)
- [ISNI (Author Identifiers)](https://isni.org/)
- [VIAF (Authority Files)](https://viaf.org/)

### Community
- [GitHub Discussions](https://github.com/Swartdraak/Bibliophilarr/discussions)
- [Discord Community](https://discord.gg/bibliophilarr)

---

## Appendix: API Comparison

| Feature | Open Library | Inventaire | Google Books | Goodreads (Legacy) |
|---------|--------------|------------|--------------|-------------------|
| **License** | AGPL (Open) | AGPL (Open) | Proprietary | Proprietary |
| **API Key Required** | No | No | Yes | Yes |
| **Rate Limit** | 100/5min | Generous | 1000/day | Deprecated |
| **Book Coverage** | 20M+ | 5M+ | 40M+ | 80M+ |
| **Author Info** | ✅ Good | ✅ Good | ✅ Good | ✅ Excellent |
| **ISBN Lookup** | ✅ Yes | ✅ Yes | ✅ Yes | ✅ Yes |
| **Cover Images** | ✅ Yes | ⚠️ Limited | ✅ Yes | ✅ Yes |
| **Series Info** | ⚠️ Limited | ⚠️ Limited | ⚠️ Limited | ✅ Good |
| **Multiple Editions** | ✅ Excellent | ✅ Good | ✅ Good | ✅ Good |
| **Search Quality** | ✅ Good | ⚠️ Fair | ✅ Excellent | ✅ Good |
| **API Stability** | ✅ Stable | ✅ Stable | ✅ Very Stable | ❌ Deprecated |
| **Community** | ✅ Active | ✅ Active | N/A | ❌ Closed |
| **Bulk Data** | ✅ Available | ⚠️ Limited | ❌ No | ❌ No |

---

## Document Version History

- **v1.0** (2024-02-16): Initial comprehensive migration plan
- Future updates will be tracked in git history

---

**Last Updated**: February 16, 2024  
**Status**: Planning Phase  
**Next Review**: After Phase 1 completion
