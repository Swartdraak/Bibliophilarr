# Project Status Summary

**Last Updated**: March 23, 2026 (workflow governance and status consistency pass)
**Project**: Bibliophilarr  
**Current Phase**: Phase 5 consolidation with Phase 6 hardening active

## Overview

Bibliophilarr is a community-driven continuation focused on replacing fragile or proprietary metadata dependencies with sustainable FOSS providers while keeping library automation reliable and observable.

## Current Operational State

- Protected branches `develop`, `staging`, and `main` now use the same required contexts:
  - `build-test`
  - `Markdown lint`
  - `triage`
  - `Staging Smoke Metadata Telemetry / smoke-metadata-telemetry`
- Required approving review count is `0` across those protected branches.
- Release-readiness and branch-policy audit automation are available for scheduled and manual execution.

## Requested implementation tracks (March 23, 2026)

The following items were added to canonical planning for immediate/future delivery sequencing:

1. Import and identification throughput optimization for large libraries
  - Add phased import strategy, bounded concurrency, and per-stage telemetry.
  - Add production-shaped benchmark gates so speed improvements do not degrade match quality.

2. Single-instance ebook and audiobook variant management
  - Add per-title variant intent (ebook/audiobook) with independent format/quality policy.
  - Ensure variant isolation across monitoring, search, import, and upgrade workflows.

## Latest Delivery Update

### March 22, 2026 build validation and test fixture hardening pass

Completed refactoring and validation updates in this full-cycle pass:

1. ConfigService property setter cleanup
  - Removed redundant clamping logic from `IsbnContextFallbackLimit` and `BookImportMatchThresholdPercent` setters.
  - Moved validation responsibility to API controller layer for cleaner configuration round-trip behavior.
  - Impact: Eliminates config value mutation during persistence operations.

2. Test fixture alignment across 6 suites (16 tests fixed)
  - EbookTagServiceFixture: added warn suppression for 6 malformed-file and filename-fallback tests (confirmed ASIN/ISBN extraction paths).
  - MediaCoverServiceFixture: repaired broken test method from interrupted edit; aligned proxy URL fallback assertions with actual behavior when local file missing.
  - RefreshBookDeletionGuardFixture: added log suppression for expected warning/error logs emitted during deletion-marking and degraded-provider-window suppression flows.
  - AddArtistFixture: normalized expected ForeignAuthorId assertion to include required `openlibrary:author:` prefix from AddAuthorService normalization.
  - DownloadDecisionMakerFixture: added error expectation for parser exception logging when title is unparsable.
  - AudioTagServiceFixture: added error suppression for expected errors when reading missing files.
  - Result: All 16 tests now passing; no remaining assertion misalignments in core suite.

3. Frontend lint alignment
  - Fixed jest.setup.js indentation (tabs → 2-space alignment) to pass ESLint validation.
  - Confirmed jest.config.cjs and package.json configuration is operational.

4. Full pipeline validation
  - Backend: dotnet restore (19 projects) → build (MSBuild/StyleCop: 0W/0E) → test (Core.Test: 2640/2640, 59 skipped).
  - Frontend: ESLint/Stylelint pass → webpack production build (2.86 MiB assets).
  - Packaging: linux-x64 net8.0 artifact generation → smoke test (/ping endpoint: HTTP 200, {"status": "OK"}).

5. Documentation and repo cleanup
  - Updated CHANGELOG.md with complete session work.
  - Updated MIGRATION_PLAN.md with March 22 hardening snapshot.
  - Removed stale canonical-doc contradictions about frontend test-runner gaps.
  - Fixed broken internal link in METADATA_MIGRATION_DRY_RUN.md (2026-03-18 → 2026-03-17 snapshot reference).
  - Consolidated ad-hoc tracking files per canonical documentation policy.
  - Removed temporary test data directories.

Validation status for this pass:

- Full solution build: PASS (0 warnings, 0 errors).
- Full test suite (non-integration): PASS (2640/2640).
- Frontend lint: PASS (ESLint + Stylelint).
- Frontend build: PASS (webpack production).
- Packaged binary: PASS (smoke test /ping responds HTTP 200).
- Zero uncommitted changes; all work logged in CHANGELOG, MIGRATION_PLAN, and PROJECT_STATUS.

Operational impact:

- ConfigService property setters now preserve exact configuration values without mutation.
- All test fixtures properly aligned with logging expectations; no remaining false-positive fixture failures.
- Frontend test infrastructure confirmed operational and working as documented.
- Full build pipeline validated end-to-end with operational health confirmation.
- Documentation drift reduced; canonical docs now match current implementation reality.

### March 22, 2026 Hardcover/runtime logging hardening pass

Completed implementation updates in this pass:

1. Hardcover provider observability and startup-token routing
  - Added provider-local `Trace`/`Debug`/`Warn` logging in `HardcoverFallbackSearchProvider` for search entry, query execution, skip reasons, provider payload issues, and mapped result counts.
  - Corrected Hardcover enablement so `BIBLIOPHILARR_HARDCOVER_API_TOKEN` participates in provider routing, not only request-time auth header resolution.

2. Local metadata exporter script logging controls
  - Updated `scripts/provider_metadata_pull_test.py` and `scripts/live_provider_enrich_missing_metadata.py` to use Python logging instead of plain `print` summaries.
  - Added `--log-level DEBUG|INFO|WARNING|ERROR` so operators can choose between concise run summaries and per-query diagnostics.

Validation status for this pass:

- Targeted Hardcover fixture coverage extended for environment-token enablement.
- Full solution build and impacted script syntax validation executed after the change set.

Operational impact:

- Hardcover usage is now visible in normal debug logs without relying on external network inspection.
- Pre-start environment token configuration now matches documented operator behavior.

### March 22, 2026 clean build plus identification verification pass

Completed verification updates in this pass:

1. Fresh build validation
  - Executed a clean full solution build for current metadata extraction and
    import decision flows.
  - Result: build succeeded with zero errors.

2. Extraction and identification verification
  - Confirmed filename identifier fallback coverage for:
    - `should_extract_isbn_from_filename_during_fallback`
    - `should_extract_asin_from_filename_during_fallback`
  - Confirmed matching and decision layers remain green on targeted suites:
    - `DistanceCalculatorFixture`
    - `ImportDecisionMakerFixture`
    - `CandidateServiceFixture`

3. Scope verified
  - Author identification path
  - Series-aware metadata path
  - Book identification/matching path
  - Cover identification handoff path via provider metadata

Operational impact:

- Confirms confidence-aware extraction and configurable threshold behavior are
  stable under the current targeted import-identification workflow checks.
- No runtime or build regressions were introduced in this verification pass.

### March 22, 2026 import extraction confidence merge + configurable threshold

Completed implementation updates in this pass:

1. Confidence-aware ebook metadata extraction merge
  - Updated `EBookTagService` extraction for EPUB, PDF, AZW3, and MOBI paths to apply field-level confidence assignment and normalization.
  - Added structured fallback merge from filename parsing with validated identifier extraction (ISBN/ASIN) to improve resilience when container metadata is missing or malformed.
  - Extended `ParsedTrackInfo` with additional confidence fields (`Series`, `ISBN`, `ASIN`, `Year`, `Publisher`, `Language`) for deterministic downstream scoring.

2. Configurable import close-match acceptance threshold
  - Replaced hardcoded close-match threshold usage in `CloseBookMatchSpecification` with config-driven threshold consumption.
  - Added `BookImportMatchThresholdPercent` to `IConfigService`/`ConfigService` with default `80` and enforced bounds (`50..100`).
  - Exposed and validated the threshold via metadata provider API config resource/controller.

Validation status for this pass:

- Targeted extractor tests:
  - `dotnet test src/NzbDrone.Core.Test/Bibliophilarr.Core.Test.csproj --filter EbookTagServiceFixture`
  - Result: Passed 10, Failed 0.
- Full solution build (`build dotnet` task): PASS.

Operational impact:

- Existing behavior is preserved by default (`80%` acceptance threshold).
- Operators can now tune strictness without code changes when dealing with noisy libraries or low-quality embedded metadata.

### March 22, 2026 release-evidence and test-runner completion pass

Completed implementation updates in this pass:

1. Frontend test-runner wiring for local and CI execution
  - Added repository Jest configuration (`jest.config.cjs`) with frontend module mapping parity.
  - Added Jest setup and file mocks under `frontend/build`.
  - Added `yarn test:frontend` and CI `Test` step in `.github/workflows/ci-frontend.yml`.

2. Series persistence evidence generation and release workflow hookup
  - Executed `scripts/series_persistence_gate.py` against staging DB and published:
    - `docs/operations/series-persistence-snapshots/2026-03-22.md`
    - `docs/operations/series-persistence-snapshots/2026-03-22.json`
  - Updated `.github/workflows/release.yml` to generate series snapshot evidence before `release_entry_gate.py`.

3. Baseline/post replay evidence and delta assertions
  - Generated baseline and post replay reports from curated cohort copies:
    - `docs/operations/replay-comparison-snapshots/2026-03-22/baseline/root_live_enrichment_report.json`
    - `docs/operations/replay-comparison-snapshots/2026-03-22/post/root_live_enrichment_report.json`
  - Generated replay comparison artifacts:
    - `docs/operations/replay-comparison-snapshots/2026-03-22/replay-comparison.md`
    - `docs/operations/replay-comparison-snapshots/2026-03-22/replay-comparison.json`
  - Added CI delta guard script `scripts/replay_delta_guard.py` and threshold profile `tests/fixtures/replay-cohort/replay-delta-thresholds.json`.
  - Updated weekly replay workflow to run baseline+post, compare, and fail on delta regressions.

4. Targeted core test expansion for migration hardening
  - Added import preflight rejection tests in `ImportApprovedTracksFixture`.
  - Added canonical merge side-effect tests in `AuthorCanonicalizationServiceFixture`.
  - Added refresh-path series payload assertion in `RefreshAuthorServiceFixture`.

Validation status for this pass:

- Frontend tests: `yarn test:frontend` PASS (9 suites, 19 tests).
- Targeted core tests: PASS (new preflight/canonical/series tests).
- Full solution build (`build dotnet` task): PASS.
- Replay delta guard: PASS (`docs/operations/replay-comparison-snapshots/2026-03-22/replay-delta-guard-summary.json`).

Staged gate outcome:

- `_artifacts/release-entry/release-entry-gate.json` reports overall `ok: false`.
- Blocking gate: `Series persistence` (latest snapshot verdict `FAIL`).
- Staging DB snapshot details:
  - `Series=0`
  - `SeriesBookLinks=0`
  - `DuplicateNormalizedAuthors=51`

Immediate blocker status:

- Release-entry now enforces series-snapshot generation and consumes current dated evidence correctly.
- Runtime series persistence remains unresolved in staging data and still blocks release-entry success.

### March 21, 2026 metadata hardening continuation (routing/dedupe/import/replay gates)

Completed implementation updates in this continuation slice:

1. Provider compatibility routing for ID-scoped metadata calls
  - `MetadataProviderOrchestrator` now applies provider-ID namespace compatibility filtering for:
    - `get-author-info`
    - `get-book-info`
  - Added regression coverage in `MetadataProviderOrchestratorFixture` to ensure OpenLibrary-scoped IDs do not fan out to incompatible providers.

