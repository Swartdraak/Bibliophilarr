# Bibliophilarr Release Automation

## Objective

Deliver installable and update-capable Bibliophilarr artifacts with minimal manual effort.

## Release channels

- GitHub Release assets (`vX.Y.Z` tags)
- Docker images (`ghcr.io/<owner>/bibliophilarr`)
- npm launcher package (`bibliophilarr`)

## Workflows

### 0) Release readiness report

File: `.github/workflows/release-readiness-report.yml`

Responsibilities:

- Snapshot protected-branch policy and review-count parity
- Report latest backend/docs/smoke/packaging workflow outcomes
- Summarize dependency security drift from Dependabot open-alert state
- Publish markdown/json artifacts for operator review

Trigger model:

- Daily schedule
- Manual dispatch

Supporting scripts:

- `scripts/release_readiness_report.py`
- `scripts/dependabot_lockfile_triage.py`
- `scripts/audit_branch_protection.py`

### 1) Release workflow

File: `.github/workflows/release.yml`

Responsibilities:

- Build matrix across Linux, macOS, and Windows
- Build backend + frontend
- Package per RID
- Archive assets
- Create draft GitHub Release with uploaded artifacts

Trigger model:

- Push tag: `v*`
- Manual dispatch with tag input

### 2) Docker image workflow

File: `.github/workflows/docker-image.yml`

Responsibilities:

- Build production image from repository Dockerfile
- Build multi-arch image (`linux/amd64`, `linux/arm64`)
- Optionally push to GHCR
- Support local smoke validation in dispatch mode

Trigger model:

- Push tag: `v*`
- Manual dispatch (`push` true/false)

### 3) npm publish workflow

File: `.github/workflows/npm-publish.yml`

Responsibilities:

- Publish launcher package from `npm/bibliophilarr-launcher`
- Align package version with release tag or manual input

Trigger model:

- Published GitHub release
- Manual dispatch with explicit version

### 4) Branch policy audit workflow

File: `.github/workflows/branch-policy-audit.yml`

Responsibilities:

- Detect required-check drift between branch protection and emitted CI contexts
- Confirm protected-branch review-count parity
- Upload machine-readable and markdown audit artifacts

Trigger model:

- Weekly schedule
- Manual dispatch

## Required repository secrets

- `NPM_TOKEN`: npm registry publish token

For GHCR publishing, `GITHUB_TOKEN` is used by default.

## Workflow dispatch commands (GitHub CLI)

Prerequisite: authenticated `gh` session (`gh auth login`) or `GH_TOKEN` set.

```bash
# Branch bootstrap (idempotent)
gh workflow run "Branch Bootstrap" --repo <owner>/Bibliophilarr

# Branch policy audit
gh workflow run "Branch Policy Audit" --repo <owner>/Bibliophilarr

# Release readiness report
gh workflow run "Release Readiness Report" --repo <owner>/Bibliophilarr

# Release workflow (manual)
gh workflow run "Bibliophilarr Release" \
  --repo <owner>/Bibliophilarr \
  -f tag=v0.1.0 -f draft=true

# Docker image workflow (build only)
gh workflow run "Bibliophilarr Docker Image" \
  --repo <owner>/Bibliophilarr \
  -f push=false

# Docker image workflow (build + push)
gh workflow run "Bibliophilarr Docker Image" \
  --repo <owner>/Bibliophilarr \
  -f push=true

# npm publish workflow
gh workflow run "Bibliophilarr npm Publish" \
  --repo <owner>/Bibliophilarr \
  -f version=0.1.0

# Required-check emission smoke sandbox
gh workflow run "Required Check Emission Smoke" \
  --repo <owner>/Bibliophilarr \
  -f base_branch=develop
```

## Secrets and variables matrix

