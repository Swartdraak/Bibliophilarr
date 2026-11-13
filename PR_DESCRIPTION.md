# PR #411 — fix(issue-284): unhide login-failed banner on rejected-credential redirect

**Repository:** Swartdraak/Bibliophilarr · **PR:** #411 · **Work branch:** `fix/issue-284-login-failed-banner-reveal` · **Base:** `develop` (`2c963c8e`)

## Summary

Fixes GitHub issue #284: the "Incorrect Username or Password" banner on the login page never unhid after a rejected-credential redirect.

Root cause: `frontend/src/login.html` (served verbatim by `LoginHtmlMapper` for `GET /login`) set the footer year with a backslash-escaped quote (`yearSpan.textContent = \"2017-\" + ...`). The literal backslash makes the inline `<script>` an invalid JavaScript token, so the browser throws `SyntaxError: Invalid or unexpected token` and the script never finishes. The banner-reveal block that follows in the SAME script (unhide `#login-failed` when the rejected-credential redirect lands on `?loginFailed=true`) therefore never ran and the banner stayed hidden.

The fix (commit `99c070c1`):
- `frontend/src/login.html:284` now uses plain quotes so the inline script parses and unhides the banner (and still sets the footer year).
- `frontend/src/Issue284/loginBannerReveal.test.js`: 5-test jsdom regression guard that reads the shipped `login.html`, asserts no backslash-escaped quote, compiles the inline scripts (parse-only), and executes them in jsdom to assert the banner unhides on `loginFailed=true` (and stays hidden on a plain `/login`).
- `frontend/build/jest.setup.js`: polyfill `TextEncoder`/`TextDecoder` (`node:util`) so tests that import jsdom load under jest-environment-jsdom.
- `.onyx/project.yaml`: add the `issue-284-regression-login-failed-banner-reveal` test command; `CHANGELOG.md`: Unreleased/Fixed entry.

## CodeQL repair at the new head (task t_51b2e8d8, commit `4519fe7b`)

Exact-head CI at the original reviewed head `99c070c1` (PR #411, run 4957) was green on 17/18 checks; the only failure was CodeQL — a deterministic exact-head CI failure that blocks the merge gate.

**Root cause of the CodeQL failure:** the new regression-guard test file (`frontend/src/Issue284/loginBannerReveal.test.js`) uses a case-sensitive `<script>` extraction regex at line 65:

```js
const re = /<script\b[^>]*>([\s\S]*?)<\/script>/g;
```

CodeQL's "Bad HTML filtering regexp" query flagged this as a potential XSS-filter bypass: the regex does not match upper-case `<SCRIPT>` tags (HTML tags are case-insensitive per the HTML spec). A filter that only catches lowercase `<script>` misses the uppercase form, which is a known vector for bypassing XSS filters.

**Fix (commit `4519fe7b`, test-only — no production source, dependency, or lockfile change):** added the `i` flag to make the regex case-insensitive:

```js
const re = /<script\b[^>]*>([\s\S]*?)<\/script>/gi;
```

This is consistent with the two other regexes in the same function (`srcRegex` and `moduleRegex`), which already use the `i` flag. The shipped `login.html` fix and all 5 regression assertions are unchanged.

## Changes

| File | Change |
|------|--------|
| `frontend/src/login.html` | Line 284: backslash-escaped quote → plain quote (the issue-284 fix). |
| `frontend/src/Issue284/loginBannerReveal.test.js` | 5-test jsdom regression guard. CodeQL repair at `4519fe7b`: `<script>` extraction regex made case-insensitive (`/g` → `/gi`). |
| `frontend/build/jest.setup.js` | Polyfill `TextEncoder`/`TextDecoder` for jest-environment-jsdom. |
| `.onyx/project.yaml` | Add `issue-284-regression-login-failed-banner-reveal` test command. |
| `CHANGELOG.md` | Unreleased/Fixed entry for issue #284. |

## Verification (at exact head `4519fe7b`, isolated Docker, Node 20.20.2 / Yarn 1.22.19)

- `yarn install --frozen-lockfile --ignore-engines` → **EXIT 0** (lockfile byte-unchanged).
- `yarn test:frontend` (jest --config ./jest.config.cjs --runInBand) → **EXIT 0**, 25 suites / 164 passed / 0 failed (including the 5 issue-284 regression-guard tests).
- `yarn lint` → **EXIT 0**, 0 errors, 8 pre-existing warnings (no-negated-condition × 6, no-var × 1, identical class × 1; identical set to prior develop tips).
- `git merge-base --is-ancestor 2c963c8e 4519fe7b` → true (develop tip in ancestry).
- `git merge-base --is-ancestor 99c070c1 4519fe7b` → true (original reviewed head in ancestry; contributor history preserved).
- `git diff 99c070c1..4519fe7b` → 1 file, 1 insertion, 1 deletion (test-only regex flag change).

## Rollback

Revert commit `4519fe7b` (the CodeQL repair) or the full PR. The issue-284 fix is a one-line quote correction in a static HTML asset plus a dev-only test-infra change — no data, config, or API contract change. Safe to revert at any time.

## Closes #284
