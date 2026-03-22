# Bibliophilarr Metadata Migration Plan

## Executive Summary

This document outlines the comprehensive technical plan for migrating Bibliophilarr from proprietary Goodreads metadata to Free and Open Source Software (FOSS) metadata providers. The goal is to create a sustainable, reliable, and community-maintainable book and audiobook collection manager.

## Implementation Progress Snapshot (March 22, 2026 release-evidence/test-runner completion)

Completed in this migration-evidence slice:

- Frontend regression runner completion:
  - Added repository Jest config/module mapper and setup hooks so frontend tests run both locally and in CI.
  - Added `yarn test:frontend` command and CI test execution in `ci-frontend.yml`.
- Replay baseline/post-fix evidence published from curated cohort:
  - Baseline report: `docs/operations/replay-comparison-snapshots/2026-03-22/baseline/root_live_enrichment_report.json`
  - Post report: `docs/operations/replay-comparison-snapshots/2026-03-22/post/root_live_enrichment_report.json`
  - Comparison outputs: `docs/operations/replay-comparison-snapshots/2026-03-22/replay-comparison.md` and `.json`
- Delta regression assertions enforced:
  - Added `scripts/replay_delta_guard.py` and `tests/fixtures/replay-cohort/replay-delta-thresholds.json`.
  - Weekly replay workflow now performs baseline+post comparison and fails on threshold regressions.
- Release-entry gate evidence chain improved:
  - Release workflow now generates same-day series persistence snapshots before `release_entry_gate.py`.
  - New staging snapshot published at `docs/operations/series-persistence-snapshots/2026-03-22.md` and `.json`.

Validation status for this slice:

- Frontend Jest suites: pass (9/9).
- New targeted core tests for import preflight, canonical merge side effects, and refresh series payload: pass.
- Replay delta guard: pass (`replay-delta-guard-summary.json` status `passed`).
- Full solution build: pass.
- Staged release-entry gate: fail (expected), blocked by series persistence verdict `FAIL`.

Migration risk posture update:

- Tooling and CI controls for replay and frontend regressions are now in place.
- Primary migration blocker remains runtime series persistence and duplicate author convergence in staging DB state.

## Implementation Progress Snapshot (March 18, 2026)

Completed in the current migration slice:

- Metadata provider orchestration is implemented and integrated into search, add, refresh, and import-list flows.
- Runtime provider controls are available via config/API/UI, including provider enablement and ordering.
- Runtime provider controls are available via config/API/UI, including timeout, retry, and circuit-breaker settings.
- Open Library and BookInfo provider enablement now respects configuration flags.
- Inventaire provider baseline is implemented and registered as a secondary metadata source.
- Inventaire can be force-disabled by environment kill-switch (`BIBLIOPHILARR_DISABLE_INVENTAIRE=1`) for staged rollout control.
- Provider telemetry collection and diagnostics API endpoints are available for operational visibility.
- Open Library identifier backfill command/service is implemented for startup-triggered migration assistance.
- Provenance fields are exposed in API resources and surfaced in book index UI.
- Status UI includes provider diagnostics, and dry-run automation captures before/after provenance snapshots on staging.

Validation status for this slice:

- API tests: pass (`Bibliophilarr.Api.Test`)
- Core targeted tests: pass for `MetadataProviderOrchestratorFixture` and `ImportListSyncServiceFixture`
- Import-list edge-case handling updated to avoid adding unresolved external-ID books

## Implementation Progress Snapshot (March 21, 2026 TD-META completion)

Completed in this migration-hardening slice:

- TD-META-001: orchestrator parity implemented for add/import/identification metadata request paths.
- TD-META-002: shared OpenLibrary ID normalization boundary implemented and backfill writes batched via command-configured BatchSize.
- TD-META-003: provider registry routing is now health/cooldown aware with deterministic fallback ordering and cooldown recovery on success.
- TD-META-004: import-list mapping now uses shared query normalization variants for parity with identification behavior.
- TD-META-005: conflict-resolution telemetry now includes per-provider score-factor breakdowns and API exposure for operator explainability.