| Name | Scope | Required | Used by | Notes |
|---|---|---|---|---|
| `NPM_TOKEN` | GitHub Actions secret | Yes (npm publish) | `.github/workflows/npm-publish.yml` | npm access token with publish permissions for `bibliophilarr` package. |
| `GITHUB_TOKEN` | Built-in Actions token | Auto | `.github/workflows/release.yml`, `.github/workflows/docker-image.yml`, `.github/workflows/branch-bootstrap.yml` | Used for release creation, branch API operations, and GHCR auth. |
| `Bibliophilarr__Postgres__Host` | Runtime env var | Optional | app runtime/tests | Preferred PostgreSQL host key during rename migration. |
| `Bibliophilarr__Postgres__Port` | Runtime env var | Optional | app runtime/tests | Preferred PostgreSQL port key during rename migration. |
| `Bibliophilarr__Postgres__User` | Runtime env var | Optional | app runtime/tests | Preferred PostgreSQL user key during rename migration. |
| `Bibliophilarr__Postgres__Password` | Runtime env var | Optional | app runtime/tests | Preferred PostgreSQL password key during rename migration. |
| `Bibliophilarr__Postgres__MainDb` | Runtime env var | Optional | app runtime/tests | Preferred PostgreSQL main DB key during rename migration. |
| `Bibliophilarr__Postgres__LogDb` | Runtime env var | Optional | app runtime/tests | Preferred PostgreSQL log DB key during rename migration. |
| `Bibliophilarr__Postgres__CacheDb` | Runtime env var | Optional | app runtime/tests | Preferred PostgreSQL cache DB key during rename migration. |
| `Bibliophilarr__Postgres__Host` | Runtime env var | Optional | app runtime/tests | Keep compatibility prefix during migration; set only for PostgreSQL mode. |
| `Bibliophilarr__Postgres__Port` | Runtime env var | Optional | app runtime/tests | Default `5432` in PostgreSQL mode. |
| `Bibliophilarr__Postgres__User` | Runtime env var | Optional | app runtime/tests | PostgreSQL user. |
| `Bibliophilarr__Postgres__Password` | Runtime env var | Optional | app runtime/tests | PostgreSQL password. |
| `Bibliophilarr__Postgres__MainDb` | Runtime env var | Optional | app runtime/tests | Main database name. |
| `Bibliophilarr__Postgres__LogDb` | Runtime env var | Optional | app runtime/tests | Log database name. |
| `Bibliophilarr__Postgres__CacheDb` | Runtime env var | Optional | app runtime/tests | Cache database name. |
| `ASPNETCORE_URLS` | Runtime env var | Optional | Docker/local run | Defaults to `http://+:8787` in container flow. |
| `BIBLIOPHILARR_OWNER` | Runtime env var | Optional | npm launcher | Defaults to `Swartdraak`. |
| `BIBLIOPHILARR_REPO` | Runtime env var | Optional | npm launcher | Defaults to `Bibliophilarr`. |
| `BIBLIOPHILARR_VERSION` | Runtime env var | Optional | npm launcher | Defaults to `latest`. |
| `SENTRY_AUTH_TOKEN` | Runtime/CI env var | Optional | Azure pipeline | Only needed if Sentry upload/release steps are used. |
| `SENTRY_ORG` | Runtime/CI env var | Optional | Azure pipeline | Sentry org identifier. |
| `SENTRY_URL` | Runtime/CI env var | Optional | Azure pipeline | Self-hosted Sentry base URL if applicable. |

## Release procedure

1. Merge validated changes to `main`.
2. Create and push tag `vX.Y.Z`.
3. Wait for `release.yml` to complete.
4. Review draft release notes/assets.
5. Publish release.
6. Validate Docker image and npm launcher channel.

## Local verification checklist

- `./build.sh --backend -r linux-x64 -f net8.0`
- `./build.sh --frontend`
- `./build.sh --packages -r linux-x64 -f net8.0`
- `docker build -t bibliophilarr:local .`
- `docker run --rm -d -p 8787:8787 bibliophilarr:local`
- `npm pack` in `npm/bibliophilarr-launcher`

## Rollback strategy

- Repoint deployment to prior image tag and prior release assets.
- Retag known-good commit and rerun release workflow.
- Keep release as draft until smoke checks pass.

## Merge reliability note

If `gh pr merge` returns policy-prohibited despite green checks and `MERGEABLE`, use:

- `scripts/merge_pr_reliably.sh`
- `docs/operations/gh-pr-merge-cli-mismatch-2026-03-16.md`
