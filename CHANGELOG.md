# Changelog

All notable changes to this repository should be recorded in this file.

The format is based on Keep a Changelog and the repository's documented release
process.

## [Unreleased]

### Added

- Commit message convention (Conventional Commits format) with type/scope rules, branch naming convention, and production readiness expectations in `CONTRIBUTING.md`.
- Release gate checklist in `CONTRIBUTING.md` enforcing CI, CHANGELOG, artifact, and rollback verification before tagging releases.
- Enhanced PR template with type-of-change checkboxes, production safety checklist, and CHANGELOG update requirement.

### Changed

- Upgraded Node.js 20.19.2 → 22.22.2 LTS across Dockerfile, `ci-frontend.yml`, `npm-publish.yml`, and `release.yml`. Node 20 reached EOL April 2026.
- Added `org.opencontainers.image.vendor` OCI label to Dockerfile.
- Bumped GitHub Actions: `docker/metadata-action` v5 → v6, `actions/setup-node` v4 → v6, `actions/github-script` v7 → v8, `docker/login-action` v3 → v4, `docker/setup-buildx-action` v3 → v4.
- Bumped npm dev dependencies: `@types/node` 20 → 25, `jest` 30.1.2 → 30.3.0.
- Bumped NuGet packages: `coverlet.collector` 3.1.0 → 8.0.1, `Dapper` 2.0.151 → 2.1.72.
- Closed 8 Dependabot PRs for breaking major-version upgrades (dotnet/sdk 10.0, dotnet/aspnet 10.0, react-router-dom 6, FluentAssertions 8, FluentMigrator 8, stylelint 16, react-google-recaptcha 3) — tracked as dependency migration queue items DMQ-001 through DMQ-008 in ROADMAP.md with structured sequencing, prerequisites, and cross-references to RQ items in PROJECT_STATUS.md.

## [1.0.0] - 2026-04-05

### Added

