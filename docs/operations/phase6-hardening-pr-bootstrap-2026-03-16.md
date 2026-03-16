# Phase 6 Hardening PR Bootstrap

Date: 2026-03-16

## Goal

Start a dedicated Phase 6 hardening branch and PR focused only on:
- packaging matrix stabilization
- packaging error taxonomy quality and signal quality

## Branch

- `phase6/hardening-packaging-taxonomy-2026-03-16`

## Scope In

- harden npm lane pinned-tag launcher validation behavior
- reduce false positives and ambiguous classification in `scripts/packaging_error_taxonomy.py`
- tighten packaging artifact assertions for binary and docker lanes
- add focused tests and fixtures for taxonomy buckets and fallback behavior

## Scope Out

- metadata provider ranking or merge-policy logic changes
- rollout flag behavior changes
- unrelated frontend or localization work

## Precondition Evidence

- Phase 5/ops branch merged into `develop` by merge commit:
  - `2175a1d7a`
- PR #16 backend CI reached success before merge

## Blocking Operations Requiring GitHub Auth

The following are pending because this environment is currently unauthenticated for GitHub CLI/API (`gh auth status` reports no login):

1. Dispatch workflow on `develop`:
   - `.github/workflows/staging-smoke-metadata-telemetry.yml`
2. Set required branch protection check on `develop`:
   - `Staging Smoke Metadata Telemetry / smoke-metadata-telemetry`
3. Open the dedicated Phase 6 PR from the hardening branch to `develop`

Default-branch dispatch note:
- GitHub manual workflow dispatch resolves workflow files from the repository default branch (`main`).
- Because staging smoke is maintained on `develop`, dispatch by file path from CLI can return `404 workflow not found on the default branch`.
- Mitigation in this phase: run staging smoke on `pull_request` and `push` to `develop` so required-check evidence is produced without manual dispatch.

## Authenticated Execution Commands

```bash
# Authenticate once (or set GH_TOKEN)
gh auth login

# Dispatch staging smoke workflow on develop
gh workflow run .github/workflows/staging-smoke-metadata-telemetry.yml \
  -R Swartdraak/Bibliophilarr \
  --ref develop

# Require staging smoke check in develop branch protection
gh api -X PUT repos/Swartdraak/Bibliophilarr/branches/develop/protection/required_status_checks \
  -f strict=true \
  -f contexts[]='Staging Smoke Metadata Telemetry / smoke-metadata-telemetry'

# Push hardening branch and open PR
git push -u origin phase6/hardening-packaging-taxonomy-2026-03-16
gh pr create \
  -R Swartdraak/Bibliophilarr \
  --base develop \
  --head phase6/hardening-packaging-taxonomy-2026-03-16 \
  --title "chore(phase6): packaging matrix stabilization and taxonomy quality" \
  --body-file docs/operations/phase6-hardening-pr-bootstrap-2026-03-16.md
```

## Local Validation Already Completed

- workflow YAML lint pass for staging/replay/packaging workflows
- local smoke-equivalent endpoint validation completed for:
  - PR branch runtime
  - `develop` runtime (using a temporary worktree)

## Next Slice (PR Content)

1. add deterministic taxonomy fixtures for each category
2. add threshold for `unknown` taxonomy share to fail noisy runs
3. add normalization for repeated connection and DNS error signatures
4. add focused assertions for npm lane launcher output shape
5. document failure triage decision tree in Phase 6 runbook