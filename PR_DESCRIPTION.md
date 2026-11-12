# PR #408 — fix(issue-407): resync dependabot train + reselect 5.x compatibility

**Repository:** Swartdraak/Bibliophilarr · **PR:** #408 · **Work branch:** `fix/issue-407-dependabot-train-resync` · **Base:** `develop` (`9b828d1a`)

## Summary

This PR repairs the S84 dependabot-train build failure (issue #407) by:

1. **Resyncing** the five S84 dependabot PRs (#391 babel-jest, #392 stylelint-order, #393 react-dnd-touch-backend, #394 reselect, #395 postcss-nested) to the develop tip `9b828d1a` via merge commits (contributor history preserved, no force-push/rebase).
2. **Governing the postcss-selector-parser lockfile lane** — restoring the #376 7.x-lane advisory-floor pin at 7.1.6 (the 6.x lane keeps 6.1.5; no 7.x version below 7.1.6 in yarn.lock).
3. **Fixing the reselect 5.x `defaultMemoize` regression** (Option A from rejected review t_fa4ecbaf): the reselect 4.1.8→5.3.0 bump (S84 PR #394, resynced at commit `27c5a009`) removed the `defaultMemoize` export that three selector files imported at module-load time via `createSelectorCreator(defaultMemoize, equalityFn)`. At runtime, `defaultMemoize` was `undefined`, breaking the book/author index and bookshelf UI surfaces. The prior review (t_c4511ffe) misclassified the 3 resulting webpack `export defaultMemoize ... not found in reselect` warnings as pre-existing bundle-size warnings.

## Why this head changed (publication repair, task t_58d3c6df)

The original reviewed head `3cb167b0` (develop tip + 5 S84 resync merges + postcss-selector-parser lockfile-lane repair) was **rejected** by independent review t_fa4ecbaf (run 4842): the reselect 4.1.8→5.3.0 bump introduced a runtime-breaking regression — the `defaultMemoize` export was removed in reselect 5.x, but three source files still imported it at module-load time.

This repair (Option A, preferred by the rejection) migrates the three affected selector files from `defaultMemoize` to `lruMemoize` (the documented reselect 5.x memoize primitive), preserving the original `createSelectorCreator(memoize, equalityFn)` caching contract. No dependency/lockfile change — `package.json` and `yarn.lock` are byte-unchanged from the prior head `3cb167b0`.

A focused regression test (`frontend/src/Store/Selectors/reselect5Compatibility.test.js`) is added that imports and exercises the three repaired files with reselect 5.3.0, proving:
- (a) the modules load without the original undefined-import runtime error,
- (b) `createDeepEqualSelector` honours its deep-equality caching contract,
- (c) the book/author client-side collection-item selector chains produce correct projected items through the `lruMemoize`-based factories.

## Lint hygiene repair at the new head (task t_a8acf32e, commit `d2582f5f`)

Independent review t_7bb1c223 (run 4849) **rejected** the reselect-repaired head `e4c899e`: the review confirmed the `lruMemoize` migration itself correct and green (24/159 jest, zero-warning production build, all evidence hashes verified), but found **4 committed lint errors in the new regression test file** (`frontend/src/Store/Selectors/reselect5Compatibility.test.js`, added at `a8cfee41`) that make `yarn lint` exit 1 — failing the CI lint job and the onyx-onboard merge guard:

- 23:8 `no-unused-vars` — `_` (lodash) imported but never used
- 29:8 `no-unused-vars` — `createBooksClientSideCollectionSelector` imported but never used
- 30:8 `no-unused-vars` — `createClientSideCollectionSelector` imported but never used
- 23:1 `simple-import-sort/imports` — import block not in configured sort order

Repair (test-file hygiene only — no production source, dependency, or lockfile change; commit `d2582f5f`): removed the 3 unused import lines and applied `simple-import-sort` autofix (the `reselect` import now shares the package-import group with the relative imports, alphabetized). `yarn lint` at the new exact head `d2582f5f` exits 0 (0 errors, 8 pre-existing warnings).

## Changes

| File | Change |
|------|--------|
| `frontend/src/Store/Selectors/createDeepEqualSelector.js` | `defaultMemoize` → `lruMemoize` (reselect 5.x migration). |
| `frontend/src/Store/Selectors/createBookClientSideCollectionItemsSelector.js` | `defaultMemoize` → `lruMemoize` (reselect 5.x migration). |
| `frontend/src/Store/Selectors/createAuthorClientSideCollectionItemsSelector.js` | `defaultMemoize` → `lruMemoize` (reselect 5.x migration). |
| `frontend/src/Store/Selectors/reselect5Compatibility.test.js` | New regression test for reselect 5.x selector compatibility (4 tests). Lint-hygiene repaired at `d2582f5f` (3 unused imports removed, import sort fixed) after review t_7bb1c223. |
| `package.json` | `reselect` 4.1.8→5.3.0 (from S84 PR #394 resync; unchanged by this repair). |
| `yarn.lock` | `reselect@5.3.0` entry (from S84 PR #394 resync; unchanged by this repair). |

## Verification (at exact head `d2582f5f`, isolated Docker, Node 20.20.2 / Yarn 1.22.19)

- `yarn install --frozen-lockfile --ignore-engines` → **EXIT 0** (lockfile byte-unchanged; `--ignore-engines` only because the audit container is Node 20.20.2 and postcss-nested 9.0.1 declares engine ^22.0.0 || ^24.0.0; CI uses Node 22.22.2 per `.github/workflows/ci-frontend.yml`).
- `yarn test:frontend` (jest --config ./jest.config.cjs --runInBand) → **EXIT 0**, 24 suites / 159 passed / 0 failed (including the 4 new reselect5Compatibility tests; the Issue #376 lockfile-pin test 14/14 pass — the postcss 6.x/7.x lane split is intact).
- `yarn lint` → **EXIT 0**, 0 errors, 8 pre-existing warnings (6 no-negated-condition + 1 no-var + 1 identical class; identical set to prior develop tips).
- `yarn build` (cross-env BIBLIOPHILARR_WEBPACK_PRODUCTION=1 webpack) → **EXIT 0**, `webpack 5.105.4 compiled successfully` with **ZERO warnings** (the 3 reselect `export defaultMemoize ... not found in reselect` warnings from the prior head `3cb167b0` are ELIMINATED — this is the key signal of the reselect repair).
- `./scripts/ops/check_ui_build_mode.sh` (UI build-mode regression guard, issue #252) → **PASS** (production bundle carries no eval-source-map devtool and no eval() calls).
- `git merge-base --is-ancestor 9b828d1a d2582f5f` → true (develop tip in ancestry).
- All five S84 dependabot contributor commits are merge-ancestors of the head (60fdd37f / efdb8011 / 1f1820de / 257cf6d0 / 2d654748) — contributor history preserved via merge, never force-pushed or rebased.
- `yarn lint` at exact head `d2582f5f` re-verified after the lint-hygiene commit: **EXIT 0**, 0 errors, 8 pre-existing warnings (reproduction log at `/workspace/logs-t_a8acf32e/verify-lint-committed.log`).

## Rollback

Revert the three reselect migration commits on the PR head branch (or squash-revert the PR). The reselect 5.x migration is a source-code compatibility repair — no data, config, or API contract change. Safe to revert at any time.

## Closes #407
