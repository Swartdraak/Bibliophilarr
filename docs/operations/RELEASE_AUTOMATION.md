# Bibliophilarr Release Automation

## Objective

Describe the repository's current readiness and release-entry automation.

## Workflows

### 0) Release readiness report

File: `.github/workflows/release-readiness-report.yml`

Responsibilities:

- Snapshot protected-branch policy and review-count parity
- Report latest backend/frontend/docs/smoke workflow outcomes
- Summarize dependency security drift from Dependabot open-alert state
- Publish markdown/json artifacts for operator review

Trigger model:

- Daily schedule
- Manual dispatch

Supporting scripts:

- `scripts/release_readiness_report.py`
- `scripts/dependabot_lockfile_triage.py`
- `scripts/audit_branch_protection.py`

### 1) Branch policy audit workflow

File: `.github/workflows/branch-policy-audit.yml`

Responsibilities:

- Detect required-check drift between branch protection and emitted CI contexts
- Confirm protected-branch review-count parity
- Upload machine-readable and markdown audit artifacts

Trigger model:

- Weekly schedule
- Manual dispatch

### 2) Operational drift check

File: `.github/workflows/operational-drift-check.yml`

Responsibilities:

- Compare `develop`, `staging`, and `main` against explicit operational drift thresholds
- Confirm active delivery lanes remain close enough for reliable release promotion
- Confirm `main` readiness workflows stay fresh and successful
- Publish markdown and JSON artifacts for operator review

Trigger model:

- Weekly schedule
- Manual dispatch

### 3) Metadata migration dry run

File: `.github/workflows/metadata-migration-dry-run.yml`

Responsibilities:

- run dry-run metadata migration evidence capture
- publish dated artifacts for provenance review
- surface blocked runs when required secrets or staging access are unavailable

Trigger model:

- Manual dispatch

### 4) Backend and docs validation

Files:

- `.github/workflows/ci-backend.yml`
- `.github/workflows/ci-frontend.yml`
- `.github/workflows/docs-validation.yml`
- `.github/workflows/staging-smoke-metadata-telemetry.yml`

Responsibilities:

- validate backend, frontend, docs, and smoke metadata behavior on active lanes
- emit required protected-branch contexts
- provide the evidence consumed by readiness reporting

### 5) Package rehearsal

`package-rehearsal.yml` runs on packaging changes in same-repository pull
requests into `develop` and after each staging push. It can also be dispatched
on `develop` or `staging` once the workflow is on the default branch.
It uses the same package build and asset checks as `release.yml` and uploads
the result as the `release-assets` Actions artifact. It requires the Six
Labors build license and read-only repository permissions.

The routine pull request and staging rehearsals compare the Linux x64 and
Windows x64 packages with their published v1.1.0 portable archives. Each job
verifies the published checksum and candidate manifest, imports both formats
into a disposable profile, and checks migration, restart, and rollback using
the matching v1.1.0 backup. Each job records its candidate commit and uploads a
receipt, even when the other platform fails.

The separate `macos-predecessor` dispatch reproduces the published Apple Silicon
v1.1.0 startup failure. The Apple Silicon upgrade remains held until the
predecessor can boot and the same migration and rollback checks can run.

The rehearsal assembly version exercises data migration and recovery. Update
eligibility and installer migration need their own release-candidate checks.

## Current repository posture

The repository currently documents release-entry readiness, branch-policy audit,
operational drift, and metadata dry-run workflows. Release and publish workflows
are present in this repository (`release.yml`, `docker-image.yml`, and
`npm-publish.yml`), while readiness and branch-policy workflows remain the
authoritative operator decision inputs for promotion decisions.

Release packaging requires a passing release-entry report. The workflow reads
the dated operational snapshots from the verified source commit. Generate the
series snapshot against a disposable or staging database before promotion and
commit the results through the development branch. Database files belong outside
the repository and are not required on the hosted release runner.

## Workflow dispatch commands (GitHub CLI)

Prerequisite: authenticated `gh` session (`gh auth login`) or `GH_TOKEN` set.

```bash
# Branch policy audit
gh workflow run "Branch Policy Audit" --repo <owner>/Bibliophilarr

# Release readiness report
gh workflow run "Release Readiness Report" --repo <owner>/Bibliophilarr

# Metadata migration dry run
gh workflow run "Metadata Migration Dry Run" \
  --repo <owner>/Bibliophilarr
```

## Secrets and variables matrix

