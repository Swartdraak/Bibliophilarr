# Bibliophilarr Roadmap

**Last Updated**: April 24, 2026 (release-readiness alignment, staging frontend CI, dependency remediation)

This roadmap reflects the repository's actual delivery posture. Bibliophilarr is no longer in a planning-only state. The project is operating in Phase 5 consolidation with Phase 6 hardening active, while provider migration work continues incrementally on the active delivery lanes.

## Current delivery posture

- Current phase: Phase 5 consolidation with Phase 6 hardening active.
- Active delivery lanes: `develop` and `staging`.
- Default branch posture: `main` is the default branch, release-entry branch, and the source for tagged releases.
- Packaging scope: v1.0.0 released with binary builds (Linux x64, macOS ARM64, Windows x64), Docker image on GHCR, and npm launcher on npmjs.org.
- Actions token posture: branch-policy and readiness workflows remain report-only when GitHub Actions integration tokens cannot read admin or Dependabot APIs.
- Release-entry posture: promotion to `main` remains blocked until fresh dated PASS evidence exists for install, metadata dry-run, telemetry, and series persistence gates.

## Delivery lanes

| Branch | Purpose | Required outcomes |
|---|---|---|
| `develop` | Active integration lane for metadata migration and operational hardening slices | `build-test`, `Markdown lint`, `triage`, smoke telemetry |
| `staging` | Pre-release validation lane mirroring `develop` with tighter release-readiness scrutiny | same required contexts as `develop`, plus current readiness/drift report evidence |
| `main` | Default branch for releases, operator runbooks, readiness reporting, and branch-policy auditing | `build-test`, `Markdown lint`, `triage`, smoke telemetry, successful readiness and branch-policy report runs |

## Phase summary

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

## Current milestones

| Milestone | Target state | Current status |
|---|---|---|
| Branch protection parity | `develop`, `staging`, and `main` share required contexts and review policy | complete |
| Main dispatch validation | manual readiness and branch-policy workflows succeed on `main` | complete |
| Packaging confidence | release confidence covered by active smoke/build/readiness workflows | complete |
| Operational drift checks | scheduled drift signal exists with actionable artifacts | in progress |
| Release entry criteria | `main` release gate documented and enforced operationally | in progress — workflow gate remains advisory until evidence generation is fully automated |
| Security drift cleanup | open Dependabot set reduced via lockfile-backed remediation slices | in progress |
| Metadata path parity debt burn-down | add/refresh/import/identification use aligned orchestrated metadata request policy | complete |
| Identifier normalization convergence | canonical external-ID normalization is enforced at persistence boundaries | complete |
| Health-aware provider routing | provider failure streaks influence fallback order with deterministic recovery | complete |
| Conflict explainability telemetry | factor-level score breakdown is exposed for operator diagnostics | complete |
| Import throughput optimization | bulk media identification/import throughput is improved on production-shaped libraries without quality regression | in progress — Slice A1 (run telemetry) complete |
| Docker hardening | base image pinning, non-root runtime, health check, Node integrity verification, OCI labels, vendor label | complete |
| CI/CD supply-chain hardening | third-party actions pinned to SHA, workflow permissions scoped to job-level, version pins centralized | complete |
| Legacy branding cleanup | remove remaining Sonarr/Readarr/Radarr/Lidarr/Prowlarr branding from frontend UI, donations, logos, and icon assets | complete |
| Frontend test infrastructure | install jest + @testing-library/react; add initial test suite for critical flows; add CI step and coverage thresholds | complete |
| Async migration (sync-over-async) | convert 10+ `.GetAwaiter().GetResult()` sites to true async/await and propagate CancellationToken | assessed — no action required (all patterns acceptable) |
| RestSharp → HttpClient migration | replace unmaintained RestSharp 106.15 with System.Net.Http.HttpClient via interface wrapper | complete |
| Security headers and input validation | add CSP/HSTS/X-Frame-Options middleware; validate API search/parse inputs at controller boundary | complete |
| React 18 + Router 6 upgrade | upgrade React 17→18, React Router 5→6; remove deprecated npm packages; establish frontend upgrade path | **assessed** — migration plan documented below |
| Node 22 LTS migration | upgrade from Node 20 (EOL April 2026) to Node 22 LTS | complete |
| .NET 10 LTS planning | prepare upgrade from .NET 8 (EOL Nov 2026) directly to .NET 10 LTS (skip .NET 9 STS) | future (DMQ-001, DMQ-002) |
| Documentation normalization | fix duplicate headings, stale references, archive dated files, align wiki with ROADMAP phases | complete |
| Installer signing | code-sign Windows installer and macOS app bundle; add GPG signing for release artifacts | future |
| Dual-format title management | ebook and audiobook variants can be tracked independently under one host/instance with non-conflicting quality/format policy | **implemented** — all 16 slices (DF-1 through DF-16) complete, enabled by default via `EnableDualFormatTracking`. Detailed architecture in [MIGRATION_PLAN.md — TD-DUAL-FORMAT-001](MIGRATION_PLAN.md), Track B |
| Native ebook tag writing | write metadata tags to EPUB files without Calibre dependency; assess PDF; document AZW3/MOBI/KFX as Calibre-only | planned — Track C (ET-1 through ET-7) |
| Application update pipeline | end-to-end update lifecycle across binary, Docker, and npm distribution channels | planned — Track D (UP-1 through UP-7) |
| Frontend standardizations | accessible form labels, toast notifications, skeleton screens, TypeScript expansion | in progress — Track E (STD-1 through STD-7) |