Validation status for this slice:

- Core targeted fixtures: pass (68/68).
- API targeted fixtures: pass (2/2).
- Full solution build: pass.

Migration safety posture:

- Changes are additive and backward-compatible at API and persistence boundaries.
- No destructive schema changes were introduced in this slice.
- Existing fallback behavior is preserved while routing now uses health-aware ordering.

## Implementation Progress Snapshot (March 21, 2026 routing/dedupe/import hardening continuation)

Completed in this continuation slice:

- ID-scoped provider compatibility routing:
    - `MetadataProviderOrchestrator` now filters provider execution for scoped IDs in `GetAuthorInfo` and `GetBookInfo`.
    - OpenLibrary ID namespaces are constrained to compatible provider execution before fallback.
- Canonical dedupe and merge tooling:
    - Added `CanonicalizeAuthorsCommand` and `AuthorCanonicalizationService`.
    - Added confidence-scored canonical match policy and bounded merge execution.
    - Integrated dedupe policy into author add flows to prevent high-confidence duplicate inserts.
- Import/identification robustness:
    - Added import preflight guards for invalid author IDs and root-folder conflicts.
    - Expanded identification fallback query variants and no-candidate diagnostics.
- Series persistence and release evidence support:
    - Added series reconstruction fallback in author refresh when author-level series payload is empty.
    - Added `scripts/series_persistence_gate.py` and integrated series snapshot requirement into `scripts/release_entry_gate.py`.
    - Added `scripts/replay_comparison.py` for baseline vs post-fix replay comparison metrics.

Validation status:

- Full solution build: pass.
- Targeted core fixtures for orchestrator compatibility and refresh flow: pass.
- Python gate/comparison scripts: syntax validation pass via `py_compile`.

Known gap:

- Frontend jump-bar interaction tests are added but local Jest execution path needs repository test-runner wiring (module mapping/Babel setup for direct CLI invocation).

## Implementation Progress Snapshot (March 21, 2026 hardening follow-up)

Completed in this hardening slice:

- Event handler stability (`TD-EVENT-001`): guarded `BookFileDeletedEvent` subscriber paths to prevent null-chain faults.
- Cover pipeline resilience (`TD-COVER-001`, `TD-COVER-002`, `TD-COVER-003`):
    - host-aware OpenLibrary cover throttling/cooldown,
    - invalid cover-id URL suppression,
    - stale local cover reconciliation and safer missing-file URL fallback.
- Series contract hardening (`TD-META-SERIES-001`, `TD-META-SERIES-002`):
    - OpenLibrary search now requests series fields,
    - deterministic works-identity + search-enrichment merge contract implemented.
- OpenLibrary operation tuning (`TD-OPENLIB-001`, `TD-ORCH-001`): per-operation timeout and retry settings (`search`, `isbn`, `work`) added across config/API/client plumbing.
- Refresh deletion safeguards (`TD-IMPORT-001`): two-phase stale mark/delete with degraded-provider suppression.
- Operational warning-noise control (`TD-OPS-INDEXER-001`): rate-limited no-indexer warning behavior with actionable guidance.

Validation status for this slice:

- Core targeted fixtures: pass (49/49).
- API targeted fixtures: pass (2/2).
- Full solution build: pass.

## Implementation progress snapshot (March 21, 2026 full-library QA triage)

A full-library validation run identified additional migration-critical gaps and one
newly confirmed provider mapping fault.

Confirmed runtime findings:

- OpenLibrary search flows emitted frequent DateTime range failures from malformed publish-year values.
- Series persistence remained at zero (`Series` / `SeriesBookLink`) in the reviewed runtime state.
- Duplicate logical authors were present under distinct OpenLibrary foreign IDs.
- Import identification quality remained constrained by repeated "no candidates" fallback exhaustion.
- GoogleBooks was enabled but `get-author-info` fallbacks were invoked with OpenLibrary ID namespaces,
    creating noisy misses without effective recovery.