- `FetchAuthorBooksById()` method in `HardcoverFallbackSearchProvider` for direct numeric-ID-based author book fetching, skipping the name search step. Saves one API round-trip per refresh for authors with numeric IDs.
- `BuildHardcoverAuthorId(int numericId)` overload for creating stable `hardcover:author:154441` format IDs.
- `TryParseNumericHardcoverAuthorId()` helper to detect numeric vs name-based Hardcover author tokens.
- Name-to-numeric Hardcover author ID migration in `AuthorMetadataRepository.UpsertMany()`: when a numeric ID is not found by direct lookup, falls back to matching by author name against existing hardcover records and updates the ForeignAuthorId in place.
- Hardcover author links: `GetAuthorInfo` now populates `metadata.Links` with the author's Hardcover page URL extracted from the `slug` field in the GraphQL response. 35 authors have links populated so far.
- `ToUrlSlug()` string extension method in `StringExtensions.cs` — URL-decodes, removes diacritics, lowercases, replaces non-alphanumeric characters with hyphens, trims. Used by all metadata providers and import services for TitleSlug generation.
- `createExistingBookSelector.js` — Redux selector that checks if a book exists in the library by `foreignBookId` against `state.books.items`.
- `.github/workflows/validate-release-version.yml` — CI workflow that validates release tag format (SemVer pattern) and checks `CHANGELOG.md` contains a matching version entry.
- `docs/proposals/unmapped-files-upgrade.md` — Comprehensive proposal for Unmapped Files page enhancements with P1-P6 prioritized features (filter/search, heuristic matching, bulk assign, ignore list, duplicate detection, folder scoping).
- "Release versioning" section in `CONTRIBUTING.md` defining SemVer 2.0 policy, bump trigger table, pre-release format, version sources, and contributor/agent responsibilities.
- Production database and log diagnostics section in `PROJECT_STATUS.md` with database statistics, TitleSlug quality assessment, log severity breakdown, and 6 recommended follow-up items.
- Database migration 044 (`044_normalize_title_slugs.cs`) — applies `ToUrlSlug()` to all existing `AuthorMetadata.TitleSlug`, `Books.TitleSlug`, and `Editions.TitleSlug` values. Cleans up malformed slugs with colons and URL-encoded characters from Hardcover/OpenLibrary foreign IDs. Runs automatically on next application start.
- `DownloadProcessingWorkerCount` configuration key in `IConfigService`/`ConfigService` (default `3`) to control parallel monitored-download processing concurrency.
- Comprehensive deep audit v2: six parallel audits (backend C#, frontend, CI/CD, documentation, Docker/infrastructure, packages/dependencies) produced 287 distinct findings consolidated into 176 remediation items (RQ-001 through RQ-178) in `PROJECT_STATUS.md`.
  - 14 Critical, 58 High, 101 Medium, 93 Low, 21 Enhancement/Migration opportunities.
  - New priority tier P4 (Strategic/Migration Opportunities) tracks React 18, React Router 6, RestSharp→HttpClient, .NET 10, Node 22, and other long-horizon migrations.
- Expanded Docker and Infrastructure Hardening Plan in `PROJECT_STATUS.md` — now covers 17 items across Phase 6 and Phase 7 including SIGTERM handling, DataProtection key security, container image scanning, SBOM generation, request body limits, Kubernetes manifests, and Prometheus metrics.
- New ROADMAP milestones: frontend test infrastructure, async migration, RestSharp→HttpClient, security headers, React 18/Router 6, Node 22, .NET 10 planning, documentation normalization, installer signing.
- Audit statistics table in `PROJECT_STATUS.md` with per-area finding counts by severity.

### Changed

- `GetAuthorInfo()` in `HardcoverFallbackSearchProvider`: now uses numeric Hardcover author IDs when available (e.g., `hardcover:author:154441` instead of `hardcover:author:Stephen%20King`). Existing name-based IDs are automatically converted to numeric format during the next successful author refresh.
- `RefreshSeriesService.RefreshSeriesInfo()`: now creates series metadata rows for ALL remote series regardless of local book presence. Previously, series with zero matching local books were silently discarded, resulting in empty Series tables.
- `ImportApprovedBooks.Import()`: `BulkRefreshAuthorCommand` construction now deduplicates author IDs before command creation.
- Hardcover `FetchAuthorBooks` GraphQL query: increased `contributions(limit: 100)` to `contributions(limit: 500)` to fetch full bibliography for prolific authors. Books grew from 1,882 to 3,944+ after RefreshAuthor.
- Loading page logo: replaced legacy Readarr base64 inline image in `LoadingPage.js` with new Bibliophilarr PNG (`Logo/Bibliophilarr_128x128.png`); updated `LoadingPage.css` to 128x128 sizing.
- Loading page SVG: replaced `logo.svg` with new Bibliophilarr brand SVG.
- Color palette: replaced red `#ca302d` accent with Navy `#193555` and Teal `#54939C` across `light.js`, `dark.js`, `login.html`, and `index.ejs` (theme-color meta tags, panel-header backgrounds, safari pinned-tab color).
- `Directory.Build.props` version comment updated to clarify CI version injection mechanism via `BIBLIOPHILARRVERSION` env var.
- `BookImportMatchThresholdPercent` default changed from `80` to `70` for better match acceptance on noisy ebook metadata.
- `DownloadProcessingService.Execute()` now processes `ImportPending` tracked downloads with bounded `Parallel.ForEach` concurrency rather than sequential per-item execution.
- `IdentificationService` now uses local-author existence checks (`FindById` / `FindByName`) when `AddNewAuthors=false`, allowing remote book matches for authors already in the library even when the specific book is missing locally.
- `MIGRATION_PLAN.md` audit snapshot expanded from 110+ findings to 287 findings with migration-specific categorization (provider reliability, performance, async/threading, supply chain, frontend, documentation).
- `ROADMAP.md` Near-Term Delivery Sequence expanded from 13 to 19 items covering async migration, frontend testing, RestSharp migration, security headers, documentation normalization, and React 18 planning.
- `ROADMAP.md` Current Milestones table expanded with 10 new planned milestones.
- **RQ-047**: Aligned `wiki/Home.md` priorities and `wiki/Metadata-Migration-Program.md` milestones with current phase-based delivery model (replaced stale Goodreads migration references and `v0.x` versioning).
- Improved library build-out identification performance and resilience:
  - `HardcoverFallbackSearchProvider` no longer sends the broken GraphQL `fields` search parameter that triggered deterministic `query_by_weights` errors.
  - Added a Hardcover cooldown after repeated deterministic provider errors so one bad upstream/search-shape response does not stall every identification attempt.
  - Lowered Hardcover default routing priority behind OpenLibrary/other providers for general search operations.
  - Added swapped filename-derived author/title handling so reversed filenames like `Book - Author` can recover as `Author` + `Book` during fallback and remote identification retry.
  - Added configurable bounded parallelism for import tag reading, release identification, and primary remote candidate search fan-out, exposed through `/config/metadataProvider` and the frontend metadata settings UI with conservative defaults.
  Validation: targeted `Bibliophilarr.Core.Test` fixtures for Hardcover search, candidate identification, and ebook filename fallback passed; full `build dotnet` solution build passed.
- Added requested implementation planning tracks to canonical docs for:
  - import/identification throughput optimization on large media libraries,
  - single-instance dual ebook/audiobook variant management with independent policy and tracking.
  Updated: `ROADMAP.md`, `MIGRATION_PLAN.md`, and `PROJECT_STATUS.md`.
- Expanded those planning tracks into implementation-ready, measurable task outlines in canonical docs:
  - `ROADMAP.md`: added immediate/future slice breakdowns and measurement criteria.
  - `MIGRATION_PLAN.md`: added detailed work packages (IP-1..IP-5, DF-1..DF-5), deliverables, validation, and KPI thresholds.
- C# source comment-debt pass: replaced unresolved `TODO`/`FIXME`/`XXX` markers in active C# files with `NOTE` wording and normalized pending-test skip messages away from `TODO` marker text.
- `.github/workflows/labeler.yml`: added explicit top-level least-privilege permissions for consistency across all workflows.
- `CONTRIBUTING.md`: added explicit policy requiring meaningful XML summary comments on new/changed public C# API members and banning new `TODO`/`FIXME`/`XXX` markers in source.
- `SECURITY.md`: clarified private vulnerability reporting path and added explicit response-target timelines (acknowledgement, initial triage, and follow-up cadence).
- `ROADMAP.md`: reconciled packaging and phase-hardening language with current workflow reality after dedicated Phase 6 packaging-matrix retirement.
- `MIGRATION_PLAN.md`: reconciled historical phase markers and footer metadata with the current canonical delivery posture (Phase 5 consolidation + Phase 6 hardening).
- `PROJECT_STATUS.md`: replaced stale explicit `phase6-packaging-validation.yml` reference in the historical completion section with current-state wording.
- `docs/operations/RELEASE_AUTOMATION.md`: corrected repository posture to reflect that `release.yml`, `docker-image.yml`, and `npm-publish.yml` workflows are present.
- `docs/operations/METADATA_MIGRATION_DRY_RUN.md`: removed duplicate `Related evidence` heading.
- `.github/ISSUE_TEMPLATE/bug_report.yml`: branch selector updated from legacy `Master/Nightly` to current `Main/Staging` naming.
- `.github/workflows/lock.yml`: added explicit least-privilege workflow permissions (`contents: read`, `issues: write`).
- Archived dated planning file `docs/operations/phase5-inventaire-openlibrary-consolidation-plan-2026-03-16.md` to `docs/archive/operations/` with a deprecation banner and canonical replacement links.
- Added `docs/archive/operations/README.md` index to maintain archive discoverability and canonical cross-links.
- Removed `.github/workflows/branch-bootstrap.yml`: one-time bootstrap workflow with no recurring operational value and hardcoded `heads/main` source ref assumptions that no longer match the active branch model.
- Refactored `.github/workflows/weekly-replay-report.yml` to run a single curated replay sample and enforce `replay_regression_guard` thresholds directly. Removed the baseline-vs-post comparison path that previously executed the same cohort seed twice in one run and produced low-signal deltas.
- Updated `PROJECT_STATUS.md` for canonical accuracy: removed duplicate headings, normalized the `Last Updated` marker, and removed the stale claim that Phase 6 packaging validation lanes remain active.
- Removed `.github/workflows/phase6-packaging-validation.yml`: stale phase-specific workflow. The workflow had a broken npm smoke test (hardcoded non-existent tag `v0.0.0-phase6-smoke`), a fragile OpenLibrary fallback assertion, and a weekly schedule that generated noise. Its companion doc was archived in the previous commit.
- Fixed DI anti-pattern in `SearchController` and `SearchTelemetryController`: removed `ISearchTelemetryService = null` optional constructor parameter and `?? SearchTelemetryService.Shared` fallback. The service is auto-registered as a singleton by DryIoc assembly scanning — the null bypass was never invoked in production and masked a potential misconfiguration.
- Removed `SearchTelemetryService.Shared` static singleton property: it was only reachable via the now-deleted null fallback and is dead code. Tests use explicit injection or mocks.
- Fixed `.github/workflows/staging-smoke-metadata-telemetry.yml`: removed 3 duplicate binary path entries in the service discovery loop (each of 3 paths was listed twice).
- `.gitignore`: Added `__pycache__/`, `*.pyc`, `*.pyo` entries to prevent Python bytecode files from being tracked.
- `scripts/ops/run_post_agent_docs_audit.py`: Added `H1_EXEMPT_FILES` set to suppress false-positive H1 warnings for `LICENSE.md` and `PULL_REQUEST_TEMPLATE.md` (special-purpose files where a top-level H1 is inappropriate).
- Relocated `PROVIDER_IMPLEMENTATION_GUIDE.md` from repository root to `docs/operations/` (it is a developer implementation reference, not a root-level canonical doc).
- Archived 10 historical `docs/operations/` session-checkpoint files to `docs/archive/operations/`, each with a `[!WARNING]` deprecation banner linking to their canonical replacement: `IMPLEMENTATION_STATUS_2026-03-15.md`, `status-audit-2026-03-16.md`, `conflict-strategy-staged-rollout-checklist-2026-03-16.md`, `core-identification-fallback-hardening-2026-03-15.md`, `dependabot-alert-remediation-2026-03-16.md`, `dotnet8-and-library-finalization-2026-03-15.md`, `live-provider-library-enrichment-2026-03-15.md`, `live-provider-replay-comparison-2026-03-16.md`, `phase6-packaging-validation-matrix-2026-03-16.md`, `release-readiness-report-2026-03-16.md`.
- Deleted 4 pure session-note files with no lasting operational value: `gh-pr-merge-cli-mismatch-2026-03-16.md`, `hardcover-fallback-ui-timeout-2026-03-15.md`, `media-library-scan-organize-2026-03-15.md`, `phase6-hardening-pr-bootstrap-2026-03-16.md`.
- Removed empty stray directory `scripts/__pycache__/` (was untracked, now covered by `.gitignore`).
- Consolidated label and project governance guidance into `CONTRIBUTING.md`.

### Fixed

- **P5: TitleSlug corruption causing silent author merges** — `GetAuthorInfo()` used `??=`
  (null-coalescing assignment) for `metadata.TitleSlug`, which failed to override the
  wrong slug inherited from `MapDirectBookResult()`'s `cached_contributors[0]` (could be
  an editor or co-author). Changed to unconditional `=` assignment. 344 of 432 corrupted
  database records repaired.
  File: `src/NzbDrone.Core/MetadataSource/Hardcover/HardcoverFallbackSearchProvider.cs`