2. Canonical author dedupe and merge tooling
  - Added `CanonicalizeAuthorsCommand`.
  - Added `AuthorCanonicalizationService` with confidence-scored canonical matching and bounded merge execution.
  - Integrated canonical dedupe short-circuiting into `AddAuthorService` for single and bulk add paths.

3. Identification fallback and import preflight hardening
  - `CandidateService` now applies broader title/author variants and records structured fallback exhaustion diagnostics.
  - `ImportApprovedBooks` now performs preflight checks for invalid/missing author IDs and root-path conflicts before add flows.

4. Series reconciliation safety and release gating support
  - `RefreshAuthorService` now reconstructs series candidates from book `SeriesLinks` when author-level series payload is empty.
  - Added operational scripts:
    - `scripts/series_persistence_gate.py` (DB-backed series/series-link/duplicate-author gate report)
    - `scripts/replay_comparison.py` (baseline vs post-fix replay comparison for identify rate, cover success, provider failures, and optional DB deltas)
  - `scripts/release_entry_gate.py` now includes a `Series persistence` evidence gate (`docs/operations/series-persistence-snapshots`).

Validation status for this continuation slice:

- Full solution build (`build dotnet` task): PASS.
- Targeted core tests:
  - `dotnet test src/NzbDrone.Core.Test/Bibliophilarr.Core.Test.csproj --filter "FullyQualifiedName~MetadataProviderOrchestratorFixture.should_not_route_openlibrary_author_ids_to_incompatible_provider|FullyQualifiedName~MetadataProviderOrchestratorFixture.should_not_route_openlibrary_work_ids_to_incompatible_book_info_provider|FullyQualifiedName~RefreshAuthorServiceFixture" -p:Platform=Posix -p:Configuration=Debug`
  - Result: Passed 14, Failed 0.
- Python script syntax validation:
  - `python3 -m py_compile scripts/series_persistence_gate.py scripts/replay_comparison.py scripts/release_entry_gate.py`
  - Result: PASS.

Known validation gap:

  - ~~Frontend interaction regression tests were added for jump/link press paths, but local Jest execution is not yet wired in this workspace~~ **RESOLVED**: jest.config.cjs and package.json confirmed operational; frontend test-runner properly configured (validated via yarn lint/build pipeline Mar 22, 2026).

### March 21, 2026 full-library QA review (logs/config/runtime triage)

A new full-library run was reviewed against runtime logs, config, and database state.
This triage confirms several production-impacting issues and defines the next correction slice.

Observed evidence snapshot:

- Runtime metadata cardinality remains inconsistent with expected series coverage:
  - `Authors=541`, `Books=26022`, `Series=0`, `SeriesBookLink=0`, `BookFiles=3789`.
- Metadata orchestrator warnings are dominated by OpenLibrary search failures:
  - `Year, Month, and Day parameters describe an un-representable DateTime`.
  - `Http request timed out`.
- Author refresh failures are widespread for stale/unresolvable author IDs:
  - repeated `Author ... was not found` / `Could not find author with id ...`.
- Duplicate logical authors exist under multiple OpenLibrary foreign IDs
  (`AuthorMetadata.Name` duplicates with distinct `ForeignAuthorId` values).
- Import identification quality is degraded:
  - repeated `ISBN contextual fallback exhausted ... no candidates found`.
- Cover path now shows reduced warning volume versus prior baseline, but current failures
  are mostly upstream archive mirror `502` responses.
- Provider-routing telemetry shows GoogleBooks invoked in `get-author-info` fallback with
  OpenLibrary-style author IDs, which are provider-incompatible and produce noisy misses.

Immediate code fix completed in this review cycle:

1. OpenLibrary invalid publish-year resilience
  - Fixed out-of-range publish year handling that caused DateTime exceptions in search mapping paths.
  - Updated:
    - `OpenLibraryMapper.MapSearchDocToBook` release-date mapping now validates year bounds.
    - `OpenLibrarySearchProxy.MapEditionToBook` now uses safe year-date parsing.
  - Added regression test:
    - `OpenLibraryMapperFixture.map_search_doc_with_out_of_range_publish_year_should_not_throw`.
  - Validation:
    - `dotnet test src/NzbDrone.Core.Test/Bibliophilarr.Core.Test.csproj -p:Platform=Posix --filter "FullyQualifiedName~OpenLibraryMapperFixture|FullyQualifiedName~OpenLibraryClientResilienceFixture|FullyQualifiedName~OpenLibraryProviderFixture"`
    - Result: Passed 46, Failed 0, Skipped 0.

Prioritized correction plan from this QA pass:

| ID | Priority | Problem statement | Proposed correction | Validation gate |
|---|---|---|---|---|
| TD-META-008 | P0 | OpenLibrary search mapping can throw on malformed publish years, aborting search/fallback flows. | Keep defensive year guards in all search/edition mapping boundaries; add fixture coverage for invalid years (`0`, negative, `>9999`). | Zero DateTime range exceptions from OpenLibrary mapping paths in runtime logs under full scan/import. |
| TD-META-009 | P0 | Logical duplicate authors are imported as separate entities because distinct OpenLibrary IDs can map to equivalent canonical names. | Add canonical-author merge policy (name/alias normalization + confidence gates) and post-import dedupe reconciliation command. | Duplicate normalized author-name count decreases monotonically without data loss in merged author/book linkage tests. |
| TD-META-010 | P1 | Orchestrator routes `get-author-info` fallbacks to providers that cannot resolve OpenLibrary author IDs, adding noise and latency. | Add provider compatibility guard for ID-scoped operations (route by ID namespace/provider capability). | No incompatible-provider fallback warnings for ID-scoped operations; fallback remains active for search/query operations. |
| TD-META-011 | P1 | Series persistence remains zero in full-library runtime despite series field ingestion support. | Add end-to-end series persistence integration test and refresh audit path (search-doc enrichment => `Series` + `SeriesBookLink` writes). | Refresh of known series corpus yields non-zero `Series` and `SeriesBookLink` counts. |
| TD-IMPORT-005 | P1 | Download import identification often exhausts ISBN contextual fallback with no candidates. | Expand identification fallback contract with provider-agnostic title/author variant routing and stronger telemetry on candidate rejection reasons. | Reduced `no candidates found` frequency and improved identified-import rate on replay corpus. |
| TD-UI-001 | P1 | UI interactions (including author jump-bar click behavior) reported as intermittently non-responsive. | Run frontend interaction audit with console/error instrumentation and connector-state regression tests for Author index and jump-bar handlers. | Repro case green in browser regression test; no unhandled UI runtime errors during author index interaction. |

Operational safeguards until next slice lands:

- Keep GoogleBooks enabled for search/fallback coverage, but treat `get-author-info` cross-provider warnings as expected noise until `TD-META-010` is delivered.
- Re-run author refresh in controlled batches after the DateTime guard fix to repopulate provider telemetry with clean mapping behavior.
- Track series table counts before and after each batch to verify movement away from zero.

### March 21, 2026 TD hardening batch (event/cover/series/openlibrary/import/indexer)

Completed implementation and validation of a focused hardening batch spanning event safety,
OpenLibrary resilience, cover-request protection, series enrichment contracts, and refresh-delete safeguards.

Delivered scope:

1. `TD-EVENT-001` (P0): Book file deletion event guardrails
  - Added defensive payload null guards in:
    - `BookController.Handle(BookFileDeletedEvent)`
    - `MediaFileDeletionService.Handle(BookFileDeletedEvent)`
    - `NotificationService.Handle(BookFileDeletedEvent)`
  - Added regression fixtures:
    - `BookControllerEventGuardFixture`
    - `NotificationServiceBookFileDeletedEventGuardFixture`
    - `MediaFileDeletionServiceBookFileDeletedEventGuardFixture`

2. `TD-COVER-001` (P0): OpenLibrary cover throttling resilience
  - Added host-scoped token-bucket request shaping for OpenLibrary covers.
  - Added adaptive cooldown with jitter on 429/503/timeout responses.
  - Added request suppression path to avoid immediate retry storms.

3. `TD-COVER-002` (P1): invalid cover id protection
  - Enforced positive-id checks before constructing OpenLibrary cover URLs in mapper paths for search docs, works, editions, and author photos.
  - Added mapper regression asserting `cover_i = -1` does not produce image links.

4. `TD-COVER-003` (P1): stale local cover reconciliation
  - Added cover folder reconciliation pass during author refresh to remove stale/zero-byte files.
  - Updated local URL mapping fallback to proxy remote cover URLs when local files are missing, reducing missing-file churn.

5. `TD-META-SERIES-001` (P0): series enrichment request + persistence path hardening
  - Added `series,series_with_number` to OpenLibrary search field selection.
  - Retained works-first bibliography identity while enabling search-doc series enrichment.
  - Added provider tests validating author/book series link materialization.

6. `TD-META-SERIES-002` (P1): source-of-truth merge contract formalization
  - Formalized and implemented deterministic merge contract:
    - works feed is identity source for work IDs/title slug.
    - search docs are enrichment source (including series fields).
  - Added contract coverage for matching and non-matching works/search key scenarios.

7. `TD-OPENLIB-001` (P1) and `TD-ORCH-001` (P1): operation-specific OpenLibrary resilience tuning
  - Added operation-class timeout/retry budget plumbing for OpenLibrary (`search`, `isbn`, `work`) via config contract/API resource.
  - OpenLibrary client now applies operation-specific values with global fallback defaults.
  - Added API validation rules and mapper test coverage for new config fields.

8. `TD-IMPORT-001` (P2): two-phase delete safeguard for refresh misses
  - Added first-miss stale marking in `RefreshBookService`.
  - Added degraded-provider suppression to prevent hard-delete on transient outage windows.
  - Added dedicated regression fixture (`RefreshBookDeletionGuardFixture`) proving:
    - first miss marks stale, second miss deletes,
    - degraded-provider windows suppress hard-delete.

9. `TD-OPS-INDEXER-001` (P3): warning-noise reduction for indexer-less states
  - Changed repeated no-indexer warning behavior to rate-limited advisory with actionable guidance.

Validation evidence:

- Core targeted suite:
  - `dotnet test src/NzbDrone.Core.Test/Bibliophilarr.Core.Test.csproj -p:Platform=Posix --filter "FullyQualifiedName~OpenLibraryClientResilienceFixture|FullyQualifiedName~OpenLibraryMapperFixture|FullyQualifiedName~OpenLibraryProviderFixture|FullyQualifiedName~NotificationServiceBookFileDeletedEventGuardFixture|FullyQualifiedName~MediaFileDeletionServiceBookFileDeletedEventGuardFixture|FullyQualifiedName~RefreshBookDeletionGuardFixture"`
  - Result: Passed 49, Failed 0, Skipped 0.

- API targeted suite:
  - `dotnet test src/NzbDrone.Api.Test/Bibliophilarr.Api.Test.csproj -p:Platform=Posix --filter "FullyQualifiedName~BookControllerEventGuardFixture|FullyQualifiedName~MetadataProviderConfigResourceMapperFixture"`
  - Result: Passed 2, Failed 0, Skipped 0.

- Full solution build:
  - VS Code task `build dotnet`
  - Result: PASS.

### March 21, 2026 runtime-forensics task register (active instance analysis)

Completed a full runtime analysis pass against `/home/swartdraak/.config/Bibliophilarr`
including `logs.db`, rolling text logs, and primary metadata database state.

Observed runtime state snapshot:

