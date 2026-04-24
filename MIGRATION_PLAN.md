# Bibliophilarr Metadata Migration Plan

## Executive summary

This document outlines the comprehensive technical plan for migrating Bibliophilarr from proprietary Goodreads metadata to Free and Open Source Software (FOSS) metadata providers. The goal is to create a sustainable, reliable, and community-maintainable book and audiobook collection manager.

## Implementation progress snapshots

### March 26, 2026 — Hardcover metadata expansion and series persistence progress

Hardcover metadata provider fixes deployed and RefreshAuthor triggered for all 430 library
authors. Key improvements:

- **Full bibliography**: Increased Hardcover GraphQL `contributions(limit: 500)` (was 100).
  Books grew from 1,882 to 3,944+.
- **Author links**: `GetAuthorInfo` now populates `metadata.Links` with the Hardcover author
  page URL. 35 authors have links so far.
- **Series persistence progress**: Series table grew from 33 to 210; SeriesBookLink from
  56 to 515. RefreshAuthor (command 1080) is queued for all 430 authors — counts will
  continue growing.
- **Crash guards**: Fixed `TrackedDownloadService` AuthorId=0 crash and
  `MediaCoverProxy` file:// scheme crash.

Migration risk posture update:

- Series persistence blocker is showing significant progress (210 series, 515 links)
  as the Hardcover provider now returns richer data. Full resolution expected after
  RefreshAuthor completes for all 430 authors.
- Hardcover API intermittently rate-limits under batch load (408/500 errors); handled
  gracefully but some authors require re-refresh.

### March 24, 2026 — Comprehensive deep audit v2

Comprehensive audit v2 across six parallel audits (backend C#, frontend, CI/CD and build,
documentation, Docker and infrastructure, packages and dependencies) identified **287 distinct
findings** — 14 Critical, 58 High, 101 Medium, 93 Low, and 21 Enhancement/Migration
opportunities. These are consolidated into **176 remediation items** (RQ-001 through RQ-178)
in `PROJECT_STATUS.md` § Prioritized Remediation Queue.

### Migration-relevant findings

**Provider reliability:**

- All provider API calls (Hardcover, Inventaire, GoogleBooks, OpenLibrary) lack explicit
  per-request timeouts; can hang indefinitely during import identification. Uniform
  20-30s timeout enforcement planned (RQ-017).
- No formal circuit breaker pattern for failing providers — partial implementation in
  `BookSearchFallbackExecutionService` but not standardized (RQ-077).
- 6+ `.FirstOrDefault()` chains on provider responses without null guards (RQ-078).
- Unvalidated external provider payloads — minimal schema validation (RQ-089).
- Provider response exception handling does not distinguish timeout vs 404 vs auth (RQ-034).

**Performance and scalability:**

- `BookController.GetBooks()` loads entire edition + author table without pagination;
  OOM risk on large libraries (RQ-002).
- `ImportListSyncService` has O(n*m) exclusion fetch pattern (RQ-019).
- `OpenLibraryIdBackfillService` loads all books + authors in one pass (RQ-031).
- `AuthorService.GetAllAuthors()` cached 30s loads entire table (RQ-033).
- Kestrel `MaxRequestBodySize` is null (unlimited) — container OOM risk (RQ-118).

**Async/threading:**

- 10+ sync-over-async `.GetAwaiter().GetResult()` sites risk thread pool starvation and
  deadlock under load (RQ-003). Affects HttpClient, BookSearchService,
  AuthorSearchService, EpubReader, LocalizationService, and others.
- Missing `CancellationToken` propagation across middleware and core services (RQ-021).

**Supply chain and infrastructure:**

- Docker supply-chain: unpinned base images, unverified Node tarball, root runtime,
  no image scanning, no SBOM. Expanded hardening plan in PROJECT_STATUS.md (RQ-004,
  RQ-005, RQ-023, RQ-024, RQ-111, RQ-112).
- RestSharp 106.15.0 unmaintained with known vulnerabilities — migration to HttpClient
  planned (RQ-064, RQ-157).
- Selenium 3.141.0 EOL with known CVEs (RQ-065).
- All GitHub Actions pinned by floating tags, not commit SHAs (RQ-015).

**Frontend:**

- Zero frontend test files — no unit, integration, or component tests exist (RQ-066).
- React 17.0.2 approaching EOL; React Router 5.x already EOL (RQ-159, RQ-160).
- moment.js adds ~13KB gzipped; 34 import sites (RQ-162).
- 5+ deprecated/abandoned npm packages still in dependency tree (RQ-067, RQ-068, RQ-069).

**Documentation:**

- This file references migration `041` at line ~909; actual file is `042` (RQ-007).
- Scripts reference deleted `phase6-packaging-validation.yml` (RQ-006).
- 10+ duplicate `## Implementation Progress Snapshot` H2 headings in this file (RQ-048).

Full prioritized remediation queue: see `PROJECT_STATUS.md` § Prioritized Remediation Queue.

### March 24, 2026 — Book import identification quality fixes

Three compounding bugs in the import identification pipeline were identified during production library analysis (81% unlinked files) and fixed:

1. **DistanceCalculator case-sensitive format matching**: `EbookFormats.Contains()` and `AudiobookFormats.Contains()` now use `StringComparer.OrdinalIgnoreCase`. Hardcover's `"Ebook"` was not matching `"ebook"` in format lists, applying a distance penalty to 100% of Hardcover editions.

2. **CloseAlbumMatchSpecification format bias on existing files**: `"ebook_format"` added to the distance exclusion set for files already on disk, preventing format distance from pushing existing library files past the acceptance threshold.

3. **CandidateService ISBN early-exit preventing author+title search**: Removed the `seenCandidates.Any()` early exit that short-circuited author+title search when ISBN/ASIN results existed. Files with wrong embedded ISBNs now get searched by author+title as well. `HashSet` deduplication prevents duplicates; a `contextualFallbackFoundCandidates` guard prevents redundant author+title searches when the ISBN-miss fallback already performed one.

Impact on metadata migration posture:

- Book identification rate improved from ~19% to projected ~67-72% on production-shaped library.
- Hardcover provider integration quality improved (format data now correctly consumed).
- Identification fallback paths are more resilient (ISBN failures no longer block title-based matching).

Validation: 40/40 targeted tests passed; 158/159 broader import tests passed (1 pre-existing flaky test confirmed unrelated).

### March 22, 2026 — Hardcover/runtime logging hardening

Completed in this migration-hardening slice:

- Hardcover provider observability now records query entry, skip reasons, token-source selection, provider-declared search errors, malformed payload anomalies, and mapped result counts at level-appropriate log severities.
- Hardcover startup environment tokens now participate in provider enablement, aligning runtime routing with documented operator setup.
- Local metadata exporter scripts (`provider_metadata_pull_test.py`, `live_provider_enrich_missing_metadata.py`) now use structured Python logging with configurable `--log-level` output for local replay and enrichment work.

Validation status for this slice:

- Targeted Hardcover provider fixture coverage updated for environment-token routing.
- Script syntax and solution build validation executed after the logging changes.

### March 22, 2026 — Release-evidence/test-runner completion

Additional verification update (March 22, 2026):

