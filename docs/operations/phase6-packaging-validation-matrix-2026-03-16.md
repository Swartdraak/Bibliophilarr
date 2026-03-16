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

Matrix lanes:
- `binary`
- `docker`
- `npm`

npm lane details:
- installs the packed launcher tarball into an isolated temp project
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

## Operational Notes

- The matrix is intentionally workflow-dispatch first.
- Keep `fail-fast=false` to preserve evidence from all lanes.
- Promote to scheduled runs after lane stability is demonstrated.
