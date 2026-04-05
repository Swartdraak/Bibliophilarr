> [!WARNING]
> **DEPRECATED** — This document has been superseded and moved to the archive.
>
> Canonical replacement: [ROADMAP.md](../../../ROADMAP.md)
> Reason: One-time staged-rollout checklist for completed conflict resolution; superseded by ROADMAP.md.
> Deprecation date: 2026-03-23

# Conflict Strategy Variants Staged Rollout Checklist

Date: 2026-03-16

## Goal

Enable `EnableMetadataConflictStrategyVariants` in a controlled non-production sequence before any production rollout.

## Preconditions

- Backend CI is green for `Bibliophilarr.Common.Test`, `Bibliophilarr.Host.Test`, and `Bibliophilarr.Api.Test`.
- Frontend build is green.
- Docs validation is green.
- Provider health endpoint is reachable at `GET /api/v1/metadata/providers/health`.
- Conflict telemetry endpoint is reachable at `GET /api/v1/metadata/conflicts/telemetry`.
- Protected branches that can change the rollout flag require the `Staging Smoke Metadata Telemetry / smoke-metadata-telemetry` status check before merge.

## Staging Rollout Steps

1. Keep `EnableMetadataConflictStrategyVariants=false` in production.
2. Enable the flag in one staging environment only.
3. Capture a pre-enable snapshot from both health and conflict telemetry endpoints.
4. Run targeted metadata aggregation regression tests in staging traffic window.
5. Replay a bounded sample with `scripts/live_provider_enrich_missing_metadata.py --sample-size <n> --sample-seed <seed>`.
6. Compare provider winner distribution, selected cover distribution, unresolved count, timeout count, and `no-candidates` trend.
7. Leave the flag enabled for at least one sustained provider polling window.
8. Review operator feedback for unexpected winner shifts or cover regressions.

## Success Criteria

- No sustained increase in `no-candidates` decisions.
- No sustained increase in provider timeout or cooldown events.
- No replay regression in accepted vs unresolved trend.
- Winner distribution changes are explainable by provider precedence policy rather than unexpected quality regression.

## Rollback

- Set `EnableMetadataConflictStrategyVariants=false`.
- Re-capture health and conflict telemetry snapshots.
- Confirm tie-break decisions return to stable default behavior.
- Retain replay artifacts and endpoint snapshots for comparison.

## Execution Record (2026-03-16)

Execution scope:
- Staging-like isolated local cohort (`/tmp/bibliophilarr-baseline-20260316`) with dedicated API key and appdata.

Recorded evidence:
- PR #15 merge confirmed (`31512e026`).
- Baseline endpoint capture pre-toggle:
  - `GET /api/v1/metadata/providers/health` => `200 OK`
  - `GET /api/v1/metadata/conflicts/telemetry` => `200 OK`
  - telemetry payload: `totalDecisions=0`, `decisionsByReason={}`, `decisionsByProvider={}`, `fieldSelectionsByProvider={}`
- Feature flag enabled for cohort:
  - `EnableMetadataConflictStrategyVariants=true` verified via config endpoint.
- Bounded replay executed:
  - `python3 scripts/live_provider_enrich_missing_metadata.py --root /tmp/bibliophilarr-live-sample-2026-03-16 --sample-size 64 --sample-seed 20260316 --report-dir _artifacts/live-provider-enrich-2026-03-16-rollout-flag-on`
  - observed: discovered `0`, accepted `0`, unresolved `0`
- Curated non-empty replay cohort executed:
  - `python3 scripts/live_provider_enrich_missing_metadata.py --root tests/fixtures/replay-cohort --sample-size 64 --sample-seed 20260316 --report-dir _artifacts/live-provider-enrich-2026-03-16-curated-cohort`
  - observed: discovered `4`, accepted `1`, unresolved `3`
  - regression guard: `python3 scripts/replay_regression_guard.py ...` => `passed`

Gate outcomes:
- Gate 1 (test readiness): pass (targeted core + API tests passed)
- Gate 2 (operational readiness): pass (health + conflict endpoints reachable, authenticated)
- Gate 3 (data quality readiness): pass (curated non-empty cohort produced non-zero accepted/unresolved signal and passed threshold guard)
- Gate 4 (merge readiness): in progress for next PR (this change set)

Follow-up required:
- Run same bounded replay against curated non-empty fixture cohort (`tests/fixtures/replay-cohort`) in CI for meaningful accepted/unresolved trend tracking.
- Enforce `Staging Smoke Metadata Telemetry / smoke-metadata-telemetry` as a required status check before any further rollout-flag changes on `develop` or release branches.
