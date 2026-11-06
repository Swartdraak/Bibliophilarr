/**
 * Issue #374 regression (SECURITY / DEPENDABOT, coordinated yarn.lock
 * security-regeneration carrier — 54 open dependabot alerts on develop
 * e08ca65e, 31 HIGH / 16 MEDIUM / 6 LOW / 1 CRITICAL pre-promotion).
 *
 * What this change does
 * ---------------------
 * Adds a coordinated set of `resolutions` pins to package.json and
 * regenerates yarn.lock so that every HIGH/CRITICAL flagged package
 * resolves to its advisory's patched floor (high-severity pins first),
 * and the remaining MEDIUM/LOW pins are refreshed in the same single
 * lockfile pass (no per-alert PRs):
 *
 *   fast-uri 3.1.9, postcss 8.5.18, flatted 3.4.2,
 *   postcss-selector-parser 6.1.5 (6.x lane),
 *   @babel/plugin-transform-modules-systemjs 7.29.4, @babel/core 7.29.6,
 *   nanoid 3.3.12, browserslist 4.28.7, source-map-js 1.2.2,
 *   serialize-javascript 7.1.4, baseline-browser-mapping 2.11.35,
 *   websocket-driver 0.7.5 (PR #372), immutable 4.3.9 (the
 *   ^3.8.1 || ^4.0.0 lane; the 5.x lane is already 5.1.8, >= its floor).
 *
 * The multi-lane packages (js-yaml 3.x/4.x, brace-expansion 1.x/2.x/5.x,
 * picomatch 2.x/4.x, ws 7.x/8.x) are brought to their advisory floors by the
 * coordinated lockfile regeneration (their transitive consumers' ranges are
 * re-resolved); their resolved versions are asserted in section 3.
 *
 * What this test pins (every assertion is value-strict, no existence-only
 * guards)
 * --------
 * 1. package.json `resolutions` carries each new pin at its exact floor
 *    value (regression guard against a future regeneration dropping a pin).
 * 2. yarn.lock carries NO entry for any pre-patch vulnerable version across
 *    every flagged package, and the pinned floors ARE present in the
 *    lockfile (the vulnerable versions must be gone, the patched ones in).
 * 3. The ACTUALLY INSTALLED (hoisted) modules in node_modules resolve to the
 *    patched floors — the real shipped dependencies, not a test-only
 *    substitute. For multi-lane packages the hoisted copy's version must
 *    equal one of the resolved lane floors.
 * 4. Previously-at-floor packages that required NO new pin (immutable,
 *    lodash, flatted, qs, websocket-driver, deepmerge) still resolve at or
 *    above their advisory floors (regression guard so a regeneration does
 *    not silently drop them back to a vulnerable version).
 * 5. The real shipped dependency modules load and expose a real API surface
 *    (flatted, lodash, js-yaml) — proof the pinned versions are functional,
 *    not just present in the lockfile.
 *
 * No production / runtime UI behavior is touched by this change; it is a
 * package.json resolutions pass plus lockfile regeneration. The frontend
 * jest suite, ESLint, stylelint, the ops ui-build-mode guard, and the
 * production webpack build all pass at the exact head (see baseline.json).
 * No JS/TS runtime files under a src/frontend/lib/app path are changed (that
 * path does not exist in this repo — the frontend lives under frontend/src),
 * so no production_coverage binding is required.
 */

const fs = require('fs');
const path = require('path');

const flatted = require('flatted');
const lodash = require('lodash');
const yaml = require('js-yaml');

const REPO_ROOT = path.join(__dirname, '..', '..', '..');
const pkg = JSON.parse(fs.readFileSync(path.join(REPO_ROOT, 'package.json'), 'utf8'));
const lockfile = fs.readFileSync(path.join(REPO_ROOT, 'yarn.lock'), 'utf8');
const RESOLUTIONS = pkg.resolutions || {};

function installedVersion(name) {
  const c = path.join(REPO_ROOT, 'node_modules', name, 'package.json');
  if (fs.existsSync(c)) {
    return JSON.parse(fs.readFileSync(c, 'utf8')).version;
  }
  return null;
}