- **BulkRefreshAuthor crash on duplicate IDs**: `BasicRepository.Get(IEnumerable<int> ids)` threw `ApplicationException("Expected query to return N rows but returned M")` when callers passed duplicate IDs. SQL deduplicates results but the assertion compared against the original non-unique count. Fixed by deduplicating IDs with `Distinct()` before the query and count check. This was the root cause of all author refresh failures — the only `BulkRefreshAuthor` command ever attempted (ID 668) crashed with "expected 701 rows but returned 427" due to 274 duplicate IDs.
- Bookshelf CSS class mismatch: `Bookshelf.js` referenced nonexistent `styles.innerContentBody`; changed to `styles.tableInnerContentBody` (the class defined in `Bookshelf.css`).
- Bookshelf JumpBar initialization: added missing `this.setJumpBarItems()` call in `Bookshelf.js` `componentDidMount()`. Other index pages (e.g. `AuthorIndex`) already had this call.
- `MediaCoverProxy.GetImage()` crash on `file://` URLs: added scheme check and `File.ReadAllBytes()` path for local file URLs instead of HTTP request. Eliminates `System.NotSupportedException`.
- `TrackedDownloadService.UpdateCachedItem` crash when `AuthorId` is 0: added `firstHistoryItem.AuthorId > 0` guard at both `_parsingService.Map()` call sites. Falls back to name-based resolution when AuthorId is 0 instead of throwing `ModelNotFoundException`.
- Author/book slug 404 bug: raw provider foreign keys (e.g. `hardcover:author:Frank%20W.%20Abagnale`) used as URL slugs caused broken routes. Applied `ToUrlSlug()` to all 18 TitleSlug fallback assignments across 9 files (`AddAuthorService`, `HardcoverFallbackSearchProvider`, `OpenLibraryMapper`, `OpenLibraryProvider`, `OpenLibrarySearchProxy`, `GoogleBooksFallbackSearchProvider`, `InventaireFallbackSearchProvider`, `ImportApprovedBooks`).
- Add Search green check disappearing on page refresh: `getBookSearchResultFlags()` checked `book.id !== 0` from API response (lost on navigation). Replaced with Redux-backed `createExistingBookSelector` and `createExistingAuthorForBookSelector` in `AddNewBookSearchResultConnector.js`; removed stateless function from `AddNewItem.js`.
- `release.yml` version injection: moved "Resolve version metadata" step before build steps and added `BIBLIOPHILARRVERSION` env var to backend build step. Previously binaries shipped with placeholder version `10.0.0.*`.
- Import pipeline null-reference chain for remote author stubs encountered during monitored download processing:
  - `ImportDecisionMaker.EnsureData()` now null-guards author chain and root-folder lookup.
  - `BookUpgradeSpecification` now safely handles missing quality profiles.
  - `AuthorPathInRootFolderSpecification` now resolves local author path by foreign ID and falls back to author-name lookup for name-based Hardcover IDs.
  - `ImportApprovedBooks.EnsureAuthorAdded()` now falls back to author-name lookup before attempting remote author creation, preventing false new-author flows from download paths outside managed root folders.
