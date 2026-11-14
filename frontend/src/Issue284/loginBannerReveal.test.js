/**
 * Issue #284 regression guard — login page footer / rejected-credential banner.
 *
 * Root cause: frontend/src/login.html is served verbatim (static asset, mapped by
 * LoginHtmlMapper for GET /login). Its single inline <script> set the footer year
 * with BACKSLASH-ESCAPED QUOTES:
 *
 *     yearSpan.textContent = \"2017-\" + new Date().getFullYear();
 *
 * The literal backslashes make the inline script an INVALID JavaScript token, so the
 * browser throws "Invalid or unexpected token" and the script never finishes. The
 * banner-reveal block that follows in the SAME script (unhide #login-failed when the
 * rejected-credential redirect lands on ?loginFailed=true) therefore never ran and the
 * "Incorrect Username or Password" banner stayed hidden.
 *
 * The served login.html is a git-ignored build artifact (_output/UI/login.html), so the
 * Durable source of truth is frontend/src/login.html. This test pins the shipped source
 * against both the exact regression (no backslash-escaped quote) and the behavior
 * contract (year set, #copy unhidden, #login-failed unhidden on loginFailed=true),
 * executing the inline script in a real jsdom environment so a future syntax regression
 * is caught the same way a browser would surface it.
 */
import fs from 'fs';
import { JSDOM } from 'jsdom';
import path from 'path';
import vm from 'vm';

/**
 * Locate the shipped login.html by walking up from this test file until the
 * frontend/src/login.html source is found. (login.html is a top-level static asset
 * under frontend/src, sibling to the App/ feature folders.)
 */
function findLoginHtmlPath() {
  let dir = __dirname;
  for (let i = 0; i < 8; i += 1) {
    const candidate = path.join(dir, 'login.html');
    if (fs.existsSync(candidate)) {
      return candidate;
    }
    const parent = path.dirname(dir);
    if (parent === dir) {
      break;
    }
    dir = parent;
  }
  throw new Error(
    `Issue #284 test: could not locate frontend/src/login.html walking up from ${__dirname}`
  );
}

const LOGIN_HTML_PATH = findLoginHtmlPath();

function readLoginHtml() {
  return fs.readFileSync(LOGIN_HTML_PATH, 'utf8');
}

/**
 * Extract the inline (non-module) script block(s) from the served login page.
 * login.html ships exactly one inline <script type="text/javascript"> block; we return
 * every non-src, non-module <script> body so the guard stays valid if the page gains
 * more inline scripts.
 *
 * Implementation: a real HTML parser (jsdom's WHATWG-compliant HTML parser), NOT a
 * regular expression. This test file has been through three CodeQL "Bad HTML
 * filtering regexp" (js/bad-tag-filter) repair rounds, each closing one facet of the
 * same defect class by widening the regex:
 *   facet 1: missing 'i' flag            -> did not match <SCRIPT>
 *   facet 2: </script> only              -> did not match </script >
 *   facet 3: </script\s*>                -> did not match </script\t\n bar>
 * A regex-based end-tag matcher is inherently facet-by-facet: the HTML spec allows
 * arbitrary whitespace, attributes, and newlines inside an end tag, and browsers
 * accept parser-error variants like `</script foo="bar">`. CodeQL's own test suite
 * (github/codeql, BadTagFilter expected output) proves no practical regex passes all
 * facets at once. The task's failure lineage therefore requires a structured
 * representation instead of a wider pattern.
 *
 * jsdom is already a devDependency (jest-environment-jsdom 30.1.2 -> jsdom 26.1.0)
 * and is already imported by this test for the execution assertions below, so this
 * adds no new dependency and no lockfile change. The parser's semantics match what
 * the test must preserve: it treats `<SCRIPT>`/`<script>` case-insensitively,
 * tolerates whitespace and attributes in open AND end tags (including newlines and
 * the browser-forgiving `</script ...>` parser-error variants), captures script
 * bodies verbatim, and excludes external (src=) and module scripts.
 */