| Name | Scope | Required | Used by | Notes |
|---|---|---|---|---|
| `GITHUB_TOKEN` | Built-in Actions token | Auto | current GitHub Actions workflows | Used for workflow API access and artifact publication within granted permissions. |
| `SIXLABORS_LICENSE_KEY` | Actions secret | Required for licensed builds | `release.yml`, `package-rehearsal.yml` | Materialized during compilation and removed before artifact upload. |
| `NPM_TOKEN` | `npm-publish` environment secret | Required for npm publish | `npm-publish.yml` | npmjs.org access token for publishing the `bibliophilarr` launcher package. |
| `Bibliophilarr__Postgres__Host` | Runtime env var | Optional | app runtime/tests | Preferred PostgreSQL host key during rename migration. |
| `Bibliophilarr__Postgres__Port` | Runtime env var | Optional | app runtime/tests | Preferred PostgreSQL port key during rename migration. |
| `Bibliophilarr__Postgres__User` | Runtime env var | Optional | app runtime/tests | Preferred PostgreSQL user key during rename migration. |
| `Bibliophilarr__Postgres__Password` | Runtime env var | Optional | app runtime/tests | Preferred PostgreSQL password key during rename migration. |
| `Bibliophilarr__Postgres__MainDb` | Runtime env var | Optional | app runtime/tests | Preferred PostgreSQL main DB key during rename migration. |
| `Bibliophilarr__Postgres__LogDb` | Runtime env var | Optional | app runtime/tests | Preferred PostgreSQL log DB key during rename migration. |
| `Bibliophilarr__Postgres__CacheDb` | Runtime env var | Optional | app runtime/tests | Preferred PostgreSQL cache DB key during rename migration. |
| `ASPNETCORE_URLS` | Runtime env var | Optional | Docker/local run | Defaults to `http://+:8787` in container flow. |

> **Removed**: Sentry (`SENTRY_AUTH_TOKEN`, `SENTRY_ORG`, `SENTRY_URL`) variables
> were inherited from the legacy Azure Pipelines configuration and are no longer
> used. Bibliophilarr uses GitHub Actions exclusively.

## Release procedure

1. Promote `develop` to `staging` through a pull request. Promote `staging`
   to `main` through a second pull request after its checks and readiness
   criteria pass.
2. Review the latest readiness, branch-policy, and operational drift artifacts.
3. Tag the release: `git tag v<major>.<minor>.<patch> && git push origin v<major>.<minor>.<patch>`.
4. `release.yml` triggers automatically on the tag push: builds all platform binaries and publishes a GitHub Release with artifacts and SHA256 checksums.
5. `npm-publish.yml` triggers automatically on the same tag push and publishes the launcher to npmjs.org.
6. `docker-image.yml` triggers on the tag push in parallel with `release.yml`.
7. Verify GitHub Release assets, GHCR image tags, and npm package version after workflows complete.

The publication workflows verify the exact tag and source commit before building
release artifacts or publishing images and packages. Stable tags use
`vMAJOR.MINOR.PATCH` on the first-parent history of `main`. Staging candidates
use `vMAJOR.MINOR.PATCH-rc.N` on the first-parent history of `staging` and publish
to GitHub prereleases, npm `next`, and versioned GHCR tags.

For manual publication, dispatch from the exact tagged commit on `main`.
The main-branch Docker build-only dispatch can still run before tagging.

## Update branch configuration

New installations default to the `main` branch for application updates.
Users receive notifications when new stable releases are published.

### Available branches

- **main**: Stable releases (default, fully implemented)
- **master**: Alias for main (backward compatibility)
- **develop**: Development branch (configured but no automated builds published)
- **nightly**: Bleeding-edge branch (configured but no automated builds published)

Users can change their update branch in Settings > General > Updates > Branch.

The publication workflows accept stable `main` releases and tagged `staging`
release candidates. Update settings also accept `develop` and `nightly`.

See [wiki/Updates-and-Branches.md](../../wiki/Updates-and-Branches.md) for user-facing update documentation.

## Release entry criteria

Before tagging a release from `main`, all of the following must be true:

1. The latest `ci-backend.yml`, `ci-frontend.yml`, `docs-validation.yml`, and `staging-smoke-metadata-telemetry.yml` runs are successful on `develop`.
2. The latest `ci-backend.yml`, `ci-frontend.yml`, `docs-validation.yml`, and `staging-smoke-metadata-telemetry.yml` runs are successful on `staging`.
3. The latest `branch-policy-audit.yml` and `release-readiness-report.yml` runs are successful on `main`.
4. Required contexts on protected branches remain aligned with branch protection policy.
5. The latest dated install evidence in `docs/operations/install-test-snapshots` still reflects successful local installation validation.
6. Open security drift is either remediated or explicitly accepted with documented rationale.

