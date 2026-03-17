# Dependabot Remediation Runbook (2026-03-16)

## What Changed

Focused security branch created: `security/dependabot-8-alerts-2026-03-16`

Follow-up branch created: `security/dependabot-pass2-no-resolutions-2026-03-16`

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

### Pass 2 (No Resolution Overrides)

- Removed `resolutions` from `package.json`
- Updated direct dependencies to remove advisory entry paths without overrides:
  - `rimraf`: `6.0.1` -> `5.0.10`
  - `webpack`: `5.104.1` -> `5.105.4`
- Removed unused `postcss-url` dependency (carried vulnerable `minimatch@~3.0.4` chain)
- Regenerated `yarn.lock` and rebuilt frontend

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
- Follow-up PR merged to `develop`: https://github.com/Swartdraak/Bibliophilarr/pull/13
- Merge commit on default branch: `47cf259ee`
- Immediate and delayed `dependabot/alerts?state=open` API rechecks still report 8 alerts.

Current lock graph evidence on `develop`:

- `glob@^10.0.0, glob@^10.3.7` -> `10.5.0`
- `immutable@^4.0.0` -> `4.3.8`
- `minimatch@^3.1.2` -> `3.1.5`
- `minimatch@^5.1.0` -> `5.1.9`
- `minimatch@^9.0.4` -> `9.0.9`
- `minimatch@^10.2.2` -> `10.2.4`
- `postcss@8.4.47` / `postcss@8.4.48`
- `serialize-javascript`: not present in lock graph

Observed behavior suggests GitHub Dependabot processing lag or lockfile interpretation lag.
Re-run command used for verification:

```bash
GH_TOKEN="$GITHUB_TOKEN" gh api \
  -H "Accept: application/vnd.github+json" \
  "/repos/Swartdraak/Bibliophilarr/dependabot/alerts?state=open&per_page=100"
```

Automated triage support added:

- `scripts/dependabot_lockfile_triage.py`
- `.github/workflows/release-readiness-report.yml` (artifact: `dependabot-triage.md`)

Example run:

```bash
python3 scripts/dependabot_lockfile_triage.py \
  --owner Swartdraak \
  --repo Bibliophilarr \
  --lockfile yarn.lock \
  --md-out _artifacts/release-readiness/dependabot-triage.md \
  --json-out _artifacts/release-readiness/dependabot-triage.json
```

## Operational Impact

- No backend/runtime service behavior changes.
- Frontend build toolchain now uses newer lint/typecheck/plugin versions.
- No dependency resolution overrides remain in `package.json`.
- Frontend build remains green after pass 2.

## Rollback

If any downstream issue appears:

1. Revert the security branch commit(s):
   - `git revert <commit_sha>`
2. Restore previous lock and plugin stack:
   - `postcss-color-function`
   - previous `@typescript-eslint`, `fork-ts-checker-webpack-plugin`, `filemanager-webpack-plugin`, `terser-webpack-plugin`
3. Re-run `yarn install` and `yarn build` to verify rollback.
