# Bibliophilarr Roadmap

**Last Updated**: April 5, 2026 (Node 22 migration, change management codification, Docker/CI/branding milestones completed)

This roadmap reflects the repository's actual delivery posture. Bibliophilarr is no longer in a planning-only state. The project is operating in Phase 5 consolidation with Phase 6 hardening active, while provider migration work continues incrementally on the active delivery lanes.

## Current Delivery Posture

- Current phase: Phase 5 consolidation with Phase 6 hardening active.
- Active delivery lanes: `develop` and `staging`.
- Default branch posture: `main` is the default branch, release-entry branch, and the source for tagged releases.
- Packaging scope: v1.0.0 released with binary builds (Linux x64, macOS ARM64, Windows x64), Docker image on GHCR, and npm launcher on npmjs.org.
- Actions token posture: branch-policy and readiness workflows remain report-only when GitHub Actions integration tokens cannot read admin or Dependabot APIs.

## Delivery Lanes

| Branch | Purpose | Required outcomes |
|---|---|---|
| `develop` | Active integration lane for metadata migration and operational hardening slices | `build-test`, `Markdown lint`, `triage`, smoke telemetry |
| `staging` | Pre-release validation lane mirroring `develop` with tighter release-readiness scrutiny | same required contexts as `develop`, plus current readiness/drift report evidence |
| `main` | Default branch for releases, operator runbooks, readiness reporting, and branch-policy auditing | `build-test`, `Markdown lint`, `triage`, smoke telemetry, successful readiness and branch-policy report runs |

## Phase Summary

### Phase 1 to Phase 4 foundation

These phases are no longer the best way to describe day-to-day execution. The core outcomes from the foundation phases now exist as ongoing program constraints rather than isolated milestones:

- migration and architecture planning are documented in [MIGRATION_PLAN.md](MIGRATION_PLAN.md);
- contributor and operator workflows are documented in [QUICKSTART.md](QUICKSTART.md) and [PROJECT_STATUS.md](PROJECT_STATUS.md);
- provider abstraction and metadata-correctness work continue as incremental slices inside `develop` and `staging` rather than a separate big-bang implementation phase;
- release safety, rollback, and observability now drive prioritization for migration work.

### Phase 5 consolidation

Phase 5 is the current program baseline.

Completed or stable:

- protected branch parity across `develop`, `staging`, and `main`;
- required check emission hardened so protected branches consistently receive required contexts;
- release-readiness reporting and branch-policy auditing available for scheduled and manual execution;
- main-compatible smoke validation added so the required smoke context can execute against legacy and current branch layouts;
- operator runbooks refreshed for branch protection, readiness, and merge reliability.

In progress:

- continue migration-safe provider work through `develop` and `staging` without destabilizing release lanes;
- keep branch drift observable and bounded as operational automation expands;
- reduce security drift with lockfile-backed remediation slices.

Phase 5 exit criteria:

- `develop` and `staging` remain operationally aligned with low drift;
- readiness and branch-policy reporting remain green on `main`;
- security remediation backlog is reduced to a small, explicitly tracked set of exceptions.

### Phase 6 hardening

Phase 6 is active and focused on release confidence rather than feature breadth.

Completed or validated:

- dedicated phase6 packaging-matrix workflow retired after hardening pass completion;
- manual workflow dispatch from `main` has been validated for release readiness and branch-policy audit workflows;
- permission-limited reporting mode preserves useful artifacts when Actions integration tokens receive `403 Resource not accessible by integration`.

In progress:

- release entry criteria for `main`;
- scheduled operational drift detection;
- security-drift cleanup sequencing with lockfile evidence;
- operator-facing readiness snapshots for release decisions.

Completed in current hardening slice:

- active Goodreads runtime provider implementations were removed from metadata/import-list/notification paths and replaced by OpenLibrary-oriented behavior in active runtime surfaces;
- OpenAPI/API, localization, and frontend active-text surfaces were migrated away from Goodreads naming to OpenLibrary naming;
- ebook extraction now applies confidence-aware metadata merging (container tags plus filename-derived identifiers) for EPUB, PDF, AZW3, and MOBI import paths;
- import acceptance threshold for close-match decisions is now configurable via metadata provider config with a default of 80 percent to preserve current behavior.
- book import identification quality: fixed case-sensitive format comparison in DistanceCalculator, excluded ebook_format from existing-file distance threshold in CloseAlbumMatchSpecification, and removed ISBN early-exit that prevented author+title search in CandidateService. Combined effect: identification rate improved from ~19% to projected ~67-72% on production-shaped library.
- clean-build and targeted verification coverage for author, series, book, and cover identification paths is now recorded in canonical status reporting.
- Hardcover provider logging now emits level-appropriate runtime diagnostics, and local metadata exporter scripts now expose explicit `--log-level` controls for operator troubleshooting.

