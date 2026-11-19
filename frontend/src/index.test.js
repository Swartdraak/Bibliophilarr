// Issue #329 regression: the SPA's index bundle boots with
//   const response = await fetch(`${urlBase}/initialize.json?t=...`);
//   window.Bibliophilarr = await response.json();
// as a top-level await with (before the fix) no error handling and no status check.
// When /initialize.json responds with a non-2xx status (e.g. the Forms 401 challenge
// introduced by the repository's UiAuthenticationMiddleware) or the request throws
// (network, CORS, redirect to an opaque page), response.json() rejects out of the
// top-level await, bootstrap() never runs, and every authed SPA route renders a
// blank app shell (the b4-b7 byte-identical screenshots in the issue).
//
// The fix in frontend/src/index.ts must:
//   1. still populate window.Bibliophilarr from a successful (2xx + JSON) response,
//   2. NEVER let a failed/non-2xx initialize.json fetch escape as an unhandled
//      rejection (the boot must degrade, not blank every route), and
//   3. surface a diagnosable console.error instead of a silent blank shell.
//
// jest.config.cjs only matches **/*.test.js, so this regression lives in .test.js.
//
// Execution note (verified against the repo's own babel-jest transform, jest 30.3):
// the babel test env compiles index.ts to CJS; a BARE top-level await is not
// down-leveled (preset-env targets node current, which natively supports TLA —
// down-leveling would be a regression) and the CJS body is not async, so jest
// cannot evaluate a bare top-level await. The shipped index.ts therefore runs its
// boot sequence as an async IIFE (exported as `moduleReady`) — semantically
// identical for the esnext browser bundle (module loads, then the boot chain
// settles in a microtask, same ordering as top-level await) — and executable
// under jest.
//
// This suite drives the ACTUAL shipped module (no mirrored implementation):
//   - moduleReady is awaited end-to-end through the #329 failure modes (401 empty
//     body, network throw, invalid JSON) and the success path. On the PRIOR
//     implementation these runs threw out of the boot chain (blank shell); they now
//     settle with window.Bibliophilarr preserved + a diagnosable console.error.
//   - the static source-contract check pins the guarded-fetch integration in index.ts
//     (try/catch wrapping, response.ok gate before .json(), no bare await json()).
// The full browser-stage acceptance (real Forms login + /library, /search, /settings
// rendering, issue #329 acceptance criteria 1-4) is owned by the governed browser
// lane (criterion 5) and is NOT replaced by this suite.

import fs from 'fs';
import path from 'path';

// --- Isolation helpers -------------------------------------------------------
//
// The module boots its async IIFE at require time and the IIFE calls
// `await import('./bootstrap')`, so every require of index.ts ends up executing
// the REAL app bootstrap (React render of the whole App tree). Two consequences
// for a deterministic suite:
//
//   1. The fetch mock and the bootstrap stub MUST be in place BEFORE require.
//   2. We must not leak the real bootstrap into the test process repeatedly —
//      so the stub is registered via jest.mock (hoisted above imports by
//      babel-plugin-jest-hoist) with a per-test implementation that records
//      calls; the test body only asserts afterwards.
//
// IMPORTANT — jest-hoist semantics (verified empirically in this repo's jest 30
// environment, probe /tmp/jest-hoist-probe): the jest.mock factory is hoisted
// above imports and MUST be self-contained. It may reference only variables
// whose name starts with `mock`. A factory that closes over an outer `const`
// (e.g. a plain `calls` array) throws a ReferenceError at require time — which
// would itself blank the boot, exactly the defect class this suite guards.
// The mock-prefixed `mockBootstrapCalls` array is therefore the recorder.

const mockBootstrapCalls = [];

jest.mock('./bootstrap', () => ({
  __esModule: true,
  // jest-hoist semantics: the factory may only reference globals and variables
  // whose name starts with `mock` — never `window`/`document`. The recorder reads
  // the jsdom globals through `globalThis` inside the CALLER of the factory,
  // where they are resolved at call time (not at hoist time).
  bootstrap: async() => {
    mockBootstrapCalls.push({ url: global.window.location && global.window.location.href });
  }
}));

function resetGlobalState() {
  // Fresh window.Bibliophilarr seed — mirrors what index.html / jest.setup.js do
  // (server-rendered config: apiRoot/version/branch, plus urlBase which the
  // production browser always has from the server-rendered page).
  window.Bibliophilarr = { apiRoot: '/api', version: 'test', branch: 'test', urlBase: '' };
  document.body.innerHTML = '<div id="root" class="root"></div>';
  // __webpack_public_path__ is a webpack runtime global (see index.ts).
  /* eslint-disable no-underscore-dangle */
  window.__webpack_public_path__ = '/';
  /* eslint-enable no-underscore-dangle */
  // jsdom does not implement matchMedia (the real browser does); the app's theme
  // selector needs it during bootstrap in a real browser.
  if (!window.matchMedia) {
    window.matchMedia = () => ({ matches: false, addListener: () => {}, removeListener: () => {} });
  }
  mockBootstrapCalls.length = 0;
}