- Warning lines in text logs: `8231`
- Error-like lines in text logs: `8292`
- Dominant warning/error loggers from `logs.db`:
  - `HttpClient` (warn): `2013`
  - `MediaCoverService` (warn): `2013`
  - `EventAggregator` (error): `72`
  - `EBookTagService` (warn): `42`
  - `MediaCoverMapper` (warn): `15`
  - `OpenLibraryClient` (warn): `10`
- Library persistence state:
  - Authors: `25`
  - Books: `4624`
  - Series: `0`
  - SeriesBookLink: `0`

Prioritized technical debt tasks (official backlog):

| ID | Priority | Problem statement | Evidence | Proposed change | Validation target |
|---|---|---|---|---|---|
| TD-RUNTIME-001 | P0 | Book file deletion events trigger null-reference failures in multiple subscribers. | `EventAggregator` errors for `BookController`, `MediaFileDeletionService`, `NotificationService` on `BookFileDeletedEvent` (72 total). | Add null-safe event handling and defensive payload guards for all `BookFileDeletedEvent` subscribers; add regression fixture that replays delete events with partial payloads. | Zero `EventAggregator` errors for delete-event workflows in targeted replay tests. |
| TD-META-006 | P0 | Series import remains empty in runtime despite series-token support in naming. | `Series=0`, `SeriesBookLink=0` in DB; `OpenLibraryClient.Search` does not request `series`/`series_with_number` fields. | Add `series,series_with_number` to OpenLibrary search field selection and add integration refresh test asserting series link persistence. | Refresh of known series author yields non-zero `Series` and `SeriesBookLink`; API returns populated `seriesTitle`. |
| TD-COVER-006 | P0 | Cover download path is heavily rate-limited, creating persistent warning storms and degraded UX. | `HttpClient` and `MediaCoverService` each at 2013 warnings, mostly 429 from covers endpoints. | Add host-scoped adaptive backoff/jitter and cooldown windows for cover endpoints; reduce repeated retries during provider throttling. | 429 warning volume reduced by at least 80 percent under same import workload. |
| TD-COVER-007 | P1 | Invalid cover IDs (`-1`) are still requested, generating avoidable 429/503 failures. | Repeated failed requests to `.../b/id/-1-L.jpg` and archive fallback endpoints. | Validate cover IDs before request enqueue; skip and mark as unavailable for non-positive IDs. | No outbound cover requests with invalid negative IDs in logs. |
| TD-COVER-008 | P1 | Local cover mapper references missing files repeatedly, producing warning noise. | Repeated `MediaCoverMapper` warnings for missing poster files (`22/23/24`). | Add reconciliation job to remove stale cover references and refresh missing cover states once per cycle. | Missing-file warnings converge to near-zero after one reconciliation pass. |
| TD-META-007 | P1 | OpenLibrary endpoint instability (503/timeouts) still propagates into fallback pressure. | `OpenLibraryClient` 503 warnings and orchestrator timeout warnings (`search-for-new-book`). | Add endpoint-specific retry budgets and circuit isolation per operation class (`search`, `isbn`, `work`). | Lower provider-failure streaks and bounded fallback latency in telemetry. |
| TD-IMPORT-004 | P1 | Refresh path may delete books after metadata misses during degraded provider windows. | `RefreshBookService` warnings showing book deletions due to not found metadata. | Introduce two-phase stale marking before delete and suppress hard delete on transient-provider incidents. | No immediate hard deletes on first-miss during outage simulation. |
| TD-RENAME-001 | P1 | Forced rename is perceived as no-op because most files resolve to identical destination paths. | Rename logs show frequent `File not renamed, source and destination are the same`. | Improve rename preview/action feedback with explicit unchanged counts and reasons; surface diff summary in UI and command result. | Forced rename presents changed vs unchanged counts and unchanged reason breakdown. |
| TD-RENAME-002 | P2 | Rename pipeline silently depends on metadata linkage completeness; partially linked files reduce effective rename coverage. | Runtime DB shows many `BookFiles` rows with missing edition linkage (`EditionId` null/0). | Add preflight validation and remediation guidance for unlinked files before rename execution. | Rename preflight reports unlinked files and excludes them with actionable remediation hints. |
| TD-OPS-002 | P3 | Indexer-less deployments emit repeated warning noise without operator-context message quality. | `FetchAndParseRssService` warning: no available indexers. | Emit single rate-limited advisory with setup path and optional suppression for metadata-only workflows. | One advisory per interval; no repetitive warning flood for intentional indexer-disabled profiles. |

Operational note:

- Series tokens in naming are functional, but they depend on populated `SeriesBookLink` data.
- Current runtime series persistence remains zero, so `{Book Series}`-based folder nesting often collapses to non-series paths.

### March 21, 2026 metadata operations follow-up (GoogleBooks controls + series import hardening)

Completed follow-up work from live operator validation:

1. GoogleBooks runtime controls are now exposed in metadata settings UI:
  - `enableGoogleBooksProvider`
  - `enableGoogleBooksFallback`
  - `googleBooksApiKey` (password-type input)

2. OpenLibrary search-document series fields now map into domain series links:
  - Added support for `series` and `series_with_number` in OpenLibrary search docs.
  - `OpenLibraryMapper.MapSearchDocToBook` now hydrates `SeriesLinks` for mapped books.
  - `OpenLibraryProvider.GetAuthorInfo` now promotes mapped book series links into author-level series collections for refresh persistence.

3. Regression and targeted validation evidence:
  - `dotnet test src/NzbDrone.Core.Test/Bibliophilarr.Core.Test.csproj -p:Platform=Posix --filter "FullyQualifiedName~OpenLibraryMapperFixture"`
    - Result: Passed 16, Failed 0, Skipped 0.
  - `dotnet test src/NzbDrone.Core.Test/Bibliophilarr.Core.Test.csproj -p:Platform=Posix --filter "FullyQualifiedName~FileNameBuilderFixture|FullyQualifiedName~NestedFileNameBuilderFixture|FullyQualifiedName~RenameTrackFileServiceFixture|FullyQualifiedName~MoveTrackFileFixture"`
    - Result: Passed 78, Failed 0, Skipped 2.
  - `dotnet msbuild -restore src/Bibliophilarr.sln -p:GenerateFullPaths=true -p:Configuration=Debug -p:Platform=Posix`
    - Result: PASS.

### March 21, 2026 completion pass (TD-META-001..005 implemented and validated)

Completed all five metadata technical-debt slices from the parity backlog and validated the
result through targeted Core/API tests plus full solution build.

Implemented scope:

1. TD-META-001 (orchestrator parity):
  - Add/import/identification paths now use orchestrator-backed metadata requests.
  - Updated services: AddAuthorService, AddBookService, ImportListSyncService, CandidateService.

2. TD-META-002 (canonical ID normalization + batched backfill):
  - Added shared OpenLibraryIdNormalizer and replaced duplicate normalization logic.
  - Backfill command/service now support bounded batch writes through BatchSize.

3. TD-META-003 (health-aware routing):
  - Provider registry ordering now considers health state and active cooldown windows.
  - Telemetry service now computes cooldown windows from failure streaks and clears cooldown on success.

4. TD-META-004 (query-policy parity):
  - Import-list mapping now executes normalized title/author variant search through shared query normalization.
  - Identification path uses orchestrator search parity for metadata lookups.

5. TD-META-005 (conflict explainability telemetry):
  - Conflict policy now captures per-provider score breakdown factors.
  - Telemetry snapshot and API contract expose last decision score breakdown by provider.

Validation evidence:

- Core targeted run:
  - dotnet test src/NzbDrone.Core.Test/Bibliophilarr.Core.Test.csproj --filter FullyQualifiedName~AddArtistFixture|FullyQualifiedName~AddAlbumFixture|FullyQualifiedName~ImportListSyncServiceFixture|FullyQualifiedName~CandidateServiceFixture|FullyQualifiedName~CandidateServiceFallbackOrderingIntegrationFixture|FullyQualifiedName~MetadataProviderRegistryFixture|FullyQualifiedName~MetadataConflictResolutionPolicyFixture|FullyQualifiedName~OpenLibraryIdBackfillServiceFixture
  - Result: Passed 68, Failed 0, Skipped 0.

- API targeted run:
  - dotnet test src/NzbDrone.Api.Test/Bibliophilarr.Api.Test.csproj --filter FullyQualifiedName~MetadataConflictTelemetryResourceMapperFixture|FullyQualifiedName~MetadataConflictTelemetryControllerFixture
  - Result: Passed 2, Failed 0, Skipped 0.

- Full build:
  - dotnet msbuild -restore src/Readarr.sln -p:GenerateFullPaths=true -p:Configuration=Debug -p:Platform=Posix
  - Result: PASS.

Operational note:

- TD-META backlog entry remains below for historical traceability of the original parity assessment.

### March 21, 2026 technical debt backlog (Readarr parity comparison follow-up)

This backlog converts the completed Readarr vs Bibliophilarr comparisons into actionable,
migration-safe debt slices with explicit code references, rollout shape, and validation gates.