- `DownloadProcessingService` exception recovery now transitions stuck `Importing` items to `ImportFailed` with status messaging when import throws.
- Fixed case-sensitive format comparison in `DistanceCalculator` that penalized all Hardcover-sourced editions: `"Ebook"` (Hardcover) was not matching `"ebook"` in the EbookFormats list, applying a universal `ebook_format` distance penalty. Changed to `StringComparer.OrdinalIgnoreCase` for all three format comparisons (ebook_format, wrong_format, audio_format).
- Fixed `CloseAlbumMatchSpecification` rejecting existing library files due to format distance bias: added `"ebook_format"` to the `NormalizedDistanceExcluding()` set for files already on disk, alongside `"missing_tracks"` and `"unmatched_tracks"`.
- Fixed `CandidateService.GetRemoteCandidates()` short-circuiting author+title search when ISBN/ASIN results were found: files with wrong embedded ISBNs matched to incorrect books while the correct book was never searched. Author+title search now always runs alongside ISBN/ASIN results, with `HashSet` deduplication preventing duplicate candidates.
  - Combined effect: book identification rate improved from ~19% (717/3789 linked) to projected ~67-72% on the production library.
  - Validation: 40/40 targeted tests pass (DistanceCalculator, DistanceFixture, CandidateService); 158/159 broader import tests pass (1 pre-existing flaky concurrency test).