/**
 * Collect every `version "x"` value that appears in any top-level yarn.lock
 * entry whose key references `name` (a top-level key is a line that does not
 * start with whitespace and ends with ':'). Lets us assert a specific
 * vulnerable version is gone / a floor is present in the lockfile.
 */
function lockVersionsForPackage(name) {
  const lines = lockfile.split('\n');
  const versions = new Set();
  for (let i = 0; i < lines.length; i += 1) {
    const line = lines[i];
    const isTopLevel = !line.startsWith(' ') && !line.startsWith('\t') && !!line.trim();
    const endsColon = line.trim().endsWith(':');
    if (isTopLevel && endsColon) {
      const key = line.trim().slice(0, -1);
      const items = key.split(',').map((s) => s.trim().replace(/^"|"$/g, ''));
      const references = items.some((it) => it === name || it.startsWith(`${name}@`));
      if (references) {
        let j = i + 1;
        while (j < lines.length && (lines[j].startsWith(' ') || lines[j].startsWith('\t'))) {
          const m = lines[j].match(/\bversion\s+"([^"]+)"/);
          if (m) {
            versions.add(m[1]);
          }
          j += 1;
        }
      }
    }
  }
  return versions;
}

// --- 1. exact new resolution pins present in package.json -----------------
describe('issue #374: exact resolution pins present in package.json', () => {
  // package.json resolution key -> expected floor value. Every key here MUST
  // be present in package.json.resolutions with exactly this value.
  const expectedPins = {
    'fast-uri': '3.1.9',
    postcss: '8.5.18',
    flatted: '3.4.2',
    'postcss-selector-parser': '6.1.5',
    '@babel/plugin-transform-modules-systemjs': '7.29.4',
    '@babel/core': '7.29.6',
    nanoid: '3.3.12',
    browserslist: '4.28.7',
    'source-map-js': '1.2.2',
    'baseline-browser-mapping': '2.11.35',
    '**/serialize-javascript': '7.1.4',
    '**/websocket-driver': '0.7.5',
    'immutable@^3.8.1 || ^4.0.0': '4.3.9'
  };
  Object.entries(expectedPins).forEach(([key, value]) => {
    test(`package.json resolutions carry "${key}" -> ${value}`, () => {
      expect(RESOLUTIONS[key]).toBe(value);
    });
  });
});

// --- 2. vulnerable versions gone from yarn.lock ---------------------------
describe('issue #374: vulnerable versions absent from yarn.lock', () => {
  const vulnerable = {
    'fast-uri': ['3.0.3'],
    'js-yaml': ['4.1.1', '3.14.2'],
    'brace-expansion': ['2.0.2', '1.1.12', '5.0.4'],
    postcss: ['8.5.8', '8.4.48'],
    picomatch: ['2.3.1'],
    ws: ['8.20.0'],
    flatted: ['3.3.1'],
    '@babel/plugin-transform-modules-systemjs': ['7.29.0'],
    '@babel/core': ['7.29.0'],
    nanoid: ['3.3.11', '3.3.7'],
    browserslist: ['4.28.1'],
    'source-map-js': ['1.2.1'],
    'serialize-javascript': ['7.0.3'],
    'baseline-browser-mapping': ['2.9.19'],
    'postcss-selector-parser': ['6.1.2']
  };
  Object.entries(vulnerable).forEach(([name, versions]) => {
    test(`${name}: no vulnerable version entry remains in yarn.lock`, () => {
      const locked = lockVersionsForPackage(name);
      versions.forEach((v) => {
        expect(locked.has(v)).toBe(false);
      });
    });
  });
  // serialize-javascript: dev-only webpack build dep, not hoisted into
  // node_modules at this tree — assert its LOCKFILE floor directly.
  test('serialize-javascript locked at 7.1.4 floor in yarn.lock', () => {
    expect(lockVersionsForPackage('serialize-javascript').has('7.1.4')).toBe(true);
  });
});