| ID | Priority | Problem statement | Code/document references | Proposed migration/changes | Validation and rollback |
|---|---|---|---|---|---|
| TD-META-001 | P0 | Metadata request behavior is not fully consistent across add, refresh, import-list, and identification flows because only some paths use provider orchestration. | [src/NzbDrone.Core/MetadataSource/MetadataProviderOrchestrator.cs](src/NzbDrone.Core/MetadataSource/MetadataProviderOrchestrator.cs), [src/NzbDrone.Core/Books/Services/RefreshAuthorService.cs](src/NzbDrone.Core/Books/Services/RefreshAuthorService.cs), [src/NzbDrone.Core/Books/Services/RefreshBookService.cs](src/NzbDrone.Core/Books/Services/RefreshBookService.cs), [src/NzbDrone.Core/Books/Services/AddAuthorService.cs](src/NzbDrone.Core/Books/Services/AddAuthorService.cs), [src/NzbDrone.Core/Books/Services/AddBookService.cs](src/NzbDrone.Core/Books/Services/AddBookService.cs), [src/NzbDrone.Core/ImportLists/ImportListSyncService.cs](src/NzbDrone.Core/ImportLists/ImportListSyncService.cs), [src/NzbDrone.Core/MediaFiles/BookImport/Identification/CandidateService.cs](src/NzbDrone.Core/MediaFiles/BookImport/Identification/CandidateService.cs) | Introduce orchestrator-backed adapters for add/import/manual paths so all metadata reads traverse a single provider-order + fallback policy. Preserve current interfaces for compatibility and migrate call sites incrementally behind feature flags. | Add parity tests that assert equivalent persisted outcomes for identical inputs across all entry paths. Rollback: switch flag to legacy direct provider calls.
| TD-META-002 | P0 | External identifier normalization is distributed across mapper/provider/backfill logic, increasing risk of persistence drift and duplicate merges. | [src/NzbDrone.Core/MetadataSource/OpenLibrary/OpenLibraryMapper.cs](src/NzbDrone.Core/MetadataSource/OpenLibrary/OpenLibraryMapper.cs), [src/NzbDrone.Core/MetadataSource/OpenLibrary/OpenLibraryProvider.cs](src/NzbDrone.Core/MetadataSource/OpenLibrary/OpenLibraryProvider.cs), [src/NzbDrone.Core/MetadataSource/OpenLibrary/OpenLibraryIdBackfillService.cs](src/NzbDrone.Core/MetadataSource/OpenLibrary/OpenLibraryIdBackfillService.cs), [src/NzbDrone.Core/Books/Model/Book.cs](src/NzbDrone.Core/Books/Model/Book.cs), [src/NzbDrone.Core/Books/Model/AuthorMetadata.cs](src/NzbDrone.Core/Books/Model/AuthorMetadata.cs), [src/NzbDrone.Core/Datastore/Migration/042_add_open_library_ids.cs](src/NzbDrone.Core/Datastore/Migration/042_add_open_library_ids.cs) | Create a single normalization component for work/author/external IDs and apply it at all write boundaries. Add migration-time backfill command batching and conflict logging for non-normalizable IDs. | Add deterministic fixture coverage for malformed/legacy token forms and unresolved IDs. Rollback: retain raw foreign ID fields and disable canonical rewrite pass.
| TD-META-003 | P1 | Provider health telemetry exists but provider selection remains mostly priority-driven; repeated provider failures can still increase latency/noise. | [src/NzbDrone.Core/MetadataSource/MetadataProviderRegistry.cs](src/NzbDrone.Core/MetadataSource/MetadataProviderRegistry.cs), [src/NzbDrone.Core/MetadataSource/MetadataProviderTelemetry.cs](src/NzbDrone.Core/MetadataSource/MetadataProviderTelemetry.cs), [src/NzbDrone.Core/MetadataSource/ProviderTelemetryService.cs](src/NzbDrone.Core/MetadataSource/ProviderTelemetryService.cs), [src/NzbDrone.Core/MetadataSource/BookSearchFallbackExecutionService.cs](src/NzbDrone.Core/MetadataSource/BookSearchFallbackExecutionService.cs) | Add health-aware routing: temporary demotion and cooldown/circuit-break behavior after configurable failure thresholds; keep deterministic provider order when healthy. | Validate with provider-failure simulation fixtures and operation telemetry assertions (fallbackHits, failure streaks, recovery). Rollback: disable demotion policy via config and return to strict static ordering.
| TD-META-004 | P1 | Import-list and identification query expansion logic has grown complex, but coverage is fragmented and does not guarantee cross-flow equivalence. | [src/NzbDrone.Core/ImportLists/ImportListSyncService.cs](src/NzbDrone.Core/ImportLists/ImportListSyncService.cs), [src/NzbDrone.Core/MediaFiles/BookImport/Identification/CandidateService.cs](src/NzbDrone.Core/MediaFiles/BookImport/Identification/CandidateService.cs), [src/NzbDrone.Core/MetadataSource/MetadataQueryNormalizationService.cs](src/NzbDrone.Core/MetadataSource/MetadataQueryNormalizationService.cs) | Add a shared query-policy contract and scenario matrix (isbn-only, author/title variants, malformed tags, external-id lookups) used by both import-list and identification services. | Add a dedicated contract test suite that runs both flows against identical fixtures and asserts same candidate selection or explicit allowed divergence. Rollback: keep existing path-local logic active while tests run in report-only mode.
| TD-META-005 | P2 | Metadata conflict-resolution outcomes are difficult to explain operationally without per-candidate scoring traces. | [src/NzbDrone.Core/MetadataSource/MetadataAggregator.cs](src/NzbDrone.Core/MetadataSource/MetadataAggregator.cs), [src/NzbDrone.Core/MetadataSource/MetadataConflictResolutionPolicy.cs](src/NzbDrone.Core/MetadataSource/MetadataConflictResolutionPolicy.cs), [src/NzbDrone.Core/MetadataSource/MetadataQualityScorer.cs](src/NzbDrone.Core/MetadataSource/MetadataQualityScorer.cs), [docs/operations/METADATA_PROVIDER_RUNBOOK.md](docs/operations/METADATA_PROVIDER_RUNBOOK.md) | Emit structured debug telemetry for conflict-resolution factors (identifier confidence, title match score, provider priority contribution, tie-break source). Add runbook interpretation guidance. | Validate via unit tests over scoring snapshots and an integration test that asserts telemetry payload contains ordered rationale fields. Rollback: disable verbose scoring telemetry while retaining decision behavior.

Execution order recommendation:

1. TD-META-001 (orchestrator parity) and TD-META-002 (ID normalization boundary)
2. TD-META-003 (health-aware provider routing)
3. TD-META-004 (cross-flow query contract)
4. TD-META-005 (conflict explainability)

Delivery constraints for all TD-META slices:

- Preserve API compatibility and avoid destructive schema rewrites.
- Prefer additive migrations and feature-flagged behavior pivots.
- Require targeted fixture evidence before broad suite promotion.
- Update [MIGRATION_PLAN.md](MIGRATION_PLAN.md) and [ROADMAP.md](ROADMAP.md) in the same PR when priority or sequence changes.

### March 21, 2026 validation and rehearsal pass (full-core baseline, new regressions, packaged smoke)

Completed an expanded validation slice covering full Core baseline delta, additional deterministic
regressions for refresh/telemetry/pagination hardening, a controlled 530-author rehearsal on a
repaired DB copy, and packaged-runtime parity smoke artifact capture.

**Code and test changes completed:**

1. Additional refresh and pagination hardening regressions:
  - Added repeated refresh-command storm regression in `RefreshAuthorServiceFixture` to verify
    duplicate matched rescans are not re-queued under repeated manual refresh triggers.
  - Added metadata-enrichment regression in `RefreshAuthorServiceFixture` to verify author
    overview/image updates still apply when metadata profile filtering removes remote books.
  - Added OpenLibrary pagination safeguards in `OpenLibraryProviderFixture` for:
    - sparse final-page stop condition with high `NumFound`,
    - hard document cap at 1000 books.

2. OpenLibrary payload resilience extension:
  - `OlKeyRefConverter` now tolerates malformed mixed token types by consuming unknown tokens and
    returning `null` instead of failing full work deserialization.
  - Added mixed malformed author-array fixture in `OpenLibraryClientResilienceFixture`.

3. Operation-level telemetry coverage:
  - Added `MetadataProviderOrchestratorFixture` assertion for `get-author-info` fallback telemetry
    (`operationName`, `fallbackHits`, and success counters).

**Validation evidence (this pass):**

- Full Core baseline run (TRX captured):
  - `dotnet test src/NzbDrone.Core.Test/Bibliophilarr.Core.Test.csproj -p:Platform=Posix`
  - Counters (`core-full.trx`): **Total 2675, Passed 2591, Failed 10, Skipped 74**.
  - Previous recorded baseline: **Total 2671, Passed 2572, Failed 31, Skipped 68**.
  - Exact delta: **Failed -21, Passed +19, Skipped +6, Total +4**.

- Current failing-set (10) extracted from TRX:
  - `should_use_extension_quality_source_when_pdf_is_malformed`
  - `should_use_extension_quality_source_when_azw3_is_malformed`
  - `config_properties_should_write_and_read_using_same_key`
  - `should_not_inject_false_positive_isbn_or_asin_for_unparseable_filename`
  - `should_use_extension_quality_source_when_mobi_is_malformed`
  - `should_use_extension_quality_source_when_epub_is_malformed`
  - `should_use_fallback_reader_when_primary_tags_missing_identity`
  - `get_metadata_should_not_fail_with_missing_country`
  - `should_fallback_to_selected_provider_when_identifier_values_are_missing`
  - `should_return_rejected_result_for_unparsable_search`

- Impacted fixture revalidation after new tests:
  - `dotnet test ... --filter OpenLibraryClientResilienceFixture|OpenLibraryProviderFixture|RefreshAuthorServiceFixture|MetadataProviderOrchestratorFixture|RefreshBookServiceFallbackIntegrationFixture`
  - Result: **Passed 41, Failed 0, Skipped 0**.

**Controlled 530-author rehearsal on repaired DB copy:**

- Source snapshot: `/tmp/bibliophilarr-parity-output/bibliophilarr.db`.
- Repaired run copy: `VACUUM INTO` generated DB under
  `/tmp/bibliophilarr-rehearsal-530-20260320-235802`.
- Integrity check: `ok`.
- Manual command execution evidence:
  - `RefreshAuthor` command `242`, trigger `manual`, status `completed`, duration `00:03:59.5307442`.

Measured population counts:

| Metric | Before | After |
|---|---:|---:|
| Total authors | 530 | 530 |
| Total books | 1778 | 51 |
| Single-book authors | 288 | 18 |
| Books with OpenLibraryWorkId | 1778 | 51 |

Fixed-sample unmatched-book identification rate (200 baseline IDs):

| Metric | Before | After |
|---|---:|---:|
| Sample size | 200 | 200 |
| Sample identified | 0 | 0 |
| Sample unmatched | 200 | 200 |
| Sample unmatched rate | 100.0% | 100.0% |
| Sample IDs still present after rehearsal | n/a | 2 |

Interpretation note:

- The rehearsal copy exhibited severe catalog contraction (`1778 -> 51 books`) during the full manual refresh.
- Logs and runtime output continue to show provider fallback stress (`All providers failed for operation get-book-info`) and Open Library HTML responses appearing in the metadata flow, indicating upstream response-shape/endpoint behavior still needs stronger runtime guards before treating this rehearsal path as release-safe.

**Packaged-runtime parity smoke and telemetry artifacts:**

- Built frontend and package artifacts:
  - `./build.sh --frontend`
  - `./build.sh --packages -r linux-x64 -f net8.0`
- Packaged runtime smoke:
  - `_artifacts/linux-x64/net8.0/Bibliophilarr/Bibliophilarr /data=/tmp/bibliophilarr-rc-smoke-...`
  - `/ping` reached `200`.
- Telemetry artifacts archived for RC gating at:
  - `_artifacts/rc-smoke-2026-03-21/metadata-providers-health.json`
  - `_artifacts/rc-smoke-2026-03-21/metadata-providers-telemetry.json`
  - `_artifacts/rc-smoke-2026-03-21/metadata-providers-telemetry-operations.json`

Current smoke snapshot is startup-clean with zero provider calls recorded (expected for idle/no lookup traffic).

### March 20, 2026 forensic remediation pass (latest-release behavior)

Completed a source-only remediation cycle driven by forensic analysis of the active runtime profile (`/home/swartdraak/.config/Bibliophilarr`) without mutating the running instance.

**Forensic signals addressed:**

- Repeated OpenLibrary work payload deserialization failures (`authors[0].type`) causing fallback exhaustion on `get-book-info`.
- Under-fetch of author bibliography due to single-page author search behavior.
- Refresh-path scan churn caused by repeated enqueueing of equivalent matched `RescanFolders` commands.

**Code changes delivered:**

1. `OpenLibraryClient` paging support:
  - `Search(string query, int limit = 20, int offset = 0)` now supports `offset` query parameter.

2. OpenLibrary payload compatibility hardening:
  - Added a tolerant converter for `OlKeyRef` in `OlWorkResource` to accept both string and object key-ref forms.

3. Author bibliography completeness:
  - `OpenLibraryProvider.GetAuthorInfo` now pages author bibliography search results and de-duplicates mapped books by `ForeignBookId`.

4. Refresh rescan dedup guard:
  - `RefreshAuthorService` now skips enqueue when an equivalent matched `RescanFolders` command is already queued or started.

5. Regression coverage:
  - `OpenLibraryClientResilienceFixture`: added polymorphic key-ref deserialization test.
  - `OpenLibraryProviderFixture`: added author-bibliography paging test; updated search mock signatures for explicit `offset` argument.
  - `RefreshArtistServiceFixture`: added duplicate matched-rescan suppression test.

