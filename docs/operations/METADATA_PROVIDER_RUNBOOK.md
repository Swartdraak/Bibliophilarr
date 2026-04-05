# Metadata Provider Outage and Fallback Runbook

## Purpose

This runbook defines how operators detect provider degradation, tune fallback behavior, and safely recover metadata operations when Open Library, Google Books, BookInfo, or Inventaire are unstable.

## Scope

- Metadata search and lookup flows (search/add/refresh/import-list).
- Provider health diagnostics endpoint.
- Runtime resilience settings and provider order controls.

OpenLibrary-only migration notes:

- Legacy Goodreads provider implementations were removed from active runtime paths.
- Identifier troubleshooting must use OpenLibrary-first identifiers (`olid`, ISBN, and provider foreign IDs).
- If operators encounter stale automation or custom integrations still using Goodreads key names, treat this as migration drift and remediate before release entry.

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

Enforcement policy:

- Warning or critical verdicts are release-entry blockers until resolved or explicitly accepted.
- Promotion decisions must reference the latest dated checkpoint under
   `docs/operations/metadata-telemetry-checkpoints/`.
- Release workflow gating consumes these verdicts through
   `scripts/release_entry_gate.py`.

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
5. Validate Google Books runtime controls when fallback coverage is needed:
   - Ensure `enableGoogleBooksProvider` is true.
   - Ensure `enableGoogleBooksFallback` is true.
   - Configure `googleBooksApiKey` for higher quota and better response consistency.

## Google Books configuration checklist

Use this checklist after metadata-provider config changes or incident recovery:

1. Read current settings from `GET /api/v1/config/metadataprovider`.
2. Confirm `enableGoogleBooksProvider` and `enableGoogleBooksFallback` are set as intended.
3. Confirm `googleBooksApiKey` is populated in secure settings storage when quota-sensitive workloads are expected.
4. Confirm provider ordering includes GoogleBooks where intended in `metadataProviderPriorityOrder`.
5. Run a known book lookup and verify provider telemetry increments for GoogleBooks operation counters.

## Series metadata validation

Open Library can provide series information through search-document fields. Bibliophilarr maps these fields into
book series links and author series during refresh.

Validation steps:

1. Refresh an author with known series data.
2. Query `GET /api/v1/book?authorId=<id>` and verify `seriesTitle` values are populated.
3. Query `GET /api/v1/series?authorId=<id>` and verify expected link counts and positions.
4. In UI naming preview, validate series tokens resolve for titles with linked series:
   - `{Book Series}`
   - `{Book SeriesPosition}`
   - `{Book SeriesTitle}`

### Series source caveat (OpenLibrary works feed)

For the `authors/{authorId}/works.json` path, `entries[]` commonly do not include a `series` field.
Series enrichment is expected to come from search-document fields (`series`, `series_with_number`) and
must be explicitly requested in OpenLibrary search field selection.

Operator verification:

1. Sample the live works feed for a known author and confirm whether `entries[].series` exists.
2. If works feed lacks `series`, verify application logs include search requests that request
   `series,series_with_number` fields.
3. Re-run author refresh and confirm `Series` / `SeriesBookLink` rows are no longer zero.

Recommended DB verification query:

```sql
SELECT
  (SELECT COUNT(*) FROM "Series") AS series_count,
  (SELECT COUNT(*) FROM "SeriesBookLink") AS series_link_count;
```

If both counts remain zero after refresh, treat this as a metadata ingestion defect and escalate to
the metadata migration backlog.

## Rename no-op diagnosis (forced rename)

Forced rename can execute correctly but still produce no filesystem changes when generated destination
paths match existing source paths.

Expected evidence in logs:

- Command execution entries such as `Renaming all files for selected author`.
- No-op entries such as `File not renamed, source and destination are the same`.
- Optional mixed outcomes where some files are renamed and others are unchanged.

Important behavior notes:

- Rename path generation uses DB metadata + naming tokens, not tag-writing side effects.
- If series links are missing (`SeriesBookLink = 0`), series tokens may collapse to empty components.
- Missing edition linkage can reduce rename coverage for affected files.

Operator triage steps:

1. Confirm naming config is enabled:
   - `NamingConfig.RenameBooks = 1`
   - standard format contains intended tokens.
2. Trigger forced rename for a controlled author sample.
3. Inspect logs for changed vs unchanged outcomes.
4. Validate metadata linkage before re-running rename:
   - series links present for series-token usage.
   - edition linkage present for renamed file set.

Recommended SQL checks:

