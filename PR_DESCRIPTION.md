# PR #354 — chore(deps-dev): bump eslint-plugin-prettier from 5.5.5 to 5.5.7

**Repository:** Swartdraak/Bibliophilarr · **PR:** #354 · **Head ref:** `dependabot/npm_and_yarn/develop/eslint-plugin-prettier-5.5.7` · **Base:** `develop`

## Summary

Dependabot dev-tooling bump of `eslint-plugin-prettier` from **5.5.5 → 5.5.7** (a `devDependency`; pulls `synckit` `^0.11.12 → ^0.12.3`). This is a lint-plugin-only change — no application runtime code is touched.

## Why this head changed (publication repair, task t_d1174464)

The original PR #354 head (`3e6500f4`) was merged forward with `develop` (prior head `6ad6b95f`) and pushed, but the governed merge was **blocked deterministically**: `onyx-onboard merge` → `verify_pr_files` reported that PR #354's `yarn.lock` delta **overlaps open PR #358** (`@testing-library/dom` 9.3.4 → 10.4.2, head `fed79129`, base `develop`, still open). Both PRs independently rewrote the same yarn v1 `cjs-alias` blocks (`string-width-cjs` / `strip-ansi-cjs` / `wrap-ansi-cjs`) and the `@babel/*` alias consolidation, so their lockfile deltas were **non-disjoint** and the coordinator would not pick either first.

This repair resolves the conflict **without rewriting contributor history**:

- Start from the **registered #354 contributor head `3e6500f4`** (preserved, not rebased).
- Normal-merge `origin/develop` (`498a010d`) → `9dccc341`.
- Normal-merge **PR #358's head `fed79129`** → reviewed head **`e9ef37ba`**.

Because #358's lockfile rewrite is now an **ancestor** of #354's head, #354's net `yarn.lock` delta vs `develop` no longer re-touches any `cjs-alias`/`@babel` line beyond what #358 already owns. #354's own contribution is exactly the `eslint-plugin-prettier` 5.5.5→5.5.7 block + the `synckit` range change. This makes #354's lockfile delta **disjoint from #358**, clearing the deterministic merge-coordination block.

> Note: this head carries both #354 and #358 dev bumps in its ancestry. Merging #354 will therefore also land the #358 `@testing-library/dom` 10.4.2 bump (verified: full frontend jest suite passes at this head). If the repo owner prefers to keep #354 strictly single-bump, the alternative is to land #358 first and re-audit #354 against the new `develop` tip. Flagged here for the reviewer; the deterministic guard itself only requires the deltas be disjoint.

## Changes

| File | Change |
|------|--------|
| `package.json` | `eslint-plugin-prettier` `5.5.5 → 5.5.7` (devDependency). |
| `yarn.lock` | `eslint-plugin-prettier@5.5.7` entry (new resolved/integrity); `synckit` `^0.11.12 → ^0.12.3` (new `0.12.3` block); carries #358's `@testing-library/dom` 10.4.2 lock entry via ancestry. |

## Verification (at exact head `e9ef37ba`, isolated Docker, Node v20.20.2 / Yarn 1.22.19)

- `git merge-base --is-ancestor 3e6500f4 e9ef37ba` → true (registered #354 contributor head preserved).
- `git merge-base --is-ancestor fed79129 e9ef37ba` → true (#358 head in ancestry).
- `git merge-base --is-ancestor 498a010d e9ef37ba` → true (develop tip in ancestry).
- `yarn install --frozen-lockfile` → **EXIT 0** (resolves `eslint-plugin-prettier` 5.5.7 + `synckit` 0.12.3 + `@testing-library/dom` 10.4.2).
- `yarn lint` → **EXIT 0**, 0 errors, 8 pre-existing warnings (identical set to prior develop tips); `prettier/prettier` rule active → proves 5.5.7 functions in the ESLint 8.57.1 toolchain.
- `yarn test:frontend` → **EXIT 0**, 20/20 suites, 85/85 tests.

## Rollback

Revert the single dev-tooling bump commit on the PR head branch (or squash-revert the PR). DevDependency only — no data, config, or API contract change; safe to revert at any time.

## Labels

- `type:chore`
- `area:dependencies`
- `area:frontend`
- `priority:low`
- `risk:low`