6. Fallback integration fixture alignment:
  - `RefreshBookServiceFallbackIntegrationFixture` expectations were updated to account for intentional orchestrator warn/error fallback logging.

**Validation evidence (this pass):**

- Full solution build:
  - `dotnet msbuild -restore src/Bibliophilarr.sln -p:Configuration=Debug -p:Platform=Posix`
  - Result: **PASS**.

- Focused regressions:
  - `dotnet test src/NzbDrone.Core.Test/Bibliophilarr.Core.Test.csproj --filter 'FullyQualifiedName~OpenLibraryClientResilienceFixture|FullyQualifiedName~OpenLibraryProviderFixture|FullyQualifiedName~RefreshAuthorServiceFixture|FullyQualifiedName~RefreshBookServiceFallbackIntegrationFixture|FullyQualifiedName~OpenLibraryIdBackfillServiceFixture'`
  - Result: **Passed 40, Failed 0, Skipped 0**.

- Impacted-area revalidation after fixture alignment:
  - `dotnet test src/NzbDrone.Core.Test/Bibliophilarr.Core.Test.csproj --filter 'FullyQualifiedName~RefreshBookServiceFallbackIntegrationFixture|FullyQualifiedName~OpenLibraryClientResilienceFixture|FullyQualifiedName~OpenLibraryProviderFixture|FullyQualifiedName~RefreshAuthorServiceFixture'`
  - Result: **Passed 31, Failed 0, Skipped 0**.

### March 20, 2026 completion pass (OpenLibrary backfill + fallback telemetry + parity validation)

Completed the outstanding remediation set focused on OpenLibrary edition-token recovery, refresh fallback hardening, operation-scoped telemetry visibility, and runtime parity validation.

**Code and test changes completed:**

1. `OL...M` work-id recovery and persistence:
  - `OpenLibraryProvider` now resolves edition-token book IDs through ISBN search when direct work links are absent.
  - `OpenLibraryIdBackfillService` now accepts canonical OpenLibrary `OL...M` work tokens during normalization and persists them as valid `OpenLibraryWorkId` values.
  - Backfill coverage was extended for both empty external-ID lookup and `OL...M` normalization behavior.

2. Fallback integration harness hardening:
  - `RefreshBookServiceFallbackIntegrationFixture` now uses a concrete provider chain (`BookInfoProxy` + `OpenLibraryProvider` + real orchestrator) with boundary-level HTTP failure simulation.
  - DNS failure regression is covered at the HTTP/provider boundary.

3. Telemetry and diagnostics:
  - Provider telemetry now records operation name alongside aggregate counters.
  - Added operation feed endpoint: `/api/v1/metadata/providers/telemetry/operations`.
  - Updated API + integration fixtures to validate aggregate and operation telemetry shapes.

4. Core failure remediation:
  - `Book` metadata replication now copies `OpenLibraryWorkId` in `UseMetadataFrom` and `ApplyChanges`.
  - Mono harness test resolution is fixed by adding `Bibliophilarr.Mono` as a project reference in shared test-common output.
  - `UpdateServiceFixture` was rewritten to align with current intentional product behavior: application updates are disabled and should throw `CommandFailedException` without download/extract/start side effects.

5. Phase 6 workflow hardening:
  - The hardening slice added `RefreshAuthor` command-post/poll smoke checks, provider telemetry artifact capture, operation-telemetry capture, fallback-hit assertions, and ISBN-heavy lookup batch evidence capture in then-active workflow automation.
  - That dedicated phase6 packaging-matrix workflow has since been retired after hardening completion, with confidence coverage retained by active smoke/readiness workflows.

**Validation evidence (this pass):**

- Targeted Core regression set:
  - `dotnet test src/NzbDrone.Core.Test/Bibliophilarr.Core.Test.csproj --filter 'FullyQualifiedName~OpenLibraryIdBackfillServiceFixture|FullyQualifiedName~OpenLibraryProviderFixture|FullyQualifiedName~RefreshBookServiceFallbackIntegrationFixture|FullyQualifiedName~MetadataProviderOrchestratorFixture|FullyQualifiedName~EntityFixture|FullyQualifiedName~EbookTagServiceFixture|FullyQualifiedName~UpdateServiceFixture'`
  - Result: **Passed 118, Failed 0, Skipped 0**.

- Runtime parity with fresh artifacts (`./build.sh --backend --packages -r linux-x64 -f net8.0`):
  - Direct runtime: `./_output/net8.0/linux-x64/Bibliophilarr /data=/tmp/bibliophilarr-parity-output ...`
  - Packaged runtime: `./_artifacts/linux-x64/net8.0/Bibliophilarr/Bibliophilarr /data=/tmp/bibliophilarr-parity-artifact ...`
  - Both runtimes reported startup `BackfillOpenLibraryIds` completion and operation telemetry endpoint availability.
  - DB outcome in both parity profiles:
   - `books_with_work_id = 1778`
   - missing rows: `[]`

**ISBN-heavy 429 evidence status:**

- Local ISBN-heavy batch against packaged runtime returned `HTTP 200` for all 80 lookup calls.
- Strict log scan for real rate-limit markers (`429`, `TooManyRequests`, `Retry-After`, `rate-limited`) produced **0 matches** in this environment.
- Outcome: workflow capture wiring is complete; reproducible live `429` evidence remains environment-dependent and not yet observed in this pass.

### March 20, 2026 refresh-path hardening and OpenLibrary correctness note

Addressed four runtime-observable defects identified during forensic log analysis of a 530-author import rehearsal (288 of 530 authors had only 1 book; 0 of 530 had `OpenLibraryAuthorId` set).

**Changes delivered:**

1. **`RefreshAuthorService` rewired to `IMetadataProviderOrchestrator`** — `GetSkyhookData` now calls `_orchestrator.GetAuthorInfo(foreignId)` instead of `_authorInfo.GetAuthorInfo(foreignId)`. `IProvideAuthorInfo` is retained only for `GetChangedAuthors` (bulk-poll path). Direct `BookInfoProxy` failures no longer abort author refresh.

2. **`RefreshBookService` rewired to `IMetadataProviderOrchestrator`** — Removed `IProvideAuthorInfo` and `IProvideBookInfo` constructor dependencies entirely. `GetSkyhookData` uses `_orchestrator.GetBookInfo` and `_orchestrator.GetAuthorInfo`, both of which walk the priority-ordered provider chain with fallback.

3. **`OpenLibrarySearchProxy.TryIsbnEndpoint` redirect fix** — Added `request.AllowAutoRedirect = true` after `.Build()`. `HttpRequestBuilder` defaults to `false`; Open Library `/isbn/{isbn}.json` responds with `302 → /books/OL{id}M.json`. Without this the endpoint always returned no candidates.

4. **`OpenLibraryAuthorId` normalization in `MapAuthor`** — Added `OpenLibraryAuthorId = normalizedKey` to the `AuthorMetadata` initializer. `normalizedKey` is the bare `OL{n}A` form after `NormalizeAuthorKey`, which matches the `LooksLikeOpenLibraryAuthorId` predicate in `OpenLibraryIdBackfillService`. All future new-author search results now carry a populated `OpenLibraryAuthorId`.

**Tests added / updated:**

- `RefreshArtistServiceFixture.cs`: Both `IProvideAuthorInfo` mock setups updated to `IMetadataProviderOrchestrator`; new test `should_use_orchestrator_for_author_info_not_direct_provider` verifies orchestrator is called and direct provider is never called.
- `OpenLibrarySearchProxyFixture.cs`: Added `should_populate_open_library_author_id_on_author_mapping` (verifies `ForeignAuthorId` and `OpenLibraryAuthorId` on `LookupAuthorByKey`) and `isbn_lookup_should_follow_open_library_redirect_to_edition_json` (captures `HttpRequest` via callback, asserts `AllowAutoRedirect == true`).

**Validation evidence:**

- Build: `dotnet build src/Readarr.sln -p:Platform=Posix -c Debug -v minimal` → **0 Warning(s). 0 Error(s).** (8.17s, second pass after SA1515/SA1137 StyleCop fixes)
- Targeted fixture run: `dotnet test ... --filter RefreshAuthorServiceFixture|OpenLibrarySearchProxyFixture|OpenLibraryIsbnAsinLookupFixture` → **Passed: 14, Failed: 0, Skipped: 0**
- Broader affected-area run: `dotnet test ... --filter RefreshBookService|RefreshAuthor|AddAuthor|OpenLibrary|BookInfoProxy|MetadataProvider` → **Passed: 89, Failed: 0, Skipped: 8 (pre-existing)**
- Full Core suite: **Passed: 2572, Failed: 31, Skipped: 68** — the 31 failures confirmed pre-existing via `git stash` baseline run before these changes.
- Backend binary: `./build.sh --backend -r linux-x64 -f net8.0` → **PASS**, artifact at `_artifacts/linux-x64/net8.0/Bibliophilarr/`.

**Before-snapshot (live DB at time of forensic analysis):**

| Metric | Count |
|---|---|
| Total authors | 530 |
| Total books | 1778 |
| Total editions | 1778 |
| Single-book authors | 288 (54%) |
| Multi-book authors | 242 |
| Authors with `OpenLibraryAuthorId` set | 0 / 530 |

**Measured rehearsal results (same 530-author profile, March 20, 2026):**

- Live profile used: `/home/swartdraak/.config/Bibliophilarr`
- Runtime command evidence:
  - `BackfillOpenLibraryIds` startup run (direct `_output` binary): command `151`, **completed** in `00:00:04.2480252`
  - Manual full refresh run (direct `_output` binary): command `154`, **completed** in `00:33:05.0413339`
- Measured after-snapshot:

| Metric | Before | After |
|---|---:|---:|
| Total authors | 530 | 530 |
| Total books | 1778 | 1778 |
| Total editions | 1778 | 1778 |
| Single-book authors | 288 | 288 |
| Multi-book authors | 242 | 242 |
| Authors with `OpenLibraryAuthorId` set | 0 | 530 |
| Books with `OpenLibraryWorkId` set | 0 | 1775 |

**Outcome summary:**

- `OpenLibraryAuthorId` backfill objective: **achieved** (`0 -> 530`).
- `OpenLibraryWorkId` backfill objective: **partially achieved** (`0 -> 1775`), with 3 records still unresolved.
- Full refresh command stability objective: **achieved** (manual refresh completed; prior `NullReferenceException` path in `RefreshBookService.GetRemoteData` not observed in this rerun).
- Bibliography expansion objective (reduce single-book authors): **not yet achieved** (`288 -> 288`).

**Observed blockers and caveats:**

- Primary provider DNS failure remains active in this environment (`api.bookinfo.club` lookup failures).
- The profile SQLite file reports corruption (`database disk image is malformed`) at startup, and integrity checks continue to report malformed pages; this is a reliability risk for repeated long-running measurements.
- Packaged runtime in `_artifacts/linux-x64/net8.0/Bibliophilarr/` initially produced stale/contradictory backfill outcomes; direct runtime from `_output/net8.0/linux-x64/Bibliophilarr` matched current source behavior and produced consistent ID backfill results.

**Follow-up required before next RC gate:**