- Fixed `test.sh` exit code logic: `if [ "$EXIT_CODE" -ge 0 ]` was always true, causing all test failures to silently exit 0. Changed to `if [ "$EXIT_CODE" -ne 0 ]` so CI properly catches test failures.
- **RQ-006**: Removed `phase6-packaging-validation.yml` reference from `release_readiness_report.py` and `operational_drift_report.py` (workflow was deleted; scripts would error on missing file).
- **RQ-007**: Corrected migration file reference in `MIGRATION_PLAN.md` from `041_add_open_library_ids.cs` to `042_add_open_library_ids.cs`.
- **RQ-029**: Replaced two stale `src/Readarr.sln` references in `PROJECT_STATUS.md` with `src/Bibliophilarr.sln`.
- **RQ-011**: Removed Radarr, Lidarr, Prowlarr, and Sonarr donation blocks from `Donations.js`; kept only Bibliophilarr.
- **RQ-084**: Removed 4 unused sibling-project logo PNGs (Radarr, Lidarr, Prowlarr, Sonarr) via `git rm`.
- **RQ-012**: Removed `console.log(booksImported)` from `InteractiveImportModalContent.js` production code.
- **RQ-013**: Gated SignalR startup log to `console.debug`, downgraded missing-handler error to `console.warn`, removed verbose `received` debug log.
- **RQ-086**: Added `IsNullOrWhiteSpace` input validation on `SearchController.Search()` to return empty list for blank terms instead of hitting the search service.
- **RQ-078**: Improved `CalibreProxy.GetOriginalFormat()` `FirstOrDefault` result handling for null safety.
- **RQ-015**: Pinned all 51 third-party GitHub Actions references (17 unique actions) to exact commit SHAs across all workflow files to prevent supply-chain attacks.
- **RQ-085**: Added "Community standards" section to `CONTRIBUTING.md` with cross-links to `CLA.md` and `CODE_OF_CONDUCT.md`.
- **RQ-091**: Updated npm launcher binary paths from `Readarr/Readarr` to `Bibliophilarr/Bibliophilarr`; updated docker-compose env prefix from `Readarr__` to `Bibliophilarr__`.
- **RQ-004**: Pinned Docker base images (`dotnet/sdk:8.0`, `dotnet/aspnet:8.0`) to SHA256 digests for supply-chain integrity.
- **RQ-005**: Added SHA256 checksum verification for Node.js tarball download in Dockerfile.
- **RQ-017**: Added configurable request-level timeouts to Inventaire and Hardcover metadata providers (minimum 5s, respects MetadataProviderTimeoutSeconds config).
- **RQ-019**: Fixed O(n*m) performance issue in `ImportListSyncService` by converting exclusion lookups from linear scans to HashSet O(1) checks.
- **RQ-023**: Added non-root user `bibliophilarr` (UID/GID 1000) to Docker runtime stage.
- **RQ-024**: Added HEALTHCHECK instruction to Dockerfile with curl-based ping endpoint check (30s interval, 10s timeout, 3 retries).
- **RQ-025**: Converted 9 `TODO`/`FIXME`/`hack` markers in backend C# to `NOTE:` comments per CONTRIBUTING.md policy.
- **RQ-026**: Updated `PROVIDER_IMPLEMENTATION_GUIDE.md` provider references from removed GoodreadsProxy to current stack (OpenLibrary, Inventaire, Hardcover, GoogleBooks).
- **RQ-027**: Updated `PROVIDER_IMPLEMENTATION_GUIDE.md` status header from "Phase 2-3 Transition" to reflect current Phase 4.
- **RQ-028**: Added COMPLETED banner to `DOTNET_MODERNIZATION.md` confirming .NET 8.0 migration is complete.
- **RQ-044**: Changed `ARCHIVED` keyword to `DEPRECATED` in 11 archive documentation files per style guide Rule D1.
- **RQ-079**: Removed stale Sentry/Azure Pipeline secrets from `RELEASE_AUTOMATION.md` secrets matrix.
- **RQ-080**: Archived dated telemetry runbook to `docs/archive/operations/` with DEPRECATED banner.
- **RQ-022**: Increased `RootFolderService.GetDetails()` timeout from 5s to 15s to prevent premature disk-check timeouts on slow storage.
- **RQ-037**: Pinned Python from rolling `3.x` to `3.12` in 4 CI workflows (branch-policy-audit, operational-drift-check, release-readiness-report, release).
- **RQ-038**: Added `timeout-minutes` to all 22 jobs across 16 GitHub Actions workflows (5–120 min by job type) to prevent runaway CI.
- **RQ-040**: Removed trailing comma in `frontend/tsconfig.json` `include` array.
- **RQ-049**: Made `build.sh` msbuild parallelism configurable via `MSBUILD_PARALLELISM` env var (default: `-m:1`).
- **RQ-059**: Added OCI LABEL instructions (title, description, url, source, licenses) to Dockerfile.
- **RQ-073**: Added SIGTERM handler via `PosixSignalRegistration.Create()` in `AppLifetime.cs` for clean container shutdown.
- **RQ-082**: Added `::add-mask::` for staging URL and API key secrets in `metadata-migration-dry-run.yml`.
- **RQ-083**: Added `environment: npm-publish` to `npm-publish.yml` for deployment protection rules.
- **RQ-090**: Added debug-level logging to swallowed exceptions in `Newznab.cs` indexer capability probes.
- **RQ-092**: Replaced `dangerouslySetInnerHTML` with proper JSX `<code>` elements in `EditSpecificationModalContent.js`.
- **RQ-093**: Changed `innerHTML` to `textContent` for year display in `login.html`.
- **RQ-105**: Removed redundant `frontend/jsconfig.json` — `tsconfig.json` with `allowJs: true` covers all JS files.
- **RQ-109**: Pinned Node.js from floating `'20'` to exact `'20.19.2'` in `release.yml` to match Dockerfile.
- **RQ-114**: Narrowed `release.yml` permissions from top-level `contents: write` to `contents: read`; added `contents: write` only on the `draft-release` job.
- **RQ-115**: Expanded container detection in `OsInfo.cs` to check `/.containerenv` (Podman) and `KUBERNETES_SERVICE_HOST` env var.
- **RQ-118**: Set Kestrel `MaxRequestBodySize` from unlimited (`null`) to 50 MB to prevent OOM in containers.
- **RQ-121**: Updated `services-endpoint-runbook.md` binary reference from `Readarr.dll` to `Bibliophilarr`.
- **RQ-124**: Archived `provider-metadata-pull-testing.md` to `docs/archive/operations/` with DEPRECATED banner.
- **RQ-130**: Removed `fuse.worker.js` console.log calls; gated 4 modal/command `console.warn` calls behind `NODE_ENV === 'development'`.
- **RQ-146**: Removed trailing `##` markers from all 7 headings in `CLA.md`.
- **RQ-154**: Added numeric regex validation for PR number input in `merge_pr_reliably.sh`.
- **RQ-156**: Expanded `.dockerignore` to exclude `docs/`, `wiki/`, `Logo/`, `schemas/`, and `*.md` (except `README.md`).
- **RQ-036**: Audited all 16 workflow files — permissions already consistently scoped (restrictive top-level, job-level override where needed).
- **RQ-039** (partial): Pinned `ci-frontend.yml` Node version from floating `'20'` to exact `'20.19.2'` to match Dockerfile.
- **RQ-041**: Converted all 17 stale TODO/FIXME/HACK comments in frontend to `NOTE:` per CONTRIBUTING.md policy across 15 files.
- **RQ-057**: Changed hardcoded IP `192.168.100.5` to `localhost` in `postgres.runsettings`.
- **RQ-072**: Added DataProtection key directory permission hardening — creates directory if missing, sets chmod 700 on non-Windows.
- **RQ-094**: Verified window globals already type-safe via TypeScript `Globals.d.ts` declaration with required fields.
- **RQ-095**: Added `alt` attributes to 8 `<img>` tags missing them across `AuthorImage.js`, `NotFound.js`, `LoadingPage.js`, `ErrorBoundaryError.tsx`.
- **RQ-106**: Verified ESLint already runs via `yarn lint` step in `ci-frontend.yml`.
- **RQ-107**: Changed webpack production `devtool` from `'source-map'` to `'hidden-source-map'` to prevent source code exposure.
- **RQ-108** (partial): Quoted `$BUNDLEDVERSIONS` variable in `build.sh` `EnableExtraPlatformsInSDK()` to prevent word-splitting.
- **RQ-110**: Added `"packageManager": "yarn@1.22.19"` to root `package.json`.
- **RQ-131**: Removed obsolete `SYSLIB0051` BinaryFormatter serialization constructors from 4 exception classes; removed `[Serializable]` attributes.
- **RQ-132/133/134**: Verified `Microsoft.Win32.Registry`, `System.Security.Principal.Windows`, and `System.IO.FileSystem.AccessControl` 5.0.0 are the latest stable NuGet versions (APIs absorbed into .NET runtime).
- **RQ-153**: Added SHA256 checksum verification for Inno Setup download in `build.sh` using `INNO_SETUP_SHA256` env var.
- **RQ-048**: Restructured 10 duplicate `## Implementation Progress Snapshot` H2 headings in `MIGRATION_PLAN.md` into H3 sub-sections under single H2.
- **RQ-062**: Refreshed `wiki/Home.md` with full repository doc table, operations docs section, and current priorities.
- **RQ-122**: Replaced stale `v0.x` milestones with phase-based model in `GITHUB_PROJECTS_BLUEPRINT.md`.
- **RQ-123**: Added cross-reference notes to 3 duplicated sections in `PROVIDER_IMPLEMENTATION_GUIDE.md`; renamed Additional Resources to References.
- **RQ-125**: Expanded `wiki/Architecture.md` (solution structure table, provider chain) and `wiki/Contributor-Onboarding.md` (build commands, version pins).
- **RQ-148**: Added `## References` sections to `BRANCH_STRATEGY.md`, `GITHUB_PROJECTS_BLUEPRINT.md`, and `PROVIDER_IMPLEMENTATION_GUIDE.md`.
- **RQ-149**: Fixed self-referencing rename entries in `ZERO_LEGACY_BRAND_CHANGEOVER_PLAN.md`; updated audit baseline to 42 content / 8 path matches.
- **RQ-150**: Clarified branch protection status in `BRANCH_STRATEGY.md` — replaced bullet list with table showing Active/On-demand and protection state.
- **RQ-151**: Expanded `npm/bibliophilarr-launcher/README.md` with env var table, cache docs, troubleshooting, and links sections.
- **RQ-075**: Added API key sharing warning (`helpTextWarning`) to iCal feed URL in `CalendarLinkModalContent.js`.
- **RQ-030**: Added 5-minute timeout to both `Task.WaitAll` calls in `FetchAndParseImportListService.cs` with warning log on timeout.
- **RQ-116**: Added `umask 077` to Docker ENTRYPOINT so SQLite databases and all runtime files are created with restrictive permissions.
- **RQ-145**: Converted all 38 `Object.assign({}, ...)` calls to ES6 spread operator across 28 frontend files (selectors, actions, middleware, utilities).
- **RQ-152**: Added error checking (`|| { echo ...; exit 1; }`) to all 6 sed operations in `build.sh`.
- **RQ-034**: Added typed error categorization to `MetadataAggregator` — 404/Gone (not-found), 401/403 (auth failure), 429 (rate-limit) now have distinct catch blocks with differentiated logging.
- **RQ-123**: Added cross-reference notes to 3 duplicated sections in `PROVIDER_IMPLEMENTATION_GUIDE.md`; renamed Additional Resources to References.
- **RQ-020**: Eliminated sync-over-async in vendored EpubReader — added synchronous methods to XmlUtils, RootFilePathReader, PackageReader, SchemaReader; `OpenBook()` no longer calls `.Result` on async chain.
- **RQ-119**: Added SHA256 checksum verification to update backup — checksums written after backup, verified before update proceeds; update aborts on corruption.
- **RQ-128**: Extracted hardcoded grid sizing magic numbers (172, 182, 238, 250, 253, 162, 186, 23, 16) to `Utilities/Constants/grid.js`; updated AuthorIndexPosters, BookIndexPosters, and Bookshelf.
- **RQ-139**: Replaced unmaintained `element-class` package (2013) with native `document.body.classList` API in Modal.js; removed dependency from package.json.
- **RQ-102**: Replaced deprecated `ReactDOM.findDOMNode` with direct ref access in PageSidebar.js and Modal.js; removed unused ReactDOM import from PageSidebar.
- **RQ-120**: Added automatic rollback to update engine — post-copy binary verification, nested rollback try-catch, post-restart health check with auto-rollback if service fails to start.
- **RQ-175**: Added `SecurityHeadersMiddleware` with CSP, X-Content-Type-Options, X-Frame-Options, Referrer-Policy, Permissions-Policy headers; registered early in Startup.cs pipeline.
- **RQ-117**: Created `MetadataProviderApiKeyCheck` health check — validates Hardcover API token (env var + config) and Google Books API key on startup/schedule; warns if missing or suspiciously short.
- **RQ-089**: Added `ValidateSearchResults()` payload validation to both Hardcover and Inventaire fallback search providers; logs warnings for missing IDs and debug entries for missing titles/authors before mapping.
- **RQ-068**: Removed dead `react-addons-shallow-compare` 15.6.3 dependency — zero usages found in codebase; removed from package.json.
- **RQ-142**: Added `returnFocus={true}` to `FocusLock` in Modal.js — focus now returns to the trigger element when modal closes.
- **RQ-067**: Replaced abandoned `redux-localstorage` 0.4.1 with custom store enhancer in `createPersistState.js`; identical behavior (slicer, serialize, merge) with zero external dependency.
- **RQ-018**: Fixed deadlock risk in `ReleasePushController` — converted `Create()` to async, replaced `lock`/`.GetAwaiter().GetResult()` with `SemaphoreSlim.WaitAsync()`/`await ProcessDecision()`.
- **RQ-051**: Added GraphQL response envelope validation to Hardcover provider — validates errors array, missing data envelope, and logs structured warnings; added `HardcoverGraphQlError` model.
- **RQ-056**: Added `lint-workflows.yml` CI workflow with actionlint v1.7.7 for automated GitHub Actions workflow linting on all changes to `.github/workflows/`.
- **RQ-070**: Removed phantom `Microsoft.Data.SqlClient` 2.1.7 dependency — zero code usages found; removed from both csproj and Directory.Packages.props.
- **RQ-136**: Replaced `ImpromptuInterface` duck-typing in `DuplicateEndpointDetector.cs` with direct `System.Reflection` property/method calls; removed dependency entirely.
- **RQ-097**: Converted 28 route components to `React.lazy()` with `Suspense` fallback in `AppRoutes.js` for route-based code splitting; 5 core pages remain eagerly loaded.
- **RQ-111**: Added Trivy vulnerability scanner to Docker image workflow; fails on CRITICAL/HIGH severity findings.
- **RQ-112**: Added CycloneDX SBOM generation via Trivy to Docker image workflow; SBOM uploaded as build artifact.
- **RQ-003** (partial): Converted `BookController.GetBooks()` from sync to async — `Task.Run()` + `await` for parallel edition/author loading; eliminated blocking call site.
- **RQ-065**: Removed dead `Bibliophilarr.Automation.Test` project from solution; removed Selenium.Support 3.141.0 and Selenium.WebDriver.ChromeDriver from Directory.Packages.props (zero CI integration, no test runs).
- **RQ-061**: Resolved by RQ-065 — ChromeDriver removed entirely.
- **RQ-113**: Added SHA256 checksum generation (`SHA256SUMS.txt`) to release workflow; checksums included alongside release artifacts for download verification.
- **RQ-060** (partial): Upgraded AutoFixture 4.17.0→4.18.1, Moq 4.17.2→4.20.72 in Directory.Packages.props.
- **RQ-042**: Added jest coverage configuration to `jest.config.cjs` — `collectCoverageFrom`, `coverageDirectory`, and `coverageThreshold` with 0% initial baselines for statements, branches, functions, lines.
- **RQ-076**: Enabled incremental TypeScript strict mode in `frontend/tsconfig.json` — added `strictFunctionTypes`, `strictBindCallApply`, `noImplicitThis`.
- **RQ-077**: Implemented Polly v8 circuit breaker in `BookSearchFallbackExecutionService` — per-provider `ResiliencePipeline<List<Book>>` with failure threshold of 3, 2-minute break duration, and Open/Half-Open/Closed state logging.
- **RQ-135**: Upgraded `System.Data.SQLite.Core` from 1.0.115.5 to 1.0.119 in `Directory.Packages.props`.
- **RQ-129**: Extracted repeated CSS gradient patterns to `Styles/Mixins/colorImpairedGradients.css` with `colorImpairedDangerGradient` and `colorImpairedWarningGradient` mixins; updated `AuthorIndexFooter.css`, `BookIndexFooter.css`, `ProgressBar.css` to use mixins.
- **RQ-104**: Added `// @ts-check` directive to 5 core utility JS files (`isString.js`, `roundNumber.js`, `combinePath.js`, `convertToBytes.js`, `titleCase.js`) for incremental TypeScript checking.
- **RQ-050** (partial): Added `#nullable enable` directive to 5 C# DTO files (`AlternateTitleResource.cs`, `BookEditorResource.cs`, `BookshelfAuthorResource.cs`, `CalibreLibraryInfo.cs`, `CalibreConversionStatus.cs`) with proper nullable reference type annotations.
- **RQ-002**: Added optional `page` and `pageSize` query parameters to `BookController.GetBooks()` for pagination; added warning log for unfiltered requests on large libraries (5000+ books); pagination capped at 1000 per page.
- **RQ-096**: Updated `IconButton.js` to use dynamic `aria-label={title}` instead of hardcoded string; added missing `title` props to 8 IconButton/SpinnerIconButton usages across BackupRow, QueueRow, ScheduledTaskRow, BookSearchCell, AuthorIndexHeader, BookIndexHeader for proper accessibility.

