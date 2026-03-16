# Phase 6 Packaging Validation Matrix

Date: 2026-03-16

## Goal

Start Phase 6 packaging validation across:
- native binary installation path
- Docker image installation path
- npm launcher installation path

## Workflow

Primary workflow:
- `.github/workflows/phase6-packaging-validation.yml`

Trigger model:
- weekly schedule (`Tue 04:00 UTC`)
- push to `develop` and `staging` for packaging-relevant paths
- manual workflow dispatch for targeted validation

Matrix lanes:
- `binary`
- `docker`
- `npm`

npm lane details:
- installs the packed launcher tarball into an isolated temp project
- writes logs/artifacts using `${GITHUB_WORKSPACE}` paths (runner-portable, no hardcoded absolute workspace)
- seeds the launcher cache for a pinned smoke tag (`v0.0.0-phase6-smoke` by default)
- executes `bibliophilarr --help` to validate the concrete launcher invocation path without requiring a published release asset during CI

## High-Verbosity Test Profile

Profile file:
- `tests/profiles/packaging-high-verbosity.config.xml`

Profile defaults:
- `LogLevel=trace`
- `ConsoleLogLevel=trace`
- explicit API key for authenticated endpoint checks

## Validation Signals

Per lane checks:
- install path completes
- runtime can start and respond to minimal health checks
- telemetry endpoint checks (binary lane)
- logs captured as artifacts

Artifact retention:
- 21 days (`actions/upload-artifact@v4`)

## Error Taxonomy

Script:
- `scripts/packaging_error_taxonomy.py`

Outputs per lane:
- `error-taxonomy.json`
- `error-taxonomy.md`

Categories:
- `network-timeout`
- `auth-failure`
- `bind-conflict`
- `startup-failure`
- `dependency-or-build`
- `unknown`

Deterministic fixture baseline:
- fixture text samples: `tests/fixtures/packaging-taxonomy/logs` (`*.txt` tracked)
- expected counts: `tests/fixtures/packaging-taxonomy/expected-taxonomy-counts.json`
- verifier: `scripts/test_packaging_error_taxonomy.py`

Unknown-share quality gate:
- workflow input: `max_unknown_share` (default `0.75`)
- taxonomy step fails when `unknownShare > maxUnknownShare`
- goal: block low-signal taxonomy reports before they become noisy operationally

## Taxonomy Triage Decision Tree

1. Is `unknownShareThresholdExceeded=true`?
yes: review `unknown` samples first; classify recurring signatures into an existing or new category.
no: proceed with normal per-category trend review.
2. Is one category dominating unexpectedly?
yes: verify lane-specific context (binary/docker/npm) before filing regressions.
no: track as baseline variation.
3. Are unknown samples mostly environmental noise (e.g. runner DNS jitter)?
yes: add ignore/normalization patterns with fixture evidence.
no: treat as genuine taxonomy gap and update `scripts/packaging_error_taxonomy.py`.

False-positive handling guidance:
- require at least one deterministic fixture line for any new pattern before merging.
- prefer specific regex fragments over broad `error|failed` catch-alls.
- if a pattern causes reclassification churn, keep old and new samples in fixtures until stable for 2 runs.

## Operational Notes

- The matrix now runs on schedule and packaging-path pushes for continuous signal.
- Keep `fail-fast=false` to preserve evidence from all lanes.
- Use workflow-dispatch for ad-hoc reruns with custom `npm_test_tag` or `max_unknown_share`.
- npm cache-seed execution behavior is validated via `scripts/test_npm_launcher_cache_seed.sh`.