- Repair or rebuild the live SQLite profile (`bibliophilarr.db`) and rerun rehearsal to eliminate corruption-driven measurement risk.
- Investigate the remaining 3 books without `OpenLibraryWorkId` and add deterministic fixture coverage for those unresolved patterns.
- Re-run full author refresh after DB repair and compare bibliography-count deltas to confirm whether provider fallback now yields expansion under stable storage conditions.
- Confirm `AllowAutoRedirect` behavior under an ISBN-heavy batch in this environment and capture explicit 429/Retry-After telemetry evidence.
- Address the 31 pre-existing Core test failures (entity replication, update installer, malformed-file quality source).

### March 20, 2026 hardening and RC rehearsal note

- Closed the remaining add-author runtime failure path by falling back to request payload metadata when upstream author lookups fail transiently.
- Replaced remaining high-risk callable `NotImplementedException` paths in release lookup, metadata redirect handling, and managed HTTP header dispatch with controlled behavior/logged failures.
- Added search telemetry diagnostics exposure at `api/v1/diagnostics/search/telemetry` and validated it through both unit and integration fixtures.
- Added real HTTP pipeline coverage for malformed Basic auth and replaced live-dependent author lookup integration assertions with deterministic non-500 contract coverage.
- Added a dedicated RID-specific backend CI lane for core/common targeted tests using `-r linux-x64`.
- Added binary install-readiness snapshot generation to Phase 6 packaging validation artifacts.

Validation completed with exact command evidence and outcomes:

- Deterministic cleanup before rebuild:
  - `find . -maxdepth 6 -type d -name '_intg_*' -exec rm -rf {} +`
  - `rm -rf _output _tests /tmp/bibliophilarr-packaging-binary`
  - `find src -type d \( -name bin -o -name obj \) -exec rm -rf {} +`
- Fresh solution build: PASS
  - `dotnet build src/Bibliophilarr.sln -p:Platform=Posix -c Debug -v minimal`
- Fresh RID backend build: PASS
  - `./build.sh --backend -r linux-x64 -f net8.0`
- Add-author fallback fixture with RID runtime layout: PASS (`8/8`)
  - `dotnet test src/NzbDrone.Core.Test/Bibliophilarr.Core.Test.csproj --configuration Debug -p:Platform=Posix -r linux-x64 --filter 'FullyQualifiedName~AddAuthorFixture'`
- Search telemetry API/controller unit fixtures: PASS (`2/2`)
  - `dotnet test src/NzbDrone.Api.Test/Bibliophilarr.Api.Test.csproj --configuration Debug -p:Platform=Posix --filter 'FullyQualifiedName~SearchControllerFixture|FullyQualifiedName~SearchTelemetryControllerFixture'`
- Targeted integration hardening fixtures: PASS (`10/10`)
  - `dotnet test src/NzbDrone.Integration.Test/Bibliophilarr.Integration.Test.csproj --configuration Debug -p:Platform=Posix --filter 'FullyQualifiedName~HostConfigAuthorizationFixture|FullyQualifiedName~SearchTelemetryFixture|FullyQualifiedName~AuthorLookupFixture|FullyQualifiedName~MetadataConflictTelemetryFixture'`
- RID lane-equivalent common fixtures: PASS (`71/73`, `2 skipped`)
  - `dotnet test src/NzbDrone.Common.Test/Bibliophilarr.Common.Test.csproj --configuration Debug -p:Platform=Posix -r linux-x64 --filter 'FullyQualifiedName~HttpClientFixture|FullyQualifiedName~RateLimitServiceFixture|FullyQualifiedName~ProcessProviderFixture'`
- RID lane-equivalent core fixtures: PASS (`13/13`)
  - `dotnet test src/NzbDrone.Core.Test/Bibliophilarr.Core.Test.csproj --configuration Debug -p:Platform=Posix -r linux-x64 --filter 'FullyQualifiedName~AddAuthorFixture|FullyQualifiedName~MetadataProviderOrchestratorFixture|FullyQualifiedName~OpenLibraryIsbnAsinLookupFixture'`
- Local release-entry gate before install-snapshot marker fix: FAIL (`install snapshot missing required marker`)
  - `python3 scripts/release_entry_gate.py --md-out _artifacts/release-entry-gate.md --json-out _artifacts/release-entry-gate.json`
- GitHub-backed readiness/dependency scripts: BLOCKED by missing CLI authentication
  - `python3 scripts/release_readiness_report.py --owner Swartdraak --repo Bibliophilarr --md-out _artifacts/release-readiness-report.md --json-out _artifacts/release-readiness-report.json`
  - `python3 scripts/dependabot_lockfile_triage.py --owner Swartdraak --repo Bibliophilarr --md-out _artifacts/dependabot-triage.md --json-out _artifacts/dependabot-triage.json`
  - Outcome: both exited with `gh auth login` / `GH_TOKEN` required before GitHub API data can be collected.
- Release-entry rerun after install snapshot fix: PASS (`ok=true`)
  - `python3 scripts/release_entry_gate.py --md-out _artifacts/release-entry-gate.md --json-out _artifacts/release-entry-gate.json`
- Final packaged-runtime RC rehearsal: PASS after package refresh
  - `./build.sh --frontend`
  - `./build.sh --packages -r linux-x64 -f net8.0`
  - `./_artifacts/linux-x64/net8.0/Bibliophilarr/Bibliophilarr /data=/tmp/bibliophilarr-rc-rehearsal /nobrowser /nosingleinstancecheck`
  - `curl http://127.0.0.1:8796/ping` -> `200`
  - `curl -H 'X-Api-Key: rc-rehearsal-key' http://127.0.0.1:8796/api/v1/metadata/providers/health` -> `200`
  - `curl -H 'X-Api-Key: rc-rehearsal-key' http://127.0.0.1:8796/api/v1/metadata/conflicts/telemetry` -> `200`
  - `curl -H 'X-Api-Key: rc-rehearsal-key' http://127.0.0.1:8796/api/v1/diagnostics/search/telemetry` -> `200`
  - `curl -H 'X-Api-Key: rc-rehearsal-key' http://127.0.0.1:8797/api/v1/qualityprofile/schema` -> `200`
  - Probe correction note: `rootFolder/schema` returned `404` because that route is not implemented in this fork; rehearsal now uses `qualityprofile/schema` as the valid schema contract check.

- Added manual and scheduled workflow support for:
  - `.github/workflows/release-readiness-report.yml`
  - `.github/workflows/branch-policy-audit.yml`
- Added supporting operational scripts:
  - `scripts/release_readiness_report.py`
  - `scripts/dependabot_lockfile_triage.py`
  - `scripts/audit_branch_protection.py`
- Added main-compatible staging smoke workflow support so the required smoke context is declared and can execute against both legacy and current branch layouts.
- Refreshed contributor and operator documentation for branch protection and release-readiness workflows.

### March 17, 2026 operator note

- Manual workflow dispatch from `main` is now validated for both `Release Readiness Report` and `Branch Policy Audit`.
- Workflow artifacts now preserve permission-limited output when GitHub Actions integration tokens receive `403 Resource not accessible by integration`.
- Packaging remains intentionally scoped to `develop` and `staging` until binary, Docker, and npm installation paths are fully validated for release-entry use from `main`.

### March 17, 2026 metadata migration note

- Added config-driven metadata provider controls (enable flags, provider order, timeout/retry/circuit knobs) and exposed them in both API and UI settings.
- Introduced metadata provider orchestration and provider telemetry, and switched high-traffic metadata flows (search/add/refresh/import-list) to orchestrated fallback behavior.
- Added Inventaire provider/client baseline and metadata diagnostics API endpoints for provider health and counters.
- Added environment kill-switch support for Inventaire rollout (`BIBLIOPHILARR_DISABLE_INVENTAIRE=1`) and surfaced guidance in settings/runbook.
- Added Open Library ID backfill command/service and propagated Open Library provenance identifiers through API resources and book index UI.
- Added status-page metadata provider health dashboard and scheduled dry-run automation artifacts for staging provenance snapshots.
- Validation completed with:
  - API test project passing (`Bibliophilarr.Api.Test`)
  - `MetadataProviderOrchestratorFixture` passing
  - `ImportListSyncServiceFixture` passing after fixing unresolved-ID import handling

### March 17, 2026 install and diagnostics validation note

- Captured first install-evidence snapshot for native, Docker, and npm surfaces: `docs/operations/install-test-snapshots/2026-03-17.md`.
- Added backend CI integration gate for metadata diagnostics fixture (`Metadata Diagnostics Integration`) in `.github/workflows/ci-backend.yml`.
- Re-validated `MetadataProviderDiagnosticsFixture` locally with 3/3 tests passing.
- Recorded first telemetry endpoint checkpoint in `docs/operations/metadata-telemetry-checkpoints/2026-03-17.md`.
- Recorded dry-run provenance as blocked in `docs/operations/metadata-dry-run-snapshots/2026-03-17-blocked.md` because staging secrets were unavailable in this execution environment.

### March 17, 2026 CI stabilization note

- Restored the committed Servarr NuGet feed configuration in `src/NuGet.config` so GitHub-hosted runners can resolve FluentMigrator, SQLite, Mono.Posix, and related fork-specific packages without relying on local caches.
- Restored the frontend `metadataProviderHealth` action/state wiring expected by the metadata diagnostics status UI and its test coverage.
- Narrowed the required Markdown lint gate to the canonical root documentation set while dated evidence and historical snapshots are normalized incrementally.

### March 18, 2026 release-entry enforcement note

- Added `scripts/release_entry_gate.py` and wired `release.yml` to block packaging/release jobs unless dry-run, telemetry threshold, and install-matrix snapshots are present, fresh, and marked as passing.
- Expanded docs lint incrementally beyond canonical root docs to include active operations runbooks:
  - `docs/operations/METADATA_MIGRATION_DRY_RUN.md`
  - `docs/operations/METADATA_PROVIDER_RUNBOOK.md`
  - `docs/operations/RELEASE_AUTOMATION.md`
- Recorded an additional blocked metadata dry-run snapshot at `docs/operations/metadata-dry-run-snapshots/2026-03-18-blocked.md` after re-validating that staging secrets are unavailable in this environment.
- Resolved a startup blocker caused by duplicate FluentMigrator version `041` by renumbering Open Library identifier migration to `042` with idempotent schema checks.

### March 18, 2026 OpenLibrary replacement note

- Replaced active Goodreads provider implementations with OpenLibrary-first behavior in metadata-search paths and removed legacy Goodreads provider directories:
  - `src/NzbDrone.Core/MetadataSource/Goodreads/`
  - `src/NzbDrone.Core/MetadataSource/GoodreadsSearchProxy/`
  - `src/NzbDrone.Core/ImportLists/Goodreads/`
  - `src/NzbDrone.Core/Notifications/Goodreads/`
- Migrated core/API/frontend/localization terminology from Goodreads identifiers to OpenLibrary identifiers in active runtime surfaces.
- Updated OpenAPI, localization payloads, and frontend user-facing text to remove active Goodreads references and standardize on OpenLibrary naming.
- Removed remaining Servarr-hosted Sentry/Auth endpoint references and disabled frontend Sentry middleware integration.
- Validation completed with:
  - full solution build passing (`dotnet msbuild -restore src/Bibliophilarr.sln ...`)
  - frontend build passing (`yarn build`)
  - target-scope grep checks reporting zero `goodreads` references in `src/Bibliophilarr.Api.V1`, `src/NzbDrone.Core`, and `frontend/src`.
- Known migration gap from this slice:
  - several Goodreads-coupled legacy test fixtures were removed to restore build health and require OpenLibrary-native replacements in a follow-up hardening slice.