```sql
SELECT "RenameBooks", "StandardBookFormat"
FROM "NamingConfig"
LIMIT 1;

SELECT
  COUNT(*) AS total_files,
  SUM(CASE WHEN "EditionId" IS NULL OR "EditionId" = 0 THEN 1 ELSE 0 END) AS missing_edition_links
FROM "BookFiles";
```

When forced rename is mostly unchanged due to identical paths, this is expected behavior and not a
pipeline failure. Escalate only if logs show rename command execution without either no-op or success
per-file outcomes.

## Queued RescanFolders triage (migration-safe)

Use this playbook when command queues grow with repeated `RescanFolders` entries and
operators observe scan-loop behavior during author refresh windows.

Detection signals:

- `Commands` table contains many `RescanFolders` rows in queued/started states.
- Logs repeatedly show refresh completions without meaningful metadata deltas.
- Metadata-provider failures increase during the same window (`get-book-info`,
  `get-author-info`) and command queue depth trends upward.

SQL triage queries:

```sql
-- Current queued/started RescanFolders pressure
SELECT "Status", COUNT(*)
FROM "Commands"
WHERE "Name" = 'RescanFolders'
GROUP BY "Status";

-- Recent refresh command throughput and runtime
SELECT "Id", "Name", "Status", "Trigger", "Duration", "Started", "Ended"
FROM "Commands"
WHERE "Name" = 'RefreshAuthor'
ORDER BY "Id" DESC
LIMIT 20;
```

Log triage patterns:

- `Skipping rescan. Reason: matching rescan command already queued or started`
- `All providers failed for operation get-book-info`
- `All providers failed for operation get-author-info`

Operator actions (safe for migration):

1. Confirm provider outage/degradation first using telemetry endpoints before
   changing refresh cadence.
2. Keep refresh running on authoritative IDs; avoid manual DB edits to remove
   books/authors during a degraded-provider window.
3. If queue pressure continues, temporarily reduce refresh concurrency by
   pausing nonessential scheduled command triggers and resume when provider
   telemetry recovers.
4. Capture command and telemetry snapshots in `_artifacts/` and attach them to
   release-entry evidence before promoting.

## Conflict-resolution explainability triage

Use this section when metadata merges pick unexpected winners and operators need
to understand why a provider was selected.

Primary endpoint:

- `GET /api/v1/metadata/conflicts/telemetry`

Primary fields:

- `decisionsByReason`
- `decisionsByProvider`
- `fieldSelectionsByProvider`
- `lastDecisionScoreBreakdownByProvider`

How to read score breakdown values:

- Values are emitted per provider as comma-separated `factor:points` entries.
- Typical high-impact factors include:
  - `title`
  - `author`
  - `foreign-book-id`
  - `has-editions`
  - `cover-images`
- Higher totals generally indicate richer metadata completeness and are used by
   conflict policy quality-score selection.

Operator triage workflow:

1. Check `decisionsByReason` to confirm whether `quality-score` is driving outcomes.
2. Compare `lastDecisionScoreBreakdownByProvider` values for the selected provider versus fallback providers.
3. If selected provider repeatedly wins with lower identifier-related factors, inspect provider payload quality and identifier mapping paths.
4. Correlate with provider health telemetry before changing provider order to avoid masking upstream outages as quality regressions.

Escalation criteria:

- Open an incident when unexpected winner shifts persist for more than two telemetry checkpoints.
- Attach conflict telemetry snapshot and matching provider operation telemetry to release-entry artifacts.

## Inventaire Kill-Switch

For emergency environment-level disablement:

- `BIBLIOPHILARR_DISABLE_INVENTAIRE=1`

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
5. Confirm no legacy Goodreads symbols are present in guarded source paths via release-entry gate output.

## Removed legacy features

- Goodreads import-list providers are removed.
- Goodreads notification providers are removed.
- Goodreads-specific metadata proxy implementations are removed.

Operational response if stale configs remain:

1. Remove or disable any persisted provider entries whose implementation name references Goodreads.
2. Re-run provider settings validation and metadata diagnostics endpoints.
3. Re-run import-list sync and verify no unresolved legacy identifier-only entries are added.

## Checkpoint Recording

Record dated telemetry checkpoints under `docs/operations/metadata-telemetry-checkpoints/`.

Each checkpoint should include:

1. Time window evaluated.
2. Per-provider fallback-hit, failure, and hit-rate values.
3. Warning/critical threshold verdict and operator action taken.

The checkpoint must include a machine-readable line in this format:

- `Overall threshold verdict: PASS|WARNING|CRITICAL|BLOCKED`

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