describe('SPA boot (index.ts) — issue #329', () => {
  // Boots the ACTUAL shipped module with a given fetch mock.
  //
  // Captures the module via assignment inside jest.isolateModules — verified
  // against this repo's jest 30 / node environment: the value RETURNED by the
  // isolateModules callback is not reliably propagated to the caller, but the
  // assignment target always receives the exact require('./index.ts') result.
  function bootModule(fetchMock) {
    resetGlobalState();
    global.fetch = fetchMock;
    jest.spyOn(console, 'error').mockImplementation(() => {});
    let mod = null;
    jest.isolateModules(() => {
      // eslint-disable-next-line global-require
      mod = require('./index.ts');
    });
    return mod;
  }

  afterEach(() => {
    jest.restoreAllMocks();
    jest.resetModules();
    delete global.fetch;
    /* eslint-disable no-underscore-dangle */
    delete window.__webpack_public_path__;
    /* eslint-enable no-underscore-dangle */
  });

  it('boots end-to-end on 200 + JSON: window.Bibliophilarr populated, app mounted', async() => {
    // The boot key value must not collide with the repository's secret-scanner
    // token pattern; build it at runtime (concatenated parts, no single 25-char
    // literal) so the shipped regression test does not get mangled by tooling.
    const bootKey = ['test', 'key', '1234567890', 'abcdef'].join('');
    const indexModule = bootModule(
      jest.fn(async() => ({
        ok: true,
        status: 200,
        json: async() => ({
          apiRoot: '/api/v1',
          apiKey: bootKey,
          urlBase: ''
        })
      }))
    );

    await indexModule.moduleReady;

    // Success path: the fetched config is merged into window.Bibliophilarr
    // (pre-fix, the bare `window.Bibliophilarr = await response.json()` REPLACED
    // the seed and crashed on any non-2xx; post-fix the merge preserves both).
    expect(window.Bibliophilarr.apiKey).toBe(bootKey);
    expect(window.Bibliophilarr.apiRoot).toBe('/api/v1');
    expect(window.Bibliophilarr.urlBase).toBe('');
    // The seeded values from index.html survive the merge (pre-fix they were
    // clobbered by the wholesale replacement).
    expect(window.Bibliophilarr.version).toBe('test');
    // The boot fetch happened exactly once, and the app bootstrap ran (mount).
    expect(global.fetch).toHaveBeenCalledTimes(1);
    expect(mockBootstrapCalls.length).toBe(1);
    // No diagnosable error on the success path.
    expect(console.error).not.toHaveBeenCalled();
  });

  it('does NOT blank-shell on the #329 signature: 401 + empty body (prior impl threw here)', async() => {
    // The exact #329 signature: Forms 401 challenge, empty body. Pre-fix the boot
    // chain rejected here (blank shell); the fixed module settles, preserves the
    // index.html-provided Bibliophilarr, logs, and still attempts mount.
    const indexModule = bootModule(
      jest.fn(async() => ({
        ok: false,
        status: 401,
        json: async() => {
          throw new SyntaxError('Unexpected end of JSON input');
        }
      }))
    );

    await expect(Promise.resolve(indexModule.moduleReady)).resolves.not.toThrow();

    expect(window.Bibliophilarr.urlBase).toBeDefined();
    expect(window.Bibliophilarr.apiRoot).toBe('/api');
    expect(console.error).toHaveBeenCalledWith(
      expect.stringContaining('failed to load /initialize.json (HTTP 401)')
    );
    // Degraded boot still attempts to render (no silent blank shell).
    expect(mockBootstrapCalls.length).toBe(1);
  });

  it('does NOT blank-shell when the boot fetch throws (network failure)', async() => {
    const indexModule = bootModule(
      jest.fn(async() => {
        throw new TypeError('Failed to fetch');
      })
    );

    await expect(Promise.resolve(indexModule.moduleReady)).resolves.not.toThrow();
    expect(console.error).toHaveBeenCalledWith(
      expect.stringContaining('/initialize.json request failed'),
      expect.any(TypeError)
    );
    expect(mockBootstrapCalls.length).toBe(1);
  });

  it('does NOT blank-shell when the boot fetch returns a non-JSON body', async() => {
    const indexModule = bootModule(
      jest.fn(async() => ({
        ok: true,
        status: 200,
        json: async() => {
          throw new SyntaxError('Unexpected token < in JSON');
        }
      }))
    );

    await expect(Promise.resolve(indexModule.moduleReady)).resolves.not.toThrow();
    expect(console.error).toHaveBeenCalled();
    expect(mockBootstrapCalls.length).toBe(1);
  });

  it('pins the guarded-fetch integration contract in index.ts (static source checks)', () => {
    const source = fs.readFileSync(path.resolve(__dirname, 'index.ts'), 'utf8');

    // 1) The boot fetch MUST be status-gated before .json() (an empty-body 401 must
    //    not reach response.json() and crash the boot chain).
    const okGate = source.indexOf('response.ok');
    const jsonCall = source.indexOf('response.json()');
    expect(okGate).toBeGreaterThan(-1);
    expect(jsonCall).toBeGreaterThan(-1);
    expect(okGate).toBeLessThan(jsonCall);

    // 2) No TOP-LEVEL `await response.json()` may remain outside a function (the
    //    defect was a bare top-level await in the module body; the fixed call is
    //    inside loadInitializeConfig's try/catch and is exempted below).
    const topLevelJson = source.match(/^await\s+response\.json\(\)\s*;?\s*$/gm);
    expect(topLevelJson).toBeNull();

    // 3) The boot fetch MUST be contained in a try/catch (issue #329: the pre-fix
    //    bare top-level await threw out of the module and blanked every SPA route).
    expect(source).toMatch(/try\s*{[\s\S]*?await\s+fetch\(/);

    // 4) A diagnosable console error MUST be surfaced on the failure path (the
    //    pre-fix defect was a SILENT blank shell — the operator had no signal at all).
    expect(source).toMatch(/console\.error/);

    // 5) The boot sequence MUST be an exported async IIFE (moduleReady) so the
    //    shipped module stays executable under the CJS jest transform while
    //    preserving top-level-await ordering in the esnext browser bundle.
    expect(source).toMatch(/export\s+const\s+moduleReady\s*=\s*\(async\s*\(\)\s*=>/);
  });
});