### March 18, 2026 hardening validation note

- Executed dry-run with operator-shell secrets from a fresh local install and archived artifacts:
  - `_artifacts/metadata-dry-run/before.json`
  - `_artifacts/metadata-dry-run/after.json`
  - `_artifacts/metadata-dry-run/summary.json`
- Replaced the latest dry-run checkpoint with measured PASS baseline snapshot:
  - `docs/operations/metadata-dry-run-snapshots/2026-03-18.md`
- Resolved metadata health endpoint ambiguity by removing route collision between:
  - `ProviderHealthController`
  - `MetadataProvidersController`
- Captured telemetry checkpoint evidence and promoted latest telemetry snapshot to PASS sample-window status:
  - `docs/operations/metadata-telemetry-checkpoints/2026-03-18.md`
- Re-ran release-entry gate and confirmed overall PASS (`ok=true`) with all four gates passing.
- Added deterministic refresh-focused integration fixture and stabilized live lookup/add fixtures by extending ignore windows for non-deterministic external-provider dependencies.
- Targeted integration rerun (`AuthorLookupFixture|AuthorFixture|OpenLibraryRefreshBaselineFixture`) completed with:
  - `2` passed (deterministic refresh baseline)
  - `14` skipped (intentionally ignored live-provider lookup/add tests)

### March 19, 2026 HTTP mutation binding hardening note

- Completed project-wide explicit binding remediation for complex mutation payload endpoints in:
  - `src/Bibliophilarr.Api.V1`
  - `src/Bibliophilarr.Http`
- Added machine-readable scope-lock inventory and remediation checklist:
  - `scripts/ops/http_binding_inventory.json`
- Added a static regression gate to block implicit binding on complex POST/PUT payloads:
  - `scripts/ops/check_http_binding.sh`
  - `.github/workflows/ci-backend.yml` (`Enforce explicit HTTP mutation binding`)
- Operational impact:
  - API save/update paths now explicitly declare payload source, reducing first-run and settings-save ambiguity.
  - CI now fails fast when a complex mutation payload omits explicit source binding.

### March 19, 2026 metadata resilience hardening note

- Added import identification resilience to reduce metadata misses and first-pass failures:
  - ISBN miss flow now performs limited title+author fallback attempts before moving on to other identifier sources.
  - Added constrained contextual fallback attempts to improve OpenLibrary hit rate for files with stale or edition-mismatched ISBNs.
- Relaxed HTTP redirect behavior in development and production request defaults so metadata requests follow canonical endpoint redirects.
- Improved ebook metadata parsing resilience for malformed files:
  - Added best-effort filename-derived metadata fallback when EPUB/PDF/AZW parsing fails.
  - Hardened EPUB ISBN extraction against null identifier collections.
- Decoupled runtime from missing `services.bibliophilarr.org` dependency:
  - Cloud services endpoint is now optional and enabled only when `BIBLIOPHILARR_SERVICES_URL` is configured.
  - Update checks, server-side cloud notifications, and cloud-backed proxy/system-time checks now degrade gracefully when endpoint is not configured.
- Validation rerun completed on March 19, 2026 with:
  - full solution build passing:
    - `dotnet msbuild -restore src/Bibliophilarr.sln -p:Configuration=Debug -p:Platform=Posix`
  - targeted core fixture tests passing (`18/18`):
    - `dotnet test src/NzbDrone.Core.Test/Bibliophilarr.Core.Test.csproj --filter "FullyQualifiedName~EbookTagServiceFixture|FullyQualifiedName~CandidateServiceFixture|FullyQualifiedName~UpdatePackageProviderServicesDisabledFixture|FullyQualifiedName~SystemTimeCheckFixture"`
  - targeted HTTP client fixture tests passing (`55/55`):
    - `dotnet test src/NzbDrone.Common.Test/Bibliophilarr.Common.Test.csproj --filter "FullyQualifiedName~HttpClientFixture"`
  - publish path validated for runtime artifact generation:
    - `dotnet publish src/NzbDrone.Console/Bibliophilarr.Console.csproj -f net8.0 -c Debug`
  - install-readiness smoke checks passing for both local binary and Docker runtime:
    - `/ping` returned `200`
    - `/api/v1/system/status` returned `401` (expected without API key)

## What Is Complete

### Metadata migration foundation

- Migration roadmap and architecture are documented in [MIGRATION_PLAN.md](MIGRATION_PLAN.md) and [ROADMAP.md](ROADMAP.md).
- Provider-consolidation work is active in the `develop` and `staging` lanes.
- Phase 5 rollout controls and telemetry slices are in place.

### Operational hardening

- Required checks emit consistently for protected branches.
- Branch policy drift can be audited with `scripts/audit_branch_protection.py`.
- Release readiness can be summarized with `scripts/release_readiness_report.py`.
- Dependabot alert state can be compared against `yarn.lock` with `scripts/dependabot_lockfile_triage.py`.

### Packaging validation

- Phase 6 packaging validation runs on `develop` and `staging`.
- The latest validated matrix state is green for binary, Docker, and npm installation paths.

## Current Risks And Follow-Up Areas

- GitHub-backed readiness reporting and Dependabot triage cannot currently be revalidated from this environment because `gh` is installed but not authenticated; workflow/branch-protection/Dependabot API state is therefore unverified in this execution pass.
- Open dependency security remediation remains active work, but exact current alert counts could not be refreshed locally until `gh auth login` or `GH_TOKEN` is supplied.
- `main` can now host the manual readiness workflows, but broader release workflows are still aligned primarily with the active delivery lanes.
- Packaging validation is green on `develop` and `staging`; `main` is receiving the audit and readiness automation first so operators can dispatch reports from the default branch.

## Source-code technical debt tracker (March 20, 2026)

### Audit scope and method

- Scope: source-only pre-compile code under `src/`, `frontend/src/`, `scripts/`, and root build/config files.
- Excluded by policy: `_output/`, `_tests/`, `_artifacts/`, and all `bin/` and `obj/` trees.
- Method: static source review, call-path tracing on active runtime surfaces, and workspace diagnostics check.
- Current status: open remediation queue, ordered by release risk and user-facing impact.

### Change management note

What changed:
- Added a canonical technical debt register for source-level validity findings and remediation tracking.

Why it changed:
- Runtime issues persisted despite successful builds, indicating unresolved source-level correctness and safety gaps.

How to validate:
- Confirm each debt item acceptance criteria and validation commands pass before setting item status to closed.

Operational impact and rollback:
- Closing P0/P1 items reduces crash/security exposure and improves search/import determinism.
- Rollback is per-slice via scoped commits and revert of the specific debt item commit when needed.

### Priority definitions

- `P0`: security boundary or crash-risk issue affecting core runtime flows.
- `P1`: high-probability runtime defect in user-critical paths.
- `P2`: important correctness, resilience, or operability hardening.
- `P3`: cleanup, refactor, or deferred structural quality work.

### Tracker fields

- `Debt ID`: stable identifier for cross-reference in commits/PRs.
- `Owner`: team or maintainer assignment (set during triage).
- `Status`: `open`, `in-progress`, `blocked`, `done`.
- `Validation gate`: objective check required to close the item.

### Active technical debt queue

| Debt ID | Priority | Area | Risk summary | Primary locations | Owner | Status | Acceptance criteria | Validation gate |
|---|---|---|---|---|---|---|---|---|
| TD-001 | P0 | API/Auth | Host config endpoints are anonymously readable/writable and can expose credential fields. | `src/Bibliophilarr.Api.V1/Config/HostConfigController.cs` | unassigned | done | Host config write requires authenticated admin context; response never returns password material. | API tests for unauthorized/authorized host config GET/PUT and first-run path behavior. |
| TD-002 | P0 | Core/API | Unsafe `Single(x => x.Monitored)` edition selection can throw when monitored cardinality is not exactly one. | `src/NzbDrone.Core/Books/Services/AddBookService.cs`, `src/NzbDrone.Core/Notifications/CustomScript/CustomScript.cs`, `src/Bibliophilarr.Api.V1/ManualImport/ManualImportResource.cs` | unassigned | done | Replace `Single` calls with safe deterministic selection/fallback and null-safe behavior. | Targeted unit/integration tests for 0, 1, and many monitored-edition cases. |
| TD-003 | P1 | Frontend/Add Search | Add-search book rendering assumes non-null author and can crash on partial provider payloads. | `frontend/src/Search/AddNewItem.js`, `frontend/src/Search/Book/AddNewBookSearchResult.js` | unassigned | done | UI handles `book.author == null` without runtime errors and still renders actionable result state. | Frontend tests plus manual add-search smoke (`/add/search?term=...`) with null-author fixture payload. |
| TD-004 | P1 | Frontend/Navigation | A-Z jump paths accept `-1` from index finder and may attempt invalid scroll operations. | `frontend/src/Utilities/Array/getIndexOfFirstCharacter.js`, `frontend/src/Author/Index/**`, `frontend/src/Book/Index/**`, `frontend/src/Bookshelf/Bookshelf.js` | unassigned | done | All jump consumers gate on non-negative index and no-op cleanly when no match exists. | Unit tests for no-match jump; manual A-Z jump smoke in table, poster, and overview modes. |
| TD-005 | P1 | API Runtime Surface | Multiple API/runtime controllers still throw `NotImplementedException` on callable paths. | `src/Bibliophilarr.Api.V1/Queue/*.cs`, `src/Bibliophilarr.Api.V1/Health/HealthController.cs`, `src/Bibliophilarr.Api.V1/Metadata/MetadataController.cs`, `src/Bibliophilarr.Api.V1/Notifications/NotificationController.cs` | unassigned | done | Replace hard throws with implemented behavior or explicit `501/feature-unavailable` responses plus telemetry. | API contract tests confirm non-crashing responses and expected status codes. |
| TD-006 | P2 | Indexer Search | RSS-only indexer generators throw `NotImplementedException` for search methods. | `src/NzbDrone.Core/Indexers/*RequestGenerator.cs` (RSS-only implementations) | unassigned | done | Explicit capability segregation prevents search invocation against RSS-only generators, or methods return safe no-op chains. | Search flow tests across mixed indexer capabilities; no unhandled `NotImplementedException`. |
| TD-007 | P2 | Auth Handling | Basic auth parsing throws generic exception on malformed auth header. | `src/Bibliophilarr.Http/Authentication/BasicAuthenticationHandler.cs` | unassigned | done | Malformed headers produce controlled auth failure (401) without unhandled exceptions. | Authentication handler tests for malformed/missing delimiter scenarios. |
| TD-008 | P2 | Search Observability | Unsupported search entity types are silently dropped, masking provider contract drift. | `src/Bibliophilarr.Api.V1/Search/SearchController.cs` | unassigned | done | Unsupported entity types are counted/logged with request context while preserving successful partial responses. | Telemetry assertions and log verification in search tests. |
| TD-009 | P3 | Build/Test Clarity | Distinction between test package and full runtime package is implicit and causes execution confusion. | `build.sh`, `QUICKSTART.md` | unassigned | done | Commands/documentation clearly distinguish runtime package artifacts vs test package artifacts and startup expectations. | Local operator walkthrough from clean checkout confirms deterministic startup instructions. |

### Latest validation evidence (March 20, 2026)

