# Bibliophilarr Branch Strategy

## Goal

Enable low-touch, highly automated release management for a solo maintainer.

## Long-lived branches

- `main`: production-ready branch. Tagged releases are cut from here.
- `develop`: integration branch for feature work.
- `staging`: pre-release verification branch for release candidates.
- `release`: optional stabilization lane for last-mile fixes.
- `hotfix`: emergency patch lane based on production.

## Short-lived branches

- `feature/*`: new functionality and refactors.
- `fix/*`: non-emergency bug fixes.
- `release/*`: release candidate hardening.
- `hotfix/*`: urgent production issue fixes.

## Merge flow

1. `feature/*` -> `develop`
2. `develop` -> `staging`
3. `staging` -> `main`
4. Tag `main` with `vX.Y.Z` to trigger release publish workflows

## Automation expectations

- CI runs on PRs and pushes for `develop`, `staging`, `release`, `hotfix`, and `main`.
- Release build workflow runs on tags and manual dispatch.
- Release publish workflow creates draft GitHub releases.
- Docker workflow publishes images to GHCR on release tags.
- npm workflow publishes launcher package from `npm/bibliophilarr-launcher`.

## Required check strategy

- Branch protection on `develop`, `staging`, and `main` should require only checks that are emitted on every PR targeting that branch.
- Do not path-filter required workflows at the trigger level. If a gate is required for merges, the workflow must always report a terminal status so docs-only and operations-only PRs are not blocked waiting on a missing check.
- Prefer a consistently emitted required check over conditional required-check lists. If runtime cost becomes a problem later, keep the workflow trigger unconditional and optimize inside the workflow.

## Safety model for solo maintenance

- Prefer automated checks over manual review requirements.
- `develop` should not require approving reviews for merge. Green PRs merge based on required automated checks so the solo-maintainer flow does not depend on a second reviewer.
- Keep deployment actions gated by semantic version tags.
- Use draft release first, then publish after smoke validation.
- Keep rollback simple: retag previous known-good release and redeploy.

## Local initialization

Use the branch bootstrap script to initialize local branch lanes:

```bash
./scripts/init-branch-schema.sh
```

This script is idempotent and only creates missing local branches.