## Dependency migration queue

Structured tracking for breaking-change dependency upgrades deferred from Dependabot PRs
(April 2026 triage). Each entry requires dedicated migration effort and is sequenced
according to EOL urgency, coupling risk, and prerequisite dependencies.

| ID | Component | Current → Target | Dependabot PR | Category | Effort | Prerequisites | Target phase | Status |
|---|---|---|---|---|---|---|---|---|
| DMQ-001 | `dotnet/sdk` Docker image | 8.0 → 10.0 | [#35](https://github.com/Swartdraak/Bibliophilarr/pull/35) | Backend / Docker | High | DMQ-002, net8.0→net10.0 TFM migration across 24 projects | Phase 7 | planned |
| DMQ-002 | `dotnet/aspnet` Docker image | 8.0 → 10.0 | [#40](https://github.com/Swartdraak/Bibliophilarr/pull/40) | Backend / Docker | High | .NET 10 GA release, TFM migration, runtime compatibility validation | Phase 7 | planned |
| DMQ-003 | `react-router-dom` | 5.3.4 → 6.x | [#38](https://github.com/Swartdraak/Bibliophilarr/pull/38) | Frontend | High | React 18 upgrade (DMQ-007 / RQ-159), remove `connected-react-router`, migrate Switch→Routes, class→hooks | Phase 7 | planned |
| DMQ-004 | `react-google-recaptcha` | 2.1.0 → 3.x | [#36](https://github.com/Swartdraak/Bibliophilarr/pull/36) | Frontend | Medium | React 18 upgrade, reCAPTCHA v3 API integration | Phase 7 | planned |
| DMQ-005 | `stylelint` | 15.11.0 → 16.x | [#39](https://github.com/Swartdraak/Bibliophilarr/pull/39) | Frontend / CI | Medium | Migrate config format, update plugin compatibility, validate all CSS rules | Phase 6-7 | **complete** |
| DMQ-006 | `FluentAssertions` | 5.10.3 → 8.x | [#44](https://github.com/Swartdraak/Bibliophilarr/pull/44) | Backend / Test | High | Update assertion syntax across 100+ test files, validate API compatibility | Phase 6-7 | **complete** |
| DMQ-007 | `FluentMigrator.Runner` | 3.3.2 → 8.x | [#45](https://github.com/Swartdraak/Bibliophilarr/pull/45) | Backend | High | Audit 44+ migration files, validate runner API changes, coordinate with DMQ-008 | Phase 7 | planned |
| DMQ-008 | `FluentMigrator.Runner.Postgres` | 3.3.2 → 8.x | [#46](https://github.com/Swartdraak/Bibliophilarr/pull/46) | Backend | High | Must upgrade with DMQ-007 in single coordinated slice | Phase 7 | planned |
| DMQ-009 | `@fortawesome/free-solid-svg-icons` | 6.7.2 → 7.x | [#52](https://github.com/Swartdraak/Bibliophilarr/pull/52) | Frontend | Medium | Audit icon import paths, verify tree-shaking output size, validate icon rendering in key pages | Phase 6-7 | planned |
| DMQ-010 | `postcss-mixins` | 9.0.4 → 12.x | [#53](https://github.com/Swartdraak/Bibliophilarr/pull/53) | Frontend / Build | Medium | Validate PostCSS plugin compatibility and build output parity in `frontend/build/webpack.config.js` | Phase 6-7 | planned |
| DMQ-011 | `webpack-cli` | 5.1.4 → 7.x | [#54](https://github.com/Swartdraak/Bibliophilarr/pull/54) | Frontend / Build | High | Validate webpack 5 + cli 7 compatibility, npm scripts, and CI frontend build behavior | Phase 6-7 | planned |
| DMQ-012 | `rimraf` | 5.0.10 → 6.x | [#55](https://github.com/Swartdraak/Bibliophilarr/pull/55) | Tooling | Medium | Validate Node engine constraints and cross-platform cleanup scripts in build/release flows | Phase 6-7 | planned |
| DMQ-013 | `prettier` | 2.8.8 → 3.x | [#56](https://github.com/Swartdraak/Bibliophilarr/pull/56) | Frontend / Docs | High | Run formatting impact audit; update lint/format config and avoid mass unrelated churn | Phase 6-7 | planned |
| DMQ-014 | `FluentMigrator.Runner.SQLite` | 3.3.2 → 8.x | [#57](https://github.com/Swartdraak/Bibliophilarr/pull/57) | Backend | High | Coordinate with DMQ-007/DMQ-008 and execute migration-runner compatibility verification in one slice | Phase 7 | planned |
| DMQ-015 | `FluentValidation` | 9.5.4 → 12.x | [#58](https://github.com/Swartdraak/Bibliophilarr/pull/58) | Backend | High | Audit validator API changes and request-pipeline behavior; update SignalR and API validation paths | Phase 6-7 | planned |
| DMQ-016 | `Ical.Net` | 4.3.1 → 5.x | [#59](https://github.com/Swartdraak/Bibliophilarr/pull/59) | Backend | Medium | Validate parser behavior and recurrence handling in calendar import and scheduling surfaces | Phase 6-7 | planned |

### Migration sequencing and dependencies

```
Phase 6-7 (independent, can start now):
  DMQ-005 stylelint 16          — standalone config migration
  DMQ-006 FluentAssertions 8    — test-only, no runtime impact
   DMQ-009 FontAwesome 7         — icon package compatibility validation
   DMQ-010 postcss-mixins 12     — PostCSS pipeline compatibility
   DMQ-011 webpack-cli 7         — frontend build tooling migration
   DMQ-012 rimraf 6              — cleanup script/node-engine compatibility
   DMQ-013 prettier 3            — formatting baseline migration with controlled churn
   DMQ-015 FluentValidation 12   — validator and request-pipeline migration
   DMQ-016 Ical.Net 5            — calendar parser compatibility checks

Phase 7 (requires .NET 10 GA):
  DMQ-002 dotnet/aspnet 10      — runtime image first
  DMQ-001 dotnet/sdk 10         — build image, depends on DMQ-002 + TFM migration
  DMQ-007 FluentMigrator 8  ──┐
   DMQ-008 FluentMigrator.PG 8 ─┼─ coordinated upgrade
   DMQ-014 FluentMigrator.SQLite 8 ┘ coordinated upgrade

Phase 7 (requires React 18 first):
  DMQ-003 react-router-dom 6   — after React 18 + connected-react-router removal
  DMQ-004 react-google-recaptcha 3 — after React 18
```

### Rollout approach

- Each migration gets a dedicated feature branch with its own PR.
- Migrations with test-only impact (DMQ-005, DMQ-006) can proceed independently.
- .NET 10 migrations (DMQ-001, DMQ-002) are available for scheduling now that .NET 10 LTS has reached GA; blocked by TFM migration scope.
- Frontend migrations (DMQ-003, DMQ-004) are sequenced after React 18 upgrade (RQ-159).
- FluentMigrator set (DMQ-007, DMQ-008, DMQ-014) must ship as a single coordinated change.
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
- RQ-182: FontAwesome 6→7 migration (new)
- RQ-183: postcss-mixins 9→12 migration (new)
- RQ-184: webpack-cli 5→7 migration (new)
- RQ-185: rimraf 5→6 migration (new)
- RQ-186: prettier 2→3 migration (new)
- RQ-187: FluentMigrator.Runner.SQLite 3→8 migration (new)
- RQ-188: FluentValidation 9→12 migration (new)
- RQ-189: Ical.Net 4→5 migration (new)

### React 18 upgrade path assessment (completed April 2026)

Readiness assessment completed against the current frontend codebase (React 17.0.2, React Router 5.3.4).

**Critical blockers** (must resolve before React 18 bump):

1. `connected-react-router` 6.9.3 — incompatible with React 18. Used in 6 files (App.js root wrapper, 4 page connectors, middleware/reducer). Must be removed entirely and replaced with React Router 6 built-in location management.
2. `ReactDOM.render()` in `frontend/src/bootstrap.tsx` — must migrate to `createRoot()` API.
3. `react-router-dom` 5.3.4 → 6.x — requires `<Switch>` → `<Routes>`, `component` → `element`, `<Redirect>` → `<Navigate>` refactors across all route definitions.

**Medium-impact items**:

- 6 `findDOMNode()` calls in drag-source components (TableOptionsColumnDragSource, QualityProfileItemDragSource, DelayProfileDragSource) — must replace with ref forwarding.
- 20+ `defaultProps` on function components — deprecated in React 18; convert to default parameter values.
- `react-redux` 7.2.9 → 8.1.0+ needed for React 18 concurrent features.
- `@testing-library/react` 12.1.5 → 13.0.0+ needed for React 18 `createRoot`.

**Low-impact items**:

- 1 `UNSAFE_componentWillReceiveProps` in RootFolderSelectInputConnector.
- 200+ class components may surface side-effect bugs under StrictMode double-render (audit, not blocking).
- Verify compatibility of react-popper 1.3.11, react-autosuggest 10.1.0, react-custom-scrollbars-2 4.5.0.

**Recommended migration sequence**:

1. Remove `connected-react-router` → use React Router 6 location hooks.
2. Upgrade `react-router-dom` 5 → 6 (largest refactor, touches all routes).
3. Bump `react` + `react-dom` to 18.x, update `bootstrap.tsx` to `createRoot()`.
4. Bump `@testing-library/react` to 13.x, `react-redux` to 8.x.
5. Replace `findDOMNode()` with ref forwarding in drag-source components.
6. Convert `defaultProps` to default parameter values on function components.

**Effort estimate**: High — the `connected-react-router` removal and React Router 5→6 migration are extensive refactors touching routing, Redux middleware, and page navigation patterns across the entire frontend.

### Redux modernization assessment (completed April 2026)

Current state: Redux 4.2.1, react-redux 7.2.9, redux-thunk 2.4.2, redux-actions 2.6.5. Redux Toolkit is **not installed**.

**Key findings**:

- 224 Connector files using legacy `connect()` HOC pattern — hooks (`useSelector`/`useDispatch`) would eliminate these.
- 35+ action modules using `redux-actions` `createHandleActions()` wrapper — Redux Toolkit `createSlice` would replace.
- 100+ thunk action handlers using custom `createThunk()` wrapper — `createAsyncThunk` would standardize.
- Store created via vanilla `createStore()` — Toolkit's `configureStore()` includes DevTools, middleware defaults.
- `redux-batched-actions` in use — Toolkit's built-in batching would replace.

**Migration sequence** (must follow React 18 + Router 6 first):

1. Install `@reduxjs/toolkit`, upgrade `react-redux` 7→8.
2. Migrate store setup: `createStore()` → `configureStore()`.
3. Incrementally convert action modules to `createSlice` (35+ modules).
4. Convert Connector HOCs to hooks (224 files, can be done per-module).
5. Remove `redux-actions` and `redux-batched-actions` dependencies.

**Effort estimate**: Very High (~15-20 KLOC refactor). Should be done incrementally per-module after React 18 + Router 6 are complete. Both legacy and modern patterns can coexist during migration.

### moment.js → date-fns migration (completed April 2026)

Replaced moment.js 2.30.1 (328KB) with date-fns 4.1.0 (tree-shakeable, ~7KB used) across all 34 frontend source files. moment.js removed from dependencies.

**What was done**:

- Created `momentFormatToDateFns.js` utility to convert backend format tokens at the formatting boundary — no backend changes required.
- Created `parseTimeSpan.js` utility to replace `moment.duration()` for .NET TimeSpan string parsing.
- Migrated 14 `Utilities/Date/` modules, 10 `Calendar/` modules, 3 `Store/Actions/` modules, 3 `System/` modules, and 4 other consumer files.
- All 9 test suites (19 tests) pass. Frontend build succeeds.

**Effort**: Medium (34 files, completed in single commit).

### UI modernization assessment (completed April 2026)

Current frontend audit reveals a mature but aging UI architecture with clear modernization opportunities. The component library is entirely custom-built (40+ components), well-structured with CSS Modules and FontAwesome 6.7.2, and includes dark mode, responsive breakpoints, and basic accessibility.

**Current state summary**:

| Area | Status | Details |
|---|---|---|
| Component patterns | ~65% class, ~35% functional | ~5% TypeScript (.tsx) |
| Styling | CSS Modules + JS theme variables | Light/dark themes, 5 breakpoints, color-impaired mode |
| Component library | Custom (40+ components) | No external library (MUI, etc.) |
| Accessibility | Improved | FocusLock in modals, ARIA roles on VirtualTable, keyboard navigation (Arrow/Page/Home/End), aria-sort on sortable headers |
| Loading UX | Spinners only | No skeleton screens or content placeholders |
| Error handling | ErrorBoundary in modals + page layout | Page-level boundary added; no toast notifications yet |
| Animation | CSS-only | No animation library; transitions work but limited |
| State management | Redux 4 + connect() HOC | 224 connectors, reselect for memoization, no Context API |
| Icons | FontAwesome 6.7.2 | 100+ named exports, tree-shaken |

**Recommended modernization priorities** (sequenced for incremental delivery):

1. **Accessibility hardening** (Low effort, high impact) — ~~Add ARIA roles to tables (`role="grid"`)~~, live regions for dynamic updates, `aria-describedby` for form errors. Can be done incrementally without React 18. **PARTIAL** — VirtualTable grid/row/cell/columnheader roles added with `aria-sort` on sortable headers; keyboard navigation (Arrow Up/Down, Page Up/Down, Home/End) added; 8 ARIA/accessibility tests.

2. **Skeleton screens for loading states** (Medium effort) — Replace spinner-only loading with content placeholders for author index, book details, calendar. Improves perceived performance.

3. **Error boundary expansion** (Low effort) — ~~Add page-level error boundaries beyond current modal-only coverage.~~ Add toast notification system for non-blocking errors. **PARTIAL** — Page-level `ErrorBoundary` wrapping route children in `Page.js`; toast system still pending.

4. **TypeScript expansion** (Ongoing, medium effort) — Convert new/touched components to .tsx. Focus on Store/Actions (type-safe reducers), Utilities, and high-reuse Components first. Currently ~5%.

5. **Class → functional component migration** (High effort, depends on React 18) — Convert ~200 class components to functional components with hooks. Prioritize components with simple state (Labels, Cards, simple forms) first. **Should follow React 18 upgrade** to leverage concurrent features.

6. **Redux → RTK migration** (Very high effort, depends on React 18) — Replace 224 connect() HOCs with `useSelector`/`useDispatch`, convert action creators to RTK slices. Already assessed in Redux Migration Assessment section.

**What NOT to do now**:

- Don't adopt an external component library (MUI, Ant Design) — the custom library is well-structured and a wholesale replacement would be massive churn for marginal benefit.
- Don't add animation libraries — CSS transitions cover current needs.
- Don't add Storybook — useful but high setup cost for limited team.
- Don't consolidate CSS to utility-first (Tailwind) — CSS Modules work well with the current architecture.

**User testing needed**: After items 1-3 are implemented, UI should be tested for:

- Calendar view: date formatting (date-fns migration), week navigation, month transitions
- Task scheduler: relative time display ("2 hours ago"), scheduled task intervals
- Book search: year display in search results
- Book details: release date formatting
- Queue: estimated completion time display

## Near-term delivery sequence

1. Keep `develop` and `staging` green for backend, docs, smoke, and packaging validation.
2. Keep `main` green for readiness reporting and branch-policy audit dispatch.
3. Reduce dependency security drift in small, verifiable lockfile slices.
4. Promote release entry criteria from documentation to routine operator practice.
5. Execute local install testing on `develop` as a first-class lane (native binary, Docker, and npm launcher).
6. Reassess whether packaging validation can safely move onto `main` after installation paths are fully validated.
7. Promote TD-META-001..005 behaviors into release-entry evidence capture, including provider telemetry and conflict-score snapshots.
8. Add end-to-end metadata parity rehearsal on production-shaped datasets with explicit pass/fail thresholds for catalog retention and match-rate drift.
9. ~~Close remaining high-priority failing Core baseline tests that impact metadata import quality signals.~~ **COMPLETED** — Core test suite passes 2703/2703 with 0 failures.
10. ~~Execute Docker hardening slice: pin base images to digests, add Node checksum verification, non-root runtime user, HEALTHCHECK, OCI labels, image scanning, SBOM. Remediation items RQ-004, RQ-005, RQ-023, RQ-024, RQ-059, RQ-111, RQ-112 from PROJECT_STATUS.md audit queue.~~ **COMPLETED**
11. ~~Pin third-party GitHub Actions to exact versions or commit SHAs; standardize workflow permissions to job-level; centralize version pins across global.json, Dockerfile, and workflows. Remediation items RQ-015, RQ-016, RQ-036, RQ-037, RQ-039, RQ-109, RQ-110, RQ-114 from audit queue.~~ **COMPLETED**
12. Begin async migration: convert highest-risk sync-over-async sites (HttpClient, BookSearchService, EpubReader) to true async/await; propagate CancellationToken to middleware and core services. Remediation items RQ-003, RQ-018, RQ-020, RQ-021.
13. ~~Install frontend test infrastructure (jest, @testing-library/react); write initial test suite covering search, import modal, and metadata mapping flows; add CI enforcement step. Remediation items RQ-066, RQ-042, RQ-043, RQ-106.~~ **COMPLETED** — Jest 30.3.0 + @testing-library/react 12.1.5 installed; 9 test suites (19 tests) covering search, metadata providers, utilities, components; CI enforcement in `ci-frontend.yml`.
14. ~~Plan and execute RestSharp 106 → HttpClient migration behind interface wrapper; update provider clients. Remediation items RQ-064, RQ-157.~~ **COMPLETED** — RestSharp fully removed, replaced by `System.Net.Http.HttpClient`.
15. ~~Add security headers middleware (CSP, HSTS, X-Frame-Options, X-Content-Type-Options); validate API inputs at controller boundary. Remediation items RQ-086, RQ-087, RQ-175.~~ **COMPLETED** — `SecurityHeadersMiddleware` added with CSP, X-Frame-Options, X-Content-Type-Options, Referrer-Policy, Permissions-Policy; search/parse input validation guards added.
16. ~~Execute documentation normalization pass: fix MIGRATION_PLAN.md duplicate H2 headings, update stale references, archive dated operational docs, align wiki with ROADMAP phases. Remediation items RQ-007, RQ-048, RQ-044, RQ-047, RQ-079, RQ-080, RQ-121-RQ-125.~~ **COMPLETED** — all 11 RQ items fixed: H2 deduplication, wiki alignment, archive banners, stale references updated.
17. Implement import-performance tranche 1 (instrumentation, batching, staged provider lookups, and bounded concurrency controls) with benchmarked before/after evidence.
18. Plan React 18 upgrade path: audit breaking changes, upgrade @testing-library, remove `react-addons-shallow-compare`, begin `connected-react-router` removal. Remediation items RQ-068, RQ-069, RQ-159.
19. ~~Implement dual-format tranche 1 data-model and policy design for per-title ebook/audiobook variant intent without requiring multiple instances.~~ **IMPLEMENTED** — all 16 slices (DF-1 through DF-16) complete. Enabled by default via `EnableDualFormatTracking` (Settings > Media Management > Dual Format). Detailed architecture in [MIGRATION_PLAN.md — TD-DUAL-FORMAT-001](MIGRATION_PLAN.md).
20. Implement native EPUB tag writing without Calibre dependency (Track C, slices ET-1 through ET-7). Assess PDF writing. Document AZW3/MOBI/KFX as Calibre-only formats.
21. Enable application update pipeline across all distribution channels (Track D, slices UP-1 through UP-7). Requires services endpoint and release automation.
22. Complete frontend standardizations: internationalize form labels, fix EnhancedSelectInput accessibility, add toast notifications, skeleton screens (Track E, slices STD-1 through STD-7).

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

**Detailed design**: See [MIGRATION_PLAN.md — TD-DUAL-FORMAT-001](MIGRATION_PLAN.md) for
full architecture, data model, migration strategy, and per-slice implementation details.

- Introduce `AuthorFormatProfile` entity: per-author, per-format (ebook/audiobook)
  configuration owning quality profile, root folder, tags, monitored state, and path.
- One metadata search per book — releases sorted into format slots by detected quality.
- Independent quality-profile/format-policy assignment per format profile.
- Ensure monitoring, search, import, and post-processing paths preserve format isolation
  (no cross-format overwrites or loss of tracking state).
- Add UI/API surfaces for format-level status, quality decisions, and missing/available
  state.
- Deliver migration-safe rollout with additive schema changes, feature flag
  (`EnableDualFormatTracking`), and rollback-safe toggles.

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

**Design assessment** (completed April 2026):

Current data model has partial infrastructure for dual-format:

- `Edition.Format` (string) and `Edition.IsEbook` (bool) exist but are not used in search/import decision logic.
- Quality system has separate ebook (PDF/MOBI/EPUB/AZW3) and audio (MP3/FLAC/M4B) quality definitions with two default profiles ("eBook" and "Spoken").
- `Book.Monitored` + `Edition.Monitored` provide per-edition control.
- Format preference is implicit via allowed qualities in author's single `QualityProfile`.

**Architecture** (finalized April 2026):

- Add `AuthorFormatProfile` entity (AuthorId, FormatType, QualityProfileId, RootFolderPath, Tags, Monitored, Path). One-to-many from Author.
- Format profile lives at Author level — quality profile, root folder, tags, and download client routing apply uniformly across all books by that author.
- Per-book format tracking via Edition monitoring: one monitored edition per format type per book.
- One metadata search per book — releases sorted into format slots by detected quality.
- Feature-flagged via config: `EnableDualFormatTracking` (default: true). When off, legacy behavior is preserved exactly.

**Migration strategy**:

- Additive schema: `AuthorFormatProfiles` table with FK to `Authors` and `QualityProfiles`.
- Auto-populate one format profile per existing author from current quality profile.
- No destructive changes; rollback = drop table and disable feature flag.

Implementation slices (detailed design in [MIGRATION_PLAN.md — TD-DUAL-FORMAT-001](MIGRATION_PLAN.md)):

1. ~~DF-1: Domain model and schema~~ **DONE** — `AuthorFormatProfile` entity, migration 045, `FormatType` enum, feature flag
2. ~~DF-2: Edition monitoring per format type~~ **DONE** — per-format housekeeping, `BookEditionSelector` overloads
3. ~~DF-3: Decision engine format-aware quality evaluation~~ **DONE** — format-specific profile resolution
4. ~~DF-4: Download client routing by format~~ **DONE** — format profile tags in `DownloadService`
5. ~~DF-5: Import pipeline format awareness~~ **DONE** — format-specific edition/root folder assignment
6. ~~DF-6: File path building by format~~ **DONE** — format profile root folder in path builder
7. ~~DF-7: Missing and cutoff evaluation by format~~ **DONE** — format-filtered SQL, controller query params
8. ~~DF-8: API resources and controllers~~ **DONE** — CRUD controller, format status resources, crash fix
9. ~~DF-9: Frontend format profile UI~~ **DONE** — author edit modal, detail header badges, Redux store
10. ~~DF-10: Rollout controls and compatibility gates~~ **DONE** — config API, frontend toggle, feature flag wiring
11. ~~DF-11: Format-aware download client categories~~ **DONE** — `IFormatCategorySettings` interface, per-format category fields on 6 download clients
12. ~~DF-12: Format-aware remote path mappings~~ **DONE** — nullable `FormatType` column (migration 046), format-priority path resolution, frontend selector
13. ~~DF-13: Queue format display~~ **DONE** — `FormatType` on `QueueResource`, queue table format column
14. ~~DF-14: Wanted format filters~~ **DONE** — ebook/audiobook filters on Missing and Cutoff Unmet views
15. ~~DF-15: Calendar format filter~~ **DONE** — ebook/audiobook filter on Calendar view
16. ~~DF-16: Author index format column~~ **DONE** — format profiles column with monitored status indicators

Measurement criteria:

- Correctness: ebook and audiobook variants co-exist under one title without conflict.
- Isolation: variant-specific upgrades do not alter opposite-format tracking state.
- Compatibility: legacy single-format libraries remain functionally unchanged when flag is off.
- Operability: variant-level telemetry and diagnostics are queryable by operators.

### Track C: Native ebook metadata tag writing (without Calibre)

**Status**: planned — Phase 6-7

**Problem**: Ebook tag writing currently requires a running Calibre Content Server instance (`CalibreId > 0`). Without Calibre configured, `MetadataTagService.WriteTags()` skips ebook files entirely. The tag read path already works natively via vendored `VersOne.Epub` reader in `src/NzbDrone.Core/MediaFiles/EpubTag/`, proving the format is accessible.

**Goal**: Enable native metadata tag writing for common ebook formats without external Calibre dependency, so ebook libraries receive the same embedded-tag management that audiobooks already have via TagLibSharp.

**Architecture**:

- EPUB files are ZIP archives containing OPF (Open Packaging Format) XML metadata. Writable using `System.IO.Compression.ZipArchive` + `System.Xml.Linq` — both built into .NET 8 with no new NuGet dependencies.
- AZW3/MOBI/KFX are proprietary Amazon formats requiring reverse-engineered binary manipulation (Calibre is the de facto implementation). Native writing is not feasible without significant reverse-engineering.
- PDF metadata is writable via iTextSharp or PdfPig (NuGet), but PDF is a secondary format for book management.

**Format feasibility matrix**:

| Format | Read support | Native write feasibility | Dependency | Priority |
|---|---|---|---|---|
| EPUB | existing (VersOne.Epub) | high — OPF XML in ZIP archive | none (built-in .NET) | P1 |
| PDF | existing (basic) | medium — requires NuGet library | iTextSharp or PdfPig | P3 |
| AZW3 | existing (Calibre only) | low — proprietary binary format | reverse-engineered or Calibre | P4 (Calibre-only) |
| MOBI | existing (Calibre only) | low — proprietary binary format | reverse-engineered or Calibre | P4 (Calibre-only) |
| KFX | none | very low — DRM-adjacent proprietary format | Calibre only | P5 (Calibre-only) |

Implementation slices:

1. ET-1: EpubWriter class — read/modify/write OPF metadata in EPUB ZIP archives
   - Parse existing OPF XML via `System.Xml.Linq`.
   - Update `dc:title`, `dc:creator`, `dc:identifier` (ISBN, ASIN, Hardcover ID), `dc:publisher`, `dc:date`, `dc:language`, `dc:subject`, `dc:description`.
   - Write modified OPF back into ZIP without corrupting other archive entries.
   - Preserve original OPF structure and namespaces (Dublin Core, OPF extensions).
   - Handle EPUB 2.0 and EPUB 3.0+ namespace differences.
   - Add deterministic unit tests with fixture EPUB files.
2. ET-2: EbookTagService refactor — remove CalibreId gate for EPUB
   - Refactor `EbookTagService.WriteTagsInternal()` to attempt native EPUB write when `CalibreId == 0` and file extension is `.epub`.
   - Preserve existing Calibre path when `CalibreId > 0` (Calibre may handle additional formats and custom columns).
   - Update `MetadataTagService.WriteTags()` routing logic to invoke ebook tag writing for EPUB regardless of Calibre configuration.
3. ET-3: Cover image embedding in EPUB
   - Read cover image from metadata or book artwork cache.
   - Replace or insert cover image entry in EPUB ZIP archive.
   - Update OPF `<meta name="cover" content="..."/>` and manifest entry.
   - Validate cover image dimensions and format (JPEG/PNG).
4. ET-4: Series and collection metadata in EPUB
   - Write Calibre-compatible series metadata (`calibre:series`, `calibre:series_index`) into OPF for interoperability.
   - Write EPUB 3.0 `belongs-to-collection` refinement for standards-compliant readers.
5. ET-5: Preview and diff for ebook tag changes
   - Expose tag preview endpoint showing current vs proposed metadata changes before writing.
   - Support dry-run mode in bulk tag operations.
   - Reuse existing preview pattern from audiobook tag writing if applicable.
6. ET-6: PDF metadata writing (optional, requires NuGet dependency decision)
   - Evaluate PdfPig (MIT license, .NET native) vs iTextSharp (AGPL license concern).
   - Write XMP/DocumentInfo metadata fields: Title, Author, Subject, Keywords, Creator.
   - Add to format write routing in `EbookTagService`.
7. ET-7: Integration tests and edge cases
   - EPUB files with missing or malformed OPF.
   - EPUB files with encryption/DRM (must skip gracefully, not corrupt).
   - Multi-OPF EPUB archives (rare but valid).
   - Round-trip validation: write then read back, verify no data loss.
   - Large EPUB files (>100MB audiobook containers sometimes use .epub extension).
   - Files on network shares (NFS/SMB locking behavior).

Measurement criteria:

- Correctness: written metadata is readable by Calibre, Adobe Digital Editions, and major e-reader apps.
- Safety: original file is backed up or write is atomic (write to temp, rename on success).
- Compatibility: Calibre-configured users see no behavior change; Calibre path remains preferred when `CalibreId > 0`.
- Coverage: EPUB write path handles both EPUB 2.0 and 3.0 format variants.
- Graceful degradation: AZW3/MOBI/KFX files without Calibre produce a clear "Calibre required" diagnostic rather than silent skip.

### Track D: Application update pipeline

**Status**: planned — Phase 7

**Problem**: The built-in update command is explicitly disabled with `throw new CommandFailedException("Application updates are disabled until release pipeline support is implemented.")`. Update checking works when `BIBLIOPHILARR_SERVICES_URL` is configured but the install step is blocked. Users have no automated path from detecting an update to installing it.

**Goal**: Enable a complete, safe, and observable update lifecycle across all distribution channels (binary, Docker, npm launcher).

**Current state**:

| Component | Status | Notes |
|---|---|---|
| Update version check | working (if services configured) | `UpdatePackageProvider` → `GET /v1/update/{branch}` |
| Frontend update page | working | Shows available updates, warns when services not configured |
| Health check for stale builds | working | `UpdateCheck` warns when build > 14 days old and update available |
| ApplicationUpdateCommand (install) | **disabled** | Hard block in `InstallUpdateService.Execute()` and `CommandController` |
| Update package download/extract | implemented but unreachable | `InstallUpdate()` pipeline: backup → extract → terminate → transfer → restart |
| Docker update awareness | working | Detects Docker via `/.dockerenv`, disables built-in updater, shows external-update message |
| npm launcher version control | working | `BIBLIOPHILARR_VERSION` env var, GitHub release download, cache in `~/.cache/bibliophilarr/` |
| Services endpoint contract | documented | `docs/operations/services-endpoint-runbook.md` |
| Graceful degradation (no services) | working | All cloud features return safe no-ops when `BIBLIOPHILARR_SERVICES_URL` unset |

Implementation slices:

1. UP-1: Services endpoint implementation
   - Implement or deploy a minimal services endpoint conforming to `docs/operations/services-endpoint-runbook.md`.
   - `GET /v1/update/{branch}` returns latest release metadata (version, URL, hash, changelog).
   - `GET /v1/update/{branch}/changes` returns recent update history.
   - Can be a static JSON file hosted on GitHub Pages, a serverless function, or a self-hosted service.
   - Must support `BIBLIOPHILARR_SERVICES_URL` pointing to it.
2. UP-2: Release artifact publishing automation
   - GitHub Actions workflow to build release artifacts on tag push.
   - Publish platform-specific archives (Linux x64/ARM64, macOS ARM64/x64, Windows x64) to GitHub Releases.
   - Generate and publish SHA256 checksums alongside artifacts.
   - Update services endpoint metadata (version, URL, hash) on successful release.
3. UP-3: Enable built-in update command (binary installs)
   - Remove the `CommandFailedException` block in `InstallUpdateService.Execute()`.
   - Remove the `ApplicationUpdateCommand` rejection in `CommandController.StartCommand()`.
   - Validate the existing backup → extract → terminate → transfer → restart pipeline.
   - Add pre-update permission checks (writable startup folder, writable UI folder).
   - Add rollback mechanism: if update fails, restore from backup.
4. UP-4: Docker update path
   - Docker users update by pulling new container images — no in-app mechanism.
   - Ensure `UpdateCheck` health check correctly identifies Docker and directs users to `docker pull`.
   - Add version label comparison: running container version vs latest available tag.
   - Document Docker update procedure in QUICKSTART.md.
5. UP-5: npm launcher update path
   - npm users update via `npm update -g bibliophilarr` (pulls latest launcher) which downloads the latest binary release.
   - Implement cache invalidation: detect when cached binary version differs from desired version.
   - Add `bibliophilarr --update` command to force re-download of latest binary.
   - Document npm update procedure in QUICKSTART.md.
6. UP-6: Update notification and UX improvements
   - Add system tray / dashboard banner when update is available.
   - Show changelog preview before update initiation.
   - Add update progress indicator during download and installation.
   - Send notification (if configured) on successful/failed update.
7. UP-7: Update safety and observability
   - Log update events to `UpdateHistory` table (already exists).
   - Add health check for failed update attempts.
   - Verify SHA256 hash before applying update (existing `UpdateVerification` class).
   - Test rollback scenario: corrupt update package triggers safe restore.

Measurement criteria:

- Safety: failed updates do not leave the application in an unrecoverable state.
- Observability: update checks, downloads, and installations are logged and queryable.
- Compatibility: all three distribution channels (binary, Docker, npm) have documented and tested update paths.
- Graceful degradation: local-only installs without `BIBLIOPHILARR_SERVICES_URL` continue to operate normally with no update noise.

### Track E: Remaining standardizations and quality improvements

**Status**: in progress — ongoing

Outstanding items identified during v1.1.0-dev.22 through v1.1.0-dev.26 delivery:

1. STD-1: Form label accessibility — internationalize hardcoded label text
   - The 26 `FormLabel` components in `MetadataProvider.js` now have proper `name`/`htmlFor` association (fixed in v1.1.0-dev.26), but label display text remains hardcoded English strings.
   - Convert all metadata provider form labels to use `translate()` with keys from `en.json`.
   - Audit other settings pages for similar hardcoded label patterns.
2. STD-2: EnhancedSelectInput accessibility
   - `EnhancedSelectInput` wraps `react-select` and does not forward `id` prop to the underlying `<input>`.
   - Add `inputId={name}` prop to `react-select` component for proper `htmlFor`↔`id` association.
   - Affects all dropdown/select fields across Settings pages.
3. STD-3: Ebook format diagnostic messaging
   - When ebook tag writing is skipped due to missing Calibre (and native EPUB writing is not yet available), show a clear user-facing diagnostic instead of silent skip.
   - Add health check or manual import warning: "Ebook tag writing requires Calibre Content Server or native EPUB support (planned)."
   - Remove silent skip once Track C (ET-2) enables native EPUB writing.
4. STD-4: Calendar edge case hardening
   - Calendar crash was fixed in v1.1.0-dev.26 with `new Date()` fallback, but the root cause is `calendar.time` being `undefined` in initial Redux state.
   - Set explicit initial state for `calendar.time` in reducer to `new Date().toISOString()` to prevent the class of bug entirely.
   - Audit other Redux store slices for undefined initial state that could cause similar runtime errors.
5. STD-5: Toast notification system
   - UI modernization assessment identified toast notifications as a gap (error boundary expansion, item 3).
   - Add non-blocking toast system for transient success/error/warning messages.
   - Replace `alert()` calls and console-only error paths with user-visible toasts.
6. STD-6: Skeleton loading screens
   - UI modernization assessment item 2: replace spinner-only loading states with content placeholders.
   - Priority pages: author index, book details, calendar.
   - Improves perceived performance on slower connections.
7. STD-7: TypeScript expansion (ongoing)
   - Currently ~5% TypeScript in frontend.
   - Convert new/touched files to `.tsx` on contact.
   - Priority: Store/Actions (type-safe reducers), Utilities, high-reuse Components.

## Local install testing enablement (develop branch)

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

## Operational notes

- Do not treat `main` packaging absence as a regression. It is an intentional scope boundary until release installation paths are fully validated.
- Do not treat Actions integration-token `403` responses as release blockers when the report artifacts explicitly mark the run as permission-limited and all available checks still succeed.
- Prefer incremental migration slices with rollback clarity over broad architectural rewrites.

## Related documents

- [MIGRATION_PLAN.md](MIGRATION_PLAN.md)
- [PROJECT_STATUS.md](PROJECT_STATUS.md)
- [QUICKSTART.md](QUICKSTART.md)
- [docs/operations/RELEASE_AUTOMATION.md](docs/operations/RELEASE_AUTOMATION.md)
