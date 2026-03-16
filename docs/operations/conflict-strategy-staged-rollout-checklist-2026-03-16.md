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