Completed in this follow-up:

- Implemented defensive publish-year range handling in OpenLibrary mapper/search-proxy paths to
    prevent DateTime exceptions from malformed provider payloads.
- Added regression fixture coverage for out-of-range publish-year search docs.
- Targeted validation (OpenLibrary mapper/client/provider fixtures): pass (46/46).

Next migration slices (priority order):

1. Provider-compatibility routing for ID-scoped operations (`get-author-info`, `get-book-info`).
2. Canonical author dedupe/merge policy for multi-ID OpenLibrary author records.
3. End-to-end series persistence verification and reconciliation for refresh/import paths.
4. Identification fallback quality expansion with richer candidate-rejection telemetry.
5. Frontend interaction audit for author index jump-bar and related click handlers.

Additional migration progress (March 18, 2026):

- Goodreads provider implementations were removed from active runtime source trees and replaced by OpenLibrary-aligned metadata/search behavior.
- Active runtime/API/frontend/localization references were migrated from Goodreads naming to OpenLibrary naming.
- OpenAPI and frontend text were patched to remove Goodreads identifiers in active user and contract surfaces.
- Legacy Goodreads-linked test fixtures were removed where they blocked build after provider removal; OpenLibrary-native replacement fixtures remain follow-up work.

Additional hardening progress (March 19, 2026):

- OpenLibrary ISBN-miss handling now performs limited contextual title+author fallback attempts before advancing to other identifier-source paths.
- Metadata HTTP requests now follow canonical redirects in development to align local validation with production behavior.
- Ebook import parsing now performs best-effort filename metadata fallback when EPUB/PDF/AZW parsing fails, reducing hard-stop identification failures.

### Technical debt backlog from parity comparison (March 21, 2026)

The post-comparison backlog below defines migration-safe debt slices with explicit implementation
touch points, rollout shape, and acceptance criteria.

#### TD-META-001: Orchestrator parity across all ingest/request paths

Objective:

- Ensure add, refresh, import-list, and identification flows all execute metadata requests through a
    single provider-selection/fallback policy.

Primary touch points:

- [src/NzbDrone.Core/MetadataSource/MetadataProviderOrchestrator.cs](src/NzbDrone.Core/MetadataSource/MetadataProviderOrchestrator.cs)
- [src/NzbDrone.Core/Books/Services/AddAuthorService.cs](src/NzbDrone.Core/Books/Services/AddAuthorService.cs)
- [src/NzbDrone.Core/Books/Services/AddBookService.cs](src/NzbDrone.Core/Books/Services/AddBookService.cs)
- [src/NzbDrone.Core/Books/Services/RefreshAuthorService.cs](src/NzbDrone.Core/Books/Services/RefreshAuthorService.cs)
- [src/NzbDrone.Core/Books/Services/RefreshBookService.cs](src/NzbDrone.Core/Books/Services/RefreshBookService.cs)
- [src/NzbDrone.Core/ImportLists/ImportListSyncService.cs](src/NzbDrone.Core/ImportLists/ImportListSyncService.cs)
- [src/NzbDrone.Core/MediaFiles/BookImport/Identification/CandidateService.cs](src/NzbDrone.Core/MediaFiles/BookImport/Identification/CandidateService.cs)

Proposed change shape:

1. Add orchestrator wrappers for operations currently executed via direct provider contracts.
2. Migrate call sites incrementally with feature flags per flow.
3. Keep legacy contract path available until parity fixtures pass.

Acceptance criteria:

- Equivalent persisted metadata outcomes across entry paths for shared fixture inputs.
- No increase in targeted failure counts for current refresh/import regression suites.

Rollback/mitigation:

- Disable per-flow orchestrator flags and revert to legacy direct provider usage.

#### TD-META-002: Canonical external-ID normalization boundary

Objective:

- Eliminate divergence between provider mapping, backfill, and persistence logic for external IDs.

Primary touch points:

