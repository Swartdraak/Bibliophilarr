# Metadata migration dry run

## Purpose

Define the dry-run process used to validate metadata provenance changes before
release-entry decisions.

## When to run it

Run a dry run when a change affects:

- provider ordering or enablement
- provider mapping logic
- identifier backfill or provenance fields
- metadata fallback behavior
- release-entry validation for metadata readiness

## Required inputs

- staging or equivalent non-production environment
- `BIBLIOPHILARR_STAGING_BASE_URL`
- `BIBLIOPHILARR_API_KEY`
- a representative lookup sample size

## Command

```bash
python3 scripts/metadata_migration_dry_run.py \
  --base-url "$BIBLIOPHILARR_STAGING_BASE_URL" \
  --api-key "$BIBLIOPHILARR_API_KEY" \
  --max-lookups 200
```

## Required artifacts

Archive the generated evidence under `_artifacts/metadata-dry-run/` and record
a dated snapshot under `docs/operations/metadata-dry-run-snapshots/`.

Minimum evidence:

1. `before.json`
2. `after.json`
3. `summary.json`
4. snapshot markdown with pass, fail, or blocked verdict

## Acceptance gates

The latest snapshot passes only when all of the following are true:

1. provenance fields are populated as expected for the sampled titles
2. fallback routing does not regress known-good lookups
3. unresolved external-ID additions do not increase unexpectedly
4. any blocked or degraded providers are explicitly documented

## Blocked runs

If the environment lacks required secrets or connectivity, create a dated
blocked snapshot instead of silently skipping the evidence trail.

## Related evidence

- [metadata-dry-run-snapshots/2026-03-17-blocked.md](metadata-dry-run-snapshots/2026-03-17-blocked.md)
- [metadata-dry-run-snapshots/2026-03-18-blocked.md](metadata-dry-run-snapshots/2026-03-18-blocked.md)
- [METADATA_PROVIDER_RUNBOOK.md](METADATA_PROVIDER_RUNBOOK.md)
- [PROJECT_STATUS.md](../../PROJECT_STATUS.md)

## References

1. [scripts/metadata_migration_dry_run.py](../../scripts/metadata_migration_dry_run.py) — dry-run script authority.
2. [PROJECT_STATUS.md](../../PROJECT_STATUS.md) — release-entry metadata readiness gate.
3. [MIGRATION_PLAN.md](../../MIGRATION_PLAN.md) — migration ordering and provenance context.