# Bibliophilarr — Full Codebase Audit Report

**Audit date:** May 24, 2026  
**Audit type:** Clean-slate full audit  
**Scope:** Codebase, CI/CD, GitHub PRs, GitHub Issues, application runtime, documentation, security, UI/UX, database, metadata, library management, deployment, upgrades, maintainability  
**Auditor:** Remy (AI Producer) + Explore + documentation-auditor-readonly subagents  

## Related documents

- [ROADMAP.md](../../ROADMAP.md) — phase and milestone context
- [PROJECT_STATUS.md](../../PROJECT_STATUS.md) — open workstreams and remediation queue
- [MIGRATION_PLAN.md](../../MIGRATION_PLAN.md) — provider architecture
- [SECURITY.md](../../SECURITY.md) — disclosure and handling policy
- Sprint plan derived from this audit: [docs/sprint-7/plan.md](../sprint-7/plan.md)

---

## Executive Summary

Bibliophilarr is in a mature, well-hardened state for a community FOSS fork.
The Phase 5/6 programme delivered significant outcomes: provider migration,
dual-format tracking, CI/CD hardening, security header implementation,
Docker hardening, and test infrastructure.

This audit found **no Critical code-safety or data-loss issues** in the application
source. The most urgent items are in three categories:

1. **Documentation version accuracy** — three files reference `v1.2.0` before the
   tag has been cut, creating operator confusion.
2. **New Dependabot PRs** — PRs #66–#71 have accumulated since the last canonical
   status update (April 24). Most are safe to merge; PR #70 is actively dangerous
   (targets .NET 10 without TFM migration) and must be explicitly blocked.
3. **Runtime operational issues** — the live instance has a stuck download item
   that has been re-evaluated every 90 seconds for over a month, and SABnzbd
   has intermittent connection failures.

---

## 1. GitHub Pull Requests

### 1.1 Open Dependabot PRs (#52–#71)