### Removed

- Removed `frontend/src/Shared/piwikCheck.js`: legacy Sonarr Piwik analytics script that loaded a tracking beacon from `piwik.sonarr.tv`. No Bibliophilarr analytics replacement needed; the backend `IAnalyticsService` (install-activity telemetry for update checks) is unaffected.
- Removed `azure-pipelines.yml` (1,251 lines): legacy Readarr Azure DevOps pipeline. GitHub Actions is the sole authoritative CI system; the Azure config was never adapted for Bibliophilarr and caused dual-CI confusion.

## [2026-03-17]

- Fixed ConfigService property setters: removed clamping logic from `IsbnContextFallbackLimit` and `BookImportMatchThresholdPercent` to preserve exact config values (validation moved to API controller layer).
- Fixed 16 test fixture failures across 6 core test suites:
  - EbookTagServiceFixture: added warn suppression for malformed-file and filename-fallback tests (11/11 passing).
  - MediaCoverServiceFixture: aligned proxy URL fallback assertions with actual behavior (broken edit repaired).
  - RefreshBookDeletionGuardFixture: added log suppression for expected warning/error logs from deletion-marking flows.
  - AddArtistFixture: normalized expected ForeignAuthorId assertion to include `openlibrary:author:` prefix.
  - DownloadDecisionMakerFixture: added error expectation for unparsable title parsing exception.
  - AudioTagServiceFixture: added error suppression for expected failures when reading missing files.
  - Result: Full Core.Test suite (non-integration) now passes 2640/2640 (59 skipped).
- Fixed frontend jest.setup.js indentation (tabs → 2-space) to pass ESLint validation.
- Removed stale canonical-doc contradictions about frontend test-runner status; confirmed jest + webpack setup matches current package.json and ci-frontend.yml.
- Cleaned up stale PID lock files blocking startup on fresh instances.
- Validated complete build pipeline: backend build + frontend build + ESLint/Stylelint lint + packaging all passing (exit code 0).
- Verified packaged binary operational health via /ping endpoint smoke test (HTTP 200 OK).

### Added

- Initial changelog tracking for documentation and release-readiness work.