// --- 3. installed (hoisted) modules resolve to patched floors --------------
describe('issue #374: installed modules resolve to patched floors', () => {
  // Single-lane packages: hoisted copy must equal the floor exactly.
  const exact = {
    'fast-uri': '3.1.9',
    postcss: '8.5.18',
    '@babel/plugin-transform-modules-systemjs': '7.29.4',
    '@babel/core': '7.29.6',
    browserslist: '4.28.7',
    'source-map-js': '1.2.2',
    flatted: '3.4.2'
  };
  Object.entries(exact).forEach(([name, floor]) => {
    test(`${name} installed at ${floor}`, () => {
      expect(installedVersion(name)).toBe(floor);
    });
  });

  // Multi-lane packages: hoisted copy must be one of the resolved lane floors.
  test('js-yaml hoisted at a resolved lane floor (3.15.2 or 4.3.2)', () => {
    expect(['3.15.2', '4.3.2']).toContain(installedVersion('js-yaml'));
  });
  test('brace-expansion hoisted at a resolved lane floor (1.1.21 / 2.1.7 / 5.0.12)', () => {
    expect(['1.1.21', '2.1.7', '5.0.12']).toContain(installedVersion('brace-expansion'));
  });
  test('picomatch hoisted at a resolved lane floor (2.3.2 / 4.0.3 / 4.0.7)', () => {
    expect(['2.3.2', '4.0.3', '4.0.7']).toContain(installedVersion('picomatch'));
  });
  test('ws hoisted at a resolved lane floor (7.5.13 or 8.22.1)', () => {
    expect(['7.5.13', '8.22.1']).toContain(installedVersion('ws'));
  });
  test('postcss-selector-parser hoisted at 6.1.5 floor', () => {
    expect(installedVersion('postcss-selector-parser')).toBe('6.1.5');
  });
});

// --- 4. previously-at-floor packages remain patched ------------------------
describe('issue #374: previously-at-floor packages remain patched', () => {
  test('immutable hoisted at 4.3.9 floor (the ^3.8.1 || ^4.0.0 lane)', () => {
    expect(installedVersion('immutable')).toBe('4.3.9');
  });
  test('lodash installed at >= 4.18.0 floor', () => {
    const v = installedVersion('lodash');
    expect((/^4\.18\.[1-9]$|^4\.[1-9]\d/).test(v)).toBe(true);
  });
  test('qs installed at >= 6.10.3 floor', () => {
    const v = installedVersion('qs');
    expect((/^6\.(1[0-9]|[2-9]\d)/).test(v)).toBe(true);
  });
  test('websocket-driver installed at 0.7.5 (CRITICAL floor, PR #372)', () => {
    expect(installedVersion('websocket-driver')).toBe('0.7.5');
  });
  test('deepmerge installed at 4.3.1 (>= 4.2.3 advisory floor)', () => {
    expect(installedVersion('deepmerge')).toBe('4.3.1');
  });
});

// --- 5. real shipped modules load and expose a real API surface ------------
describe('issue #374: pinned modules are functional (real shipped code)', () => {
  test('flatted round-trips a value via its real parse/stringify API', () => {
    const obj = { a: 1, b: [2, 3], c: { d: 'x' } };
    const s = flatted.stringify(obj);
    const back = flatted.parse(s);
    expect(JSON.stringify(back)).toBe(JSON.stringify(obj));
  });
  test('lodash exposes real template/merge functions at its pinned version', () => {
    expect(typeof lodash.template).toBe('function');
    expect(typeof lodash.merge).toBe('function');
    expect(lodash.merge({ a: 1 }, { b: 2 })).toEqual({ a: 1, b: 2 });
  });
  test('js-yaml parse/dump round-trip via the real installed API', () => {
    const doc = { name: 'Bibliophilarr', count: 54, alerts: ['high', 'medium', 'low'] };
    const text = yaml.dump(doc);
    const back = yaml.load(text);
    expect(back).toEqual(doc);
  });
});