| PR | Package | Bump | Risk | Recommendation |
|---|---|---|---|---|
| [#52](https://github.com/Swartdraak/Bibliophilarr/pull/52) | `@fortawesome/free-solid-svg-icons` | 6.7.2 → 7.x | Medium | Controlled migration slice per PROJECT_STATUS.md |
| [#53](https://github.com/Swartdraak/Bibliophilarr/pull/53) | `postcss-mixins` | 9.0.4 → 12.x | Medium | Controlled migration slice |
| [#54](https://github.com/Swartdraak/Bibliophilarr/pull/54) | `webpack-cli` | 5.1.4 → 7.x | Medium | Controlled migration slice |
| [#55](https://github.com/Swartdraak/Bibliophilarr/pull/55) | `rimraf` | 5.0.10 → 6.x | Low | Controlled migration slice |
| [#56](https://github.com/Swartdraak/Bibliophilarr/pull/56) | `prettier` | 2.8.8 → 3.x | Medium | Controlled migration slice (formatting churn risk) |
| [#57](https://github.com/Swartdraak/Bibliophilarr/pull/57) | `FluentMigrator.Runner.SQLite` | 3.3.2 → 8.x | High | Grouped with #58, migration DB runner slice |
| [#58](https://github.com/Swartdraak/Bibliophilarr/pull/58) | `FluentValidation` | 9.5.4 → 12.x | High | Grouped with #57, validation API audit required |
| [#59](https://github.com/Swartdraak/Bibliophilarr/pull/59) | `Ical.Net` | 4.3.1 → 5.x | Medium | Calendar recurrence regression testing needed |
| [#66](https://github.com/Swartdraak/Bibliophilarr/pull/66) | `aquasecurity/trivy-action` | 0.35.0 → 0.36.0 | Low | **SAFE — merge this sprint** |
| [#67](https://github.com/Swartdraak/Bibliophilarr/pull/67) | `sigstore/cosign-installer` | 3.10.1 → 4.1.1 | Low | **SAFE — cosign v3 support; merge this sprint** |
| [#68](https://github.com/Swartdraak/Bibliophilarr/pull/68) | `dessant/label-actions` | 3.1.0 → 5.0.0 | Medium | Requires Node.js 24 runner — verify GitHub runner version before merging |
| [#69](https://github.com/Swartdraak/Bibliophilarr/pull/69) | `coverlet.collector` | 8.0.1 → 10.0.0 | Low | **SAFE — test-only tool, merge this sprint** |
| [#70](https://github.com/Swartdraak/Bibliophilarr/pull/70) | `Microsoft.AspNetCore.SignalR.Client` | 8.0.11 → **10.0.7** | **CRITICAL** | **DO NOT MERGE** — targets .NET 10 SDK. Requires DMQ-001/002 TFM migration across 24 projects. Close with a comment directing to DMQ-001. |
| [#71](https://github.com/Swartdraak/Bibliophilarr/pull/71) | `devcontainers/features/node` | 1.7.1 → 2.0.0 | Low | **SAFE — devcontainer only, merge this sprint** |

**Note:** PRs #60–#65 are not listed in open PRs; presumably either merged or closed
between April 27 and this audit date.

### 1.2 Open Issues

| Issue | Title | Status |
|---|---|---|
| [#14](https://github.com/Swartdraak/Bibliophilarr/issues/14) | Dependabot alerts remain open after PR #12/#13 | Stale — yarn.lock already resolved all flagged families. Alerts may be a GitHub indexing lag. **Dismiss each alert with rationale or confirm the lock graph satisfies the requirement and close the issue.** |

---

## 2. CI/CD Audit

### 2.1 Workflow Inventory

19 workflows found. All GitHub Actions pinned to SHA commits. ✅  
All sensitive workflow secrets masked with `::add-mask::`. ✅  
Timeout-minutes set on most jobs. ✅  
Most jobs declare least-privilege permissions. ✅  

### 2.2 Gaps and Risks

| Finding | Severity | Detail |
|---|---|---|
| No CodeQL / SAST workflow | **High** | No static application security testing for C# or JavaScript. GitHub's CodeQL is free for public repos. |
| No dependency-review workflow | **Medium** | PRs introducing vulnerable npm/NuGet packages are not automatically blocked. |
| `docs-validation.yml` lint scope too narrow | **Medium** | Only 11 files enumerated. `wiki/`, `.github/instructions/`, and most `docs/operations/` files receive no lint enforcement. |
| `release.yml` has `continue-on-error: true` on series persistence gate | **Medium** | Advisory mode allows release with failing gate. Must be removed before Phase 7 entry. |
| PR #70 targets `.NET 10` without TFM migration | **Critical** | If merged, the build would fail; Signal R client targeting net10.0 is incompatible with current net8.0 TFMs. |
| `branch-policy-audit.yml` requests `security-events: read` but does not query security events | **Low** | Over-declared permission; tighten to remove `security-events` from that workflow. |
| `npm-publish.yml` environment restriction not verified | **Low** | Confirm the `npm-publish` environment in repository settings is restricted to `main` branch only. |

### 2.3 Runner Posture

All workflows use `ubuntu-latest`. No self-hosted runners observed.  
GitHub-hosted runners for public repos use ephemeral environments — supply chain posture is sound. ✅

---

## 3. Security Audit

### 3.1 Strengths

- Non-root Docker user (`uid 1000`, shell `/bin/false`). ✅
- Base image pinned by SHA digest. ✅
- Node.js tarball verified with `sha256sum`. ✅
- `SecurityHeadersMiddleware` implements CSP, `X-Frame-Options: DENY`,
  `X-Content-Type-Options: nosniff`, `Referrer-Policy`, `Permissions-Policy`. ✅
- `MaxRequestBodySize = 50 MB` in Kestrel. ✅
- No hardcoded secrets, API keys, or connection strings in source. ✅
- Docker Compose credentials injected from shell environment with empty defaults. ✅
- Media directories mounted read-only in Docker Compose. ✅
- Forms authentication enabled on the live instance. ✅

### 3.2 Risks

| Finding | Severity | Detail |
|---|---|---|
| `style-src 'unsafe-inline'` in CSP | Medium | Enables style injection. Mitigation: extract inline styles to CSS files over time. |
| `'unsafe-eval'` in debug CSP | Low | Ensure Release builds never include this. CI builds Release; confirm docker image uses Release build. |
| No CodeQL SAST | **High** | See CI/CD §2.2. |
| No dependency-review on PRs | **Medium** | See CI/CD §2.2. |
| GitHub PAT stored in `.env.local` (file present in workspace) | **High** | `.env.local` should be in `.gitignore`. **Verify now that it is not tracked.** If committed, rotate the PAT immediately. |
| Instance API key visible in `config.xml` | Info | Expected — this is the app's own Kestrel API key. Ensure `config.xml` and the config directory have appropriate filesystem permissions (owner-only read). |

### 3.3 Disclosure Posture

`SECURITY.md` is compliant: advisory-first, 72h acknowledgement target, no tokens in repo. ✅

---

## 4. Application Runtime Audit

**Instance:** `http://127.0.0.1:8787` · Branch: `develop` · AuthMethod: Forms · LogLevel: Debug  
**Last log entries analysed:** April 26, 2026 (logs rotate; runtime logs are ~4 weeks old)

### 4.1 Stuck Download — OPERATIONAL BLOCKER

`Ellen.Hopkins.Impulse.2008.RETAiL.EPUB.eBook-NODE` is being re-evaluated by
`DownloadProcessingService` every ~90 seconds and consistently returns 0 files /
0 identified / 0 imported.

**Root cause hypothesis:** The download completed and files were either already imported
or manually moved. The item remains in qBittorrent's completed list but has no local
files at the remapped path `/media/torrents/ebooks/Ellen.Hopkins.Impulse.2008.RETAiL.EPUB.eBook-NODE`.

**Impact:** Generates ~40 debug/info log entries per hour. This has been running for over
a month (visible in `.0.txt` through `.debug.50.txt`). Fills log rotation buffers faster.

**Resolution:** Manually remove this item from the qBittorrent completed list, or use
the Bibliophilarr UI Activity > Queue > Failed Imports to mark as imported/ignored.

### 4.2 SABnzbd Intermittent Failures

Log entry (April 26): `Temporarily ignoring download client SABnzbd till 4/26/2026 3:48:16 AM due to recent failures.`

SABnzbd is the backup Usenet client. Transient failures are normal (Usenet indexer
rate limits, API key expiry, server connectivity). Check SABnzbd status page and
verify the API key is current.

### 4.3 Remote Path Mapping in Fallback Mode

All 56+ active download items are being remapped using fallback mode:

```
Remapped remote path [/downloads/ebooks/...] to local path
[/media/torrents/ebooks/...] for host [192.168.40.103] (fallback, mapping format: Ebook)
```

`fallback` means no exact match was found in the RemotePathMappings table for this
host/path combination. The fallback is format-aware (Ebook/Audiobook) and working
correctly. However, explicit mappings would:

1. Remove the `(fallback)` log noise
2. Make path remapping deterministic even if format detection heuristics change

**Recommendation:** Add explicit RemotePathMappings for `192.168.40.103`:
- `/downloads/ebooks` → `/media/torrents/ebooks` (Ebook)
- `/downloads/audiobooks` → `/media/torrents/audiobooks` (Audiobook)

### 4.4 Import Match Rate

`ImportDecisionMaker|Import run complete: files=517 (filtered=517), match_rate=0.0%`

517 files are being found on disk but all are filtered before identification. This is
normal behaviour when all files are already imported (filtered=already-in-library).
The 0.0% match rate is for the *unfiltered* remainder after existing-file filtering —
not a sign of broken identification on new imports.

### 4.5 Instance Branch

The live instance is running on `develop` (per `config.xml`). For stability, production
instances should track `main` (stable releases). The `develop` branch may have
unreleased code and can receive breaking commits between releases.

---

## 5. Backend Code Audit

### 5.1 Architecture

Provider abstraction is clean: `IMetadataProvider` → `MetadataProviderOrchestrator`
→ Hardcover / OpenLibrary / Inventaire. Interface boundaries are respected. ✅

Input validation: All controllers use explicit `[FromBody]`, `[FromQuery]`, `[FromRoute]`
binding. No implicit model binding vulnerabilities. ✅

SQL injection: Dapper parameterised queries throughout. Raw SQL in migrations uses
hardcoded values only (no user input). ✅

### 5.2 Findings

| Finding | Severity | Detail |
|---|---|---|
| Sync-over-async patterns (30+ sites) | Medium | `.GetAwaiter().GetResult()` in HttpClient, BookSearchService, RssSyncService, DelugeProxy. Thread-pool starvation risk under sustained load. ROADMAP marks this "assessed — no action required" but new load-shedding features should not add new sync-over-async sites. |
| Missing CancellationToken propagation | Low | BookSearchService, AuthorSearchService. Low immediate risk; flag for future async migration. |
| No timeout enforcement at orchestrator level | Medium | Provider timeouts rely solely on HttpClient defaults. MetadataProviderOrchestrator does not enforce a budget across the full provider chain. Polly 8.5.2 is available. |
| No circuit breaker at orchestrator level | Medium | `BookSearchFallbackExecutionService` has partial implementation. Standardise with Polly's `CircuitBreakerPolicy`. |
| FluentValidation 9.5.4 (6 years old) | Medium | Current is v11.x. Upgrade path in DMQ as PR #58. No active CVEs but missing validation improvements. |

### 5.3 Database / Migrations

47 migrations. All reversible. All use FluentMigrator API. No destructive schema changes
without guards. Raw SQL in migrations is hardcoded (no injection risk). ✅

Migration 047 is current. Next migration slot: 048.

---

## 6. Frontend Audit

### 6.1 Strengths

- TypeScript adopted for new components (36 `.tsx` files). ✅
- ARIA roles, `htmlFor`/`id` associations, and `aria-label` present on key components. ✅
- `ErrorBoundary` wraps modal and top-level components. ✅
- `@testing-library/react` 12.1.5 in use with CI enforcement. ✅
- Legacy branding fully removed. ✅

### 6.2 Findings

| Finding | Severity | Detail |
|---|---|---|
| React 17 (not 18) | Low | Upgrade path assessed and documented in ROADMAP for Phase 7. React 17 still receives security updates. No action required this sprint. |
| 20+ legacy React class components | Low | Functional equivalents acceptable in React 17. Migrate to hooks as components are touched. |
| Error boundaries not on all page-level containers | Low | Errors in Search, Wanted, Organize page trees bubble to root ErrorBoundary. Acceptable fallback; add page-level boundaries incrementally. |
| Frontend Jest test coverage is minimal | Medium | `VirtualTableAccessibility.test.js` is the primary test. Modal/form logic, dual-format UI flows, and calendar state are untested. |
| Translation key coverage unverified | Info | `en.json` location not in standard `frontend/src/` path; served dynamically. Confirm API serves all UI strings or add a coverage check to CI. |

---

## 7. Deployment Audit

### 7.1 Docker

Base image pinned by SHA. ✅  
Non-root user. ✅  
Node.js SHA-verified in multi-stage build. ✅  
Trivy scanner in `docker-image.yml` with `continue-on-error: true` (advisory). ✅  
OCI labels and vendor labels applied. ✅  
Health check defined in Dockerfile. ✅  

**Gap:** No SBOM generation in the release workflow. Consider adding `syft` to
`docker-image.yml` to produce an SPDX SBOM alongside each Docker image push.

### 7.2 Binary Releases

Linux x64, macOS ARM64, Windows x64 binaries published via `release.yml`. ✅  
GitHub Releases page has packaged artefacts. ✅  

**Gap:** Binaries are not code-signed (Windows Authenticode, macOS notarisation).
Noted as future milestone "Installer signing" in ROADMAP. Users see OS security
warnings on first run. Track as planned, not urgent.

### 7.3 npm Launcher

npm package `bibliophilarr` published on npmjs.org. ✅  
`npm-publish.yml` uses `environment: npm-publish` with restricted publishing token. ✅

**Gap:** Confirm the `npm-publish` GitHub Actions environment is branch-restricted to
`main` in repository settings to prevent accidental publishing from feature branches.

### 7.4 Application Update Pipeline

`BIBLIOPHILARR_SERVICES_URL` env var is optional; local-only mode is the permanent
default. ✅  
Update checking works when the services URL is set; installation step explicitly
disabled pending release pipeline (Track D, UP-1 through UP-7, planned). ✅

---

## 8. Documentation Audit

Full findings from the documentation-auditor-readonly agent are preserved at
[docs/audit-2026-05/documentation-audit-findings.md](documentation-audit-findings.md).

### Summary Table

| ID | Severity | File | Finding |
|---|---|---|---|
| CRIT-01 | Critical | ROADMAP.md, wiki/Updates-and-Branches.md, docs/operations/RELEASE_AUTOMATION.md | `v1.2.0` referenced before tag exists |
| HIGH-01 | High | wiki/Metadata-Migration-Program.md | Phase 7 lists "test infrastructure" as planned; it is complete |
| HIGH-02 | High | MIGRATION_PLAN.md | Three March 24 audit findings never annotated as FIXED |
| HIGH-03 | High | docs/operations/ZERO_LEGACY_BRAND_CHANGEOVER_PLAN.md | Missing `## References` section (RQ-148 partially applied) |
| HIGH-04 | High | docs/operations/DOTNET_MODERNIZATION.md | Completed doc lacks formal `> [!WARNING]` archive banner |
| HIGH-05 | High | .github/workflows/docs-validation.yml | Lint scope excludes wiki/, instructions/, most docs/operations/ |
| MED-01 | Medium | docs/operations/DOTNET_MODERNIZATION.md | `### References` should be `## References` |
| MED-02 | Medium | wiki/Architecture.md | React 17 has no cross-reference to React 18 upgrade assessment |
| MED-03 | Medium | docs/proposals/unmapped-files-upgrade.md | 60+ days stale, not in ROADMAP |
| MED-04 | Medium | PROJECT_STATUS.md RQ-164 | ".NET 10 LTS expected late 2025" is stale; it shipped in Nov 2025 |
| LOW-01 | Low | npm/bibliophilarr-launcher/README.md | Absolute GitHub URLs (Rule L1 exception needed or exempt noted) |
| LOW-02 | Low | .github/ISSUE_TEMPLATE/bug_report.yml | Example version `0.1.0.432` (Readarr lineage placeholder) |
| LOW-03 | Low | docs/operations/GITHUB_PROJECTS_BLUEPRINT.md, REPOSITORY_TAGS.md | Orphaned advisory docs not linked from wiki or any runbook |

---

## 9. Metadata Provider Audit

### 9.1 Hardcover (Primary)

Provider abstraction implemented. ✅  
GraphQL queries fetch up to 500 contributions (expanded from 100 in March). ✅  
`SelectBestEdition()` scores by language preference, ISBN/ASIN richness, page data. ✅  
Duplicate deduplication by normalised base title. ✅  
`TryEnrichEditionMetadata()` cross-provider enrichment via OpenLibrary. ✅  
Rate-limit handling: 408/500 errors handled gracefully, authors require re-refresh. ✅  

**Gap:** No integration tests for Hardcover search/book retrieval paths.
Only configuration validation tests exist (`MetadataProviderConfigFixture.cs`).

### 9.2 OpenLibrary (Secondary)

`OpenLibraryIsbnAsinLookupFixture` integration test exists. ✅  
`OpenLibraryRefreshBaselineFixture` health check test exists. ✅  
OLID backfill service loads all books in one pass — OOM risk on very large libraries (RQ-031). Still open.

### 9.3 Provider Orchestration

No timeout enforcement budget at orchestrator level (see Backend §5.2). Medium risk.  
No standardised circuit breaker (see Backend §5.2). Medium risk.  
Health-aware routing with failure-streak-based fallback order. ✅  
Conflict explainability telemetry (factor-level score breakdown). ✅  

---

## 10. Library Management Audit

### 10.1 Dual-Format Tracking

All 16 slices (DF-1 through DF-16) complete and enabled by default. ✅  
Decision engine format-aware (all 7 specifications using `ResolveProfile()`). ✅  
Download client format categories, remote path mappings, queue display, wanted/missing filters, calendar filter, author index column — all complete. ✅  

### 10.2 Import Pipeline

Identification rate improved from ~19% to ~67-72% (clean-build verified). ✅  
Format-aware import (ebook vs audiobook routing). ✅  
Hardlink-aware download tracking (inode comparison). ✅  

**Gap:** `OpenLibraryIdBackfillService` loads all books + authors in one pass.
OOM risk on large libraries (RQ-031). No pagination implemented.

### 10.3 File Organisation

`MediaFileDeletionService` uses `IRootFolderService.GetBestRootFolder()` for cross-root deletion. ✅  
Path builder resolves format-profile root folders. ✅  

---

## 11. Usability / UX Audit

- Author edit modal: format profiles editable with quality profile names. ✅
- Queue: format type column with `ebook`/`audiobook` string serialisation. ✅
- Wanted/Missing: ebook/audiobook filter options. ✅
- Calendar: format filter options. ✅
- Progress bar: unmonitored items show PRIMARY (blue), not WARNING (orange). ✅
- Manual import: scans all format profile root folders. ✅
- History table: null guard for deleted books (`book ? book.title : translate('Unknown')`). ✅

**Outstanding UX items (Track E, STD-1 through STD-7):**
- STD-1: Form label i18n (`name` props added, i18n pending)
- STD-2: EnhancedSelectInput accessibility (unstarted)
- STD-3: Ebook format diagnostics (unstarted)
- STD-4: Calendar Redux initial state (`new Date()` fallback in place, root cause pending)
- STD-5: Toast notifications (unstarted)
- STD-6: Skeleton screens (unstarted)
- STD-7: TypeScript expansion (in progress organically)

---

## 12. Maintainability Audit

| Dimension | Assessment |
|---|---|
| Codebase navigation | Good — clear project structure, separation of core/api/host layers |
| Test coverage | Adequate for integration paths; gaps in Hardcover and dual-format unit tests |
| CI feedback loop | Fast (30-min backend, 15-min frontend timeouts) with targeted fixture jobs |
| Documentation completeness | High — canonical docs well-maintained; 13 doc findings all addressable in one focused pass |
| Dependency freshness | Mixed — npm and NuGet well-managed with explicit pinning; 8 deferred major-version PRs outstanding |
| Contributor onboarding | Good — QUICKSTART.md, CONTRIBUTING.md, and wiki/Contributor-Onboarding.md are current |
| Operational observability | Good — NLog structured logging, debug log rotation, metadata telemetry checkpoints, smoke workflows |

---

## 13. Summary Scorecard

| Domain | Rating | Top Concern |
|---|---|---|
| Security | ✅ Strong | No CodeQL; PAT in `.env.local` needs gitignore check |
| CI/CD | ✅ Strong | Docs lint scope too narrow; no SAST |
| Backend code | ✅ Good | Provider timeout budget; FluentValidation upgrade |
| Frontend code | ✅ Good | React 17; minimal Jest coverage |
| Database | ✅ Strong | No concerns |
| Metadata | ✅ Good | Hardcover integration tests missing |
| Library mgmt | ✅ Good | OOM risk in OpenLibrary backfill |
| Deployment | ✅ Good | No SBOM; no binary code-signing |
| Runtime | ⚠️ Attention | Stuck download; SABnzbd failures; all paths in fallback mode |
| Documentation | ⚠️ Attention | v1.2.0 CRIT; 4 HIGH doc fixes outstanding |
| PRs/Issues | ⚠️ Attention | PR #70 must not be merged; 3 PRs safe to merge now |
| Upgrades | 🔄 In Progress | 8 deferred major-version upgrades; .NET 10 migration planned |

---

## References

1. [ROADMAP.md](../../ROADMAP.md)
2. [PROJECT_STATUS.md](../../PROJECT_STATUS.md)
3. [MIGRATION_PLAN.md](../../MIGRATION_PLAN.md)
4. [CONTRIBUTING.md](../../CONTRIBUTING.md)
5. [SECURITY.md](../../SECURITY.md)
6. [docs/sprint-7/plan.md](../sprint-7/plan.md) — sprint plan derived from this audit
