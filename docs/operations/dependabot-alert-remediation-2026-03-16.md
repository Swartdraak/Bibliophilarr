# Dependabot Remediation Runbook (2026-03-16)

## What Changed

Focused security branch created: `security/dependabot-8-alerts-2026-03-16`

Updated frontend dependency graph to remediate the 8 open npm Dependabot alerts reported on `develop`:

- `glob` (high): patched to `11.1.0` in lock graph
- `immutable` (high): patched to `4.3.8`
- `minimatch` (high, multiple ranges): resolved to `10.2.4`
- `postcss` (moderate): removed legacy `postcss@6` source by replacing plugin stack
- `serialize-javascript` (high): patched to `7.0.3`

### Files changed

- `package.json`
  - Replaced `postcss-color-function` with `@csstools/postcss-color-function`
  - Upgraded build/lint dependencies:
    - `@typescript-eslint/eslint-plugin` -> `8.57.0`
    - `@typescript-eslint/parser` -> `8.57.0`
    - `fork-ts-checker-webpack-plugin` -> `9.1.0`
    - `filemanager-webpack-plugin` -> `9.0.1`
    - `terser-webpack-plugin` -> `5.4.0`
  - Added Yarn `resolutions` for vulnerable transitive chains:
    - `**/immutable`: `4.3.8`
    - `**/minimatch`: `10.2.4`
    - `**/serialize-javascript`: `7.0.3`
- `frontend/postcss.config.js`
  - Switched plugin from `postcss-color-function` to `@csstools/postcss-color-function`
- `yarn.lock`
  - Regenerated with patched transitive versions

## Why It Changed

`yarn.lock` on `develop` had eight open Dependabot alerts, including multiple vulnerable `minimatch` ranges and old `postcss`/`serialize-javascript` transitive versions. This update removes vulnerable versions from the lock graph while preserving current frontend behavior.

## Validation

### Build

- `yarn build` passes successfully after dependency updates.

### Lock Graph Checks

Validated lockfile entries for flagged packages:

- `glob@^11.0.0` -> `11.1.0`
- `immutable` -> `4.3.8`
- `minimatch` -> `10.2.4`
- `serialize-javascript` -> `7.0.3`
- No `postcss@^6.0.23` entry remains

### Post-Merge Verification

- PR merged to `develop`: https://github.com/Swartdraak/Bibliophilarr/pull/12
- Merge commit on default branch: `c5656a492`
- Immediate `dependabot/alerts?state=open` API recheck still reports 8 alerts.

Observed behavior suggests GitHub Dependabot processing lag or lockfile interpretation lag.
Re-run command used for verification:

```bash
GH_TOKEN="$GITHUB_TOKEN" gh api \
  -H "Accept: application/vnd.github+json" \
  "/repos/Swartdraak/Bibliophilarr/dependabot/alerts?state=open&per_page=100"
```

## Operational Impact

- No backend/runtime service behavior changes.
- Frontend build toolchain now uses newer lint/typecheck/plugin versions.
- Yarn emits compatibility warnings due resolution overrides on older transitive semver ranges; build remains green.

## Rollback

If any downstream issue appears:

1. Revert the security branch commit(s):
   - `git revert <commit_sha>`
2. Restore previous lock and plugin stack:
   - `postcss-color-function`
   - previous `@typescript-eslint`, `fork-ts-checker-webpack-plugin`, `filemanager-webpack-plugin`, `terser-webpack-plugin`
3. Re-run `yarn install` and `yarn build` to verify rollback.