- [src/NzbDrone.Core/MetadataSource/OpenLibrary/OpenLibraryMapper.cs](src/NzbDrone.Core/MetadataSource/OpenLibrary/OpenLibraryMapper.cs)
- [src/NzbDrone.Core/MetadataSource/OpenLibrary/OpenLibraryProvider.cs](src/NzbDrone.Core/MetadataSource/OpenLibrary/OpenLibraryProvider.cs)
- [src/NzbDrone.Core/MetadataSource/OpenLibrary/OpenLibraryIdBackfillService.cs](src/NzbDrone.Core/MetadataSource/OpenLibrary/OpenLibraryIdBackfillService.cs)
- [src/NzbDrone.Core/Books/Model/Book.cs](src/NzbDrone.Core/Books/Model/Book.cs)
- [src/NzbDrone.Core/Books/Model/AuthorMetadata.cs](src/NzbDrone.Core/Books/Model/AuthorMetadata.cs)
- [src/NzbDrone.Core/Datastore/Migration/042_add_open_library_ids.cs](src/NzbDrone.Core/Datastore/Migration/042_add_open_library_ids.cs)

Proposed change shape:

1. Introduce one normalization component for work/author/external IDs.
2. Apply normalization at write boundaries in mappers and persistence updates.
3. Add batched migration-time normalization command with unresolved-id telemetry.

Acceptance criteria:

- Deterministic normalization for legacy/malformed ID forms.
- Monotonic increase in normalized ID coverage with unresolved cases explicitly counted.

Rollback/mitigation:

- Keep raw foreign IDs intact; disable canonical rewrite pass via config flag.

#### TD-META-003: Health-aware provider routing

Objective:

- Reduce repeated slow failures by feeding provider health telemetry into selection order.

Primary touch points:

- [src/NzbDrone.Core/MetadataSource/MetadataProviderRegistry.cs](src/NzbDrone.Core/MetadataSource/MetadataProviderRegistry.cs)
- [src/NzbDrone.Core/MetadataSource/MetadataProviderTelemetry.cs](src/NzbDrone.Core/MetadataSource/MetadataProviderTelemetry.cs)
- [src/NzbDrone.Core/MetadataSource/ProviderTelemetryService.cs](src/NzbDrone.Core/MetadataSource/ProviderTelemetryService.cs)
- [src/NzbDrone.Core/MetadataSource/BookSearchFallbackExecutionService.cs](src/NzbDrone.Core/MetadataSource/BookSearchFallbackExecutionService.cs)

Proposed change shape:

1. Add failure-streak thresholds and cooldown windows.
2. Temporarily demote failing providers while preserving deterministic ordering for healthy providers.
3. Add recovery probes and automatic reintegration.

Acceptance criteria:

- Provider-failure simulations show bounded retry behavior and successful recovery reinsertion.
- Operation telemetry reflects demotion and reintegration events.

Rollback/mitigation:

- Disable health-aware demotion and return to static priority order.

#### TD-META-004: Query-policy contract parity for import and identification

Objective:

- Guarantee predictable query behavior for equivalent inputs across import-list and identification
    workflows.

Primary touch points:

- [src/NzbDrone.Core/ImportLists/ImportListSyncService.cs](src/NzbDrone.Core/ImportLists/ImportListSyncService.cs)
- [src/NzbDrone.Core/MediaFiles/BookImport/Identification/CandidateService.cs](src/NzbDrone.Core/MediaFiles/BookImport/Identification/CandidateService.cs)
- [src/NzbDrone.Core/MetadataSource/MetadataQueryNormalizationService.cs](src/NzbDrone.Core/MetadataSource/MetadataQueryNormalizationService.cs)

Proposed change shape:

1. Define a shared query-policy contract and scenario matrix.
2. Route both flows through shared normalization/expansion helpers.
3. Mark intentionally divergent behaviors explicitly in tests/docs.

Acceptance criteria:

- New contract test suite demonstrates parity or documented divergence for all matrix scenarios.

Rollback/mitigation:

- Keep existing local query logic active behind compatibility switch while parity suite matures.

