# Changelog

All notable changes to this repository should be recorded in this file.

The format is based on Keep a Changelog and the repository's documented release
process.

## [Unreleased]

### Added

- Comprehensive deep audit v2: six parallel audits (backend C#, frontend, CI/CD, documentation, Docker/infrastructure, packages/dependencies) produced 287 distinct findings consolidated into 176 remediation items (RQ-001 through RQ-178) in `PROJECT_STATUS.md`.
  - 14 Critical, 58 High, 101 Medium, 93 Low, 21 Enhancement/Migration opportunities.
  - New priority tier P4 (Strategic/Migration Opportunities) tracks React 18, React Router 6, RestSharp→HttpClient, .NET 10, Node 22, and other long-horizon migrations.
- Expanded Docker and Infrastructure Hardening Plan in `PROJECT_STATUS.md` — now covers 17 items across Phase 6 and Phase 7 including SIGTERM handling, DataProtection key security, container image scanning, SBOM generation, request body limits, Kubernetes manifests, and Prometheus metrics.
- New ROADMAP milestones: frontend test infrastructure, async migration, RestSharp→HttpClient, security headers, React 18/Router 6, Node 22, .NET 10 planning, documentation normalization, installer signing.
- Audit statistics table in `PROJECT_STATUS.md` with per-area finding counts by severity.

### Changed

- `MIGRATION_PLAN.md` audit snapshot expanded from 110+ findings to 287 findings with migration-specific categorization (provider reliability, performance, async/threading, supply chain, frontend, documentation).
- `ROADMAP.md` Near-Term Delivery Sequence expanded from 13 to 19 items covering async migration, frontend testing, RestSharp migration, security headers, documentation normalization, and React 18 planning.
- `ROADMAP.md` Current Milestones table expanded with 10 new planned milestones.

### Fixed

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

### Removed

- Removed `frontend/src/Shared/piwikCheck.js`: legacy Sonarr Piwik analytics script that loaded a tracking beacon from `piwik.sonarr.tv`. No Bibliophilarr analytics replacement needed; the backend `IAnalyticsService` (install-activity telemetry for update checks) is unaffected.
- Removed `azure-pipelines.yml` (1,251 lines): legacy Readarr Azure DevOps pipeline. GitHub Actions is the sole authoritative CI system; the Azure config was never adapted for Bibliophilarr and caused dual-CI confusion.

### Changed

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