function extractInlineScripts(html) {
  const dom = new JSDOM(html, { url: 'http://localhost/login', runScripts: 'outside-only' });
  const scripts = [];
  dom.window.document.querySelectorAll('script').forEach((el) => {
    // Only inline classic scripts are relevant: exclude external (src) and
    // module scripts. Structured DOM property checks — no regex over HTML.
    if (!el.hasAttribute('src') && el.type !== 'module') {
      scripts.push(el.textContent);
    }
  });
  dom.window.close();
  return scripts;
}

/**
 * Compile each inline script body with vm.Script. This is a parse-only check that throws
 * the same SyntaxError a browser engine would throw on a token error — WITHOUT executing
 * the script — so it precisely isolates the "invalid token" defect class from runtime
 * behavior (which the next test covers in jsdom).
 */
function assertInlineScriptsParse() {
  const scripts = extractInlineScripts(readLoginHtml());
  expect(scripts.length).toBeGreaterThanOrEqual(1);
  scripts.forEach((body, i) => {
    // A fresh Script compiles (parses) without throwing. The pre-fix body throws
    // "SyntaxError: Invalid or unexpected token" here.
    expect(() => new vm.Script(body, { filename: `login.html-inline-${i}` })).not.toThrow();
  });
}

/**
 * Execute the inline script in a real jsdom document and assert the three behaviors the
 * rejected-credential UX depends on:
 *   1. the footer year is set to "2017-<currentYear>"
 *   2. the footer #copy element is unhidden
 *   3. when ?loginFailed=true is present, the #login-failed banner is unhidden
 * The pre-fix script throws at the year line, so NONE of these state changes happen.
 */
function runInlineScriptInDom(search) {
  const html = readLoginHtml();
  const dom = new JSDOM(html, {
    url: `http://localhost/login${search}`,
    runScripts: 'outside-only'
  });
  const { window } = dom;
  const document = window.document;

  const scripts = extractInlineScripts(html);
  // The inline script references the browser globals (document, window, location).
  // Expose the jsdom window's own globals on a plain context object so the script
  // resolves them exactly as it would in a real browser tab.
  const contextObject = {
    document: window.document,
    window,
    location: window.location,
    Date: window.Date
  };
  const context = vm.createContext(contextObject);
  scripts.forEach((body) => {
    vm.runInContext(body, context, { filename: 'login.html-inline' });
  });

  const yearEl = document.getElementById('year');
  const copyEl = document.getElementById('copy');
  const loginFailedEl = document.getElementById('login-failed');

  return {
    document,
    year: yearEl ? yearEl.textContent : null,
    copyHidden: copyEl ? copyEl.classList.contains('hidden') : null,
    loginFailedHidden: loginFailedEl ? loginFailedEl.classList.contains('hidden') : null
  };
}

describe('Issue #284 — login page footer + rejected-credential banner', () => {
  it('ships no backslash-escaped quote in the inline script (root cause)', () => {
    const html = readLoginHtml();
    // The exact regression token: a backslash immediately before a double quote.
    expect(html).not.toMatch(/\x5C"/);
  });

  it('inline script parses with no JS token error', () => {
    assertInlineScriptsParse();
  });

  it('sets the footer year and unhides the footer', () => {
    const result = runInlineScriptInDom('');
    const expectedYear = `2017-${new Date().getFullYear()}`;
    expect(result.year).toBe(expectedYear);
    expect(result.copyHidden).toBe(false);
  });

  it('unhides the #login-failed banner on the rejected-credential redirect (loginFailed=true)', () => {
    const result = runInlineScriptInDom('?loginFailed=true');
    // The whole point of issue #284: banner becomes visible after a rejected login.
    expect(result.loginFailedHidden).toBe(false);
    // ...and the rest of the script still ran.
    expect(result.year).toBe(`2017-${new Date().getFullYear()}`);
  });

  it('leaves the #login-failed banner hidden on a plain /login visit (no false positive)', () => {
    const result = runInlineScriptInDom('');
    expect(result.loginFailedHidden).toBe(true);
  });
});