Mandatory checklist flow:

1. Operator updates the latest dated entries in:

- `docs/operations/metadata-dry-run-snapshots/`
- `docs/operations/metadata-telemetry-checkpoints/`
- `docs/operations/install-test-snapshots/`
- `docs/operations/series-persistence-snapshots/`

2. Each snapshot must include explicit gate lines:

- `Verdict: PASS|WARNING|CRITICAL|BLOCKED` (dry-run snapshots)
- `Overall threshold verdict: PASS|WARNING|CRITICAL|BLOCKED` (telemetry checkpoints)
- `Overall matrix verdict: PASS|BLOCKED` (install snapshots)
- `Verdict: PASS|BLOCKED` (series persistence snapshots)
- `Series persistence verdict: PASS|FAIL` (series persistence snapshots)

3. Operator runs:

```bash
python3 scripts/release_entry_gate.py \
  --max-age-days 7 \
  --md-out _artifacts/release-entry/release-entry-gate.md \
  --json-out _artifacts/release-entry/release-entry-gate.json
```

Legacy symbol guard:

- `scripts/release_entry_gate.py` now enforces a source scan gate over:
  - `src/NzbDrone.Core`
  - `src/Bibliophilarr.Api.V1`
  - `frontend/src`
- Release promotion is blocked if the forbidden legacy symbol `goodreads` is detected in those guarded paths.
- The gate supports an explicit allowlist for approved compatibility shims via `--symbol-allowlist` (defaults include parser model alias files used for backward-compatible JSON deserialization).

4. Promotion is blocked unless the gate report returns `Overall: PASS`.
5. The `Release Entry Gate` job uploads the report. Packaging requires that job to pass.

Packaging scope note:

- Packaging validation has been completed on `main` with the v1.0.0 release.
- `release.yml` builds Linux x64/ARM64, macOS Intel/Apple Silicon, and Windows x64 packages. The release also includes the Windows x64 installer and checksums for all six assets.
- `docker-image.yml` builds multi-platform Docker images, pushes to GHCR, signs with cosign, and runs Trivy scanning.
- `npm-publish.yml` publishes the launcher package to npmjs.org when a release is published.

Operator decision note:

- If readiness or branch-policy workflows on `main` are permission-limited because the Actions integration token cannot read an admin or Dependabot endpoint, use the uploaded artifacts as the decision record rather than treating the limitation itself as a failed release gate.

## Local verification checklist

- `./build.sh`
- `docker build -t bibliophilarr:local .`
- `docker run --rm -d -p 8787:8787 bibliophilarr:local`
- `yarn build`

## Rollback strategy

- Revert the slice that broke readiness.
- Re-run readiness and drift reporting.
- Delay tagging until active readiness criteria are green again.

## Merge reliability note

If `gh pr merge` returns policy-prohibited despite green checks and `MERGEABLE`, use:

- `scripts/merge_pr_reliably.sh`

Recommended operator sequence:

1. Verify preconditions first:

- `mergeable == MERGEABLE`
- no pending checks in progress
- no failing required checks

2. Try `gh pr merge` first.
3. If `gh pr merge` still reports policy-prohibited while all preconditions are
   green, use the REST merge endpoint fallback:

```bash
gh api -X PUT repos/<owner>/Bibliophilarr/pulls/<PR_NUMBER>/merge \
  -f merge_method=merge
```

## References

1. [ROADMAP.md](../../ROADMAP.md) — release-hardening and promotion posture.
2. [PROJECT_STATUS.md](../../PROJECT_STATUS.md) — current readiness status.
3. [.github/workflows/release-readiness-report.yml](../../.github/workflows/release-readiness-report.yml) — current readiness workflow.
4. [.github/workflows/branch-policy-audit.yml](../../.github/workflows/branch-policy-audit.yml) — branch-policy audit workflow.
5. [.github/workflows/operational-drift-check.yml](../../.github/workflows/operational-drift-check.yml) — drift workflow.
6. [.github/workflows/metadata-migration-dry-run.yml](../../.github/workflows/metadata-migration-dry-run.yml) — dry-run workflow.