- Executed a fresh full solution build and confirmed success.
- Re-verified targeted extraction and import-identification suites covering:
  - ISBN fallback extraction,
  - ASIN fallback extraction,
  - distance calculation,
  - import decision behavior,
  - candidate ranking behavior.
- Verification scope explicitly included author, series, book, and cover
    identification paths.

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
- Series persistence blocker is actively resolving — 210 series and 515 links populated
  as of March 26, 2026 (was zero in staging). RefreshAuthor for all 430 authors is in
  progress via Hardcover provider.
- Duplicate author convergence in staging DB state remains a secondary concern.

### March 18, 2026 — Provider orchestration integration

Completed in the current migration slice:

- Metadata provider orchestration is implemented and integrated into search, add, refresh, and import-list flows.
- Runtime provider controls are available via config/API/UI, including provider enablement and ordering.
- Runtime provider controls are available via config/API/UI, including timeout, retry, and circuit-breaker settings.
- Open Library, Google Books, and Inventaire provider enablement now respect configuration flags.
- Inventaire provider baseline is implemented and registered as a secondary metadata source.
- Inventaire can be disabled through the runtime metadata-provider configuration exposed in API and UI settings.
- Provider telemetry collection and diagnostics API endpoints are available for operational visibility.
- Open Library identifier backfill command/service is implemented for startup-triggered migration assistance.
- Provenance fields are exposed in API resources and surfaced in book index UI.
- Status UI includes provider diagnostics, and dry-run automation captures before/after provenance snapshots on staging.

Validation status for this slice:

- API tests: pass (`Bibliophilarr.Api.Test`)
- Core targeted tests: pass for `MetadataProviderOrchestratorFixture` and `ImportListSyncServiceFixture`
- Import-list edge-case handling updated to avoid adding unresolved external-ID books

### March 17, 2026 — Open Library provider implementation

Completed in code on branch `feature/open-library-provider-2026-03-17`:

- Added provider abstraction and fallback orchestration (`IMetadataProvider`, `IMetadataProviderRegistry`, `MetadataProviderRegistry`).
- Refactored search abstraction to be provider-agnostic (`ISearchForNewBook.SearchByExternalId(string idType, string id)` replaces direct `SearchByGoodreadsBookId(...)` interface usage).
- Implemented Open Library provider stack: `OpenLibraryClient` with endpoint wrappers and 429 retry handling, `OpenLibraryMapper` with deterministic resource-to-domain mapping, `OpenLibraryProvider` implementing search and metadata interfaces.
- Added additive database migration for Open Library foreign IDs (`042_add_open_library_ids.cs`, `Book.OpenLibraryWorkId`, `AuthorMetadata.OpenLibraryAuthorId`).
- Updated import/sync path to remove direct Goodreads proxy coupling in `ImportListSyncService` by using `ISearchForNewBook` abstraction.

Validation status: Core and test projects build cleanly. Open Library mapper and model equality tests pass. Provider fixture tests blocked by pre-existing test harness platform assembly naming mismatch (not caused by Open Library implementation).

### March 21, 2026 — TD-META completion

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

### March 21, 2026 — Routing/dedupe/import hardening continuation

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

Validation status: Deferred — covered by subsequent hardening passes (March 22–26).

Known gap: Series persistence completeness under concurrent author-refresh scenarios not yet validated end-to-end.

### March 22, 2026 — Hardening pass

Completed in this hardening and validation slice:

- Config validation cleanup: removed clamping logic from `IsbnContextFallbackLimit` and `BookImportMatchThresholdPercent` setters, moving validation to API layer for cleaner round-trip behavior.
- Test fixture alignment: fixed 16 test failures across 6 suites by properly aligning assertion expectations with actual logging behavior and normalizing identifier prefixes.
- Frontend test-runner confirmation: validated current jest.config.cjs and package.json setup is operational; removed stale canonical-doc contradictions claiming test-runner gaps no longer present.
- Full pipeline validation: backend build (MSBuild/StyleCop), Core.Test suite (2640/2640 passing), frontend ESLint/Stylelint, webpack build, and linux-x64 net8.0 packaging all passing with exit code 0.
- Binary operational validation: packaged artifact confirms /ping health endpoint responsive with HTTP 200 and {"status": "OK"} response.

Validation status:

- Full solution build: PASS (0 warnings, 0 errors).
- Full test suite (non-integration): PASS (2640 passed, 59 skipped, 0 failed).
- Frontend lint + build: PASS (ESLint + Stylelint + Webpack production build).
- Packaged binary smoke test: PASS (HTTP 200 from /ping endpoint).

Migration safety posture:

- Session changes are low-impact test/config corrections with no schema or persistence changes.
- ConfigService removals preserve API contracts; setter validation redundancy eliminated.
- All changes backward-compatible and non-breaking.
- No temporary files or test artifacts remain in working tree.

### March 21, 2026 — Hardening follow-up

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

### March 21, 2026 — Full-library QA triage

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
6. Import throughput optimization for production-shaped libraries (instrumentation + phased execution + bounded concurrency).
7. ~~Single-instance dual-format management for ebook/audiobook variants per title.~~ **DONE** — all 16 slices (DF-1 through DF-16) implemented.

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

#### TD-IMPORT-PERF-001: Production-scale import throughput optimization

Objective:

- Reduce wall-clock time for large media identification/import runs while preserving match quality and deterministic behavior.

Primary touch points:

- `src/NzbDrone.Core/MediaFiles/BookImport/ImportDecisionMaker.cs`
- `src/NzbDrone.Core/MediaFiles/BookImport/Identification/CandidateService.cs`
- `src/NzbDrone.Core/Books/Services/RefreshBookService.cs`
- `src/NzbDrone.Core/MetadataSource/MetadataProviderOrchestrator.cs`
- `src/Bibliophilarr.Api.V1/Search/SearchTelemetryService.cs`

Proposed change shape:

1. Add phase-level timing/volume telemetry for import pipeline stages.
2. Add configurable bounded concurrency and provider request ceilings.
3. Implement phased identification strategy (local/identifier first, provider fallback only when needed).
4. Add checkpoint/resume-friendly queue progression for long-running import jobs.

Acceptance criteria:

- Measured throughput improvement on production-shaped fixture cohorts.
- No statistically significant regression in accepted match rate.
- Provider timeout/error rates remain within explicit threshold bounds.

Rollback/mitigation:

- Keep existing import path behind a feature flag and allow immediate reversion to current sequential strategy.

Implementation task outline:

1. IP-1 Baseline and telemetry contract
     - Deliverables:
         - Stage timers for parse, identify, provider fetch, score, persist.
         - Structured run summary output (`processed`, `duration`, `throughput`, `timeouts`, `errors`).
     - Validation:
         - Unit coverage for telemetry aggregation.
         - Fixture run emits deterministic summary file.
2. IP-2 Concurrency and throttling controls
     - Deliverables:
         - Configurable import worker count.
         - Provider-specific request concurrency and timeout settings.
     - Validation:
         - Concurrency stress fixture verifies bounded in-flight calls.
         - No unbounded queue growth under synthetic slow-provider conditions.
3. IP-3 Phased identification path
     - Deliverables:
         - Phase A (identifier/local) then Phase B (constrained provider search) then Phase C (expanded fallback).
     - Validation:
         - Contract tests verify phase ordering and escalation conditions.
         - No phase skipping for unresolved low-confidence candidates.