Long-term cloud strategy — decided and implemented (March 2026):

- The hardcoded `services.bibliophilarr.org` dependency has been replaced by an
   optional `BIBLIOPHILARR_SERVICES_URL` environment variable.
- Default mode is **local-only**: all cloud-backed features (update checks,
   system-time drift, proxy health, server-side notifications) degrade gracefully
   to no-op when the env var is not set. No errors are logged, no registry entries
   are required.
- Operators who want those features may point to a self-hosted or community endpoint.
- There is no plan to operate a Bibliophilarr-managed cloud service. The
   local-only default is the permanent production baseline.
- Runbook: `docs/operations/services-endpoint-runbook.md`.

Phase 6 exit criteria:

- release entry criteria are documented and repeatable;
- branch drift is automatically surfaced before release work stalls;
- release-entry evidence remains stable without compatibility exceptions.

### Phase 7 release preparation

Phase 7 starts only after Phase 6 hardening gates are consistently met.

Planned entry conditions:

- latest backend/frontend/docs/smoke validation successful on both `develop` and `staging`;
- latest readiness and branch-policy report runs successful on `main`;
- remaining open dependency alerts either remediated or explicitly accepted with documented rationale;
- release workflows, runbooks, and rollback steps verified against current repository reality.

## Current Milestones

| Milestone | Target state | Current status |
|---|---|---|
| Branch protection parity | `develop`, `staging`, and `main` share required contexts and review policy | complete |
| Main dispatch validation | manual readiness and branch-policy workflows succeed on `main` | complete |
| Packaging confidence | release confidence covered by active smoke/build/readiness workflows | complete |
| Operational drift checks | scheduled drift signal exists with actionable artifacts | in progress |
| Release entry criteria | `main` release gate documented and enforced operationally | in progress |
| Security drift cleanup | open Dependabot set reduced via lockfile-backed remediation slices | in progress |
| Metadata path parity debt burn-down | add/refresh/import/identification use aligned orchestrated metadata request policy | complete |
| Identifier normalization convergence | canonical external-ID normalization is enforced at persistence boundaries | complete |
| Health-aware provider routing | provider failure streaks influence fallback order with deterministic recovery | complete |
| Conflict explainability telemetry | factor-level score breakdown is exposed for operator diagnostics | complete |
| Import throughput optimization | bulk media identification/import throughput is improved on production-shaped libraries without quality regression | in progress |
| Docker hardening | base image pinning, non-root runtime, health check, Node integrity verification, OCI labels, vendor label | complete |
| CI/CD supply-chain hardening | third-party actions pinned to SHA, workflow permissions scoped to job-level, version pins centralized | complete |
| Legacy branding cleanup | remove remaining Sonarr/Readarr/Radarr/Lidarr/Prowlarr branding from frontend UI, donations, logos, and icon assets | complete |
| Frontend test infrastructure | install jest + @testing-library/react; add initial test suite for critical flows; add CI step and coverage thresholds | planned |
| Async migration (sync-over-async) | convert 10+ `.GetAwaiter().GetResult()` sites to true async/await and propagate CancellationToken | planned |
| RestSharp → HttpClient migration | replace unmaintained RestSharp 106.15 with System.Net.Http.HttpClient via interface wrapper | planned |
| Security headers and input validation | add CSP/HSTS/X-Frame-Options middleware; validate API search/parse inputs at controller boundary | planned |
| React 18 + Router 6 upgrade | upgrade React 17→18, React Router 5→6; remove deprecated npm packages; establish frontend upgrade path | planned (DMQ-003, DMQ-004) |
| Node 22 LTS migration | upgrade from Node 20 (EOL April 2026) to Node 22 LTS | complete |
| .NET 10 LTS planning | prepare upgrade from .NET 8 (EOL Nov 2026) directly to .NET 10 LTS (skip .NET 9 STS) | future (DMQ-001, DMQ-002) |
| Documentation normalization | fix duplicate headings, stale references, archive dated files, align wiki with ROADMAP phases | planned |
| Installer signing | code-sign Windows installer and macOS app bundle; add GPG signing for release artifacts | future |
| Dual-format title management | ebook and audiobook variants can be tracked independently under one host/instance with non-conflicting quality/format policy | planned |