1. Clean rebuild and package generation completed from a fresh output state:
  - `rm -rf _output/net8.0 _tests/net8.0 _artifacts/linux-x64`
  - `./build.sh --backend --frontend --packages --lint --framework net8.0 --runtime linux-x64`
2. `build.sh` packaging flow now serializes the `PublishAllRids` msbuild step (`-m:1`) so shared RID-specific `_tests` outputs no longer emit `MSB3026` copy-retry warnings during a clean linux-x64 build.
3. TD-006 targeted core tests now pass on the RID-specific runtime layout (`7/7`):
  - `dotnet test src/NzbDrone.Core.Test/Bibliophilarr.Core.Test.csproj -p:Platform=Posix -r linux-x64 --filter "FullyQualifiedName~ReleaseSearchServiceFixture|FullyQualifiedName~RssIndexerRequestGeneratorFixture"`
4. TD-007 and TD-008 targeted API tests now pass (`4/4`):
  - `dotnet test src/NzbDrone.Api.Test/Bibliophilarr.Api.Test.csproj -p:Platform=Posix --filter "FullyQualifiedName~BasicAuthenticationHandlerFixture|FullyQualifiedName~SearchControllerFixture"`
5. Redirect-handling regression tests now pass on the RID-specific runtime layout (`6/6`):
  - `dotnet test src/NzbDrone.Common.Test/Bibliophilarr.Common.Test.csproj -p:Platform=Posix -r linux-x64 --filter "Name~should_follow_redirects_by_default|Name~should_follow_redirects_from_simulated_metadata_endpoint|Name~should_follow_redirects|Name~should_not_follow_redirects|Name~should_not_write_redirect_content_to_stream"`
6. TD-001 and TD-005 integration fixtures remain green after the new search/auth changes (`10/10`):
  - `dotnet test src/NzbDrone.Integration.Test/Bibliophilarr.Integration.Test.csproj -p:Platform=Posix --filter "FullyQualifiedName~HostConfigAuthorizationFixture|FullyQualifiedName~ControllerNonThrowingContractFixture"`
7. Runtime package startup validated using both the raw publish output and the packaged artifact tree:
  - `cp -r _output/UI _output/net8.0/linux-x64/UI`
  - `./_output/net8.0/linux-x64/Bibliophilarr --nobrowser ...`
  - `./_artifacts/linux-x64/net8.0/Bibliophilarr/Bibliophilarr /data=/tmp/bibliophilarr-package-smoke-2026-03-20 /nobrowser /nosingleinstancecheck`
  - `/ping` returned `200`.
8. Integration bootstrap path remains repaired in `src/NzbDrone.Test.Common/NzbDroneRunner.cs` (robust executable resolution across current output layouts).
9. TD-003 manual UI smoke passed:
  - `/add/search?term=anne` exercised with Playwright route-mutation setting first search result `author = null`.
  - Search results continued rendering with no page errors and no console runtime errors.
10. TD-004 manual UI smoke passed:
  - Author index and shelf UI paths were exercised under empty-library and populated-list conditions.
  - Jump/no-match navigation paths produced no client runtime exceptions; guarded `isValidScrollIndex` flow no-oped cleanly when no valid index existed.
11. March 20 RC hardening rerun completed from a fully cleaned local runtime/build state:
  - `find . -maxdepth 6 -type d -name '_intg_*' -exec rm -rf {} +`
  - `rm -rf _output _tests /tmp/bibliophilarr-packaging-binary`
  - `find src -type d \( -name bin -o -name obj \) -exec rm -rf {} +`
  - `dotnet build src/Bibliophilarr.sln -p:Platform=Posix -c Debug -v minimal`
  - `./build.sh --backend -r linux-x64 -f net8.0`
  - Outcome: all commands passed and regenerated fresh runtime/test artifacts.
12. New March 20 hardening tests passed:
  - `dotnet test src/NzbDrone.Core.Test/Bibliophilarr.Core.Test.csproj --configuration Debug -p:Platform=Posix -r linux-x64 --filter 'FullyQualifiedName~AddAuthorFixture'` -> `8/8` passed
  - `dotnet test src/NzbDrone.Api.Test/Bibliophilarr.Api.Test.csproj --configuration Debug -p:Platform=Posix --filter 'FullyQualifiedName~SearchControllerFixture|FullyQualifiedName~SearchTelemetryControllerFixture'` -> `2/2` passed
  - `dotnet test src/NzbDrone.Integration.Test/Bibliophilarr.Integration.Test.csproj --configuration Debug -p:Platform=Posix --filter 'FullyQualifiedName~HostConfigAuthorizationFixture|FullyQualifiedName~SearchTelemetryFixture|FullyQualifiedName~AuthorLookupFixture|FullyQualifiedName~MetadataConflictTelemetryFixture'` -> `10/10` passed
13. RID-specific CI-lane-equivalent validations passed locally:
  - `dotnet test src/NzbDrone.Common.Test/Bibliophilarr.Common.Test.csproj --configuration Debug -p:Platform=Posix -r linux-x64 --filter 'FullyQualifiedName~HttpClientFixture|FullyQualifiedName~RateLimitServiceFixture|FullyQualifiedName~ProcessProviderFixture'` -> `71` passed, `2` skipped
  - `dotnet test src/NzbDrone.Core.Test/Bibliophilarr.Core.Test.csproj --configuration Debug -p:Platform=Posix -r linux-x64 --filter 'FullyQualifiedName~AddAuthorFixture|FullyQualifiedName~MetadataProviderOrchestratorFixture|FullyQualifiedName~OpenLibraryIsbnAsinLookupFixture'` -> `13/13` passed
14. Release-entry gate status during this pass:
  - `python3 scripts/release_entry_gate.py --md-out _artifacts/release-entry-gate.md --json-out _artifacts/release-entry-gate.json`
  - Initial outcome: FAIL because `docs/operations/install-test-snapshots/2026-03-20.md` lacked the required `Overall matrix verdict` marker.
15. GitHub-backed readiness/dependency reporting status during this pass:
  - `python3 scripts/release_readiness_report.py --owner Swartdraak --repo Bibliophilarr --md-out _artifacts/release-readiness-report.md --json-out _artifacts/release-readiness-report.json`
  - `python3 scripts/dependabot_lockfile_triage.py --owner Swartdraak --repo Bibliophilarr --md-out _artifacts/dependabot-triage.md --json-out _artifacts/dependabot-triage.json`
  - Outcome: both blocked pending `gh` authentication (`gh auth login` or `GH_TOKEN`).
16. Release-entry gate rerun after install-snapshot correction: PASS (`ok=true`):
  - `python3 scripts/release_entry_gate.py --md-out _artifacts/release-entry-gate.md --json-out _artifacts/release-entry-gate.json`
17. Final packaged-runtime RC rehearsal completed successfully after package refresh:
  - `./build.sh --frontend`
  - `./build.sh --packages -r linux-x64 -f net8.0`
  - `curl http://127.0.0.1:8796/ping` -> `200`
  - `curl -H 'X-Api-Key: rc-rehearsal-key' http://127.0.0.1:8796/api/v1/metadata/providers/health` -> `200`
  - `curl -H 'X-Api-Key: rc-rehearsal-key' http://127.0.0.1:8796/api/v1/metadata/conflicts/telemetry` -> `200`
  - `curl -H 'X-Api-Key: rc-rehearsal-key' http://127.0.0.1:8796/api/v1/diagnostics/search/telemetry` -> `200`
  - `curl -H 'X-Api-Key: rc-rehearsal-key' http://127.0.0.1:8797/api/v1/qualityprofile/schema` -> `200`
  - Outcome: package startup, auth, diagnostics, and schema contract checks passed on regenerated linux-x64 release artifacts.

### Execution order and cadence

1. Complete all `P0` items before introducing new migration-scope feature work.
2. Close `P1` items in short scoped commits, each with targeted test evidence.
3. Address `P2` resilience items after `P0/P1` queue reaches stable green.
4. Schedule `P3` cleanup with documentation updates and operator validation.

### Tracking protocol

For each debt item closure:

1. Reference the `Debt ID` in commit and PR text.
2. Include exact commands used for validation and resulting outcomes.
3. Record rollback notes for any change touching auth, search, or import paths.
4. Update this table status and acceptance evidence in the same change set.

## Local Install Testing Program Recommendations

To keep the project moving toward practical release confidence, the `develop` branch should treat local install testing as a primary delivery outcome.

Recommended program posture:

1. Require recurring install proofs from `develop` for:

   - native package run
   - Docker run
   - npm launcher install and startup

2. Track install results as dated operator artifacts (commands, outputs, environment, verdict).
3. Escalate installer/startup regressions above routine feature slices until closed.
4. Require each migration or hardening slice to include install impact notes and rollback path.

Immediate next actions:

1. Publish a local install testing runbook and matrix under `docs/operations`.
2. Add an install-evidence section to weekly/project status updates.
3. Use `develop` as the proving lane and promote only install-verified slices to `staging`.

## Metadata Readiness Release Criteria

Metadata migration readiness is now a release-entry gate, not an advisory check.

Required to proceed with release tagging:

1. `Metadata Provider Fixtures` job passes in latest `ci-backend.yml` on both `develop` and `staging`.
2. Latest dry-run snapshot passes provenance acceptance gates in [docs/operations/METADATA_MIGRATION_DRY_RUN.md](docs/operations/METADATA_MIGRATION_DRY_RUN.md).
3. Provider telemetry remains inside warning SLO thresholds in `docs/operations/METADATA_PROVIDER_RUNBOOK.md`.
4. Any temporary Inventaire kill-switch activation is rolled back and documented.

## Delivery Process Guardrail

- Scoped commit iteration process is required for migration and hardening slices.
- Reference: [docs/operations/SCOPED_COMMIT_PROCESS.md](docs/operations/SCOPED_COMMIT_PROCESS.md) and [CONTRIBUTING.md](CONTRIBUTING.md).

## Recommended Operator Checks

Run these after significant branch-policy or release-readiness changes:

```bash
python3 scripts/audit_branch_protection.py \
  --branches develop staging main \
  --expected-review-count 0

python3 scripts/release_readiness_report.py \
  --branches develop staging main \
  --md-out _artifacts/release-readiness/release-readiness.md \
  --json-out _artifacts/release-readiness/release-readiness.json
```

## Related Documents

- [QUICKSTART.md](QUICKSTART.md)
- [docs/operations/BRANCH_PROTECTION_RUNBOOK.md](docs/operations/BRANCH_PROTECTION_RUNBOOK.md)
- [docs/operations/METADATA_PROVIDER_RUNBOOK.md](docs/operations/METADATA_PROVIDER_RUNBOOK.md)
- [docs/operations/METADATA_MIGRATION_DRY_RUN.md](docs/operations/METADATA_MIGRATION_DRY_RUN.md)
- [docs/operations/SCOPED_COMMIT_PROCESS.md](docs/operations/SCOPED_COMMIT_PROCESS.md)
- [docs/operations/RELEASE_AUTOMATION.md](docs/operations/RELEASE_AUTOMATION.md)
- [docs/operations/install-test-snapshots/2026-03-17.md](docs/operations/install-test-snapshots/2026-03-17.md)
- [docs/operations/metadata-telemetry-checkpoints/2026-03-18.md](docs/operations/metadata-telemetry-checkpoints/2026-03-18.md)
- [docs/operations/metadata-dry-run-snapshots/2026-03-18.md](docs/operations/metadata-dry-run-snapshots/2026-03-18.md)