4. IP-4 Resume/checkpoint support
     - Deliverables:
         - Durable checkpoint cursor for long-running import batches.
         - Restart behavior resumes from checkpoint.
     - Validation:
         - Integration test: forced interruption + resume with no duplicate imports.
5. IP-5 Performance gate
     - Deliverables:
         - Benchmark job for production-shaped fixture cohort.
         - Threshold profile for throughput, quality drift, and provider-failure budget.
     - Validation:
         - Gate fails on threshold breach and emits actionable artifact.

Measurement plan:

- Baseline dataset: production-shaped cohort used by replay/perf runs.
- Primary KPI: `objects_per_minute`.
- Guardrail KPIs:
  - `accepted_match_rate_delta`
  - `provider_timeout_rate`
  - `unresolved_ratio_delta`
- Success threshold (initial target): at least 30 percent throughput gain with no quality regressions beyond accepted threshold profile.

#### TD-DUAL-FORMAT-001: Single-instance ebook and audiobook variant management

**Status**: Implementation complete (April 2026). All 16 slices (DF-1 through DF-16) delivered and feature-flagged via `EnableDualFormatTracking`.

##### Objective

Manage ebook and audiobook variants for the same title in one instance without
policy conflicts, tracking loss, or requiring duplicate author entries. A single
Author object tracks both formats independently at the per-book level, with each
format owning its own quality profile, root folder, download client routing tags,
monitored state, and file tracking.

##### Design principles

1. **One author, multiple formats** — metadata is shared (author, series, book,
   cover, identifiers). Only the physical-format tracking (quality, files, paths)
   diverges per format.
2. **One metadata search** — a search for a book queries indexers once. The
   returned releases are then matched to the appropriate format slot based on the
   detected quality falling within a format's quality profile.
3. **Format-scoped policy** — each format has its own quality profile, root
   folder, tags, monitored state, and upgrade tracking. No cross-format
   interference.
4. **Additive schema** — new tables and columns only. No destructive changes to
   existing tables. Feature-flagged with full rollback path.

##### Current architecture (blockers)

The existing data model has three structural constraints that prevent dual-format
tracking:

