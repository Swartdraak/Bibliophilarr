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

## CodeQL repair at the new head (tasks t_51b2e8d8 + t_c25924d2, commits `4519fe7b` + this repair)

Exact-head CI at the original reviewed head `99c070c1` (PR #411, run 4957) was green on 17/18 checks; the only failure was CodeQL — a deterministic exact-head CI failure that blocks the merge gate. CodeQL's "Bad HTML filtering regexp" audit query carries **two facets**, each surfacing at a successive head:

### Facet 1 — case-sensitive OPEN tag (commit `4519fe7b`, task t_51b2e8d8)

The new regression-guard test file (`frontend/src/Issue284/loginBannerReveal.test.js`) used a case-sensitive `<script>` extraction regex at line 65:

```js
const re = /<script\b[^>]*>([\s\S]*?)<\/script>/g;
```

CodeQL flagged this as a potential XSS-filter bypass: the regex did not match upper-case `<SCRIPT>` tags (HTML tags are case-insensitive per the HTML spec). **Fix (commit `4519fe7b`, test-only):** added the `i` flag:

```js
const re = /<script\b[^>]*>([\s\S]*?)<\/script>/gi;
```

### Facet 2 — end-tag trailing whitespace (this repair, task t_c25924d2)

At the repaired head `aab6cd71`, CodeQL re-ran and reported a **new, different** annotation at the same line: *"This regular expression does not match script end tags like `</script >`."* The `i` flag closed the open-tag facet but the END-tag match `<\/script>` still did not tolerate legal trailing whitespace in the closing tag, so `</script >` (and `</SCRIPT >`) would slip through the extractor. **Fix (this repair, test-only):** allow optional whitespace before the closing `>`:

```js
const re = /<script\b[^>]*>([\s\S]*?)<\/script\s*>/gi;
```

The extractor is now a strict superset of the previous one: it still matches every well-formed `<script>…</script>` block (verified — normal-case extraction is unchanged) and additionally captures end tags carrying trailing whitespace, which is exactly what the CodeQL audit query requires. All 5 regression assertions are unchanged; no production source, dependency, or lockfile changed.

## Changes

| File | Change |
|------|--------|
| `frontend/src/login.html` | Line 284: backslash-escaped quote → plain quote (the issue-284 fix). |
| `frontend/src/Issue284/loginBannerReveal.test.js` | 5-test jsdom regression guard. CodeQL repair facet 1 at `4519fe7b`: extraction regex case-insensitive (`/g` → `/gi`). CodeQL repair facet 2 (this commit): end-tag match `<\/script>` → `<\/script\s*>` to tolerate `</script >`. |
| `frontend/build/jest.setup.js` | Polyfill `TextEncoder`/`TextDecoder` for jest-environment-jsdom. |
| `.onyx/project.yaml` | Add `issue-284-regression-login-failed-banner-reveal` test command. |
| `CHANGELOG.md` | Unreleased/Fixed entry for issue #284 (documents the root-cause regression guard, which is unchanged by both test-only CodeQL repairs). |

## Verification (at this repair head, isolated Docker, Node 20.20.2 / Yarn 1.22.19)

- `yarn install --frozen-lockfile --ignore-engines` → **EXIT 0** (lockfile byte-unchanged).
- Targeted regression guard `jest --config ./jest.config.cjs frontend/src/Issue284/loginBannerReveal.test.js` → **EXIT 0**, 5 passed / 5 total.
- `yarn test:frontend` (jest --config ./jest.config.cjs --runInBand) → **EXIT 0**, 25 suites / 164 passed / 0 failed (count identical to the prior repair head `aab6cd71`; no test added or weakened).
- `yarn lint` → **EXIT 0**, 0 errors, 8 pre-existing warnings (identical set to the prior head: no-negated-condition × 7, no-var × 1).
- Regex semantics verified in isolation: the old regex missed `<script …>…</script >` (0 bodies) and over-matched a mixed `<script>foo()</script > <script type="module">bar()</script>` page; the new regex captures `</script >` correctly, still extracts the normal `<script>…</script>` case, and correctly excludes the module script.
- `git diff aab6cd71..<this repair>` → 1 file, test-only (the end-tag regex + an explanatory comment). No production source, dependency, or lockfile change.
- `git merge-base --is-ancestor 2c963c8e <this repair>` → true (develop tip in ancestry); `git merge-base --is-ancestor 99c070c1 <this repair>` → true (original reviewed head in ancestry; contributor history preserved).

## Rollback

Revert this repair commit (the end-tag regex change), or the full PR. Both CodeQL repairs are test-only; the issue-284 fix is a one-line quote correction in a static HTML asset plus a dev-only test-infra change — no data, config, or API contract change. Safe to revert at any time.

## Closes #284
