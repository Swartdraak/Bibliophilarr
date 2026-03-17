# Project Status Summary

**Last Updated**: March 17, 2026  
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

- Added manual and scheduled workflow support for:
  - `.github/workflows/release-readiness-report.yml`
  - `.github/workflows/branch-policy-audit.yml`
- Added supporting operational scripts:
  - `scripts/release_readiness_report.py`
  - `scripts/dependabot_lockfile_triage.py`
  - `scripts/audit_branch_protection.py`
- Added main-compatible staging smoke workflow support so the required smoke context is declared and can execute against both legacy and current branch layouts.
- Refreshed contributor and operator documentation for branch protection and release-readiness workflows.

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

- GitHub still reports 8 open Dependabot alerts, so dependency graph refresh and residual remediation remain active work.
- `main` can now host the manual readiness workflows, but broader release workflows are still aligned primarily with the active delivery lanes.
- Packaging validation is green on `develop` and `staging`; `main` is receiving the audit and readiness automation first so operators can dispatch reports from the default branch.

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
- [docs/operations/RELEASE_AUTOMATION.md](docs/operations/RELEASE_AUTOMATION.md)
- [docs/operations/release-readiness-report-2026-03-16.md](docs/operations/release-readiness-report-2026-03-16.md)