#### TD-META-005: Conflict-resolution explainability telemetry

Objective:

- Make metadata winner selection auditable and operator-explainable.

Primary touch points:

- [src/NzbDrone.Core/MetadataSource/MetadataAggregator.cs](src/NzbDrone.Core/MetadataSource/MetadataAggregator.cs)
- [src/NzbDrone.Core/MetadataSource/MetadataConflictResolutionPolicy.cs](src/NzbDrone.Core/MetadataSource/MetadataConflictResolutionPolicy.cs)
- [src/NzbDrone.Core/MetadataSource/MetadataQualityScorer.cs](src/NzbDrone.Core/MetadataSource/MetadataQualityScorer.cs)
- [docs/operations/METADATA_PROVIDER_RUNBOOK.md](docs/operations/METADATA_PROVIDER_RUNBOOK.md)

Proposed change shape:

1. Emit structured per-candidate scoring factors in debug telemetry.
2. Add operator guidance for interpreting factor-level outputs.

Acceptance criteria:

- Unit and integration fixtures assert presence and ordering of rationale fields.
- Runbook includes factor interpretation and mitigation steps.

Rollback/mitigation:

- Disable verbose scoring traces while keeping selection behavior unchanged.

#### Runtime-forensics debt intake (March 21, 2026)

The following migration-relevant debt was identified from production-like runtime forensics
(`~/.config/Bibliophilarr` logs + DB state) and is tracked in canonical status records:

- `TD-RUNTIME-001`: null-reference faults in `BookFileDeletedEvent` subscriber paths.
- `TD-META-006`: series ingestion gap remains unresolved in runtime (`Series` and
    `SeriesBookLink` persisted counts at zero).
- `TD-META-007`: OpenLibrary timeout/503 resilience hardening still needed for sustained
    migration safety.
- `TD-IMPORT-004`: refresh hard-delete behavior under provider degradation requires
    two-phase stale-mark safeguards.
- `TD-RENAME-001` and `TD-RENAME-002`: rename user-perception and linkage preflight gaps.

Migration risk note:

- Series-based naming outcomes and migration confidence remain constrained until
    `TD-META-006` is complete, because current OpenLibrary `works.json` flows do not reliably
    provide `series` and runtime enrichment must come from search-document fields.

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

2. **OpenLibrarySearchProxy** (Primary search/lookup provider)
    - Implements: `IOpenLibrarySearchProxy`
    - OpenLibrary API-backed search and identifier lookup
    - Handles title/author query search and ISBN/ASIN lookup fallback behavior
    - Active and in use

### Problems with Current System

- **Legacy Goodreads API paths**: Removed from active runtime provider implementations
- **Proprietary Dependency**: Not community maintainable
- **Single Point of Failure**: No fallback options
- **Legal Concerns**: Terms of service restrictions
- **Data Quality**: Inconsistent metadata, missing books
- **Rate Limiting**: Restrictive API quotas

### Foreign ID System

Current migration direction uses provider-agnostic/OpenLibrary-oriented foreign IDs as the active identity path:

- Database schema uses these IDs
- User libraries are progressively normalized toward OpenLibrary identifiers
- Import/export flows are being migrated to OpenLibrary-oriented external identifier handling

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

**URL**: <https://openlibrary.org/>

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

**URL**: <https://inventaire.io/>

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

**URL**: <https://developers.google.com/books>

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

### Session Progress Update (2026-03-17)

Completed in code on branch `feature/open-library-provider-2026-03-17`:

- Added provider abstraction and fallback orchestration:
  - `IMetadataProvider`
  - `IMetadataProviderRegistry`
  - `MetadataProviderRegistry`
- Refactored search abstraction to be provider-agnostic:
  - `ISearchForNewBook.SearchByExternalId(string idType, string id)` replaces direct `SearchByGoodreadsBookId(...)` interface usage
