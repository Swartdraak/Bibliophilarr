# Project Status Summary

**Last Updated**: March 20, 2026  
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
- Phase 6 packaging validation is green on both `develop` and `staging` across the `binary`, `docker`, and `npm` lanes.
- Release-readiness and branch-policy audit automation are available for scheduled and manual execution.

## Latest Delivery Update

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
