> [!WARNING]
> **DEPRECATED** — This document has been superseded and moved to the archive.
>
> Canonical replacement: [PROJECT_STATUS.md](../../../PROJECT_STATUS.md)
> Reason: Point-in-time status audit snapshot, superseded by PROJECT_STATUS.md.
> Deprecation date: 2026-03-23

# Status Audit: Documented vs Actual (2026-03-16)

## Scope

This audit compares repository documentation status claims with implementation reality after security PRs #12 and #13.

## Executive Summary

- Phase 2 infrastructure is effectively complete in code.
- Phase 3 Open Library work is further along than previously documented.
- Security remediation executed in two passes and merged to `develop`.
- Dependabot still reports 8 open npm alerts despite lock graph showing patched-or-newer versions, indicating dependency graph processing lag or scanner interpretation lag.

## Verified Actual State

### Security Work

- PR #12 merged: https://github.com/Swartdraak/Bibliophilarr/pull/12
- PR #13 merged: https://github.com/Swartdraak/Bibliophilarr/pull/13
- `package.json` no longer uses `resolutions` overrides.
- Current lock graph values for flagged families are patched-or-newer:
  - `glob`: `10.5.0`
  - `immutable`: `4.3.8`
  - `minimatch`: `3.1.5`, `5.1.9`, `9.0.9`, `10.2.4`
  - `postcss`: `8.4.47`, `8.4.48`
  - `serialize-javascript`: not present
- Frontend build passes:
  - `yarn build`

### Metadata / Provider Implementation

- Open Library primary search integrated.
- ISBN/ASIN lookup integrated and tested.
- Provider fallback orchestration implemented.
- Provider telemetry and health endpoint implemented.
- Provider config API + UI implemented.
- Google Books and Hardcover fallback providers implemented.

## Documentation Drift Found

- `PROJECT_STATUS.md` previously under-reported Phase 3 progress (ISBN/ASIN lookup and rate limiting were implemented but unchecked).
- Security section previously only referenced PR #12 and not pass-2 remediation in PR #13.

## Updates Applied in This Run

- Updated `PROJECT_STATUS.md`:
  - Added PR #13 merge details.
  - Added post-pass-2 Dependabot verification status.
  - Corrected Phase 3 checklist for implemented items (ISBN/ASIN lookup, rate limiting, comprehensive testing).
- Updated `docs/operations/dependabot-alert-remediation-2026-03-16.md`:
  - Added pass-2 no-resolution remediation details.
  - Added merged PR #13 and current lock graph evidence.
  - Added post-merge verification notes.

## Immediate Next Implementation Priorities

1. Trigger/await GitHub dependency graph refresh and rerun Dependabot alerts query until state converges.
2. Add Inventaire provider implementation for Phase 4 multi-provider completeness.
3. Implement Open Library author detail retrieval path as a dedicated service call path.
4. Implement cover image handling/selection policy across Open Library, Hardcover, and Google Books fallbacks.
5. Add integration test coverage for dependency/health scenarios using deterministic fixtures and CI proof capture.

## Verification Commands

```bash
# Dependabot open alerts
GH_TOKEN="$GITHUB_TOKEN" gh api \
  -H "Accept: application/vnd.github+json" \
  "/repos/Swartdraak/Bibliophilarr/dependabot/alerts?state=open&per_page=100"

# Frontend build
yarn build

# Lock graph package versions
awk '
/^[^[:space:]].*:$/{key=$0}
/^  version "/{ver=$2;gsub(/"/,"",ver); if (key ~ /(^|[ ,"])glob@|immutable@|minimatch@|postcss@|serialize-javascript@/) print key " -> " ver}
' yarn.lock
```