- Implemented Open Library provider stack:
  - `OpenLibraryClient` with endpoint wrappers (`/search`, `/works`, `/authors`, `/isbn`, `/books`) and 429 retry handling
  - `OpenLibraryMapper` with deterministic resource-to-domain mapping
  - `OpenLibraryProvider` implementing search and metadata interfaces with priority-based fallback role
  - Open Library resource DTOs and `OpenLibraryException`
- Added additive database migration for Open Library foreign IDs:
  - `041_add_open_library_ids.cs`
  - `Book.OpenLibraryWorkId`
  - `AuthorMetadata.OpenLibraryAuthorId`
- Updated import/sync path to remove direct Goodreads proxy coupling in `ImportListSyncService` by using `ISearchForNewBook` abstraction.

Validation status:

- `Bibliophilarr.Core.csproj` builds cleanly (0 errors).
- `Bibliophilarr.Core.Test.csproj` builds cleanly (0 errors).
- Open Library mapper and model equality tests pass.
- Provider fixture tests currently fail due to pre-existing test harness platform assembly naming mismatch (`AutoMoqer.LoadPlatformLibrary()` expected name does not match embedded mono assembly name), not due to Open Library implementation logic.

### Phase 1: Foundation & Documentation ✓

**Status**: Current Phase

**Tasks:**

- [x] Document current architecture
- [x] Research FOSS alternatives
- [x] Create migration plan
- [x] Update README and contributing guides
- [x] Set up project roadmap

**Deliverables:**

- MIGRATION_PLAN.md (this document)
- Updated README.md
- Contributor guidelines for metadata work

### Phase 2: Infrastructure Setup ✓

**Status**: Completed (core slice)

**Tasks:**

1. Create new provider interfaces
2. Implement provider registry system
3. Build metadata quality scorer
4. Create provider testing framework
5. Set up monitoring/logging for providers

**Deliverables:**

- `IMetadataProvider` interface hierarchy
- `MetadataProviderRegistry` service
- `MetadataQualityScorer` implementation
- Unit test framework for providers

**Completed this phase:**

- `IMetadataProvider` and `IMetadataProviderRegistry`
- `MetadataProviderRegistry` priority-based fallback execution
- Provider abstraction wiring for `BookInfoProxy` (priority 1) and Open Library (priority 2)

**Deferred to later phases:**

- Metadata quality scorer
- Expanded provider health/telemetry and scoring instrumentation

### Phase 3: Open Library Provider Implementation ✓

**Status**: Implemented and partially validated

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
    │   ├── OlWorkResource.cs
    │   ├── OlEditionResource.cs
    │   ├── OlAuthorResource.cs
    │   ├── OlSearchDoc.cs
    │   └── OlSearchResponse.cs
  └── OpenLibraryException.cs
```

**Completed this phase:**

- Open Library client, mapper, provider, resources, and exception types
- Identifier search support (`isbn`, `olid`) and explicit unsupported handling (`asin`, `goodreads`)
- 429 retry and retry-after parsing in client
- Unit test coverage for mapper and provider behavior

**Known validation gap:**

- Provider fixture execution is blocked by existing test harness assembly load mismatch and needs a dedicated infrastructure fix before full provider fixture green status can be asserted.

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
- ✅ Documentation updates
- 🔄 Community engagement (ongoing)

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

### Milestone 7: Stable Release (Week 31-34)

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

1. [ROADMAP.md](ROADMAP.md) — current phase ordering and release-hardening posture.
2. [PROJECT_STATUS.md](PROJECT_STATUS.md) — current migration slice completion and validation status.
3. [Open Library developer documentation](https://openlibrary.org/developers/api) — API endpoints and platform guidance.
4. [Open Library data dumps](https://openlibrary.org/developers/dumps) — bulk data option referenced in provider planning.
5. [Inventaire API documentation](https://api.inventaire.io/) — API behavior and endpoint model.
6. [BookBrainz](https://bookbrainz.org/) — future provider consideration.
7. [ISBN International](https://www.isbn-international.org/) — ISBN standard reference.
8. [ISNI](https://isni.org/) — author identifier reference.
9. [VIAF](https://viaf.org/) — authority-file reference.

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