| Constraint | Location | Impact |
|---|---|---|
| One `QualityProfileId` per Author | [Author.cs](src/NzbDrone.Core/Books/Model/Author.cs#L29) | Cannot express "Spoken for audiobooks AND eBook for ebooks" on the same author |
| One `Path` / `RootFolderPath` per Author | [Author.cs](src/NzbDrone.Core/Books/Model/Author.cs#L26-L27) | Files for both formats would land under one directory tree |
| One monitored Edition per Book | [FixMultipleMonitoredEditions.cs](src/NzbDrone.Core/Housekeeping/Housekeepers/FixMultipleMonitoredEditions.cs#L17), [BookEditionSelector.cs](src/NzbDrone.Core/Books/BookEditionSelector.cs#L21) | Even if both ebook and audiobook editions exist, housekeeping forcibly un-monitors all but one |

Additional pipeline constraints:

- `QualityAllowedByProfileSpecification` reads `subject.Author.QualityProfile.Value` — a single profile per author. ([QualityAllowedByProfileSpecification.cs](src/NzbDrone.Core/DecisionEngine/Specifications/QualityAllowedByProfileSpecification.cs#L22))
- `DownloadService.DownloadReport` passes `remoteBook.Author.Tags` for download client routing — a single tag set per author. ([DownloadService.cs](src/NzbDrone.Core/Download/DownloadService.cs#L58))
- `AuthorPathBuilder.BuildPath` uses `author.RootFolderPath` — a single root folder. ([AuthorPathBuilder.cs](src/NzbDrone.Core/Books/Utilities/AuthorPathBuilder.cs#L28))
- `BookCutoffService.BooksWhereCutoffUnmet` evaluates against one quality profile. ([BookCutoffService.cs](src/NzbDrone.Core/Books/Services/BookCutoffService.cs))
- `ReleaseSearchService.BookSearch` uses `book.Editions.Value.SingleOrDefault(x => x.Monitored).Title` — expects exactly one monitored edition. ([ReleaseSearchService.cs](src/NzbDrone.Core/IndexerSearch/ReleaseSearchService.cs#L84))

##### Target data model

New entity: **AuthorFormatProfile**

```
┌─────────────────────────────────────────────┐
│ AuthorFormatProfiles (new table)            │
├─────────────────────────────────────────────┤
│ Id              INT PK AUTO                 │
│ AuthorId        INT FK → Authors.Id         │
│ FormatType      INT (enum: 0=Ebook,         │
│                            1=Audiobook)     │
│ QualityProfileId INT FK → QualityProfiles.Id│
│ RootFolderPath  TEXT NOT NULL               │
│ Tags            TEXT (JSON array of int)    │
│ Monitored       BOOL DEFAULT true           │
│ Path            TEXT (computed author path   │
│                       under this root)      │
├─────────────────────────────────────────────┤
│ UNIQUE(AuthorId, FormatType)                │
└─────────────────────────────────────────────┘
```

Modified entity: **Edition** monitoring

```
Current:  One Monitored=true edition per Book (enforced globally)
Target:   One Monitored=true edition per Book PER FormatType
          (i.e. one monitored ebook edition AND one monitored audiobook edition)
```

Relationship map:

```
Author (Robert Blaise)
  ├─ AuthorFormatProfiles
  │    ├─ { FormatType: Audiobook, QualityProfileId: 2 (Spoken),
  │    │    RootFolderPath: /media/audiobooks/, Tags: [1],
  │    │    Path: /media/audiobooks/Robert Blaise }
  │    └─ { FormatType: Ebook, QualityProfileId: 1 (eBook),
  │         RootFolderPath: /media/ebooks/, Tags: [2],
  │         Path: /media/ebooks/Robert Blaise }
  │
  └─ Book (1% Lifesteal)
       ├─ Edition (audiobook edition)
       │    ├─ Format: "Audiobook"
       │    ├─ Monitored: true  ← monitored for Audiobook format
       │    └─ BookFiles: [Robert Blaise - 1% Lifesteal.m4b]
       └─ Edition (ebook edition)
            ├─ Format: "Ebook"
            ├─ Monitored: true  ← monitored for Ebook format
            └─ BookFiles: [Robert Blaise - 1% Lifesteal.epub]
```

**Design decision rationale: AuthorFormatProfile vs. BookVariant**

The format profile lives at the Author level (not per-Book) because:

- Quality profile, root folder, tags, and download client routing are
  author-scoped concerns — they apply uniformly across all books by that author.
- Per-book format configuration would create O(books x formats) configuration
  overhead. Authors typically want "all audiobooks go here with this profile."
- The per-book format tracking is handled by Edition monitoring: the Edition
  already has `Format` and `IsEbook` fields, and the "one monitored edition per
  format type" constraint provides per-book format-level control.
- This mirrors how the current single-profile model works: QualityProfile is set
  on Author and applies to all books.

##### Data flow changes by pipeline stage

**1. Author add flow**

Current: User selects one root folder and one quality profile when adding an author.

Target: User can configure one or more format profiles when adding an author.
Each format profile specifies: format type, quality profile, root folder, and
tags. At minimum one format profile is required (backward compatible with current
single-profile behavior).

Files affected:

- [AddAuthorService.cs](src/NzbDrone.Core/Books/Services/AddAuthorService.cs) — `SetPropertiesAndValidate()` creates format profiles from add options
- [AuthorController.cs](src/Bibliophilarr.Api.V1/Author/AuthorController.cs) — `AddAuthor()` accepts format profile array
- [AuthorResource.cs](src/Bibliophilarr.Api.V1/Author/AuthorResource.cs) — new `FormatProfiles` property
- Frontend `AddNewAuthorModal` — format profile configuration UI

**2. Book monitoring**

Current: `Book.Monitored` is a single boolean. `Edition.Monitored` allows one
monitored edition per book. `FixMultipleMonitoredEditions` housekeeping enforces
the single-monitored-edition constraint.

Target: `Book.Monitored` remains a single boolean (does the user want this book
at all?). Edition monitoring becomes per-format: one monitored edition per format
type per book. The housekeeping task is updated to enforce "one monitored edition
per format type" instead of "one monitored edition globally."

Files affected:

- [FixMultipleMonitoredEditions.cs](src/NzbDrone.Core/Housekeeping/Housekeepers/FixMultipleMonitoredEditions.cs) — scope constraint per format type
- [BookEditionSelector.cs](src/NzbDrone.Core/Books/BookEditionSelector.cs) — `GetPreferredEdition(formatType)` overload
- [EditionRepository.cs](src/NzbDrone.Core/Books/Repositories/EditionRepository.cs) — `SetMonitoredEdition` scoped by format
- [BookMonitoredService.cs](src/NzbDrone.Core/Books/Services/BookMonitoredService.cs) — monitor/unmonitor per format

**3. Search and indexer query**

Current: `ReleaseSearchService.BookSearch()` builds search criteria from the
single monitored edition's title and sends one query to indexers.

Target: Search remains a single indexer query (metadata is shared across
formats). The search criteria is built from the book's title. The change is
downstream: returned releases are evaluated against each active format's quality
profile separately during decision-making.

Key insight: a search for "Robert Blaise 1% Lifesteal" returns BOTH m4b and epub
releases. The decision engine sorts them into the correct format slot.

Files affected:

- [ReleaseSearchService.cs](src/NzbDrone.Core/IndexerSearch/ReleaseSearchService.cs) — use book title instead of monitored edition title; pass format context to decision maker
- [BookSearchService.cs](src/NzbDrone.Core/IndexerSearch/BookSearchService.cs) — `MissingBookSearchCommand` checks missing status per format

**4. Download decision engine**

Current: `QualityAllowedByProfileSpecification` evaluates against
`subject.Author.QualityProfile.Value` — one profile.

Target: When the feature flag is active, the specification identifies which
format the release belongs to (based on detected quality: audio qualities
MP3/M4B/FLAC → Audiobook format; ebook qualities PDF/MOBI/EPUB/AZW3 → Ebook
format). It then evaluates against the matching `AuthorFormatProfile`'s quality
profile.

Quality-to-format mapping (deterministic, based on existing quality weight
ranges):

| Quality | Weight | FormatType |
|---|---|---|
| PDF | 5 | Ebook |
| MOBI | 10 | Ebook |
| EPUB | 11 | Ebook |
| AZW3 | 12 | Ebook |
| MP3 | 100 | Audiobook |
| M4B | 105 | Audiobook |
| FLAC | 110 | Audiobook |
| Unknown | 0 | Falls back to author's legacy profile |
| UnknownAudio | 50 | Audiobook |

Files affected:

- [QualityAllowedByProfileSpecification.cs](src/NzbDrone.Core/DecisionEngine/Specifications/QualityAllowedByProfileSpecification.cs) — resolve format-specific profile
- [Quality.cs](src/NzbDrone.Core/Qualities/Quality.cs) — add `FormatType` classification helper
- [RemoteBook.cs](src/NzbDrone.Core/Parser/Model/RemoteBook.cs) — carry resolved format context
- [UpgradableSpecification.cs](src/NzbDrone.Core/DecisionEngine/Specifications/UpgradableSpecification.cs) — compare against format-specific cutoff

**5. Download client routing**

Current: `DownloadService.DownloadReport` passes `remoteBook.Author.Tags` to
`DownloadClientProvider.GetDownloadClient()`.

Target: When format is resolved, use the format profile's tags instead of the
author's tags. This routes audiobook grabs to qBittorrent-Audiobooks (via
audiobook tag) and ebook grabs to qBittorrent-Ebooks (via ebook tag).

Files affected:

- [DownloadService.cs](src/NzbDrone.Core/Download/DownloadService.cs) — resolve tags from format profile

**6. Import pipeline**

Current: `ImportDecisionMaker` → `IdentificationService` matches files to an
Edition by metadata/tags. `ImportApprovedBooks` writes `BookFile` with
`EditionId`.

Target: After identification matches a file to a Book, the import pipeline also
resolves which format profile applies based on the file's detected quality. The
file is assigned to the correct format-specific edition. Import rejection
(`AuthorPathInRootFolderSpecification`) checks against the format-specific root
folder path, not the author's single path.

Files affected:

- [ImportDecisionMaker.cs](src/NzbDrone.Core/MediaFiles/BookImport/ImportDecisionMaker.cs) — pass format context through import decisions
- [IdentificationService.cs](src/NzbDrone.Core/MediaFiles/BookImport/Identification/IdentificationService.cs) — prefer edition matching format type
- [ImportApprovedBooks.cs](src/NzbDrone.Core/MediaFiles/BookImport/ImportApprovedBooks.cs) — assign to format-specific edition
- [AuthorPathInRootFolderSpecification.cs](src/NzbDrone.Core/MediaFiles/BookImport/Specifications/AuthorPathInRootFolderSpecification.cs) — check against format-specific root

**7. File path building**

Current: `AuthorPathBuilder.BuildPath` uses `author.RootFolderPath` to construct
`/media/audiobooks/Robert Blaise/`.

Target: Path building uses the format profile's `RootFolderPath` to construct
format-specific paths:

- Audiobook: `/media/audiobooks/Robert Blaise/1% Lifesteal/`
- Ebook: `/media/ebooks/Robert Blaise/1% Lifesteal/`

Files affected:

- [AuthorPathBuilder.cs](src/NzbDrone.Core/Books/Utilities/AuthorPathBuilder.cs) — accept format context, use format profile root
- [FileNameBuilder.cs](src/NzbDrone.Core/Organizer/FileNameBuilder.cs) — `BuildBookFilePath` uses format-aware author path
- [BookFileMovingService.cs](src/NzbDrone.Core/MediaFiles/BookFileMovingService.cs) — pass format context to path builder

**8. Missing and cutoff evaluation**

Current: `BookCutoffService.BooksWhereCutoffUnmet` evaluates book files against
the author's single quality profile cutoff.

Target: Missing/cutoff evaluation is per-format. A book can be simultaneously
"missing audiobook" and "has ebook at cutoff." The Wanted/Missing and
Wanted/Cutoff Unmet pages show format-scoped results.

Files affected:

- [BookCutoffService.cs](src/NzbDrone.Core/Books/Services/BookCutoffService.cs) — evaluate per format profile
- [BookService.cs](src/NzbDrone.Core/Books/Services/BookService.cs) — `BooksWithoutFiles` scoped by format
- [WantedController / CutoffController](src/Bibliophilarr.Api.V1/Wanted/) — expose format filter
- [BookSearchService.cs](src/NzbDrone.Core/IndexerSearch/BookSearchService.cs) — missing/cutoff commands specify format

**9. API resources**

New and modified API resources:

```csharp
// New resource
public class AuthorFormatProfileResource
{
    public int Id { get; set; }
    public int FormatType { get; set; }       // 0=Ebook, 1=Audiobook
    public int QualityProfileId { get; set; }
    public string RootFolderPath { get; set; }
    public HashSet<int> Tags { get; set; }
    public bool Monitored { get; set; }
    public string Path { get; set; }          // computed
}

// Modified: AuthorResource
public class AuthorResource
{
    // ... existing fields ...
    public List<AuthorFormatProfileResource> FormatProfiles { get; set; }
    // QualityProfileId, Path, RootFolderPath kept for backward compat
    // (legacy clients read these; new clients use FormatProfiles)
}

// Modified: BookResource
public class BookResource
{
    // ... existing fields ...
    public List<BookFormatStatusResource> FormatStatuses { get; set; }
    // Per-format: { formatType, monitored, hasFile, quality, editionId }
}
```

Files affected:

- [AuthorResource.cs](src/Bibliophilarr.Api.V1/Author/AuthorResource.cs) — add `FormatProfiles`
- [BookResource.cs](src/Bibliophilarr.Api.V1/Books/BookResource.cs) — add `FormatStatuses`
- [AuthorController.cs](src/Bibliophilarr.Api.V1/Author/AuthorController.cs) — CRUD for format profiles
- New: `AuthorFormatProfileResource.cs`, `BookFormatStatusResource.cs`

**10. Frontend changes**

Author detail page:

- Header shows format profile badges (e.g., "Audiobook: Spoken | Ebook: eBook")
- Each format profile's root folder, quality profile, and tags are configurable
  via the author edit modal
- Add Author modal allows configuring format profiles (with sensible defaults
  based on selected root folder)

Book table:

- Each book row shows per-format status columns (e.g., audiobook icon with
  green/red, ebook icon with green/red)
- Monitoring toggle can be per-format (click audiobook icon to toggle audiobook
  monitoring for that book)

Files affected:

- [AuthorDetails.js](frontend/src/Author/Details/AuthorDetails.js) — format profile display
- [AuthorDetailsHeader.js](frontend/src/Author/Details/AuthorDetailsHeader.js) — show format badges
- [AuthorDetailsSeason.js](frontend/src/Author/Details/AuthorDetailsSeason.js) — per-format status columns
- [EditAuthorModalContent.js](frontend/src/Author/Edit/EditAuthorModalContent.js) — format profile configuration
- [AddNewAuthorModal*.js](frontend/src/AddNewItem/) — format profile setup
- [bookActions.js](frontend/src/Store/Actions/bookActions.js) — per-format monitoring toggles
- New: `AuthorFormatProfileEditor.js` component

##### Feature flag and backward compatibility

Configuration key: `EnableDualFormatTracking` (default: `true`)

When **disabled** (legacy mode):

- Author continues to use its single `QualityProfileId`, `Path`,
  `RootFolderPath`, and `Tags` fields exactly as today.
- `AuthorFormatProfiles` table exists but is not read.
- `FixMultipleMonitoredEditions` enforces the current single-monitored-edition
  constraint.
- All API responses omit `FormatProfiles` and `FormatStatuses` (or return empty
  arrays).
- No behavioral change for existing users.

When **enabled** (dual-format mode):

- Author's legacy `QualityProfileId`, `Path`, `RootFolderPath`, `Tags` become
  fallback defaults for new format profiles.
- `AuthorFormatProfiles` table is authoritative for per-format routing.
- `FixMultipleMonitoredEditions` enforces one monitored edition per format type
  per book.
- API responses include populated `FormatProfiles` and `FormatStatuses`.
- All pipeline stages (search, decision, grab, import, path build, missing/cutoff)
  operate in format-aware mode.

##### Database migration

Migration number: 045 (next sequential after 044_normalize_title_slugs)

```sql
-- Additive: new table
CREATE TABLE "AuthorFormatProfiles" (
    "Id"               INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
    "AuthorId"         INTEGER NOT NULL,
    "FormatType"       INTEGER NOT NULL,   -- 0=Ebook, 1=Audiobook
    "QualityProfileId" INTEGER NOT NULL,
    "RootFolderPath"   TEXT NOT NULL,
    "Tags"             TEXT NOT NULL DEFAULT '[]',
    "Monitored"        BOOLEAN NOT NULL DEFAULT 1,
    "Path"             TEXT NOT NULL DEFAULT '',
    FOREIGN KEY ("AuthorId") REFERENCES "Authors"("Id") ON DELETE CASCADE,
    FOREIGN KEY ("QualityProfileId") REFERENCES "QualityProfiles"("Id")
);

CREATE UNIQUE INDEX "IX_AuthorFormatProfiles_AuthorId_FormatType"
    ON "AuthorFormatProfiles" ("AuthorId", "FormatType");

-- Auto-populate: one format profile per existing author from current config
-- Detect format type from current quality profile's allowed qualities
INSERT INTO "AuthorFormatProfiles"
    ("AuthorId", "FormatType", "QualityProfileId", "RootFolderPath", "Tags",
     "Monitored", "Path")
SELECT
    a."Id",
    CASE
        WHEN EXISTS (
            SELECT 1 FROM json_each(qp."Items")
            WHERE json_extract(value, '$.quality') IN (10, 11, 12, 13)
              AND json_extract(value, '$.allowed') = 1
        ) THEN 1  -- Audiobook
        ELSE 0    -- Ebook
    END,
    a."QualityProfileId",
    COALESCE(a."RootFolderPath",
             substr(a."Path", 1, length(a."Path")
                    - length(replace(a."Path",
                                     rtrim(a."Path", replace(a."Path",'/','')),
                                     '')))),
    COALESCE(a."Tags", '[]'),
    1,
    a."Path"
FROM "Authors" a
JOIN "QualityProfiles" qp ON qp."Id" = a."QualityProfileId";
```

Rollback: `DROP TABLE "AuthorFormatProfiles"` and set feature flag to false. No
existing tables are modified.

##### Acceptance criteria

- Ebook and audiobook variants can co-exist for the same title without conflict.
- Variant-specific quality upgrades do not alter opposite-format tracking state.
- Existing single-format libraries remain functionally unchanged when flag is off.
- Search produces one indexer query per book (not per format).
- Releases are routed to the correct format slot based on detected quality.
- Download client routing uses format-specific tags.
- Imported files land in format-specific root folder paths.
- Missing/cutoff evaluation reports per-format status.
- API backward compatibility: legacy clients ignoring `FormatProfiles` continue
  to work via the existing author-level `QualityProfileId`/`Path`/`Tags` fields.

##### Rollback and mitigation

- Feature flag `EnableDualFormatTracking=false` reverts to legacy behavior.
- `AuthorFormatProfiles` table is additive — dropping it restores the original
  schema with no data loss.
- Auto-populated format profiles are derived from existing author config, so
  enabling the flag does not require manual re-configuration.
- If a format profile is deleted, the author falls back to its legacy fields.

##### Measurement plan

Correctness KPIs:

- `variant_conflict_count` (target: 0) — books where format profiles disagree
  on the same file.
- `cross_variant_overwrite_count` (target: 0) — import events that overwrote a
  file tracked by the opposite format.

Usability KPIs:

- `variant_policy_apply_success_rate` (target: 100 percent in acceptance suite)
  — format profile correctly resolved for every grab/import decision.
- `search_query_count_per_book` (target: 1) — confirm searches are not
  multiplied per format.

Compatibility KPIs:

- `legacy_library_regression_failures` (target: 0) — existing test suite passes
  with flag off and with flag on (single-profile author).

##### Implementation slices

**DF-1: Domain model and schema** (backend only, no behavioral change)

Deliverables:

- New `FormatType` enum (`Ebook = 0`, `Audiobook = 1`).
- New `AuthorFormatProfile` entity class.
- New `IAuthorFormatProfileRepository` and `IAuthorFormatProfileService`.
- Migration 045: create `AuthorFormatProfiles` table with auto-population.
- `Quality.cs`: add `GetFormatType(Quality quality)` static helper.
- Feature flag `EnableDualFormatTracking` in `IConfigService`.
- Unit tests for migration up/down, format type mapping, repository CRUD.

Files to create:

- `src/NzbDrone.Core/Books/Model/FormatType.cs`
- `src/NzbDrone.Core/Books/Model/AuthorFormatProfile.cs`
- `src/NzbDrone.Core/Books/Repositories/AuthorFormatProfileRepository.cs`
- `src/NzbDrone.Core/Books/Services/AuthorFormatProfileService.cs`
- `src/NzbDrone.Core/Datastore/Migration/045_add_author_format_profiles.cs`

Files to modify:

- [Quality.cs](src/NzbDrone.Core/Qualities/Quality.cs) — add `GetFormatType()` helper
- [ConfigService.cs](src/NzbDrone.Core/Configuration/ConfigService.cs) — add `EnableDualFormatTracking` property
- [IConfigService.cs](src/NzbDrone.Core/Configuration/IConfigService.cs) — add interface member
- [TableMapping.cs](src/NzbDrone.Core/Datastore/TableMapping.cs) — register `AuthorFormatProfile` entity

Validation:

- Migration test verifies schema creation and auto-population.
- Repository test confirms CRUD operations and unique constraint.
- `Quality.GetFormatType()` test covers all quality values.
- Build succeeds. Existing test suite passes unchanged.

**DF-2: Edition monitoring per format type** (behavioral change when flag on)

Deliverables:

- Modify `FixMultipleMonitoredEditions.Clean()` to enforce one monitored edition
  per format type per book (when flag enabled) instead of one globally.
- Modify `BookEditionSelector.GetPreferredEdition()` to accept optional
  `FormatType` parameter.
- Modify `EditionRepository.SetMonitoredEdition()` to scope by format.
- `BookMonitoredService` — apply monitoring changes per format when flag enabled.

Files to modify:

- [FixMultipleMonitoredEditions.cs](src/NzbDrone.Core/Housekeeping/Housekeepers/FixMultipleMonitoredEditions.cs)
- [BookEditionSelector.cs](src/NzbDrone.Core/Books/BookEditionSelector.cs)
- [EditionRepository.cs](src/NzbDrone.Core/Books/Repositories/EditionRepository.cs)
- [BookMonitoredService.cs](src/NzbDrone.Core/Books/Services/BookMonitoredService.cs)

Validation:

- Test: book with ebook edition (monitored) + audiobook edition (monitored) —
  housekeeping does NOT un-monitor either.
- Test: book with two ebook editions (both monitored) — housekeeping un-monitors
  one, keeps one.
- Test: flag off — legacy single-monitored behavior preserved exactly.

**DF-3: Decision engine format-aware quality evaluation** (behavioral change when flag on)

Deliverables:

- `QualityAllowedByProfileSpecification` resolves the format-specific quality
  profile from `AuthorFormatProfileService` when flag enabled.
- `UpgradableSpecification` evaluates cutoff against format-specific profile.
- `RemoteBook` gains optional `ResolvedFormatType` property for downstream use.

Files to modify:

- [QualityAllowedByProfileSpecification.cs](src/NzbDrone.Core/DecisionEngine/Specifications/QualityAllowedByProfileSpecification.cs)
- [UpgradableSpecification.cs](src/NzbDrone.Core/DecisionEngine/Specifications/UpgradableSpecification.cs)
- [RemoteBook.cs](src/NzbDrone.Core/Parser/Model/RemoteBook.cs)

Validation:

- Test: EPUB release for author with both profiles → evaluated against eBook
  profile, accepted.
- Test: M4B release for same author → evaluated against Spoken profile, accepted.
- Test: EPUB release but only audiobook profile configured → rejected (not in
  profile).
- Test: flag off → falls back to `Author.QualityProfile.Value` exactly as before.

**DF-4: Download client routing by format** (behavioral change when flag on)

Deliverables:

- `DownloadService.DownloadReport` resolves tags from the format profile matching
  the release's detected format type. Falls back to author tags if no format
  profile match.

Files to modify:

- [DownloadService.cs](src/NzbDrone.Core/Download/DownloadService.cs)

Validation:

- Test: audiobook release → uses audiobook format profile tags → routes to
  qBittorrent-Audiobooks.
- Test: ebook release → uses ebook format profile tags → routes to
  qBittorrent-Ebooks.
- Test: no matching format profile → falls back to author tags.
- Test: flag off → uses `Author.Tags` exactly as before.

**DF-5: Import pipeline format awareness** (behavioral change when flag on)

Deliverables:

- `ImportDecisionMaker` carries format context through import decisions.
- `IdentificationService` prefers edition with matching format type when multiple
  editions exist for a book.
- `ImportApprovedBooks` assigns files to format-specific editions.
- `AuthorPathInRootFolderSpecification` checks the format-specific root folder
  when resolving path validity.

Files to modify:

- [ImportDecisionMaker.cs](src/NzbDrone.Core/MediaFiles/BookImport/ImportDecisionMaker.cs)
- [IdentificationService.cs](src/NzbDrone.Core/MediaFiles/BookImport/Identification/IdentificationService.cs)
- [ImportApprovedBooks.cs](src/NzbDrone.Core/MediaFiles/BookImport/ImportApprovedBooks.cs)
- [AuthorPathInRootFolderSpecification.cs](src/NzbDrone.Core/MediaFiles/BookImport/Specifications/AuthorPathInRootFolderSpecification.cs)

Validation:

- Test: import m4b file → assigned to audiobook edition, lands under audiobook
  root folder.
- Test: import epub file → assigned to ebook edition, lands under ebook root
  folder.
- Test: import m4b for book with no audiobook edition → creates/selects audiobook
  edition.
- Test: flag off → existing import behavior unchanged.

**DF-6: File path building by format** (behavioral change when flag on)

Deliverables:

- `AuthorPathBuilder.BuildPath` accepts optional `FormatType` and uses matching
  format profile's `RootFolderPath`.
- `FileNameBuilder.BuildBookFilePath` passes format context to path builder.
- `BookFileMovingService.MoveBookFile` resolves format from file quality.

Files to modify:

- [AuthorPathBuilder.cs](src/NzbDrone.Core/Books/Utilities/AuthorPathBuilder.cs)
- [FileNameBuilder.cs](src/NzbDrone.Core/Organizer/FileNameBuilder.cs)
- [BookFileMovingService.cs](src/NzbDrone.Core/MediaFiles/BookFileMovingService.cs)

Validation:

- Test: audiobook file → path under `/media/audiobooks/Author Name/Book Title/`.
- Test: ebook file → path under `/media/ebooks/Author Name/Book Title/`.
- Test: flag off → uses `Author.RootFolderPath` as before.

**DF-7: Missing and cutoff evaluation by format** (behavioral change when flag on)

Deliverables:

- `BookCutoffService.BooksWhereCutoffUnmet` evaluates per format profile.
- `BookService.BooksWithoutFiles` can filter by format type.
- `MissingBookSearchCommand` and `CutoffUnmetBookSearchCommand` carry optional
  format filter.
- Wanted/Missing and Wanted/Cutoff Unmet API endpoints support format filter
  query parameter.

Files to modify:

- [BookCutoffService.cs](src/NzbDrone.Core/Books/Services/BookCutoffService.cs)
- [BookService.cs](src/NzbDrone.Core/Books/Services/BookService.cs)
- [BookSearchService.cs](src/NzbDrone.Core/IndexerSearch/BookSearchService.cs)
- [MissingController.cs](src/Bibliophilarr.Api.V1/Wanted/MissingController.cs)
- [CutoffController.cs](src/Bibliophilarr.Api.V1/Wanted/CutoffController.cs)

Validation:

- Test: book has audiobook file but no ebook file → appears in missing (ebook)
  but not missing (audiobook).
- Test: book has low-quality audiobook → appears in cutoff (audiobook) but not if
  ebook cutoff is met.
- Test: flag off → existing missing/cutoff behavior unchanged.

**DF-8: API resources and controllers** (API surface change)

Deliverables:

- New `AuthorFormatProfileResource` and `BookFormatStatusResource` classes.
- `AuthorResource` mapper includes `FormatProfiles`.
- `BookResource` mapper includes `FormatStatuses`.
- `AuthorController` CRUD endpoints for format profiles.
- New `AuthorFormatProfileController` for dedicated format profile management.

Files to create:

- `src/Bibliophilarr.Api.V1/Author/AuthorFormatProfileResource.cs`
- `src/Bibliophilarr.Api.V1/Books/BookFormatStatusResource.cs`
- `src/Bibliophilarr.Api.V1/Author/AuthorFormatProfileController.cs`

Files to modify:

- [AuthorResource.cs](src/Bibliophilarr.Api.V1/Author/AuthorResource.cs)
- [BookResource.cs](src/Bibliophilarr.Api.V1/Books/BookResource.cs)
- [AuthorController.cs](src/Bibliophilarr.Api.V1/Author/AuthorController.cs)

Validation:

- API contract tests for `GET /api/v1/author/{id}` returning `FormatProfiles`.
- API contract tests for `GET /api/v1/book/{id}` returning `FormatStatuses`.
- API contract tests for `POST/PUT/DELETE /api/v1/authorformatprofile`.
- Backward compat: legacy clients ignoring new fields continue to work.

**DF-9: Frontend format profile UI** (UI change)

Deliverables:

- Author edit modal: format profile editor (add/remove format, assign quality
  profile, root folder, tags per format).
- Add Author modal: format profile setup with defaults from selected root folder.
- Author detail header: format profile badges showing active formats.
- Book table: per-format status icons (audiobook/ebook with monitored/missing/
  available indicators).
- Per-format monitoring toggle on book rows.
- Wanted/Missing and Wanted/Cutoff pages: format filter dropdown.
- Redux store updates for format profile CRUD and per-format book status.

Files to create:

- `frontend/src/Author/Edit/AuthorFormatProfileEditor.js`
- `frontend/src/Store/Actions/authorFormatProfileActions.js`

Files to modify:

- [AuthorDetails.js](frontend/src/Author/Details/AuthorDetails.js)
- [AuthorDetailsHeader.js](frontend/src/Author/Details/AuthorDetailsHeader.js)
- [AuthorDetailsSeason.js](frontend/src/Author/Details/AuthorDetailsSeason.js)
- [EditAuthorModalContent.js](frontend/src/Author/Edit/EditAuthorModalContent.js)
- [bookActions.js](frontend/src/Store/Actions/bookActions.js)
- [authorActions.js](frontend/src/Store/Actions/authorActions.js)
- Add Author modal components

Validation:

- Manual UI testing: add author with dual format, verify both format profiles
  visible.
- Manual UI testing: toggle audiobook monitoring independently of ebook.
- Manual UI testing: Wanted pages filter by format.
- Frontend build succeeds.

**DF-10: Rollout controls and compatibility gates** (operational)

Deliverables:

- Feature flag wiring: config service, API exposure, frontend config consumer.
- Migration runbook documenting enable/disable/rollback procedure.
- Acceptance test suite covering all dual-variant scenarios.
- Operator diagnostic endpoint for format profile health.

Files to modify:

- [ConfigService.cs](src/NzbDrone.Core/Configuration/ConfigService.cs)
- [HostConfigResource.cs](src/Bibliophilarr.Api.V1/Config/HostConfigResource.cs) (or equivalent config resource)

Validation:

- Full test suite passes with flag off.
- Full test suite passes with flag on (with dual-format test data).
- Manual toggle test: enable flag mid-run, verify format profiles activate.
- Manual toggle test: disable flag mid-run, verify legacy behavior restored.

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
- [Historical milestones](#historical-milestones)

---

## Current state

### Existing architecture

Bibliophilarr currently uses a multi-provider metadata system:

1. **Hardcover** (Primary runtime provider)

- Primary source for author, book, and series metadata.
- GraphQL-backed provider with fallback-oriented search behavior.

2. **Open Library** (Secondary provider and primary FOSS lookup path)

- OpenLibrary API-backed search and identifier lookup.
- Active in search, lookup, refresh, and identifier backfill flows.

3. **Google Books** (Supplementary fallback provider)

- Used for selected enrichment and fallback scenarios when enabled.

4. **Inventaire** (Supplementary provider)

- Optional secondary metadata source when enabled.

Legacy `BookInfoProxy` references later in this document are historical architecture context, not the current active runtime baseline.

### Problems with current system

- **Legacy Goodreads API paths**: Removed from active runtime provider implementations
- **Proprietary Dependency**: Not community maintainable
- **Single Point of Failure**: No fallback options
- **Legal Concerns**: Terms of service restrictions
- **Data Quality**: Inconsistent metadata, missing books
- **Rate Limiting**: Restrictive API quotas

### Foreign ID system

Current migration direction uses provider-agnostic/OpenLibrary-oriented foreign IDs as the active identity path:

- Database schema uses these IDs
- User libraries are progressively normalized toward OpenLibrary identifiers
- Import/export flows are being migrated to OpenLibrary-oriented external identifier handling

---

## Goals

### Primary goals

1. **Complete FOSS Migration**: Replace all Goodreads dependencies with FOSS providers
2. **Multi-Provider Support**: Implement fallback and aggregation strategies
3. **Data Preservation**: Maintain existing user libraries without data loss
4. **Backward Compatibility**: Support legacy Goodreads IDs during transition
5. **Improved Reliability**: Multiple sources prevent single point of failure

### Secondary goals

1. **Better Metadata Quality**: Aggregate data from multiple sources
2. **Community Contribution**: Enable users to improve metadata
3. **Extensibility**: Easy to add new providers
4. **Performance**: Efficient caching and request handling
5. **Transparency**: Show metadata sources to users

---

## FOSS metadata provider options

> **Note:** This section was originally written before Hardcover was integrated as the
> primary provider. The hierarchy below reflects the current operational state.
> See [README.md](README.md) for the current provider summary.

### Primary Provider: Hardcover

**URL**: <https://hardcover.app/>

Hardcover is the primary metadata source for Bibliophilarr. It provides a GraphQL API
with comprehensive book, author, and series data. The Hardcover provider was implemented
with structured GraphQL error handling, rate-limit awareness, and fallback search
capability. See `HardcoverProxy.cs` and `HardcoverFallbackSearchProvider.cs` in the
codebase for implementation details.

### Secondary Provider: Open Library

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

### Additional data sources

#### MusicBrainz BookBrainz (future consideration)

- Still in development
- Community-driven book database
- Would be ideal when mature

#### ISBN database services

- ISBN.org (official ISBN registry)
- ISBNdb.com (freemium, requires key)
- Use for ISBN → metadata resolution

---

## Architecture design

### Multi-provider architecture

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
│   Hardcover    │  │ Open Library│  │  Google Books    │
│    Provider    │  │   Provider  │  │    Provider      │
│   (Primary)    │  │ (Secondary) │  │   (Fallback)     │
└────────────────┘  └─────────────┘  └──────────────────┘
```

### Provider interface design

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

### ID mapping strategy

To handle the transition from Goodreads IDs to multiple provider IDs:

> **Note:** `GoodreadsId` properties are retained as read-only legacy compatibility fields
> for existing library databases. They are not populated by active providers.

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

### Caching strategy

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

## Implementation phases

> **Note:** Phase headings below reflect the original 2024 plan structure.
> Current delivery is tracked via [ROADMAP.md](ROADMAP.md) phase model.

### Phase 1: Foundation & documentation ✓

**Status**: Completed foundational phase (historical)

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

### Phase 2: Infrastructure setup ✓

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
- Provider abstraction wiring that enabled the later Hardcover/Open Library/Google Books/Inventaire runtime model

**Deferred to later phases:**

- Metadata quality scorer
- Expanded provider health/telemetry and scoring instrumentation

### Phase 3: Open Library provider implementation ✓

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

### Phase 4: Inventaire provider implementation

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

### Phase 5: Provider aggregation layer

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

### Phase 6: Database migration

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

### Phase 7: Migration tools

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

### Phase 8: UI/UX updates

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

### Phase 9: Testing & quality assurance

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

### Phase 10: Documentation & release

**Tasks:**

1. User migration guide
2. API documentation
3. Provider comparison docs
4. Troubleshooting guide
5. Update wiki
6. Release notes
7. Migration FAQ

---

## Technical specifications

### Open Library implementation details

#### Search endpoint

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

#### Work endpoint

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

### Rate limiting implementation

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

### Metadata quality scoring

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

## Testing strategy

### Unit testing

- Test each provider independently with mocked HTTP responses
- Test data mapping/transformation logic
- Test rate limiting logic
- Test error handling

### Integration testing

- Test against real provider APIs (with caching to avoid rate limits)
- Test provider fallback scenarios
- Test metadata aggregation
- Test database migrations

### Performance testing

- Benchmark search performance
- Test with libraries of varying sizes (100, 1000, 10000+ books)
- Measure cache effectiveness
- Test concurrent request handling

### User acceptance testing

- Beta release to community
- Migration of real user libraries
- Feedback collection
- Bug reporting and fixes

---

## Migration tools

### Goodreads ID mapper

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

### Migration report

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

### Risk 1: Open Library rate limiting

**Impact**: High  
**Probability**: Medium

**Mitigation:**

- Implement aggressive caching (7-day default)
- Use batch API calls where possible
- Implement exponential backoff
- Register for API key (5x higher limits)
- Consider hosting a local Open Library mirror for large instances

### Risk 2: Incomplete metadata coverage

**Impact**: Medium  
**Probability**: Medium

**Mitigation:**

- Multiple provider fallback
- Allow manual metadata entry
- Preserve Goodreads data as read-only reference
- Community contribution tools
- Gradual migration with user validation

### Risk 3: ISBN mapping failures

**Impact**: High  
**Probability**: Medium

**Mitigation:**

- Extract ISBNs from ebook file metadata
- Use title/author fuzzy matching
- Manual user mapping tools
- Community-contributed mapping database
- Keep Goodreads IDs as legacy reference

### Risk 4: Performance degradation

**Impact**: Medium  
**Probability**: Low

**Mitigation:**

- Comprehensive performance testing
- Efficient caching strategy
- Background metadata updates
- Async/await throughout
- Connection pooling and keep-alive

### Risk 5: Provider API changes

**Impact**: Medium  
**Probability**: Low

**Mitigation:**

- Version provider implementations
- Comprehensive integration tests
- Monitor provider announcements
- Quick rollback capability
- Multiple provider redundancy

### Risk 6: Data quality issues

**Impact**: Medium  
**Probability**: High

**Mitigation:**

- Metadata quality scoring
- Multiple source verification
- User reporting tools
- Fallback to alternative providers
- Community metadata improvement

---

## Historical milestones

> **Note:** This section reflects the original 2024 migration proposal timeline.
> The project now follows the phase-based delivery model documented in [ROADMAP.md](ROADMAP.md).
> Milestones 1–4 have been substantially completed; the remaining work is tracked
> as Phase 6–7 items in the roadmap.

| Milestone | Original scope | Status |
|---|---|---|
| 1. Foundation | Repository analysis, migration plan, documentation, community engagement | Complete |
| 2. Infrastructure | Provider interfaces, testing framework, quality scoring, monitoring | Complete |
| 3. Provider implementation | Open Library + Hardcover providers, testing, performance | Complete (Hardcover added as primary) |
| 4. Multi-provider support | Aggregation layer, fallback logic, provider management | Complete |
| 5. Migration tools | Database migration, ID mapping, bulk updater | Partially complete — see ROADMAP Track A |
| 6. Beta / Stable release | Community testing, performance tuning, documentation | In progress — see ROADMAP Phase 6–7 |

---

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md) for contribution guidelines and priority areas.

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

> **Note:** Goodreads has been removed from the runtime. The column is retained for historical comparison only.

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

## Document version history

Major revisions are tracked via git history. Key milestones:

- **v1.0** (2024-02-16): Initial comprehensive migration plan
- **v2.0** (2026-03): Phase 5/6 consolidation, Hardcover primary provider, telemetry integration
- **v2.1** (2026-04): Documentation normalization — updated provider hierarchy to reflect Hardcover as primary, rewrote stale Timeline section, backfilled empty validation sections, aligned with ROADMAP.md phase model

---

**Last Updated**: April 5, 2026  
**Status**: Active migration program (Phase 5 consolidation, Phase 6 hardening)  
**Next Review**: Next canonical roadmap/status update cycle