## Dependency Migration Queue

Structured tracking for breaking-change dependency upgrades deferred from Dependabot PRs
(April 2026 triage). Each entry requires dedicated migration effort and is sequenced
according to EOL urgency, coupling risk, and prerequisite dependencies.

| ID | Component | Current → Target | Dependabot PR | Category | Effort | Prerequisites | Target phase | Status |
|---|---|---|---|---|---|---|---|---|
| DMQ-001 | `dotnet/sdk` Docker image | 8.0 → 10.0 | [#35](https://github.com/Swartdraak/Bibliophilarr/pull/35) | Backend / Docker | High | DMQ-002, net8.0→net10.0 TFM migration across 24 projects | Phase 7 | planned |
| DMQ-002 | `dotnet/aspnet` Docker image | 8.0 → 10.0 | [#40](https://github.com/Swartdraak/Bibliophilarr/pull/40) | Backend / Docker | High | .NET 10 GA release (Nov 2025), TFM migration, runtime compatibility validation | Phase 7 | planned |
| DMQ-003 | `react-router-dom` | 5.3.4 → 6.x | [#38](https://github.com/Swartdraak/Bibliophilarr/pull/38) | Frontend | High | React 18 upgrade (DMQ-007 / RQ-159), remove `connected-react-router`, migrate Switch→Routes, class→hooks | Phase 7 | planned |
| DMQ-004 | `react-google-recaptcha` | 2.1.0 → 3.x | [#36](https://github.com/Swartdraak/Bibliophilarr/pull/36) | Frontend | Medium | React 18 upgrade, reCAPTCHA v3 API integration | Phase 7 | planned |
| DMQ-005 | `stylelint` | 15.11.0 → 16.x | [#39](https://github.com/Swartdraak/Bibliophilarr/pull/39) | Frontend / CI | Medium | Migrate config format, update plugin compatibility, validate all CSS rules | Phase 6-7 | planned |
| DMQ-006 | `FluentAssertions` | 5.10.3 → 8.x | [#44](https://github.com/Swartdraak/Bibliophilarr/pull/44) | Backend / Test | High | Update assertion syntax across 100+ test files, validate API compatibility | Phase 6-7 | planned |
| DMQ-007 | `FluentMigrator.Runner` | 3.3.2 → 8.x | [#45](https://github.com/Swartdraak/Bibliophilarr/pull/45) | Backend | High | Audit 44+ migration files, validate runner API changes, coordinate with DMQ-008 | Phase 7 | planned |
| DMQ-008 | `FluentMigrator.Runner.Postgres` | 3.3.2 → 8.x | [#46](https://github.com/Swartdraak/Bibliophilarr/pull/46) | Backend | High | Must upgrade with DMQ-007 in single coordinated slice | Phase 7 | planned |

### Migration sequencing and dependencies

```
Phase 6-7 (independent, can start now):
  DMQ-005 stylelint 16          — standalone config migration
  DMQ-006 FluentAssertions 8    — test-only, no runtime impact

Phase 7 (requires .NET 10 GA):
  DMQ-002 dotnet/aspnet 10      — runtime image first
  DMQ-001 dotnet/sdk 10         — build image, depends on DMQ-002 + TFM migration
  DMQ-007 FluentMigrator 8  ──┐
  DMQ-008 FluentMigrator.PG 8 ┘  coordinated upgrade

Phase 7 (requires React 18 first):
  DMQ-003 react-router-dom 6   — after React 18 + connected-react-router removal
  DMQ-004 react-google-recaptcha 3 — after React 18
```

### Rollout approach

- Each migration gets a dedicated feature branch with its own PR.
- Migrations with test-only impact (DMQ-005, DMQ-006) can proceed independently.
- .NET 10 migrations (DMQ-001, DMQ-002) are blocked until .NET 10 LTS reaches GA.
- Frontend migrations (DMQ-003, DMQ-004) are sequenced after React 18 upgrade (RQ-159).
- FluentMigrator pair (DMQ-007, DMQ-008) must ship as a single coordinated change.
- Re-open Dependabot PRs or create fresh PRs against the target version available at migration time.

### Cross-references

- RQ-060: FluentAssertions upgrade (partial — AutoFixture/Moq done, assertions deferred)
- RQ-140: react-google-recaptcha upgrade
- RQ-159: React 17→18 upgrade path
- RQ-160: React Router 5→6 migration
- RQ-164: .NET 10 LTS planning
- RQ-179: stylelint 15→16 migration (new)
- RQ-180: FluentMigrator.Runner 3→8 migration (new)
- RQ-181: FluentMigrator.Runner.Postgres 3→8 migration (new)

## Near-Term Delivery Sequence

1. Keep `develop` and `staging` green for backend, docs, smoke, and packaging validation.
2. Keep `main` green for readiness reporting and branch-policy audit dispatch.
3. Reduce dependency security drift in small, verifiable lockfile slices.
4. Promote release entry criteria from documentation to routine operator practice.
5. Execute local install testing on `develop` as a first-class lane (native binary, Docker, and npm launcher).
6. Reassess whether packaging validation can safely move onto `main` after installation paths are fully validated.
7. Promote TD-META-001..005 behaviors into release-entry evidence capture, including provider telemetry and conflict-score snapshots.
8. Add end-to-end metadata parity rehearsal on production-shaped datasets with explicit pass/fail thresholds for catalog retention and match-rate drift.
9. Close remaining high-priority failing Core baseline tests that impact metadata import quality signals.
10. Execute Docker hardening slice: pin base images to digests, add Node checksum verification, non-root runtime user, HEALTHCHECK, OCI labels, image scanning, SBOM. Remediation items RQ-004, RQ-005, RQ-023, RQ-024, RQ-059, RQ-111, RQ-112 from PROJECT_STATUS.md audit queue.
11. Pin third-party GitHub Actions to exact versions or commit SHAs; standardize workflow permissions to job-level; centralize version pins across global.json, Dockerfile, and workflows. Remediation items RQ-015, RQ-016, RQ-036, RQ-037, RQ-039, RQ-109, RQ-110, RQ-114 from audit queue.
12. Begin async migration: convert highest-risk sync-over-async sites (HttpClient, BookSearchService, EpubReader) to true async/await; propagate CancellationToken to middleware and core services. Remediation items RQ-003, RQ-018, RQ-020, RQ-021.
13. Install frontend test infrastructure (jest, @testing-library/react); write initial test suite covering search, import modal, and metadata mapping flows; add CI enforcement step. Remediation items RQ-066, RQ-042, RQ-043, RQ-106.
14. Plan and execute RestSharp 106 → HttpClient migration behind interface wrapper; update provider clients. Remediation items RQ-064, RQ-157.
15. Add security headers middleware (CSP, HSTS, X-Frame-Options, X-Content-Type-Options); validate API inputs at controller boundary. Remediation items RQ-086, RQ-087, RQ-175.
16. Execute documentation normalization pass: fix MIGRATION_PLAN.md duplicate H2 headings, update stale references, archive dated operational docs, align wiki with ROADMAP phases. Remediation items RQ-007, RQ-048, RQ-044, RQ-047, RQ-079, RQ-080, RQ-121-RQ-125.
17. Implement import-performance tranche 1 (instrumentation, batching, staged provider lookups, and bounded concurrency controls) with benchmarked before/after evidence.
18. Plan React 18 upgrade path: audit breaking changes, upgrade @testing-library, remove `react-addons-shallow-compare`, begin `connected-react-router` removal. Remediation items RQ-068, RQ-069, RQ-159.
19. Implement dual-format tranche 1 data-model and policy design for per-title ebook/audiobook variant intent without requiring multiple instances.

## Requested implementation additions (March 2026)

Immediate track: import and identification throughput

- Add import pipeline timing telemetry for queue wait, provider lookup, candidate scoring, and persistence phases.
- Add bounded concurrency controls (configurable worker count and provider request ceilings) for large-library runs.
- Add phased identification path:
  - phase A: low-cost local/identifier match
  - phase B: constrained provider search
  - phase C: fallback expansion only when confidence remains below threshold
- Add checkpoint/resume-friendly processing for long-running library imports so interrupted runs do not restart from zero.
- Add performance acceptance gates using production-shaped fixture cohorts (throughput, match-rate stability, and provider error-rate ceilings).

Future track: single-instance dual ebook/audiobook management

- Introduce per-title format intent as separate managed variants (ebook, audiobook) under one logical title identity.
- Add independent quality-profile/format-policy assignment per variant, including preferred and allowed format sets.
- Ensure monitoring, search, import, and post-processing paths preserve variant isolation (no cross-variant overwrites or loss of tracking state).
- Add UI/API surfaces for variant-level status, quality decisions, and missing/available state.
- Deliver migration-safe rollout with additive schema changes, feature flags, and rollback-safe toggles.

## Implementation task outline (immediate and future)

### Track A: Import and identification throughput optimization

Immediate implementation slices:

1. Slice A1: Baseline instrumentation
   - Add stage telemetry for queue wait, local parse/identifier pass, provider calls, scoring, and persistence.
   - Persist run-level summary artifact with processed count, duration, throughput, and error buckets.
2. Slice A2: Bounded concurrency controls
   - Add configurable worker pool for import identification.
   - Add provider-specific in-flight limits and timeout ceilings.
3. Slice A3: Phased identification strategy
   - Phase A local/identifier-only pass before network fallbacks.
   - Phase B constrained provider search.
   - Phase C expanded fallback only for unresolved low-confidence candidates.
4. Slice A4: Resume/checkpoint behavior
   - Add resumable progress checkpoints for long-running import queues.
   - Ensure interrupted runs restart from last durable checkpoint.
5. Slice A5: Performance gate integration
   - Add replay/fixture benchmark gate in CI/manual workflow.
   - Compare run to baseline thresholds before promotion.

Measurement criteria:

- Throughput: measured objects/minute improves versus baseline fixture run.
- Quality: accepted match rate regression is within predefined tolerance.
- Stability: provider timeout/error rates remain within threshold budget.
- Recoverability: interrupted run resumes from checkpoint with no duplicate imports.

### Track B: Single-instance dual ebook/audiobook management

Future implementation slices:

1. Slice B1: Variant model and persistence
   - Add additive per-title variant intent model (`ebook`, `audiobook`).
   - Add migration-safe schema updates and compatibility defaults.
2. Slice B2: Policy separation
   - Support independent quality/format profiles per variant.
   - Keep variant policy state isolated and explicit.
3. Slice B3: Pipeline isolation
   - Partition search/import/upgrade decisions by variant.
   - Prevent cross-variant replacement or state overwrite.
4. Slice B4: API/UI surfaces
   - Add variant-level state in API resources.
   - Add UI controls for per-variant wanted status, quality decisions, and missing state.
5. Slice B5: Migration and rollout controls
   - Feature-flag rollout with reversible toggles.
   - Add data migration validation and rollback rehearsal.

Measurement criteria:

- Correctness: ebook and audiobook variants co-exist under one title without conflict.
- Isolation: variant-specific upgrades do not alter opposite-format tracking state.
- Compatibility: legacy single-format libraries remain functionally unchanged when flag is off.
- Operability: variant-level telemetry and diagnostics are queryable by operators.

## Local Install Testing Enablement (Develop Branch)

Purpose:

- The primary mission of `develop` is to prove users can install and run Bibliophilarr locally with deterministic results, not only to pass CI.

Project-level recommendations:

1. Define a weekly install validation matrix owned by maintainers.

   - Linux native package install and first start
   - Docker image run and API/UI health check
   - npm launcher install and binary bootstrap

2. Enforce install readiness as a merge expectation for risky slices.

   - packaging path changes
   - startup/config changes
   - update/installer changes

3. Keep a rolling install evidence trail in versioned markdown snapshots.

   - exact commands
   - environment assumptions
   - pass/fail outcome
   - rollback notes

4. Prioritize install blockers ahead of non-critical feature work on `develop`.
5. Promote only install-proven commits from `develop` to `staging`.

Suggested acceptance criteria for local install readiness:

- Clean install starts and serves `/ping` and core API metadata endpoints.
- Existing config/data directories upgrade without manual data surgery.
- Fallback to prior release is documented and verified for the same environment.
- Any known installer/runtime caveats are captured in QUICKSTART and runbooks.

## Operational Notes

- Do not treat `main` packaging absence as a regression. It is an intentional scope boundary until release installation paths are fully validated.
- Do not treat Actions integration-token `403` responses as release blockers when the report artifacts explicitly mark the run as permission-limited and all available checks still succeed.
- Prefer incremental migration slices with rollback clarity over broad architectural rewrites.

## Related Documents

- [MIGRATION_PLAN.md](MIGRATION_PLAN.md)
- [PROJECT_STATUS.md](PROJECT_STATUS.md)
- [QUICKSTART.md](QUICKSTART.md)
- [docs/operations/RELEASE_AUTOMATION.md](docs/operations/RELEASE_AUTOMATION.md)
