# Metadata Provider Outage and Fallback Runbook

## Purpose

This runbook defines how operators detect provider degradation, tune fallback behavior, and safely recover metadata operations when Open Library, BookInfo, or Inventaire are unstable.

## Scope

- Metadata search and lookup flows (search/add/refresh/import-list).
- Provider health diagnostics endpoint.
- Runtime resilience settings and provider order controls.

## Prerequisites

- Instance admin access.
- API key with read/write config permission.
- Access to status page and logs.

## Health Signals

Check provider diagnostics first:

- `GET /api/v1/metadata/providers/health`
- `GET /api/v1/metadata/providers/telemetry`

Important counters:

- `calls`
- `successes`
- `failures`
- `nullResults`
- `fallbackHits`
- `hitRate`

## Telemetry SLO Thresholds

Evaluate over a 24-hour rolling window per provider and across aggregate metadata requests.

Target thresholds:

- fallback-hit rate: `< 15%`
- failure rate: `< 5%`
- hit rate: `>= 80%`

Alert thresholds:

- warning: fallback-hit rate `>= 15%` OR failure rate `>= 5%` OR hit rate `< 80%`
- critical: fallback-hit rate `>= 30%` OR failure rate `>= 10%` OR hit rate `< 65%`

Action at warning:

1. Confirm whether one provider dominates failures.
2. Reorder providers to route around degradation.
3. Reduce timeout/retry settings to protect API responsiveness.

Action at critical:

1. Disable impacted provider immediately.
2. Enable environment kill-switch for Inventaire when needed.
3. Open incident and track recovery checkpoints every 30 minutes.

### Interpretation

- High `failures` + increasing `fallbackHits`: provider degraded, fallback active.
- High `nullResults` with low failures: provider reachable but poor match quality.
- Low `hitRate` and high latency: reduce timeout/retry pressure and reorder providers.

## Immediate Mitigation Steps

1. Reduce blast radius by reordering providers:
   - Put most reliable provider first in `metadataProviderPriorityOrder`.
2. Temporarily disable failing provider:
   - `enableOpenLibraryProvider` or `enableInventaireProvider`.
3. Tighten timeout and retry settings:
   - Lower `metadataProviderTimeoutSeconds`.
   - Lower `metadataProviderRetryBudget` during sustained outage.
4. Increase circuit-breaker sensitivity for hard outages:
   - Lower `metadataProviderCircuitBreakerThreshold`.
   - Keep `metadataProviderCircuitBreakerDurationSeconds` non-zero to avoid request storms.

## Inventaire Kill-Switch

For emergency environment-level disablement:

- `BIBLIOPHILARR_DISABLE_INVENTAIRE=1`
- `READARR_DISABLE_INVENTAIRE=1`

This forces `EnableInventaireProvider` to resolve as disabled at runtime regardless of stored config.

### Rollout Policy

- Authorized operators: release manager, on-call maintainer, or incident commander.
- Activation window: immediate during P1/P2 metadata degradation.
- Rollback target: remove kill-switch within 2 hours after telemetry recovers below warning thresholds for at least 30 continuous minutes.
- Change record: document activation and rollback timestamps in incident notes and release-entry checklist.

## Recommended Tuning Profiles

### Degraded external provider

- Timeout: `10`
- Retry budget: `1`
- Circuit threshold: `2`
- Circuit duration: `60`

### Stable environment

- Timeout: `30`
- Retry budget: `2`
- Circuit threshold: `5`
- Circuit duration: `60`

## Validation Checklist

1. Confirm diagnostics endpoint shows expected provider enablement and counters.
2. Execute a known book search and verify fallback behavior in logs.
3. Run import-list sync and confirm no unresolved-ID book additions.
4. Confirm API tests and targeted core fixtures pass in CI for metadata changes.

## Checkpoint Recording

Record dated telemetry checkpoints under `docs/operations/metadata-telemetry-checkpoints/`.

Each checkpoint should include:

1. Time window evaluated.
2. Per-provider fallback-hit, failure, and hit-rate values.
3. Warning/critical threshold verdict and operator action taken.

## Rollback

1. Restore previous provider order and enablement flags.
2. Restore prior resilience values.
3. Remove emergency environment kill-switch if set.
4. Re-run health and telemetry checks to verify normalization.

## References

1. [MIGRATION_PLAN.md](../../MIGRATION_PLAN.md) — provider ordering and migration constraints.
2. [PROJECT_STATUS.md](../../PROJECT_STATUS.md) — current rollout posture and validation state.
3. [Open Library developer documentation](https://openlibrary.org/developers/api) — provider API behavior.
4. [Inventaire API documentation](https://api.inventaire.io/) — secondary provider behavior.
