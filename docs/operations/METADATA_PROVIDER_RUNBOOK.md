# Metadata Provider Outage and Fallback Runbook

## Purpose

This runbook defines how operators detect provider degradation, tune fallback behavior, and safely recover metadata operations when Open Library, Google Books, Hardcover, or Inventaire are unstable.

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
- `GET /api/v1/metadata/providers/health/basic`
- `GET /api/v1/metadata/providers/telemetry`
- `GET /api/v1/metadata/providers/telemetry/operations`

The health endpoint and System diagnostics include fallback-only providers,
such as Inventaire, even before their first request. The API sets
`isFallbackOnly` and uses a priority of `0` for these rows. The WebUI shows
their enabled state and book-search scope.

For services used only in automatic import fallback, check basic health and
the `fallback-search` operation. Operation counts distinguish matches,
successful empty lists, null responses and failed requests. Disabled
providers and requests skipped during cooldown do not add a call.
The fallback rechecks cooldown immediately after each rate-limit wait.
A successful request already in flight leaves an active cooldown in effect.

Hardcover basic health reports the shared GraphQL retry deadline for direct
lookups and fallback searches. Locally deferred requests use the active
deadline. Upstream rate-limit counts reflect actual responses. Eligible
providers can continue handling general searches during a hold. The saved
bibliography remains available while an author refresh is deferred.

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
2. Use the metadata-provider config/API toggles to disable the impacted provider when needed.
3. Open incident and track recovery checkpoints every 30 minutes.

### Interpretation

- High `failures` + increasing `fallbackHits`: provider degraded, fallback active.
- High `nullResults` with low failures: provider reachable but poor match quality.
- Low `hitRate` and high latency: reduce timeout/retry pressure and reorder providers.

## Immediate Mitigation Steps

1. Reduce blast radius by reordering providers:
   - Put most reliable provider first in `metadataProviderPriorityOrder`.
2. Temporarily disable failing provider:
   - Use the [provider switches](#provider-emergency-disablement) for the service that is failing.
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

In **Add New**, choose **Series** to search Hardcover by series name. This
scope sends one bounded search query and returns up to 20 provider summaries.
Choose **View books** to browse up to 50 active books in order from one
membership query. The normal Add Book action resolves the selected work and
its editions. A successful empty response shows no matches. An unavailable
response gives retry guidance. The default
**Authors and books** scope continues to use the combined lookup. [Hardcover
search operation](https://github.com/hardcoverapp/hardcover-sdk/blob/9e5b90f3e76ed16ff00973c2088fbe0917a86be6/shared/operations/queries/search.graphql)

Hardcover author refresh continues with verified works if the optional series
request fails. It preserves existing series links until the provider returns a
complete membership response. The first verification request checks up to 500
membership rows against the provider's aggregate count.

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

Use these totals to check whether the catalogue has any series data. For an
individual author, compare the API's member links and positions with a known
series from the provider. If the provider supplies matching series data and a
successful refresh leaves the links missing, record the provider, author ID,
and work IDs for investigation.

## Incomplete author bibliographies

Inspect the author refresh command and its provider log before starting another
scan. A premature end to pagination can leave a refresh unsuccessful while
existing author metadata remains available. Record the provider, the page offset,
the number of distinct entries returned, and the reported total when the log
includes them. An HTTP success on the last page does not establish that the
provider returned the advertised bibliography.

Check provider health for any active rate limit. Wait until the reported retry
time before refreshing the affected author again. Check the author's work and
series lists after the retry and use the Files view to verify that existing ebook
and audiobook assignments are still present. Preserve the import holds while
the provider response is incomplete or work identity is uncertain.

## Shared bibliography credits

Refresh a tracked Hardcover author whose provider bibliography credits them as an author of a work already owned by another tracked author. Compare `GET /api/v1/book?authorId=<id>` with `GET /api/v1/book?authorId=<id>&includeShared=true`. The second response also includes the shared canonical work with its original owner, work ID, editions and monitoring policy. Bibliography membership requires an explicit author credit.

An Open Library author's refresh can add unclaimed Hardcover works after
Bibliophilarr verifies both author identities. Each added work needs an explicit
Hardcover author credit. The author's metadata profile still applies, and
Open Library remains the source for the primary author record and series.

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

## Unmapped files held for author review

Search the debug log for `Import hold LowConfidence` and the file path. The
entry shows the proposed work and edition IDs, the matching distance and the
threshold for automatically adding an author. Compare the proposed identity
with the file before selecting the author manually.

## Audiobook part holds

`DuplicateInScan` indicates that another file occupied the same audiobook edition
and part. An automatic retry requires an original track tag that confirms the
stored part and a free slot in the selected edition. A later match or read
failure leaves the occupied-part hold in Unmapped Files. System events and the
task result report the failure from that scan.

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

## Provider emergency disablement

For emergency provider disablement, use the persisted metadata-provider configuration exposed in the UI and API:

- `enableHardcoverFallback` maps to **Enable Hardcover Provider** and controls
  Hardcover metadata requests.
- `enableOpenLibraryProvider`
- `enableGoogleBooksProvider` and `enableGoogleBooksFallback` control the Google
  Books provider and its separate import fallback. Turn both off to pause Google
  Books metadata requests.
- `enableInventaireFallback`

The **Enable Inventaire** setting controls title and author fallback during automatic import. The configuration API still reads and writes `enableInventaireProvider` for compatibility with existing clients. That deprecated field has no effect on provider requests.

### Rollout Policy

- Authorized operators: release manager, on-call maintainer, or incident commander.
- Activation window: immediate during P1/P2 metadata degradation.
- Rollback target: restore the provider setting within 2 hours after telemetry recovers below warning thresholds for at least 30 continuous minutes.
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

The native integration suite uses the real provider adapters with local
responses to verify Hardcover HTTP 401 fallback, Open Library results, and
metadata settings after restart. Its minimum-length check verifies that
a two-character search produces no provider traffic. Use the separate
credentialed browser workflow for a live provider check.
[Browser workflow](../../tests/browser/CATALOGUE.md)

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
