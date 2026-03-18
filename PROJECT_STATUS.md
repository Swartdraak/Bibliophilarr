# Project Status Summary

**Last Updated**: March 18, 2026  
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
